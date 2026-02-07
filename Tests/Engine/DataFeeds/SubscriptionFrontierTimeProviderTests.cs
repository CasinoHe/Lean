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

using System;
using System.Collections.Generic;
using NodaTime;
using NUnit.Framework;
using QuantConnect;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Securities;

namespace QuantConnect.Tests.Engine.DataFeeds
{
    [TestFixture, Parallelizable(ParallelScope.All)]
    public class SubscriptionFrontierTimeProviderTests
    {
        [Test]
        public void AdvancesToFutureStartOfUnprimedDataSubscription()
        {
            var initialUtc = new DateTime(2026, 1, 2, 5, 0, 0, DateTimeKind.Utc);
            var minuteStartUtc = new DateTime(2026, 1, 2, 14, 30, 0, DateTimeKind.Utc);
            var minuteFirstEmitUtc = minuteStartUtc.AddMinutes(1);
            var hourlyFirstEmitUtc = new DateTime(2026, 1, 2, 15, 0, 0, DateTimeKind.Utc);

            var manager = new TestDataFeedSubscriptionManager();
            manager.DataFeedSubscriptions.TryAdd(CreateSubscription(Symbols.MSFT, Resolution.Minute, minuteStartUtc, minuteFirstEmitUtc));
            manager.DataFeedSubscriptions.TryAdd(CreateSubscription(Symbols.SPY, Resolution.Hour, initialUtc, hourlyFirstEmitUtc));

            var provider = new SubscriptionFrontierTimeProvider(initialUtc, manager);

            Assert.AreEqual(minuteStartUtc, provider.GetUtcNow());
        }

        [Test]
        public void PrimesDataSubscriptionWhenFrontierReachesStart()
        {
            var initialUtc = new DateTime(2026, 1, 2, 5, 0, 0, DateTimeKind.Utc);
            var minuteStartUtc = new DateTime(2026, 1, 2, 14, 30, 0, DateTimeKind.Utc);
            var minuteFirstEmitUtc = minuteStartUtc.AddMinutes(1);

            var manager = new TestDataFeedSubscriptionManager();
            manager.DataFeedSubscriptions.TryAdd(CreateSubscription(Symbols.MSFT, Resolution.Minute, minuteStartUtc, minuteFirstEmitUtc));

            var provider = new SubscriptionFrontierTimeProvider(initialUtc, manager);

            // First call advances to the subscription start without forcing a read in the future
            Assert.AreEqual(minuteStartUtc, provider.GetUtcNow());
            // Once the frontier reaches the start, the subscription is primed and the first emit time is surfaced
            Assert.AreEqual(minuteFirstEmitUtc, provider.GetUtcNow());
        }

        private static Subscription CreateSubscription(
            Symbol symbol,
            Resolution resolution,
            DateTime startTimeUtc,
            DateTime firstEmitTimeUtc)
        {
            var config = new SubscriptionDataConfig(
                typeof(TradeBar),
                symbol,
                resolution,
                DateTimeZone.Utc,
                DateTimeZone.Utc,
                fillForward: false,
                extendedHours: false,
                isInternalFeed: false);

            var security = new Security(
                SecurityExchangeHours.AlwaysOpen(DateTimeZone.Utc),
                config,
                new Cash(Currencies.USD, 0, 1m),
                SymbolProperties.GetDefault(Currencies.USD),
                ErrorCurrencyConverter.Instance,
                RegisteredSecurityDataTypesProvider.Null,
                new SecurityCache());

            var request = new SubscriptionRequest(
                isUniverseSubscription: false,
                universe: null,
                security: security,
                configuration: config,
                startTimeUtc: startTimeUtc,
                endTimeUtc: startTimeUtc.AddDays(1));

            var period = resolution.ToTimeSpan();
            if (period <= TimeSpan.Zero)
            {
                period = TimeSpan.FromSeconds(1);
            }

            var bar = new TradeBar
            {
                Symbol = symbol,
                Time = firstEmitTimeUtc - period,
                EndTime = firstEmitTimeUtc,
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1
            };

            var data = new List<SubscriptionData>
            {
                new SubscriptionData(bar, firstEmitTimeUtc)
            };

            var offsetProvider = new TimeZoneOffsetProvider(DateTimeZone.Utc, startTimeUtc, startTimeUtc.AddDays(1));
            return new Subscription(request, data.GetEnumerator(), offsetProvider);
        }

        private sealed class TestDataFeedSubscriptionManager : IDataFeedSubscriptionManager
        {
            public event EventHandler<Subscription> SubscriptionAdded { add { } remove { } }
            public event EventHandler<Subscription> SubscriptionRemoved { add { } remove { } }

            public SubscriptionCollection DataFeedSubscriptions { get; } = new();

            public UniverseSelection UniverseSelection => null;

            public bool RemoveSubscription(SubscriptionDataConfig configuration, Universe universe = null)
            {
                throw new NotImplementedException();
            }

            public bool AddSubscription(SubscriptionRequest request)
            {
                throw new NotImplementedException();
            }
        }
    }
}
