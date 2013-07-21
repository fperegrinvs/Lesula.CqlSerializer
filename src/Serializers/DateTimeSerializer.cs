#if !NO_RUNTIME
using System;

#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
#endif

namespace ProtoBuf.Serializers
{
    sealed class DateTimeSerializer : IProtoSerializer
    {

        static readonly Type expectedType = typeof(DateTime);
        public Type ExpectedType { get { return expectedType; } }

        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }

        public DateTimeSerializer(ProtoBuf.Meta.TypeModel model)
        {
        }

        public object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // since replaces
            return BclHelpers.ReadDateTime(source);
        }

#if FEAT_COMPILER
        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead(ctx.MapType(typeof(BclHelpers)), "ReadDateTime", ExpectedType);
        }
#endif

    }
}
#endif