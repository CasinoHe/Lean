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

namespace QuantConnect.Tests.Common
{
    [TestFixture]
    public class MarketTests
    {
        [TestCase(Market.XAMS, 100)]
        [TestCase(Market.XBRU, 101)]
        [TestCase(Market.XETR, 102)]
        [TestCase(Market.XHEL, 103)]
        [TestCase(Market.XMAD, 104)]
        [TestCase(Market.XMIL, 105)]
        [TestCase(Market.XPAR, 106)]
        public void MapsEuropeanMicMarketsToStableIdentifiers(string market, int identifier)
        {
            Assert.AreEqual(identifier, Market.Encode(market));
            Assert.AreEqual(market, Market.Decode(identifier));
        }

        [Test]
        public void MapsAllMarketsInMarketClass()
        {
            var markets = typeof(Market).GetFields();
            foreach (var field in markets)
            {
                var market = (string)field.GetValue(null);
                var code = Market.Encode(market);
                Assert.IsTrue(code.HasValue);

                var decoded = Market.Decode(code.Value);
                Assert.AreEqual(market, decoded);
            }
        }
    }
}
