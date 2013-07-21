using System;
using System.IO;

using System.Collections;
using System.Reflection;

namespace ProtoBuf.Meta
{
    /// <summary>
    /// Provides protobuf serialization support for a number of types
    /// </summary>
    internal abstract class TypeModel
    {
        /// <summary>
        /// Resolve a System.Type to the compiler-specific type
        /// </summary>
        protected internal Type MapType(System.Type type)
        {
            return MapType(type, true);
        }
        /// <summary>
        /// Resolve a System.Type to the compiler-specific type
        /// </summary>
        protected internal virtual Type MapType(System.Type type, bool demand)
        {
            return type;
        }

        internal static WireType GetWireType(ProtoTypeCode code, DataFormat format, ref Type type)
        {
            if (Helpers.IsEnum(type))
            {
                return WireType.Variant;
            }

            switch (code)
            {
                case ProtoTypeCode.Int64:
                case ProtoTypeCode.UInt64:
                    return format == DataFormat.FixedSize ? WireType.Fixed64 : WireType.Variant;
                case ProtoTypeCode.Int16:
                case ProtoTypeCode.Int32:
                case ProtoTypeCode.UInt16:
                case ProtoTypeCode.UInt32:
                    return format == DataFormat.FixedSize ? WireType.Fixed32 : WireType.Variant;
                case ProtoTypeCode.Boolean:
                case ProtoTypeCode.SByte:
                case ProtoTypeCode.Byte:
                case ProtoTypeCode.Char:
                    return WireType.Fixed8;
                case ProtoTypeCode.Double:
                case ProtoTypeCode.DateTime:
                    return WireType.Fixed64;
                case ProtoTypeCode.Single:
                    return WireType.Fixed32;
                case ProtoTypeCode.String:
                case ProtoTypeCode.Decimal:
                case ProtoTypeCode.ByteArray:
                case ProtoTypeCode.TimeSpan:
                case ProtoTypeCode.Guid:
                case ProtoTypeCode.Uri:
                    return WireType.String;
            }

            return WireType.None;
        }

        private WireType GetWireType(ProtoTypeCode code, DataFormat format, ref Type type, out int modelKey)
        {
            modelKey = -1;
            if (Helpers.IsEnum(type))
            {
                modelKey = GetKey(ref type);
                return WireType.Variant;
            }
            switch (code)
            {
                case ProtoTypeCode.Int64:
                case ProtoTypeCode.UInt64:
                    return format == DataFormat.FixedSize ? WireType.Fixed64 : WireType.Variant;
                case ProtoTypeCode.Int16:
                case ProtoTypeCode.Int32:
                case ProtoTypeCode.UInt16:
                case ProtoTypeCode.UInt32:
                case ProtoTypeCode.Boolean:
                case ProtoTypeCode.SByte:
                case ProtoTypeCode.Byte:
                case ProtoTypeCode.Char:
                    return format == DataFormat.FixedSize ? WireType.Fixed32 : WireType.Variant;
                case ProtoTypeCode.Double:
                    return WireType.Fixed64;
                case ProtoTypeCode.Single:
                    return WireType.Fixed32;
                case ProtoTypeCode.String:
                case ProtoTypeCode.DateTime:
                case ProtoTypeCode.Decimal:
                case ProtoTypeCode.ByteArray:
                case ProtoTypeCode.TimeSpan:
                case ProtoTypeCode.Guid:
                case ProtoTypeCode.Uri:
                    return WireType.String;
            }

            if ((modelKey = GetKey(ref type)) >= 0)
            {
                return WireType.String;
            }
            return WireType.None;
        }

        /// <summary>
        /// Applies a protocol-buffer stream to an existing instance (which may be null).
        /// </summary>
        /// <param name="type">The type (including inheritance) to consider.</param>
        /// <param name="value">The existing instance to be modified (can be null).</param>
        /// <param name="source">The binary stream to apply to the instance (cannot be null).</param>
        /// <returns>The updated instance; this may be different to the instance argument if
        /// either the original instance was null, or the stream defines a known sub-type of the
        /// original instance.</returns>
        /// <param name="context">Additional information about this serialization operation.</param>
        public object Deserialize(Stream source, object value, System.Type type, SerializationContext context, MemberInfo[] members = null)
        {
            bool autoCreate = PrepareDeserialize(value, ref type);
            using (ProtoReader reader = new ProtoReader(source, this, context, members))
            {
                if (value != null) reader.SetRootObject(value);
                return DeserializeCore(reader, type, value, autoCreate);
            }
        }

        private bool PrepareDeserialize(object value, ref Type type)
        {
            if (type == null)
            {
                if (value == null)
                {
                    throw new ArgumentNullException("type");
                }

                type = this.MapType(value.GetType());
            }
            bool autoCreate = true;
            Type underlyingType = Helpers.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                type = underlyingType;
                autoCreate = false;
            }

            return autoCreate;
        }

#if !FEAT_IKVM
        private object DeserializeCore(ProtoReader reader, Type type, object value, bool noAutoCreate)
        {
            int key = GetKey(ref type);
            if (key >= 0 && !Helpers.IsEnum(type))
            {
                return Deserialize(key, value, reader);
            }
            // this returns true to say we actively found something, but a value is assigned either way (or throws)
            TryDeserializeAuxiliaryType(reader, DataFormat.Default, Serializer.ListItemTag, type, ref value, true, false, noAutoCreate, false);
            return value;
        }
#endif

        private static readonly System.Type ilist = typeof(IList);
        internal static MethodInfo ResolveListAdd(TypeModel model, Type listType, Type itemType, out bool isList)
        {

            Type listTypeInfo = listType;
            isList = model.MapType(ilist).IsAssignableFrom(listTypeInfo);

            Type[] types = { itemType };
            MethodInfo add = Helpers.GetInstanceMethod(listTypeInfo, "Add", types);
#if !NO_GENERICS
            if (add == null)
            {   // fallback: look for ICollection<T>'s Add(typedObject) method
#if WINRT
                TypeInfo constuctedListType = typeof(System.Collections.Generic.ICollection<>).MakeGenericType(types).GetTypeInfo();
#else
                Type constuctedListType = model.MapType(typeof(System.Collections.Generic.ICollection<>)).MakeGenericType(types);
#endif
                if (constuctedListType.IsAssignableFrom(listTypeInfo))
                {
                    add = Helpers.GetInstanceMethod(constuctedListType, "Add", types);
                }
            }
#endif
            if (add == null)
            {   // fallback: look for a public list.Add(object) method
                types[0] = model.MapType(typeof(object));
                add = Helpers.GetInstanceMethod(listTypeInfo, "Add", types);
            }
            if (add == null && isList)
            {   // fallback: look for IList's Add(object) method
                add = Helpers.GetInstanceMethod(model.MapType(ilist), "Add", types);
            }
            return add;
        }
        internal static Type GetListItemType(TypeModel model, Type listType)
        {
            Helpers.DebugAssert(listType != null);

#if WINRT
            TypeInfo listTypeInfo = listType.GetTypeInfo();
            if (listType == typeof(string) || listType.IsArray
                || !typeof(IEnumerable).GetTypeInfo().IsAssignableFrom(listTypeInfo)) return null;
#else
            if (listType == model.MapType(typeof(string)) || listType.IsArray
                || !model.MapType(typeof(IEnumerable)).IsAssignableFrom(listType)) return null;
#endif

            BasicList candidates = new BasicList();
#if WINRT
            foreach (MethodInfo method in listType.GetRuntimeMethods())
#else
            foreach (MethodInfo method in listType.GetMethods())
#endif
            {
                if (method.IsStatic || method.Name != "Add") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && !candidates.Contains(parameters[0].ParameterType))
                {
                    candidates.Add(parameters[0].ParameterType);
                }
            }
#if !NO_GENERICS
#if WINRT
            foreach (Type iType in listTypeInfo.ImplementedInterfaces)
            {
                TypeInfo iTypeInfo = iType.GetTypeInfo();
                if (iTypeInfo.IsGenericType && iTypeInfo.GetGenericTypeDefinition() == typeof(System.Collections.Generic.ICollection<>))
                {
                    Type[] iTypeArgs = iTypeInfo.GenericTypeArguments;
                    if (!candidates.Contains(iTypeArgs[0]))
                    {
                        candidates.Add(iTypeArgs[0]);
                    }
                }
            }
#else
            foreach (Type iType in listType.GetInterfaces())
            {
                if (iType.IsGenericType && iType.GetGenericTypeDefinition() == model.MapType(typeof(System.Collections.Generic.ICollection<>)))
                {
                    Type[] iTypeArgs = iType.GetGenericArguments();
                    if (!candidates.Contains(iTypeArgs[0]))
                    {
                        candidates.Add(iTypeArgs[0]);
                    }
                }
            }
#endif
#endif

#if WINRT
            // more convenient GetProperty overload not supported on all platforms
            foreach (PropertyInfo indexer in listType.GetRuntimeProperties())
            {
                if (indexer.Name != "Item" || candidates.Contains(indexer.PropertyType)) continue;
                ParameterInfo[] args = indexer.GetIndexParameters();
                if (args.Length != 1 || args[0].ParameterType != typeof(int)) continue;
                MethodInfo getter = indexer.GetMethod;
                if (getter == null || getter.IsStatic) continue;
                candidates.Add(indexer.PropertyType);
            }
#else
            // more convenient GetProperty overload not supported on all platforms
            foreach (PropertyInfo indexer in listType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (indexer.Name != "Item" || candidates.Contains(indexer.PropertyType)) continue;
                ParameterInfo[] args = indexer.GetIndexParameters();
                if (args.Length != 1 || args[0].ParameterType != model.MapType(typeof(int))) continue;
                candidates.Add(indexer.PropertyType);
            }
#endif

            switch (candidates.Count)
            {
                case 0:
                    return null;
                case 1:
                    return (Type)candidates[0];
                case 2:
                    if (CheckDictionaryAccessors(model, (Type)candidates[0], (Type)candidates[1])) return (Type)candidates[0];
                    if (CheckDictionaryAccessors(model, (Type)candidates[1], (Type)candidates[0])) return (Type)candidates[1];
                    break;
            }

            return null;
        }

        private static bool CheckDictionaryAccessors(TypeModel model, Type pair, Type value)
        {

#if NO_GENERICS
            return false;
#elif WINRT
            TypeInfo finalType = pair.GetTypeInfo();
            return finalType.IsGenericType && finalType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.KeyValuePair<,>)
                && finalType.GenericTypeArguments[1] == value;
#else
            return pair.IsGenericType && pair.GetGenericTypeDefinition() == model.MapType(typeof(System.Collections.Generic.KeyValuePair<,>))
                && pair.GetGenericArguments()[1] == value;
#endif
        }

#if !FEAT_IKVM
        private bool TryDeserializeList(TypeModel model, ProtoReader reader, DataFormat format, int tag, Type listType, Type itemType, ref object value)
        {
            bool isList;
            MethodInfo addMethod = TypeModel.ResolveListAdd(model, listType, itemType, out isList);
            if (addMethod == null) throw new NotSupportedException("Unknown list variant: " + listType.FullName);
            bool found = false;
            object nextItem = null;
            IList list = value as IList;
            object[] args = isList ? null : new object[1];
            BasicList arraySurrogate = listType.IsArray ? new BasicList() : null;

            while (TryDeserializeAuxiliaryType(reader, format, tag, itemType, ref nextItem, true, true, true, true))
            {
                found = true;
                if (value == null && arraySurrogate == null)
                {
                    value = CreateListInstance(listType, itemType);
                    list = value as IList;
                }
                if (list != null)
                {
                    list.Add(nextItem);
                }
                else if (arraySurrogate != null)
                {
                    arraySurrogate.Add(nextItem);
                }
                else
                {
                    args[0] = nextItem;
                    addMethod.Invoke(value, args);
                }
                nextItem = null;
            }
            if (arraySurrogate != null)
            {
                Array newArray;
                if (value != null)
                {
                    if (arraySurrogate.Count == 0)
                    {   // we'll stay with what we had, thanks
                    }
                    else
                    {
                        Array existing = (Array)value;
                        newArray = Array.CreateInstance(itemType, existing.Length + arraySurrogate.Count);
                        Array.Copy(existing, newArray, existing.Length);
                        arraySurrogate.CopyTo(newArray, existing.Length);
                        value = newArray;
                    }
                }
                else
                {
                    newArray = Array.CreateInstance(itemType, arraySurrogate.Count);
                    arraySurrogate.CopyTo(newArray, 0);
                    value = newArray;
                }
            }
            return found;
        }

        private static object CreateListInstance(Type listType, Type itemType)
        {
            Type concreteListType = listType;

            if (listType.IsArray)
            {
                return Array.CreateInstance(itemType, 0);
            }

#if WINRT
            TypeInfo listTypeInfo = listType.GetTypeInfo();
            if (!listTypeInfo.IsClass || listTypeInfo.IsAbstract ||
                Helpers.GetConstructor(listTypeInfo, Helpers.EmptyTypes, true) == null)
#else
            if (!listType.IsClass || listType.IsAbstract ||
                Helpers.GetConstructor(listType, Helpers.EmptyTypes, true) == null)
#endif
            {
                string fullName;
                bool handled = false;
#if WINRT
                if (listTypeInfo.IsInterface &&
#else
                if (listType.IsInterface &&
#endif
 (fullName = listType.FullName) != null && fullName.IndexOf("Dictionary") >= 0) // have to try to be frugal here...
                {
#if !NO_GENERICS
#if WINRT
                    TypeInfo finalType = listType.GetTypeInfo();
                    if (finalType.IsGenericType && finalType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IDictionary<,>))
                    {
                        Type[] genericTypes = listType.GenericTypeArguments;
                        concreteListType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(genericTypes);
                        handled = true;
                    }
#else
                    if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IDictionary<,>))
                    {
                        Type[] genericTypes = listType.GetGenericArguments();
                        concreteListType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(genericTypes);
                        handled = true;
                    }
#endif
#endif
#if !SILVERLIGHT && !WINRT && !PORTABLE
                    if (!handled && listType == typeof(IDictionary))
                    {
                        concreteListType = typeof(Hashtable);
                        handled = true;
                    }
#endif
                }
#if !NO_GENERICS
                if (!handled)
                {
                    concreteListType = typeof(System.Collections.Generic.List<>).MakeGenericType(itemType);
                    handled = true;
                }
#endif

#if !SILVERLIGHT && !WINRT && !PORTABLE
                if (!handled)
                {
                    concreteListType = typeof(ArrayList);
                    handled = true;
                }
#endif
            }
            return Activator.CreateInstance(concreteListType);
        }

        /// <summary>
        /// This is the more "complete" version of Deserialize, which handles single instances of mapped types.
        /// The value is read as a complete field, including field-header and (for sub-objects) a
        /// length-prefix..kmc  
        /// 
        /// In addition to that, this provides support for:
        ///  - basic values; individual int / string / Guid / etc
        ///  - IList sets of any type handled by TryDeserializeAuxiliaryType
        /// </summary>
        internal bool TryDeserializeAuxiliaryType(ProtoReader reader, DataFormat format, int tag, Type type, ref object value, bool skipOtherFields, bool asListItem, bool autoCreate, bool insideList)
        {
            if (type == null) throw new ArgumentNullException("type");
            Type itemType = null;
            ProtoTypeCode typecode = Helpers.GetTypeCode(type);
            int modelKey;
            WireType wiretype = GetWireType(typecode, format, ref type, out modelKey);

            bool found = false;
            if (wiretype == WireType.None)
            {
                itemType = GetListItemType(this, type);
                if (itemType == null && type.IsArray && type.GetArrayRank() == 1 && type != typeof(byte[]))
                {
                    itemType = type.GetElementType();
                }
                if (itemType != null)
                {
                    if (insideList) throw TypeModel.CreateNestedListsNotSupported();
                    found = TryDeserializeList(this, reader, format, tag, type, itemType, ref value);
                    if (!found && autoCreate)
                    {
                        value = CreateListInstance(type, itemType);
                    }
                    return found;
                }

                // otherwise, not a happy bunny...
                ThrowUnexpectedType(type);
            }

            // to treat correctly, should read all values

            while (true)
            {
                // for convenience (re complex exit conditions), additional exit test here:
                // if we've got the value, are only looking for one, and we aren't a list - then exit
                if (found && asListItem) break;


                // read the next item
                int fieldNumber = reader.ReadFieldHeader();
                if (fieldNumber <= 0) break;
                if (fieldNumber != tag)
                {
                    if (skipOtherFields)
                    {
                        reader.SkipField();
                        continue;
                    }
                    throw ProtoReader.AddErrorData(new InvalidOperationException(
                        "Expected field " + tag + ", but found " + fieldNumber), reader);
                }
                found = true;
                reader.Hint(wiretype); // handle signed data etc

                if (modelKey >= 0)
                {
                    switch (wiretype)
                    {
                        case WireType.String:
                        case WireType.StartGroup:
                            SubItemToken token = ProtoReader.StartSubItem(reader);
                            value = Deserialize(modelKey, value, reader);
                            ProtoReader.EndSubItem(token, reader);
                            continue;
                        default:
                            value = Deserialize(modelKey, value, reader);
                            continue;
                    }
                }
                switch (typecode)
                {
                    case ProtoTypeCode.Int16: value = reader.ReadInt16(); continue;
                    case ProtoTypeCode.Int32: value = reader.ReadInt32(); continue;
                    case ProtoTypeCode.Int64: value = reader.ReadInt64(); continue;
                    case ProtoTypeCode.UInt16: value = reader.ReadUInt16(); continue;
                    case ProtoTypeCode.UInt32: value = reader.ReadUInt32(); continue;
                    case ProtoTypeCode.UInt64: value = reader.ReadUInt64(); continue;
                    case ProtoTypeCode.Boolean: value = reader.ReadBoolean(); continue;
                    case ProtoTypeCode.SByte: value = reader.ReadSByte(); continue;
                    case ProtoTypeCode.Byte: value = reader.ReadByte(); continue;
                    case ProtoTypeCode.Char: value = (char)reader.ReadUInt16(); continue;
                    case ProtoTypeCode.Double: value = reader.ReadDouble(); continue;
                    case ProtoTypeCode.Single: value = reader.ReadSingle(); continue;
                    case ProtoTypeCode.DateTime: value = BclHelpers.ReadDateTime(reader); continue;
                    case ProtoTypeCode.Decimal: value = BclHelpers.ReadDecimal(reader); continue;
                    case ProtoTypeCode.String: value = reader.ReadString(); continue;
                    case ProtoTypeCode.ByteArray: value = ProtoReader.AppendBytes((byte[])value, reader); continue;
                    case ProtoTypeCode.TimeSpan: value = BclHelpers.ReadTimeSpan(reader); continue;
                    case ProtoTypeCode.Guid: value = BclHelpers.ReadGuid(reader); continue;
                    case ProtoTypeCode.Uri: value = new Uri(reader.ReadString()); continue;
                }

            }
            if (!found && !asListItem && autoCreate)
            {
                if (type != typeof(string))
                {
                    value = Activator.CreateInstance(type);
                }
            }
            return found;
        }
#endif

#if !NO_RUNTIME
        /// <summary>
        /// Creates a new runtime model, to which the caller
        /// can add support for a range of types. A model
        /// can be used "as is", or can be compiled for
        /// optimal performance.
        /// </summary>
        public static RuntimeTypeModel Create()
        {
            return new RuntimeTypeModel(false);
        }
#endif

        /// <summary>
        /// Applies common proxy scenarios, resolving the actual type to consider
        /// </summary>
        protected internal static Type ResolveProxies(Type type)
        {
            if (type == null) return null;
#if !NO_GENERICS
            if (type.IsGenericParameter) return null;
            // Nullable<T>
            Type tmp = Helpers.GetUnderlyingType(type);
            if (tmp != null) return tmp;
#endif

#if !(WINRT || CF)
            // EF POCO
            string fullName = type.FullName;
            if (fullName != null && fullName.StartsWith("System.Data.Entity.DynamicProxies.")) return type.BaseType;

            // NHibernate
            Type[] interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                switch (interfaces[i].FullName)
                {
                    case "NHibernate.Proxy.INHibernateProxy":
                    case "NHibernate.Proxy.DynamicProxy.IProxy":
                    case "NHibernate.Intercept.IFieldInterceptorAccessor":
                        return type.BaseType;
                }
            }
#endif
            return null;
        }
        /// <summary>
        /// Indicates whether the supplied type is explicitly modelled by the model
        /// </summary>
        public bool IsDefined(Type type)
        {
            return GetKey(ref type) >= 0;
        }
        /// <summary>
        /// Provides the key that represents a given type in the current model.
        /// The type is also normalized for proxies at the same time.
        /// </summary>
        protected internal int GetKey(ref Type type)
        {
            int key = GetKeyImpl(type);
            if (key < 0)
            {
                Type normalized = ResolveProxies(type);
                if (normalized != null)
                {
                    type = normalized; // hence ref
                    key = GetKeyImpl(type);
                }
            }
            return key;
        }

        /// <summary>
        /// Provides the key that represents a given type in the current model.
        /// </summary>
        protected abstract int GetKeyImpl(Type type);

        /// <summary>
        /// Applies a protocol-buffer stream to an existing instance (which may be null).
        /// </summary>
        /// <param name="key">Represents the type (including inheritance) to consider.</param>
        /// <param name="value">The existing instance to be modified (can be null).</param>
        /// <param name="source">The binary stream to apply to the instance (cannot be null).</param>
        /// <returns>The updated instance; this may be different to the instance argument if
        /// either the original instance was null, or the stream defines a known sub-type of the
        /// original instance.</returns>
        protected internal abstract object Deserialize(int key, object value, ProtoReader source);

        /// <summary>
        /// Indicates the type of callback to be used
        /// </summary>
        protected internal enum CallbackType
        {
            /// <summary>
            /// Invoked before an object is serialized
            /// </summary>
            BeforeSerialize,
            /// <summary>
            /// Invoked after an object is serialized
            /// </summary>
            AfterSerialize,
            /// <summary>
            /// Invoked before an object is deserialized (or when a new instance is created)
            /// </summary>            
            BeforeDeserialize,
            /// <summary>
            /// Invoked after an object is deserialized
            /// </summary>
            AfterDeserialize
        }

        /// <summary>
        /// Indicates that while an inheritance tree exists, the exact type encountered was not
        /// specified in that hierarchy and cannot be processed.
        /// </summary>
        protected internal static void ThrowUnexpectedSubtype(Type expected, Type actual)
        {
            if (expected != TypeModel.ResolveProxies(actual))
            {
                throw new InvalidOperationException("Unexpected sub-type: " + actual.FullName);
            }
        }

        /// <summary>
        /// Indicates that the given type was not expected, and cannot be processed.
        /// </summary>
        protected internal static void ThrowUnexpectedType(Type type)
        {
            string fullName = type == null ? "(unknown)" : type.FullName;
#if !NO_GENERICS && !WINRT
            if (type != null)
            {
                Type baseType = type.BaseType;
                if (baseType != null && baseType.IsGenericType && baseType.GetGenericTypeDefinition().Name == "GeneratedMessage`2")
                {
                    throw new InvalidOperationException(
                        "Are you mixing protobuf-net and protobuf-csharp-port? See http://stackoverflow.com/q/11564914; type: " + fullName);
                }
            }
#endif
            throw new InvalidOperationException("Type is not expected, and no contract can be inferred: " + fullName);
        }
        internal static Exception CreateNestedListsNotSupported()
        {
            return new NotSupportedException("Nested or jagged lists and arrays are not supported");
        }
        /// <summary>
        /// Indicates that the given type cannot be constructed; it may still be possible to 
        /// deserialize into existing instances.
        /// </summary>
        public static void ThrowCannotCreateInstance(Type type)
        {
            throw new ProtoException("No parameterless constructor found for " + type.Name);
        }

        internal static System.Type DeserializeType(TypeModel model, string value)
        {
            TypeFormatEventHandler handler;
            if (model != null && (handler = model.DynamicTypeFormatting) != null)
            {
                TypeFormatEventArgs args = new TypeFormatEventArgs(value);
                handler(model, args);
                if (args.Type != null) return args.Type;
            }
            return System.Type.GetType(value);
        }

        /// <summary>
        /// Used to provide custom services for writing and parsing type names when using dynamic types. Both parsing and formatting
        /// are provided on a single API as it is essential that both are mapped identically at all times.
        /// </summary>
        public event TypeFormatEventHandler DynamicTypeFormatting;

        internal virtual Type GetType(string fullName, Assembly context)
        {

            return ResolveKnownType(fullName, this, context);
        }

        internal static Type ResolveKnownType(string name, TypeModel model, Assembly assembly)
        {
            if (Helpers.IsNullOrEmpty(name)) return null;
            try
            {
                Type type = Type.GetType(name);
                if (type != null) return type;
            }
            catch { }
            try
            {
                int i = name.IndexOf(',');
                string fullName = (i > 0 ? name.Substring(0, i) : name).Trim();
                if (assembly == null) assembly = Assembly.GetCallingAssembly();
                Type type = assembly == null ? null : assembly.GetType(fullName);
                if (type != null) return type;
            }
            catch { }
            return null;
        }

    }

}

