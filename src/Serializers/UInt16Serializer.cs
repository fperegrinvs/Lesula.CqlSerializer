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
    class UInt16Serializer : IProtoSerializer
    {
#if FEAT_IKVM
        readonly Type expectedType;
#else
        static readonly Type expectedType = typeof(ushort);
#endif
        public UInt16Serializer(ProtoBuf.Meta.TypeModel model)
        {
#if FEAT_IKVM
            expectedType = model.MapType(typeof(ushort));
#endif
        }
        public virtual Type ExpectedType { get { return expectedType; } }

        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }
#if !FEAT_IKVM
        public virtual object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // since replaces
            return source.ReadUInt16();
        }
#endif
#if FEAT_COMPILER
        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead("ReadUInt16", ctx.MapType(typeof(ushort)));
        }
#endif
    }
}
#endif