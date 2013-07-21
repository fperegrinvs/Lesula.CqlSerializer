#if !NO_RUNTIME
using System;

namespace ProtoBuf.Serializers
{
    sealed class ByteSerializer : IProtoSerializer
    {
        public Type ExpectedType { get { return expectedType; } }


        static readonly Type expectedType = typeof(byte);
        public ByteSerializer(ProtoBuf.Meta.TypeModel model)
        {
        }
        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }
#if !FEAT_IKVM
        public object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // since replaces
            return source.ReadByte();
        }
#endif

#if FEAT_COMPILER
        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead("ReadByte", ExpectedType);
        }
#endif

    }
}
#endif