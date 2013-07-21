using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    using System.Runtime.Serialization;

    using Lesula.Cassandra;
    using Lesula.Cassandra.Client.Cql.Enumerators;

    using ProtoBuf.Cql;

    [TestClass]
    public class IntegratedTests
    {
        [DataContract]
        public class User
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
        public void Connect()
        {
            const string DefaultCluster = "CqlLesula";
            var client = AquilesHelper.RetrieveCluster(DefaultCluster);
            client.ExecuteNonQueryAsync("USE mytest", CqlConsistencyLevel.ONE);
            try
            {
                client.ExecuteNonQueryAsync("DROP TABLE users", CqlConsistencyLevel.ONE);
            }
            catch (Exception)
            {
            }

            client.ExecuteNonQueryAsync("CREATE TABLE users ( userName varchar PRIMARY KEY, password varchar, gender varchar, sessionToken varchar, state varchar, birth_year bigint, isActive boolean, childs int, lastLogin timestamp, wage double)",
                CqlConsistencyLevel.ONE);
            client.ExecuteNonQueryAsync(
                "INSERT into users (userName, password, gender, sessionToken, state, birth_year, isActive, childs, lastLogin, wage) VALUES ( 'lstern', 'testing', 'male', 'ABFG3', 'PR', 1981, true, 1, 1356847200, 5500.32)",
                CqlConsistencyLevel.ONE);
            var str = client.QueryAsync("SELECT * from users", new ResultsReader<User>(), CqlConsistencyLevel.ONE);
        }

        [TestMethod]
        public void TestMethod1()
        {
        }
    }
}
