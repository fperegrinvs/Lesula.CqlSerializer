using System;
using System.Collections.Generic;
using System.IO;

namespace ProtoBuf.unittest.Meta
{
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;

    using Lesula.Cassandra;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using ProtoBuf.Cql;

    [TestClass]
    public class PocoClass
    {
        public class Company
        {
            private readonly List<Employee> employees = new List<Employee>();
            public List<Employee> Employees { get { return employees; } }
        }

        public class Employee
        {
            public string EmployeeName { get; set; }
            public string Designation { get; set; }
        }

        [DataContract]
        public class User
        {
            [DataMember(Name = "FirstName")]
            public string Name { get; set; }

            [DataMember()]
            public int Age { get; set; }

            public string Error { get; set; }
        }


        [TestMethod]
        public void ParseHeader()
        {
            var contents = "000000010000000A00066D7974657374000575736572730008757365726E616D65000D000A62697274685F79656172000200066368696C64730009000667656E646572000D00086973616374697665000400096C6173746C6F67696E000B000870617373776F7264000D000C73657373696F6E746F6B656E000D00057374617465000D000477616765000700000001000000066C737465726E0000000800000000000007BD0000000400000001000000046D616C650000000101000000080000000050DFD8600000000774657374696E670000000541424647330000000250520000000840B57C51EB851EB8".FromHexString();
            using (var ms = new MemoryStream())
            {
                ms.Write(contents, 0, contents.Length);
                ms.Position = 0;
                var meta = ResultsReader<object>.ReadMeta(ms);
            }
        }

        //[TestMethod]
        //public void ParseRows()
        //{
        //    var contents = ("000000010000000A00066D7974657374000575736572730008757365726E616D65000D000A62697274685F79656172000200066368696C64730009000667656E646572000D00086973616374697665000400096C6173746C6F67696E000B000870617373776F7264000D000C73657373696F6E746F6B656E000D00057374617465000D000477616765000700000001000000066C737465726E0000000800000000000007BD0000000400000001000000046D616C65000000010100000008"
        //                   +
        //                   "002DA0328BE73C0"
        //                   +
        //                   "00000000774657374696E670000000541424647330000000250520000000840B57C51EB851EB8").FromHexString();
        //    using (var ms = new MemoryStream())
        //    {
        //        ms.Write(contents, 0, contents.Length);
        //        ms.Position = 0;
        //        var meta = ResultsReader<MyUser>.ReadRows(ms, ResultsReader<object>.ReadMeta(ms));
        //    }
        //}

        [DataContract]
        public class MyUser
        {
            [DataMember]
            public string UserName { get; set; }

            [DataMember]
            public string Password { get; set; }

            [DataMember]
            public string Gender { get; set; }

            [DataMember]
            public string State { get; set; }

            [DataMember]
            public string SessionToken { get; set; }

            [DataMember(Name = "birth_year")]
            public decimal BirthYear { get; set; }

            [DataMember]
            public bool IsActive { get; set; }

            [DataMember]
            public int Childs { get; set; }

            [DataMember]
            public DateTime LastLogin { get; set; }

            [DataMember]
            public double Wage { get; set; }
        }

        [TestMethod]
        public void TestAlias()
        {
            var myType = typeof(User);
            var hasContract = Attribute.GetCustomAttribute(myType, typeof(DataContractAttribute)) != null;
            var members = myType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OfType<MemberInfo>().Union(myType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

            var memberAttr = typeof(DataMemberAttribute);

            var dictionary = (from m in members
                              let attr = hasContract ? Attribute.GetCustomAttribute(m, memberAttr) as DataMemberAttribute : null
                              where !hasContract || attr != null
                              select new { Alias = attr.Name ?? m.Name, Info = m }).ToDictionary(a => a.Alias);

            Assert.IsTrue(hasContract);
        }
    }
}
