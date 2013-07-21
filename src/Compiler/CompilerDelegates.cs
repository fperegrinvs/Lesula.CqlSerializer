#if FEAT_COMPILER
namespace ProtoBuf.Compiler
{
    internal delegate object ProtoDeserializer(object value, ProtoReader source);
}
#endif