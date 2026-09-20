/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using NUnit.Framework;
using Newtonsoft.Json.Linq;
using QuantConnect.Configuration;
using QuantConnect.Queues;
using System;
using System.IO;

namespace QuantConnect.Tests
{
    [TestFixture]
    public class JobQueueTests
    {
        [TestCase("QuantConnect.Algorithm.CSharp.dll", "CSharp", Language.CSharp, true)]
        [TestCase("QuantConnect.Algorithm.CSharp.dll", "", Language.CSharp, true)]
        [TestCase("QUANTCONNECT.ALGORITHM.CSHARP.DLL", "", Language.CSharp, true)]
        [TestCase("../../../Algorithm.Python/BasicTemplateFrameworkAlgorithm.py", "Python", Language.Python, true)]
        [TestCase("../../../Algorithm.Python/BasicTemplateFrameworkAlgorithm.py", "", Language.Python, true)]
        [TestCase("../../../ALGORITHM.PYTHON/BASICTEMPLATEFRAMEWORKALGORITHM.PY", "", Language.Python, true)]
        [TestCase("../../../test.jar", "", Language.Java, false)]
        public void JobQueueSetsAlgorithmLanguageCorrectly(string algorithmLocation, string algorithmLanguage, Language expectedLangauge, bool isValidExtension)
        {
            Config.Set("algorithm-location", algorithmLocation);
            Config.Set("algorithm-language", algorithmLanguage);

            var jobQueue = new JobQueueTestClass();
            if (isValidExtension)
            {
                Assert.AreEqual(expectedLangauge, jobQueue.GetLanguage());
            }
            else
            {
                Assert.Throws<ArgumentException>(() => jobQueue.GetLanguage());
            }
        }

        [Test]
        public void JobQueueUsesConfiguredRamAllocationForLocalJobs()
        {
            var originalAlgorithmLocation = Config.GetToken("algorithm-location")?.DeepClone();
            var originalAlgorithmLanguage = Config.GetToken("algorithm-language")?.DeepClone();
            var originalAlgorithmTypeName = Config.GetToken("algorithm-type-name")?.DeepClone();
            var originalRamAllocation = Config.GetToken("ram-allocation")?.DeepClone();
            var originalRamAllocationValue = Config.GetInt("ram-allocation", int.MaxValue);
            var originalLiveModeToken = Config.GetToken("live-mode")?.DeepClone();
            var originalLiveMode = Config.GetBool("live-mode");
            var algorithmPath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(algorithmPath, new byte[] { 1 });
                Config.Set("algorithm-location", algorithmPath);
                Config.Set("algorithm-language", "CSharp");
                Config.Set("algorithm-type-name", "ConfiguredRamAllocationTest");
                Config.Set("ram-allocation", "2048");
                Config.Set("live-mode", false);
                Globals.Reset();

                var job = new JobQueue().NextJob(out _);

                Assert.That(job.RamAllocation, Is.EqualTo(2048));
            }
            finally
            {
                RestoreConfigValue("algorithm-location", originalAlgorithmLocation);
                RestoreConfigValue("algorithm-language", originalAlgorithmLanguage);
                RestoreConfigValue("algorithm-type-name", originalAlgorithmTypeName);
                RestoreConfigValue("ram-allocation", originalRamAllocation);
                RestoreConfigValue("live-mode", originalLiveModeToken);
                Globals.Reset();
                File.Delete(algorithmPath);
            }

            Assert.Multiple(() =>
            {
                Assert.That(JToken.DeepEquals(Config.GetToken("algorithm-location"), originalAlgorithmLocation), Is.True);
                Assert.That(JToken.DeepEquals(Config.GetToken("algorithm-language"), originalAlgorithmLanguage), Is.True);
                Assert.That(JToken.DeepEquals(Config.GetToken("algorithm-type-name"), originalAlgorithmTypeName), Is.True);
                Assert.That(JToken.DeepEquals(Config.GetToken("ram-allocation"), originalRamAllocation), Is.True);
                Assert.That(Config.GetInt("ram-allocation", int.MaxValue), Is.EqualTo(originalRamAllocationValue));
                Assert.That(JToken.DeepEquals(Config.GetToken("live-mode"), originalLiveModeToken), Is.True);
                Assert.That(Globals.LiveMode, Is.EqualTo(originalLiveMode));
            });
        }

        private static void RestoreConfigValue(string key, JToken originalValue)
        {
            Config.GetToken(key)?.Parent?.Remove();
            if (originalValue != null)
            {
                Config.Set(key, originalValue.DeepClone());
            }
        }
    }

    public class JobQueueTestClass : JobQueue
    {
        public Language GetLanguage() { return Language; }
    }
}
