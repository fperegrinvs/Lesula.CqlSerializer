
using System;


namespace ProtoBuf
{
    /// <summary>
    /// Provides protocol-buffer serialization capability for concrete, attributed types. This
    /// is a *default* model, but custom serializer models are also supported.
    /// </summary>
    /// <remarks>
    /// Protocol-buffer serialization is a compact binary format, designed to take
    /// advantage of sparse data and knowledge of specific data types; it is also
    /// extensible, allowing a type to be deserialized / merged even if some data is
    /// not recognised.
    /// </remarks>
    internal static class Serializer
    {
        /// <summary>
        /// The field number that is used as a default when serializing/deserializing a list of objects.
        /// The data is treated as repeated message with field number 1.
        /// </summary>
        public const int ListItemTag = 1;
    }
}
