#if !NO_RUNTIME
using System;

namespace ProtoBuf.Serializers
{
    sealed class Int32Serializer : IProtoSerializer
    {
#if FEAT_IKVM
        readonly Type expectedType;
#else
        static readonly Type expectedType = typeof(int);
#endif
        public Int32Serializer(ProtoBuf.Meta.TypeModel model)
        {
        }
        public Type ExpectedType { get { return expectedType; } }

        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }

        public object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // since replaces
            return source.ReadInt32();
        }
#if FEAT_COMPILER
        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead("ReadInt32", ExpectedType);
        }
#endif

    }
}
#endif