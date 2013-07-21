#if !NO_RUNTIME
using System;
using ProtoBuf.Meta;

namespace ProtoBuf.Serializers
{
    sealed class DecimalSerializer : IProtoSerializer
    {
        static readonly Type expectedType = typeof(decimal);
        public DecimalSerializer(ProtoBuf.Meta.TypeModel model)
        {
        }

        public Type ExpectedType { get { return expectedType; } }

        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }
        public object Read(object value, ProtoReader source)
        {
            return source.ReadDecimal();
        }

#if FEAT_COMPILER

        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead(ctx.MapType(typeof(BclHelpers)), "ReadDecimal", ExpectedType);
        }
#endif

    }
}
#endif