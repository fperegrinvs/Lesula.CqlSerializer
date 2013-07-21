#if !NO_RUNTIME
using System;

using ProtoBuf.Meta;
using System.Reflection;

namespace ProtoBuf.Serializers
{
    sealed class StringSerializer : IProtoSerializer
    {
#if FEAT_IKVM
        readonly Type expectedType;
#else
        static readonly Type expectedType = typeof(string);
#endif
        public StringSerializer(ProtoBuf.Meta.TypeModel model)
        {
#if FEAT_IKVM
            expectedType = model.MapType(typeof(string));
#endif
        }
        public Type ExpectedType { get { return expectedType; } }
        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }

        public object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // since replaces
            return source.ReadString();
        }

#if FEAT_COMPILER
        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead("ReadString", ExpectedType);
        }
#endif
    }
}
#endif