// cassandra-sharp - a .NET client for Apache Cassandra
// Copyright (c) 2011-2012 Pierre Chalamet
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace ProtoBuf.Cql
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;

    using Lesula.Cassandra.Client.Cql;
    using Lesula.Cassandra.Client.Cql.Enumerators;
    using Lesula.Cassandra.Client.Cql.Extensions;

    using ProtoBuf.Meta;

    /// <summary>
    /// The results reader.
    /// </summary>
    /// <typeparam name="T">
    /// </typeparam>
    public class ResultsReader<T> : ICqlObjectBuilder<T>
    {
        internal static byte ProtocolVersion = 0x01;

        private readonly Stream ms;


        public byte ReadByte()
        {
            return (byte)this.ms.ReadByte();
        }

        public short ReadShort()
        {
            return this.ms.ReadShort();
        }

        public int ReadInt()
        {
            return this.ms.ReadInt();
        }

        public string ReadString()
        {
            return this.ms.ReadString();
        }

        public string[] ReadStringList()
        {
            return this.ms.ReadStringList();
        }

        public byte[] ReadBytes()
        {
            return this.ms.ReadBytes();
        }

        public byte[] ReadShortBytes()
        {
            return this.ms.ReadShortBytes();
        }

        public Dictionary<string, string[]> ReadStringMultimap()
        {
            return this.ms.ReadStringMultimap();
        }

        public T ReadRows(Stream s, CqlMetadata metadata)
        {
            var model = TypeModel.Create();
            var type = typeof(T);

            var members = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OfType<MemberInfo>().Union(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

            var hasContract = Attribute.GetCustomAttribute(type, typeof(DataContractAttribute)) != null;
            var memberAttr = typeof(DataMemberAttribute);

            var dictionary = (from m in members
                              let attr = hasContract ? Attribute.GetCustomAttribute(m, memberAttr) as DataMemberAttribute : null
                              where !hasContract || attr != null
                              select new { Alias = (attr != null && !string.IsNullOrEmpty(attr.Name)) ? attr.Name.ToLowerInvariant() : m.Name.ToLowerInvariant(), Info = m }).ToDictionary(a => a.Alias);

            var meta = model.Add(type, false);

            // meta.Columns = new MetadataColumn[meta.ColumnsCount];
            var fieldNumber = 1;
            var memberInfo = new List<MemberInfo>();
            foreach (var column in metadata.Columns)
            {
                var name = column.ColumnName.ToLowerInvariant();

                if (!dictionary.ContainsKey(name))
                {
                    throw new Exception("Could not find and field or property to receive the column '" + name + "'");
                }

                var member = dictionary[name].Info;
                var itemType = member.MemberType == MemberTypes.Field ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;
                
                // TODO: Support list, sets and maps
                meta.AddField(fieldNumber++, member, null, itemType, null);
                memberInfo.Add(member);
            }

            var result = default(T);
            result = (T)model.Deserialize(s, result, type, null, memberInfo.ToArray());

            return result;
        }

        public static CqlMetadata ReadMeta(Stream stream)
        {
            var meta = new CqlMetadata();
            meta.Flags = (CqlMetadataFlag)stream.ReadInt();
            meta.ColumnsCount = stream.ReadInt();

            bool global = (meta.Flags & CqlMetadataFlag.GlobalTable) == CqlMetadataFlag.GlobalTable;
            if (global)
            {
                meta.Keyspace = stream.ReadString();
                meta.ColumnFamily = stream.ReadString();
            }

            meta.Columns = new MetadataColumn[meta.ColumnsCount];
            for (var i = 0; i < meta.ColumnsCount; i++)
            {
                var col = new MetadataColumn();
                if (!global)
                {
                    col.Keyspace = stream.ReadString();
                    col.ColumnFamily = stream.ReadString();
                }

                col.ColumnName = stream.ReadString();
                col.Type = ProcessColumnType(stream);
                meta.Columns[i] = col;
            }

            return meta;
        }

        protected static CqlType ProcessColumnType(Stream stream)
        {
            var type = new CqlType();
            type.ColumnType = (CqlColumnType)stream.ReadShort();
            switch (type.ColumnType)
            {
                case CqlColumnType.Custom:
                    type.CustomType = stream.ReadString();
                    break;
                case CqlColumnType.List:
                case CqlColumnType.Set:
                    type.SubType = new CqlType[1];
                    type.SubType[0] = ProcessColumnType(stream);
                    break;
                case CqlColumnType.Map:
                    type.SubType = new CqlType[2];
                    type.SubType[0] = ProcessColumnType(stream);
                    type.SubType[1] = ProcessColumnType(stream);
                    break;
            }

            return type;
        }
    }
}