using System;
using System.Net;
using System.Threading;
using Enyim.Caching;
using Enyim.Caching.Configuration;
using Enyim.Caching.Memcached;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace MemcachedTest
{
    public abstract class MemcachedClientTest
    {
        private static readonly Enyim.Caching.ILog log = Enyim.Caching.LogManager.GetLogger(typeof(MemcachedClientTest));
        public const string TestObjectKey = "Hello_World";

        protected virtual MemcachedClient GetClient(MemcachedProtocol protocol = MemcachedProtocol.Binary)
        {
            IServiceCollection services = new ServiceCollection();
            services.AddEnyimMemcached(options =>
            {
                options.AddServer("memcached", 11211);
                options.Protocol = protocol;
            });
            services.AddLogging();
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            var client = serviceProvider.GetService<IMemcachedClient>() as MemcachedClient;
            client.Remove("VALUE");
            return client;
        }

        public class TestData
        {
            public TestData() { }

            public string FieldA;
            public string FieldB;
            public int FieldC;
            public bool FieldD;
        }

        /// <summary>
        ///A test for Store (StoreMode, string, byte[], int, int)
        ///</summary>
        [Fact]
        public async Task StoreObjectTest()
        {
            TestData td = new TestData();
            td.FieldA = "Hello";
            td.FieldB = "World";
            td.FieldC = 19810619;
            td.FieldD = true;

            using (MemcachedClient client = GetClient())
            {
                Assert.True(await client.StoreAsync(StoreMode.Set, TestObjectKey, td, DateTime.Now.AddSeconds(5)));
            }
        }

        [Fact]
        public void GetObjectTest()
        {
            TestData td = new TestData();
            td.FieldA = "Hello";
            td.FieldB = "World";
            td.FieldC = 19810619;
            td.FieldD = true;

            using (MemcachedClient client = GetClient())
            {
                Assert.True(client.Store(StoreMode.Set, TestObjectKey, td), "Initialization failed.");

                TestData td2 = client.Get<TestData>(TestObjectKey);

                Assert.NotNull(td2);
                Assert.Equal(td2.FieldA, "Hello");
                Assert.Equal(td2.FieldB, "World");
                Assert.Equal(td2.FieldC, 19810619);
                Assert.True(td2.FieldD, "Object was corrupted.");
            }
        }

        [Fact]
        public void DeleteObjectTest()
        {
            using (MemcachedClient client = GetClient())
            {
                TestData td = new TestData();
                Assert.True(client.Store(StoreMode.Set, TestObjectKey, td), "Initialization failed.");

                Assert.True(client.Remove(TestObjectKey), "Remove failed.");
                Assert.Null(client.Get(TestObjectKey));
            }
        }

        [Fact]
        public async Task StoreStringTest()
        {
            using (MemcachedClient client = GetClient())
            {
                Assert.True(await client.StoreAsync(StoreMode.Set, "TestString", "Hello world!", DateTime.Now.AddSeconds(10)), "StoreString failed.");

                Assert.Equal("Hello world!", await client.GetValueAsync<string>("TestString"));
            }
        }


        [Fact]
        public void StoreLongTest()
        {
            using (MemcachedClient client = GetClient())
            {
                Assert.True(client.Store(StoreMode.Set, "TestLong", 65432123456L), "StoreLong failed.");

                Assert.Equal(65432123456L, client.Get<long>("TestLong"));
            }
        }

        [Fact]
        public void StoreArrayTest()
        {
            byte[] bigBuffer = new byte[200 * 1024];

            for (int i = 0; i < bigBuffer.Length / 256; i++)
            {
                for (int j = 0; j < 256; j++)
                {
                    bigBuffer[i * 256 + j] = (byte)j;
                }
            }

            using (MemcachedClient client = GetClient())
            {
                Assert.True(client.Store(StoreMode.Set, "BigBuffer", bigBuffer), "StoreArray failed");

                byte[] bigBuffer2 = client.Get<byte[]>("BigBuffer");

                for (int i = 0; i < bigBuffer.Length / 256; i++)
                {
                    for (int j = 0; j < 256; j++)
                    {
                        if (bigBuffer2[i * 256 + j] != (byte)j)
                        {
                            Assert.Equal(j, bigBuffer[i * 256 + j]);
                            break;
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task ExpirationTestTimeSpan()
        {
            using (MemcachedClient client = GetClient())
            {
                await client.RemoveAsync("ExpirationTest:TimeSpan");
                Assert.True(await client.StoreAsync(StoreMode.Set, "ExpirationTest:TimeSpan", "ExpirationTest:TimeSpan", new TimeSpan(0, 0, 5)), "Expires:Timespan failed");
                Assert.Equal("ExpirationTest:TimeSpan", await client.GetValueAsync<string>("ExpirationTest:TimeSpan"));

                Thread.Sleep(8000);
                Assert.Null(await client.GetValueAsync<string>("ExpirationTest:TimeSpan"));
            }
        }

        [Fact]
        public void ExpirationTestDateTime()
        {
            using (MemcachedClient client = GetClient())
            {
                DateTime expiresAt = DateTime.Now.AddSeconds(5);

                Assert.True(client.Store(StoreMode.Set, "Expires:DateTime", "Expires:DateTime", expiresAt), "Expires:DateTime failed");
                Assert.Equal("Expires:DateTime", client.Get("Expires:DateTime"));

                Thread.Sleep(8000);

                Assert.Null(client.Get("Expires:DateTime"));
            }
        }

        [Fact]
        public void AddSetReplaceTest()
        {
            using (MemcachedClient client = GetClient())
            {
                log.Debug("Cache should be empty.");

                Assert.True(client.Store(StoreMode.Set, "VALUE", "1"), "Initialization failed");

                log.Debug("Setting VALUE to 1.");

                Assert.Equal("1", client.Get("VALUE"));

                log.Debug("Adding VALUE; this should return false.");
                Assert.False(client.Store(StoreMode.Add, "VALUE", "2"), "Add should have failed");

                log.Debug("Checking if VALUE is still '1'.");
                Assert.Equal("1", client.Get("VALUE"));

                log.Debug("Replacing VALUE; this should return true.");
                Assert.True(client.Store(StoreMode.Replace, "VALUE", "4"), "Replace failed");

                log.Debug("Checking if VALUE is '4' so it got replaced.");
                Assert.Equal("4", client.Get("VALUE"));

                log.Debug("Removing VALUE.");
                Assert.True(client.Remove("VALUE"), "Remove failed");

                log.Debug("Replacing VALUE; this should return false.");
                Assert.False(client.Store(StoreMode.Replace, "VALUE", "8"), "Replace should not have succeeded");

                log.Debug("Checking if VALUE is 'null' so it was not replaced.");
                Assert.Null(client.Get("VALUE"));

                log.Debug("Adding VALUE; this should return true.");
                Assert.True(client.Store(StoreMode.Add, "VALUE", "16"), "Item should have been Added");

                log.Debug("Checking if VALUE is '16' so it was added.");
                Assert.Equal("16", client.Get("VALUE"));

                log.Debug("Passed AddSetReplaceTest.");
            }
        }

        private string[] keyParts = { "multi", "get", "test", "key", "parts", "test", "values" };

        protected string MakeRandomKey(int partCount)
        {
            var sb = new StringBuilder();
            var rnd = new Random();

            for (var i = 0; i < partCount; i++)
            {
                sb.Append(keyParts[rnd.Next(keyParts.Length)]).Append(":");
            }

            sb.Length--;

            return sb.ToString();
        }

        [Fact]
        public async Task MultiGetTest()
        {
            using (var client = GetClient())
            {
                var keys = new List<string>();

                for (int i = 0; i < 100; i++)
                {
                    string k = $"Hello_Multi_Get_{Guid.NewGuid()}_" + i;
                    keys.Add(k);

                    Assert.True(await client.StoreAsync(StoreMode.Set, k, i, DateTime.Now.AddSeconds(300)), "Store of " + k + " failed");
                }

                IDictionary<string, object> retvals = client.Get(keys);

                Assert.NotEmpty(retvals);
                Assert.Equal(keys.Count, retvals.Count);

                object value = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    string key = keys[i];

                    Assert.True(retvals.TryGetValue(key, out value), "missing key: " + key);
                    Assert.Equal(value, i);
                }
            }
        }

        [Fact]
        public virtual async Task MultiGetWithCasTest()
        {
            using (var client = GetClient())
            {
                var keys = new List<string>();

                for (int i = 0; i < 100; i++)
                {
                    string k = $"Hello_Multi_Get_{Guid.NewGuid()}_" + i;
                    keys.Add(k);

                    Assert.True(await client.StoreAsync(StoreMode.Set, k, i, DateTime.Now.AddSeconds(300)), "Store of " + k + " failed");
                }

                var retvals = client.GetWithCas(keys);

                CasResult<object> value;

                Assert.Equal(keys.Count, retvals.Count);

                for (int i = 0; i < keys.Count; i++)
                {
                    string key = keys[i];

                    Assert.True(retvals.TryGetValue(key, out value), "missing key: " + key);
                    Assert.Equal(value.Result, i);
                    Assert.NotEqual(value.Cas, (ulong)0);
                }
            }
        }

        [Fact]
        public void IncrementLongTest()
        {
            var initialValue = 56UL * (ulong)System.Math.Pow(10, 11) + 1234;

            using (MemcachedClient client = GetClient())
            {
                Assert.Equal(initialValue, client.Increment("VALUE", initialValue, 2UL));
                Assert.Equal(initialValue + 24, client.Increment("VALUE", 10UL, 24UL));
            }
        }
    }
}

#region [ License information          ]
/* ************************************************************
 * 
 *    Copyright (c) 2010 Attila Kisk? enyim.com
 *    
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *    
 *        http://www.apache.org/licenses/LICENSE-2.0
 *    
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *    
 * ************************************************************/
#endregion
