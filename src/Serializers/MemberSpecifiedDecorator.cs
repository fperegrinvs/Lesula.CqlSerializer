#if !NO_RUNTIME
using System;
using ProtoBuf.Meta;

#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
#endif



namespace ProtoBuf.Serializers
{
    sealed class MemberSpecifiedDecorator : ProtoDecoratorBase
    {

        public override Type ExpectedType { get { return Tail.ExpectedType; } }
        public override bool RequiresOldValue { get { return Tail.RequiresOldValue; } }
        public override bool ReturnsValue { get { return Tail.ReturnsValue; } }
        private readonly MethodInfo getSpecified, setSpecified;
        public MemberSpecifiedDecorator(MethodInfo getSpecified, MethodInfo setSpecified, IProtoSerializer tail)
            : base(tail)
        {
            if (getSpecified == null && setSpecified == null) throw new InvalidOperationException();
            this.getSpecified = getSpecified;
            this.setSpecified = setSpecified;
        }
#if !FEAT_IKVM
        public override object Read(object value, ProtoReader source)
        {
            object result = Tail.Read(value, source);
            if (setSpecified != null) setSpecified.Invoke(value, new object[] { true });
            return result;
        }
#endif

#if FEAT_COMPILER
        protected override void EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            if (setSpecified == null)
            {
                Tail.EmitRead(ctx, valueFrom);
                return;
            }
            using (Compiler.Local loc = ctx.GetLocalWithValue(ExpectedType, valueFrom))
            {
                Tail.EmitRead(ctx, loc);
                ctx.LoadAddress(loc, ExpectedType);
                ctx.LoadValue(1); // true
                ctx.EmitCall(setSpecified);
            }
        }
#endif
    }
}
#endif