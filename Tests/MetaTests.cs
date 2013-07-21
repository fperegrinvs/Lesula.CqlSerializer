namespace ProtoBuf.unittest.Meta
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class PocoClass
    {
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
