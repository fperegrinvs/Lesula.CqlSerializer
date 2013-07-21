namespace ProtoBuf
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Text;

    using Lesula.Cassandra.FrontEnd;

    using ProtoBuf.Meta;

    /// <summary>
    /// A stateful reader, used to read a protobuf stream. Typical usage would be (sequentially) to call
    /// ReadFieldHeader and (after matching the field) an appropriate Read* method.
    /// </summary>
    internal sealed class ProtoReader : IDisposable
    {
        MemberInfo[] members = null;
        int? size = null;
        Stream source;
        byte[] ioBuffer;
        TypeModel model;

        private int fieldNumber;
        WireType wireType = WireType.None;

        /// <summary>
        /// Gets the number of the field being processed.
        /// </summary>
        public int FieldNumber { get { return fieldNumber; } }
        /// <summary>
        /// Indicates the underlying proto serialization format on the wire.
        /// </summary>
        public WireType WireType { get { return wireType; } }

        /// <summary>
        /// Creates a new reader against a stream
        /// </summary>
        /// <param name="source">The source stream</param>
        /// <param name="model">The model to use for serialization; this can be null, but this will impair the ability to deserialize sub-objects</param>
        /// <param name="context">Additional context about this serialization operation</param>
        public ProtoReader(Stream source, TypeModel model, SerializationContext context, MemberInfo[] members = null)
        {
            this.members = members;
            if (source == null) throw new ArgumentNullException("source");
            if (!source.CanRead) throw new ArgumentException("Cannot read from stream", "source");
            this.source = source;
            this.ioBuffer = BufferPool.GetBuffer();
            this.model = model;
            this.isFixedLength = false;
            this.dataRemaining = 0;

            if (context == null) { context = SerializationContext.Default; }
            else { context.Freeze(); }
            this.context = context;
        }

        private int dataRemaining;
        private readonly bool isFixedLength;
        private bool internStrings = true;

        private readonly SerializationContext context;

        /// <summary>
        /// Addition information about this deserialization operation.
        /// </summary>
        public SerializationContext Context { get { return context; } }
        /// <summary>
        /// Releases resources used by the reader, but importantly <b>does not</b> Dispose the 
        /// underlying stream; in many typical use-cases the stream is used for different
        /// processes, so it is assumed that the consumer will Dispose their stream separately.
        /// </summary>
        public void Dispose()
        {
            // importantly, this does **not** own the stream, and does not dispose it
            source = null;
            model = null;
            BufferPool.ReleaseBufferToPool(ref ioBuffer);
        }
        internal int TryReadUInt32VariantWithoutMoving(bool trimNegative, out uint value)
        {
            if (available < 10) Ensure(10, false);
            if (available == 0)
            {
                value = 0;
                return 0;
            }
            int readPos = ioIndex;
            value = ioBuffer[readPos++];
            if ((value & 0x80) == 0) return 1;
            value &= 0x7F;
            if (available == 1) throw EoF(this);

            uint chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 7;
            if ((chunk & 0x80) == 0) return 2;
            if (available == 2) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 14;
            if ((chunk & 0x80) == 0) return 3;
            if (available == 3) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 21;
            if ((chunk & 0x80) == 0) return 4;
            if (available == 4) throw EoF(this);

            chunk = ioBuffer[readPos];
            value |= chunk << 28; // can only use 4 bits from this chunk
            if ((chunk & 0xF0) == 0) return 5;

            if (trimNegative // allow for -ve values
                && (chunk & 0xF0) == 0xF0
                && available >= 10
                    && ioBuffer[++readPos] == 0xFF
                    && ioBuffer[++readPos] == 0xFF
                    && ioBuffer[++readPos] == 0xFF
                    && ioBuffer[++readPos] == 0xFF
                    && ioBuffer[++readPos] == 0x01)
            {
                return 10;
            }
            throw AddErrorData(new OverflowException(), this);
        }
        private uint ReadUInt32Variant(bool trimNegative)
        {
            uint value;
            int read = TryReadUInt32VariantWithoutMoving(trimNegative, out value);
            if (read > 0)
            {
                ioIndex += read;
                available -= read;
                position += read;
                return value;
            }
            throw EoF(this);
        }

        /// <summary>
        /// Reads an unsigned 32-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64
        /// </summary>
        public uint ReadUInt32()
        {
            switch (wireType)
            {
                case WireType.Fixed8:
                    if (available < 1) this.Ensure(1, true);
                    position += 1;
                    available -= 1;
                    return this.ioBuffer[this.ioIndex++];
                case WireType.Variant:
                    return ReadUInt32Variant(false);
                case WireType.Fixed32:
                    if (available < 4) Ensure(4, true);
                    position += 4;
                    available -= 4;
                    return ((uint)ioBuffer[ioIndex++])
                        | (((uint)ioBuffer[ioIndex++]) << 8)
                        | (((uint)ioBuffer[ioIndex++]) << 16)
                        | (((uint)ioBuffer[ioIndex++]) << 24);
                case WireType.Fixed64:
                    ulong val = ReadUInt64();
                    checked { return (uint)val; }
                default:
                    throw CreateWireTypeException();
            }
        }
        int ioIndex, position, available; // maxPosition
        /// <summary>
        /// Returns the position of the current reader (note that this is not necessarily the same as the position
        /// in the underlying stream, if multiple readers are used on the same stream)
        /// </summary>
        public int Position { get { return position; } }
        internal void Ensure(int count, bool strict)
        {
            Helpers.DebugAssert(available <= count, "Asking for data without checking first");
            if (count > ioBuffer.Length)
            {
                BufferPool.ResizeAndFlushLeft(ref ioBuffer, count, ioIndex, available);
                ioIndex = 0;
            }
            else if (ioIndex + count >= ioBuffer.Length)
            {
                // need to shift the buffer data to the left to make space
                Helpers.BlockCopy(ioBuffer, ioIndex, ioBuffer, 0, available);
                ioIndex = 0;
            }
            count -= available;
            int writePos = ioIndex + available, bytesRead;
            int canRead = ioBuffer.Length - writePos;
            if (isFixedLength)
            {   // throttle it if needed
                if (dataRemaining < canRead) canRead = dataRemaining;
            }
            while (count > 0 && canRead > 0 && (bytesRead = source.Read(ioBuffer, writePos, canRead)) > 0)
            {
                available += bytesRead;
                count -= bytesRead;
                canRead -= bytesRead;
                writePos += bytesRead;
                if (isFixedLength) { dataRemaining -= bytesRead; }
            }
            if (strict && count > 0)
            {
                throw EoF(this);
            }

        }
        /// <summary>
        /// Reads a signed 16-bit integer from the stream: Variant, Fixed32, Fixed64, SignedVariant
        /// </summary>
        public short ReadInt16()
        {
            checked { return (short)ReadInt32(); }
        }

        /// <summary>
        /// Reads an unsigned 16-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64
        /// </summary>
        public ushort ReadUInt16()
        {
            checked { return (ushort)ReadUInt32(); }
        }

        /// <summary>
        /// Reads an unsigned 8-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64
        /// </summary>
        public byte ReadByte()
        {
            checked { return (byte)ReadUInt32(); }
        }

        /// <summary>
        /// Reads a signed 8-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64, SignedVariant
        /// </summary>
        public sbyte ReadSByte()
        {
            checked { return (sbyte)ReadInt32(); }
        }

        /// <summary>
        /// Reads a signed 32-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64, SignedVariant
        /// </summary>
        public int ReadInt32(WireType wire = WireType.None)
        {
            if (wire == WireType.None)
            {
                wire = wireType;
            }

            switch (wire)
            {
                case WireType.Variant:
                    return (int)ReadUInt32Variant(true);
                case WireType.Fixed32:
                    {
                        if (available < 4) Ensure(4, true);
                        position += 4;
                        available -= 4;
                        byte[] bytes = new byte[4];
                        bytes[0] = ioBuffer[ioIndex++];
                        bytes[1] = ioBuffer[ioIndex++];
                        bytes[2] = ioBuffer[ioIndex++];
                        bytes[3] = ioBuffer[ioIndex++];
                        var result = bytes.ToInt32();
                        return result;
                    }
                case WireType.Fixed16:
                    {
                        if (available < 2) Ensure(2, true);
                        position += 2;
                        available -= 2;
                        byte[] bytes = new byte[2];
                        bytes[0] = ioBuffer[ioIndex++];
                        bytes[1] = ioBuffer[ioIndex++];
                        var result = bytes.ToInt32();
                        return result;
                    }
                case WireType.Fixed64:
                    long l = ReadInt64();
                    checked { return (int)l; }
                case WireType.SignedVariant:
                    return Zag(ReadUInt32Variant(true));
                default:
                    throw CreateWireTypeException();
            }
        }
        private const long Int64Msb = ((long)1) << 63;
        private const int Int32Msb = ((int)1) << 31;
        private static int Zag(uint ziggedValue)
        {
            int value = (int)ziggedValue;
            return (-(value & 0x01)) ^ ((value >> 1) & ~ProtoReader.Int32Msb);
        }

        private static long Zag(ulong ziggedValue)
        {
            long value = (long)ziggedValue;
            return (-(value & 0x01L)) ^ ((value >> 1) & ~ProtoReader.Int64Msb);
        }
        /// <summary>
        /// Reads a signed 64-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64, SignedVariant
        /// </summary>
        public long ReadInt64()
        {
            switch (wireType)
            {
                case WireType.Variant:
                    return (long)ReadUInt64Variant();
                case WireType.Fixed32:
                    return ReadInt32();
                case WireType.Fixed64:
                    if (available < 8) Ensure(8, true);
                    position += 8;
                    available -= 8;

                    var result = ((long)ioBuffer[ioIndex + 7])
                        | (((long)ioBuffer[ioIndex + 6]) << 8)
                        | (((long)ioBuffer[ioIndex + 5]) << 16)
                        | (((long)ioBuffer[ioIndex + 4]) << 24)
                        | (((long)ioBuffer[ioIndex + 3]) << 32)
                        | (((long)ioBuffer[ioIndex + 2]) << 40)
                        | (((long)ioBuffer[ioIndex + 1]) << 48)
                        | (((long)ioBuffer[ioIndex]) << 56);
                    ioIndex += 8;
                    return result;
                case WireType.SignedVariant:
                    return Zag(ReadUInt64Variant());
                default:
                    throw CreateWireTypeException();
            }
        }

        private int TryReadUInt64VariantWithoutMoving(out ulong value)
        {
            if (available < 10) Ensure(10, false);
            if (available == 0)
            {
                value = 0;
                return 0;
            }
            int readPos = ioIndex;
            value = ioBuffer[readPos++];
            if ((value & 0x80) == 0) return 1;
            value &= 0x7F;
            if (available == 1) throw EoF(this);

            ulong chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 7;
            if ((chunk & 0x80) == 0) return 2;
            if (available == 2) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 14;
            if ((chunk & 0x80) == 0) return 3;
            if (available == 3) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 21;
            if ((chunk & 0x80) == 0) return 4;
            if (available == 4) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 28;
            if ((chunk & 0x80) == 0) return 5;
            if (available == 5) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 35;
            if ((chunk & 0x80) == 0) return 6;
            if (available == 6) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 42;
            if ((chunk & 0x80) == 0) return 7;
            if (available == 7) throw EoF(this);


            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 49;
            if ((chunk & 0x80) == 0) return 8;
            if (available == 8) throw EoF(this);

            chunk = ioBuffer[readPos++];
            value |= (chunk & 0x7F) << 56;
            if ((chunk & 0x80) == 0) return 9;
            if (available == 9) throw EoF(this);

            chunk = ioBuffer[readPos];
            value |= chunk << 63; // can only use 1 bit from this chunk

            if ((chunk & ~(ulong)0x01) != 0) throw AddErrorData(new OverflowException(), this);
            return 10;
        }

        private ulong ReadUInt64Variant()
        {
            ulong value;
            int read = TryReadUInt64VariantWithoutMoving(out value);
            if (read > 0)
            {
                ioIndex += read;
                available -= read;
                position += read;
                return value;
            }
            throw EoF(this);
        }

        private System.Collections.Generic.Dictionary<string, string> stringInterner;
        private string Intern(string value)
        {
            if (value == null) return null;
            if (value.Length == 0) return "";
            string found;
            if (stringInterner == null)
            {
                stringInterner = new System.Collections.Generic.Dictionary<string, string>();
                stringInterner.Add(value, value);
            }
            else if (stringInterner.TryGetValue(value, out found))
            {
                value = found;
            }
            else
            {
                stringInterner.Add(value, value);
            }
            return value;
        }

        static readonly UTF8Encoding encoding = new UTF8Encoding();

        public decimal ReadDecimal()
        {
            var scale = this.ReadInt32(WireType.Fixed32);
            if (scale > 28)
            {
                throw new ArgumentException("Out of decimal range, use double instead");
            }

            size -= 4;
            if (available < size) Ensure(size.Value, true);
            position += size.Value;
            available -= size.Value;

            var integer = 0;
            for (var i = 0; i < size.Value; i++)
            {
                integer = (integer << 8) + ioBuffer[ioIndex++];
            }


            // var integer = (decimal)bytes.ToInt64();
            var multiplier = 1;
            for (int i = 0; i < scale; i++)
            {
                multiplier /= 10;
            }

            var result = integer * multiplier;
            return result;
        }

        /// <summary>
        /// Reads a string from the stream (using UTF8); supported wire-types: String
        /// </summary>
        public string ReadString()
        {
            if (wireType == WireType.String)
            {
                int bytes = size ?? this.ReadInt32(WireType.Fixed16);
                if (bytes == 0) return "";
                if (available < bytes) Ensure(bytes, true);

                string s = encoding.GetString(ioBuffer, ioIndex, bytes);
                if (internStrings) { s = Intern(s); }
                available -= bytes;
                position += bytes;
                ioIndex += bytes;
                return s;
            }
            throw CreateWireTypeException();
        }
        /// <summary>
        /// Throws an exception indication that the given value cannot be mapped to an enum.
        /// </summary>
        public void ThrowEnumException(System.Type type, int value)
        {
            string desc = type == null ? "<null>" : type.FullName;
            throw AddErrorData(new ProtoException("No " + desc + " enum is mapped to the wire-value " + value), this);
        }
        private Exception CreateWireTypeException()
        {
            return CreateException("Invalid wire-type; this usually means you have over-written a file without truncating or setting the length; see http://stackoverflow.com/q/2152978/23354");
        }
        private Exception CreateException(string message)
        {
            return AddErrorData(new ProtoException(message), this);
        }
        /// <summary>
        /// Reads a double-precision number from the stream; supported wire-types: Fixed32, Fixed64
        /// </summary>
        public
#if !FEAT_SAFE
 unsafe
#endif
 double ReadDouble()
        {
            switch (wireType)
            {
                case WireType.Fixed32:
                    return ReadSingle();
                case WireType.Fixed64:
                    long value = ReadInt64();
                    return *(double*)&value;
                default:
                    throw CreateWireTypeException();
            }
        }

        /// <summary>
        /// Reads (merges) a sub-message from the stream, internally calling StartSubItem and EndSubItem, and (in between)
        /// parsing the message in accordance with the model associated with the reader
        /// </summary>
        public static object ReadObject(object value, int key, ProtoReader reader)
        {
#if FEAT_IKVM
            throw new NotSupportedException();
#else
            return ReadTypedObject(value, key, reader, null);
#endif
        }
#if !FEAT_IKVM
        internal static object ReadTypedObject(object value, int key, ProtoReader reader, Type type)
        {
            if (reader.model == null)
            {
                throw AddErrorData(new InvalidOperationException("Cannot deserialize sub-objects unless a model is provided"), reader);
            }
            SubItemToken token = ProtoReader.StartSubItem(reader);
            if (key >= 0)
            {
                value = reader.model.Deserialize(key, value, reader);
            }
            else if (type != null && reader.model.TryDeserializeAuxiliaryType(reader, DataFormat.Default, Serializer.ListItemTag, type, ref value, true, false, true, false))
            {
                // ok
            }
            else
            {
                TypeModel.ThrowUnexpectedType(type);
            }
            ProtoReader.EndSubItem(token, reader);
            return value;
        }
#endif

        /// <summary>
        /// Makes the end of consuming a nested message in the stream; the stream must be either at the correct EndGroup
        /// marker, or all fields of the sub-message must have been consumed (in either case, this means ReadFieldHeader
        /// should return zero)
        /// </summary>
        public static void EndSubItem(SubItemToken token, ProtoReader reader)
        {
            int value = token.value;
            switch (reader.wireType)
            {
                case WireType.EndGroup:
                    if (value >= 0) throw AddErrorData(new ArgumentException("token"), reader);
                    if (-value != reader.fieldNumber) throw reader.CreateException("Wrong group was ended"); // wrong group ended!
                    reader.wireType = WireType.None; // this releases ReadFieldHeader
                    reader.depth--;
                    break;
                // case WireType.None: // TODO reinstate once reads reset the wire-type
                default:
                    if (value < reader.position) throw reader.CreateException("Sub-message not read entirely");
                    if (reader.blockEnd != reader.position && reader.blockEnd != int.MaxValue)
                    {
                        throw reader.CreateException("Sub-message not read correctly");
                    }
                    reader.blockEnd = value;
                    reader.depth--;
                    break;
                /*default:
                    throw reader.BorkedIt(); */
            }
        }

        /// <summary>
        /// Begins consuming a nested message in the stream; supported wire-types: StartGroup, String
        /// </summary>
        /// <remarks>The token returned must be help and used when callining EndSubItem</remarks>
        public static SubItemToken StartSubItem(ProtoReader reader)
        {
            switch (reader.wireType)
            {
                case WireType.StartGroup:
                    reader.wireType = WireType.None; // to prevent glitches from double-calling
                    reader.depth++;
                    return new SubItemToken(-reader.fieldNumber);
                case WireType.String:
                    int len = reader.size ?? (int)reader.ReadUInt32Variant(false);
                    if (len < 0) throw AddErrorData(new InvalidOperationException(), reader);
                    int lastEnd = reader.blockEnd;
                    reader.blockEnd = reader.position + len;
                    reader.depth++;
                    return new SubItemToken(lastEnd);
                default:
                    throw reader.CreateWireTypeException(); // throws
            }
        }

        int depth = 0, blockEnd = int.MaxValue;
        /// <summary>
        /// Reads a field header from the stream, setting the wire-type and retuning the field number. If no
        /// more fields are available, then 0 is returned. This methods respects sub-messages.
        /// </summary>
        public int ReadFieldHeader()
        {
            // at the end of a group the caller must call EndSubItem to release the
            // reader (which moves the status to Error, since ReadFieldHeader must
            // then be called)
            if (blockEnd <= position || wireType == WireType.EndGroup) { return 0; }
            uint tag;
            if (available < 4)
            {
                Ensure(4, false);
            }

            if (available > 3)
            {
                if (fieldNumber == members.Length)
                {
                    fieldNumber = 1;
                }
                else
                {
                    fieldNumber++;
                }

                size = this.ReadInt32(WireType.Fixed32);
                var member = members[this.fieldNumber - 1];
                var type = member.MemberType == MemberTypes.Field ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;
                ProtoTypeCode typecode = Helpers.GetTypeCode(type);
                wireType = TypeModel.GetWireType(typecode, DataFormat.FixedSize, ref type);
            }
            else
            {
                wireType = WireType.None;
                fieldNumber = 0;
            }

            // watch for end-of-group
            return wireType == WireType.EndGroup ? 0 : fieldNumber;
        }

        /// <summary>
        /// Looks ahead to see whether the next field in the stream is what we expect
        /// (typically; what we've just finished reading - for example ot read successive list items)
        /// </summary>
        public bool TryReadFieldHeader(int field)
        {
            // check for virtual end of stream
            if (blockEnd <= position || wireType == WireType.EndGroup) { return false; }
            uint tag;
            int read = TryReadUInt32VariantWithoutMoving(false, out tag);
            WireType tmpWireType; // need to catch this to exclude (early) any "end group" tokens
            if (read > 0 && ((int)tag >> 3) == field
                && (tmpWireType = (WireType)(tag & 7)) != WireType.EndGroup)
            {
                wireType = tmpWireType;
                fieldNumber = field;
                position += read;
                ioIndex += read;
                available -= read;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get the TypeModel associated with this reader
        /// </summary>
        public TypeModel Model { get { return model; } }

        /// <summary>
        /// Compares the streams current wire-type to the hinted wire-type, updating the reader if necessary; for example,
        /// a Variant may be updated to SignedVariant. If the hinted wire-type is unrelated then no change is made.
        /// </summary>
        public void Hint(WireType wireType)
        {
            if (this.wireType == wireType) { }  // fine; everything as we expect
            else if (((int)wireType & 7) == (int)this.wireType)
            {   // the underling type is a match; we're customising it with an extension
                this.wireType = wireType;
            }
            // note no error here; we're OK about using alternative data
        }

        /// <summary>
        /// Verifies that the stream's current wire-type is as expected, or a specialized sub-type (for example,
        /// SignedVariant) - in which case the current wire-type is updated. Otherwise an exception is thrown.
        /// </summary>
        public void Assert(WireType wireType)
        {
            if (this.wireType == wireType) { }  // fine; everything as we expect
            else if (((int)wireType & 7) == (int)this.wireType)
            {   // the underling type is a match; we're customising it with an extension
                this.wireType = wireType;
            }
            else
            {   // nope; that is *not* what we were expecting!
                throw CreateWireTypeException();
            }
        }

        /// <summary>
        /// Discards the data for the current field.
        /// </summary>
        public void SkipField()
        {
            switch (wireType)
            {
                case WireType.Fixed32:
                    if (available < 4) Ensure(4, true);
                    available -= 4;
                    ioIndex += 4;
                    position += 4;
                    return;
                case WireType.Fixed64:
                    if (available < 8) Ensure(8, true);
                    available -= 8;
                    ioIndex += 8;
                    position += 8;
                    return;
                case WireType.String:
                    int len = (int)ReadUInt32Variant(false);
                    if (len <= available)
                    { // just jump it!
                        available -= len;
                        ioIndex += len;
                        position += len;
                        return;
                    }
                    // everything remaining in the buffer is garbage
                    position += len; // assumes success, but if it fails we're screwed anyway
                    len -= available; // discount anything we've got to-hand
                    ioIndex = available = 0; // note that we have no data in the buffer
                    if (isFixedLength)
                    {
                        if (len > dataRemaining) throw EoF(this);
                        // else assume we're going to be OK
                        dataRemaining -= len;
                    }
                    ProtoReader.Seek(source, len, ioBuffer);
                    return;
                case WireType.Variant:
                case WireType.SignedVariant:
                    ReadUInt64Variant(); // and drop it
                    return;
                case WireType.StartGroup:
                    int originalFieldNumber = this.fieldNumber;
                    while (ReadFieldHeader() > 0) { SkipField(); }
                    if (wireType == WireType.EndGroup && fieldNumber == originalFieldNumber)
                    { // we expect to exit in a similar state to how we entered
                        wireType = ProtoBuf.WireType.None;
                        return;
                    }
                    throw CreateWireTypeException();
                case WireType.None: // treat as explicit errorr
                case WireType.EndGroup: // treat as explicit error
                default: // treat as implicit error
                    throw CreateWireTypeException();
            }
        }

        /// <summary>
        /// Reads an unsigned 64-bit integer from the stream; supported wire-types: Variant, Fixed32, Fixed64
        /// </summary>
        public ulong ReadUInt64()
        {
            switch (wireType)
            {
                case WireType.Variant:
                    return ReadUInt64Variant();
                case WireType.Fixed32:
                    return ReadUInt32();
                case WireType.Fixed64:
                    if (available < 8) Ensure(8, true);
                    position += 8;
                    available -= 8;

                    return ((ulong)ioBuffer[ioIndex++])
                        | (((ulong)ioBuffer[ioIndex++]) << 8)
                        | (((ulong)ioBuffer[ioIndex++]) << 16)
                        | (((ulong)ioBuffer[ioIndex++]) << 24)
                        | (((ulong)ioBuffer[ioIndex++]) << 32)
                        | (((ulong)ioBuffer[ioIndex++]) << 40)
                        | (((ulong)ioBuffer[ioIndex++]) << 48)
                        | (((ulong)ioBuffer[ioIndex++]) << 56);
                default:
                    throw CreateWireTypeException();
            }
        }
        /// <summary>
        /// Reads a single-precision number from the stream; supported wire-types: Fixed32, Fixed64
        /// </summary>
        public unsafe float ReadSingle()
        {
            switch (wireType)
            {
                case WireType.Fixed32:
                    {
                        int value = ReadInt32();
#if FEAT_SAFE
                        return BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
#else
                        return *(float*)&value;
#endif
                    }
                case WireType.Fixed64:
                    {
                        double value = ReadDouble();
                        float f = (float)value;
                        if (Helpers.IsInfinity(f)
                            && !Helpers.IsInfinity(value))
                        {
                            throw AddErrorData(new OverflowException(), this);
                        }
                        return f;
                    }
                default:
                    throw CreateWireTypeException();
            }
        }

        /// <summary>
        /// Reads a boolean value from the stream; supported wire-types: Variant, Fixed32, Fixed64
        /// </summary>
        /// <returns></returns>
        public bool ReadBoolean()
        {
            var value = this.ReadByte();
            switch (value)
            {
                case 0: return false;
                case 1: return true;
                default: throw CreateException("Unexpected boolean value");
            }
        }

        private static readonly byte[] EmptyBlob = new byte[0];
        /// <summary>
        /// Reads a byte-sequence from the stream, appending them to an existing byte-sequence (which can be null); supported wire-types: String
        /// </summary>
        public static byte[] AppendBytes(byte[] value, ProtoReader reader)
        {
            switch (reader.wireType)
            {
                case WireType.String:
                    int len = (int)reader.ReadUInt32Variant(false);
                    reader.wireType = WireType.None;
                    if (len == 0) return value == null ? EmptyBlob : value;
                    int offset;
                    if (value == null || value.Length == 0)
                    {
                        offset = 0;
                        value = new byte[len];
                    }
                    else
                    {
                        offset = value.Length;
                        byte[] tmp = new byte[value.Length + len];
                        Helpers.BlockCopy(value, 0, tmp, 0, value.Length);
                        value = tmp;
                    }
                    // value is now sized with the final length, and (if necessary)
                    // contains the old data up to "offset"
                    reader.position += len; // assume success
                    while (len > reader.available)
                    {
                        if (reader.available > 0)
                        {
                            // copy what we *do* have
                            Helpers.BlockCopy(reader.ioBuffer, reader.ioIndex, value, offset, reader.available);
                            len -= reader.available;
                            offset += reader.available;
                            reader.ioIndex = reader.available = 0; // we've drained the buffer
                        }
                        //  now refill the buffer (without overflowing it)
                        int count = len > reader.ioBuffer.Length ? reader.ioBuffer.Length : len;
                        if (count > 0) reader.Ensure(count, true);
                    }
                    // at this point, we know that len <= available
                    if (len > 0)
                    {   // still need data, but we have enough buffered
                        Helpers.BlockCopy(reader.ioBuffer, reader.ioIndex, value, offset, len);
                        reader.ioIndex += len;
                        reader.available -= len;
                    }
                    return value;
                default:
                    throw reader.CreateWireTypeException();
            }
        }

        internal static void Seek(Stream source, int count, byte[] buffer)
        {
            if (source.CanSeek)
            {
                source.Seek(count, SeekOrigin.Current);
                count = 0;
            }
            else if (buffer != null)
            {
                int bytesRead;
                while (count > buffer.Length && (bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    count -= bytesRead;
                }
                while (count > 0 && (bytesRead = source.Read(buffer, 0, count)) > 0)
                {
                    count -= bytesRead;
                }
            }
            else // borrow a buffer
            {
                buffer = BufferPool.GetBuffer();
                try
                {
                    int bytesRead;
                    while (count > buffer.Length && (bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        count -= bytesRead;
                    }
                    while (count > 0 && (bytesRead = source.Read(buffer, 0, count)) > 0)
                    {
                        count -= bytesRead;
                    }
                }
                finally
                {
                    BufferPool.ReleaseBufferToPool(ref buffer);
                }
            }
            if (count > 0) throw EoF(null);
        }
        internal static Exception AddErrorData(Exception exception, ProtoReader source)
        {
#if !CF && !FX11 && !PORTABLE
            if (exception != null && source != null && !exception.Data.Contains("protoSource"))
            {
                exception.Data.Add("protoSource", string.Format("tag={0}; wire-type={1}; offset={2}; depth={3}",
                    source.fieldNumber, source.wireType, source.position, source.depth));
            }
#endif
            return exception;

        }
        private static Exception EoF(ProtoReader source)
        {
            return AddErrorData(new EndOfStreamException(), source);
        }

        /// <summary>
        /// Indicates whether the reader still has data remaining in the current sub-item,
        /// additionally setting the wire-type for the next field if there is more data.
        /// This is used when decoding packed data.
        /// </summary>
        public static bool HasSubValue(ProtoBuf.WireType wireType, ProtoReader source)
        {
            // check for virtual end of stream
            if (source.blockEnd <= source.position || wireType == WireType.EndGroup) { return false; }
            source.wireType = wireType;
            return true;
        }

        internal int GetTypeKey(ref Type type)
        {
            return model.GetKey(ref type);
        }

        private readonly NetObjectCache netCache = new NetObjectCache();
        internal NetObjectCache NetCache
        {
            get { return netCache; }
        }

        internal System.Type DeserializeType(string value)
        {
            return TypeModel.DeserializeType(model, value);
        }

        internal void SetRootObject(object value)
        {
            netCache.SetKeyedObject(NetObjectCache.Root, value);
            trapCount--;
        }

        // this is how many outstanding objects do not currently have
        // values for the purposes of reference tracking; we'll default
        // to just trapping the root object
        // note: objects are trapped (the ref and key mapped) via NoteObject
        uint trapCount = 1; // uint is so we can use beq/bne more efficiently than bgt

        /// <summary>
        /// Utility method, not intended for public use; this helps maintain the root object is complex scenarios
        /// </summary>
        public static void NoteObject(object value, ProtoReader reader)
        {
            if (reader.trapCount != 0)
            {
                reader.netCache.RegisterTrappedObject(value);
                reader.trapCount--;
            }
        }

        /// <summary>
        /// Reads a Type from the stream, using the model's DynamicTypeFormatting if appropriate; supported wire-types: String
        /// </summary>
        public System.Type ReadType()
        {
            return TypeModel.DeserializeType(model, ReadString());
        }

        internal void TrapNextObject(int newObjectKey)
        {
            trapCount++;
            netCache.SetKeyedObject(newObjectKey, null); // use null as a temp
        }

        internal void CheckFullyConsumed()
        {
            if (isFixedLength && dataRemaining != 0)
            {
                throw new ProtoException("Incorrect number of bytes consumed");
            }
        }
    }
}
