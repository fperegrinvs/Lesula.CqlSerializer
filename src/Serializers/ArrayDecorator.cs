﻿#if !NO_RUNTIME
using System;
using System.Collections;
using ProtoBuf.Meta;

#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
#endif

namespace ProtoBuf.Serializers
{
    sealed class ArrayDecorator : ProtoDecoratorBase
    {

        private readonly int fieldNumber;
        private const byte
                   OPTIONS_WritePacked = 1,
                   OPTIONS_OverwriteList = 2,
                   OPTIONS_SupportNull = 4;
        private readonly byte options;
        private readonly WireType packedWireType;
        public ArrayDecorator(TypeModel model, IProtoSerializer tail, int fieldNumber, bool writePacked, WireType packedWireType, Type arrayType, bool overwriteList, bool supportNull)
            : base(tail)
        {
            Helpers.DebugAssert(arrayType != null, "arrayType should be non-null");
            Helpers.DebugAssert(arrayType.IsArray && arrayType.GetArrayRank() == 1, "should be single-dimension array; " + arrayType.FullName);
            this.itemType = arrayType.GetElementType();
#if NO_GENERICS
            Type underlyingItemType = itemType;
#else
            Type underlyingItemType = supportNull ? itemType : (Helpers.GetUnderlyingType(itemType) ?? itemType);
#endif

            Helpers.DebugAssert(underlyingItemType == Tail.ExpectedType, "invalid tail");
            Helpers.DebugAssert(Tail.ExpectedType != model.MapType(typeof(byte)), "Should have used BlobSerializer");
            if ((writePacked || packedWireType != WireType.None) && fieldNumber <= 0) throw new ArgumentOutOfRangeException("fieldNumber");
            if (!ListDecorator.CanPack(packedWireType))
            {
                if (writePacked) throw new InvalidOperationException("Only simple data-types can use packed encoding");
                packedWireType = WireType.None;
            }       
            this.fieldNumber = fieldNumber;
            this.packedWireType = packedWireType;
            if (writePacked) options |= OPTIONS_WritePacked;
            if (overwriteList) options |= OPTIONS_OverwriteList;
            if (supportNull) options |= OPTIONS_SupportNull;
            this.arrayType = arrayType;
        }
        readonly Type arrayType, itemType; // this is, for example, typeof(int[])
        public override Type ExpectedType { get { return arrayType; } }
        public override bool RequiresOldValue { get { return AppendToCollection; } }
        public override bool ReturnsValue { get { return true; } }

        private bool AppendToCollection
        {
            get { return (options & OPTIONS_OverwriteList) == 0; }
        }
        private bool SupportNull { get { return (options & OPTIONS_SupportNull) != 0; } }

#if !FEAT_IKVM
        public override object Read(object value, ProtoReader source)
        {
            int field = source.FieldNumber;
            BasicList list = new BasicList();
            if (packedWireType != WireType.None && source.WireType == WireType.String)
            {
                SubItemToken token = ProtoReader.StartSubItem(source);
                while (ProtoReader.HasSubValue(packedWireType, source))
                {
                    list.Add(Tail.Read(null, source));
                }
                ProtoReader.EndSubItem(token, source);
            }
            else
            { 
                do
                {
                    list.Add(Tail.Read(null, source));
                } while (source.TryReadFieldHeader(field));
            }
            int oldLen = AppendToCollection ? ((value == null ? 0 : ((Array)value).Length)) : 0;
            Array result = Array.CreateInstance(itemType, oldLen + list.Count);
            if (oldLen != 0) ((Array)value).CopyTo(result, 0);
            list.CopyTo(result, oldLen);
            return result;
        }
#endif

#if FEAT_COMPILER
        protected override void EmitRead(ProtoBuf.Compiler.CompilerContext ctx, ProtoBuf.Compiler.Local valueFrom)
        {
            Type listType;

            listType = ctx.MapType(typeof(System.Collections.Generic.List<>)).MakeGenericType(itemType);
            using (Compiler.Local oldArr = AppendToCollection ? ctx.GetLocalWithValue(ExpectedType, valueFrom) : null)
            using (Compiler.Local newArr = new Compiler.Local(ctx, ExpectedType))
            using (Compiler.Local list = new Compiler.Local(ctx, listType))
            {
                ctx.EmitCtor(listType);
                ctx.StoreValue(list);
                ListDecorator.EmitReadList(ctx, list, Tail, listType.GetMethod("Add"), packedWireType);

                // leave this "using" here, as it can share the "FieldNumber" local with EmitReadList
                using(Compiler.Local oldLen = AppendToCollection ? new ProtoBuf.Compiler.Local(ctx, ctx.MapType(typeof(int))) : null) {
                    Type[] copyToArrayInt32Args = new Type[] { ctx.MapType(typeof(Array)), ctx.MapType(typeof(int)) };

                    if (AppendToCollection)
                    {
                        ctx.LoadLength(oldArr, true);
                        ctx.CopyValue();
                        ctx.StoreValue(oldLen);

                        ctx.LoadAddress(list, listType);
                        ctx.LoadValue(listType.GetProperty("Count"));
                        ctx.Add();
                        ctx.CreateArray(itemType, null); // length is on the stack
                        ctx.StoreValue(newArr);

                        ctx.LoadValue(oldLen);
                        Compiler.CodeLabel nothingToCopy = ctx.DefineLabel();
                        ctx.BranchIfFalse(nothingToCopy, true);
                        ctx.LoadValue(oldArr);
                        ctx.LoadValue(newArr);
                        ctx.LoadValue(0); // index in target

                        ctx.EmitCall(ExpectedType.GetMethod("CopyTo", copyToArrayInt32Args));
                        ctx.MarkLabel(nothingToCopy);

                        ctx.LoadValue(list);
                        ctx.LoadValue(newArr);
                        ctx.LoadValue(oldLen);
                        
                    }
                    else
                    {
                        ctx.LoadAddress(list, listType);
                        ctx.LoadValue(listType.GetProperty("Count"));
                        ctx.CreateArray(itemType, null);
                        ctx.StoreValue(newArr);

                        ctx.LoadAddress(list, listType);
                        ctx.LoadValue(newArr);
                        ctx.LoadValue(0);
                    }

                    copyToArrayInt32Args[0] = ExpectedType; // // prefer: CopyTo(T[], int)
                    MethodInfo copyTo = listType.GetMethod("CopyTo", copyToArrayInt32Args);
                    if (copyTo == null)
                    { // fallback: CopyTo(Array, int)
                        copyToArrayInt32Args[1] = ctx.MapType(typeof(Array));
                        copyTo = listType.GetMethod("CopyTo", copyToArrayInt32Args);
                    }
                    ctx.EmitCall(copyTo);
                }
                ctx.LoadValue(newArr);
            }


        }
#endif
    }
}
#endif