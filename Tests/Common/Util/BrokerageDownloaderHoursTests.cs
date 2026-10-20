using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NUnit.Framework;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using Downloader = QuantConnect.DownloaderDataProvider.Launcher.Models.BrokerageDataDownloader;

namespace QuantConnect.Tests.Common.Util
{
    [TestFixture, NonParallelizable]
    public class BrokerageDownloaderHoursTests
    {
        [TestCase("America/New_York", 1, 0)]
        [TestCase("America/New_York", 2, 1)]
        [TestCase("America/Chicago", 1, 0)]
        [TestCase("America/Chicago", 2, 1)]
        public void DailyReferenceEndpointIncludesFollowingExchangeMidnight(string zoneId, int paddedDays, int expected)
        {
            var symbol = Symbol.CreateFuture("ES", Market.CME, new DateTime(2026, 12, 18));
            var zone = NodaTime.DateTimeZoneProviders.Tzdb[zoneId];
            var begin = new DateTime(2026, 10, 1);
            var hours = SecurityExchangeHours.AlwaysOpen(zone);
            var request = new QuantConnect.Data.HistoryRequest(begin, begin.AddDays(paddedDays), typeof(TradeBar),
                symbol, Resolution.Daily, hours, NodaTime.DateTimeZone.Utc, Resolution.Daily,
                true, false, DataNormalizationMode.Raw, TickType.Trade);
            var brokerageType = typeof(QuantConnect.Brokerages.InteractiveBrokers.InteractiveBrokersBrokerage);
            var brokerage = RuntimeHelpers.GetUninitializedObject(brokerageType);
            var method = brokerageType.GetMethod("FilterHistory", BindingFlags.Instance | BindingFlags.NonPublic);
            var contract = Activator.CreateInstance(method.GetParameters()[4].ParameterType);
            contract.GetType().GetProperty("LocalSymbol").SetValue(contract, "ESZ6");
            contract.GetType().GetProperty("SecType").SetValue(contract, "FUT");
            var bar = new TradeBar(begin, symbol, 100m, 101m, 99m, 100m, 1, Time.OneDay);
            var result = (IEnumerable<BaseData>)method.Invoke(brokerage, new object[]
            {
                new BaseData[] { bar }, request, begin.ConvertFromUtc(zone), begin.AddDays(paddedDays).ConvertFromUtc(zone),
                contract
            });
            Assert.That(result.Count(), Is.EqualTo(expected));
        }

        [TestCase(SecurityType.Future, Resolution.Hour, "true", true)]
        [TestCase(SecurityType.Future, Resolution.Daily, "true", true)]
        [TestCase(SecurityType.Future, Resolution.Minute, "false", false)]
        [TestCase(SecurityType.Future, Resolution.Hour, null, false)]
        [TestCase(SecurityType.Future, Resolution.Minute, null, true)]
        [TestCase(SecurityType.Equity, Resolution.Hour, "true", false)]
        [TestCase(SecurityType.Equity, Resolution.Daily, "true", false)]
        [TestCase(SecurityType.Equity, Resolution.Minute, "false", true)]
        public void ExplicitHoursOnlyOverrideFuturesRequests(SecurityType type, Resolution resolution, string setting, bool expected)
        {
            const string key = "data-downloader-include-extended-market-hours";
            var previous = Config.Get(key, null);
            try
            {
                Config.Set(key, setting);
                QuantConnect.Data.HistoryRequest request = null;
                var brokerage = new Mock<IBrokerage>();
                brokerage.Setup(b => b.GetHistory(It.IsAny<QuantConnect.Data.HistoryRequest>()))
                    .Callback<QuantConnect.Data.HistoryRequest>(r => request = r)
                    .Returns(Enumerable.Empty<BaseData>());
                // Replace only external brokerage construction: Get creates the real history request.
                var downloader = (Downloader)RuntimeHelpers.GetUninitializedObject(typeof(Downloader));
                typeof(Downloader).GetField("_brokerage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(downloader, brokerage.Object);
                typeof(Downloader).GetField("_marketHoursDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(downloader, MarketHoursDatabase.FromDataFolder());
                var symbol = type == SecurityType.Future
                    ? Symbol.CreateFuture("ES", Market.CME, new DateTime(2026, 12, 18))
                    : Symbol.Create("SPY", SecurityType.Equity, Market.USA);
                downloader.Get(new DataDownloaderGetParameters(symbol, resolution,
                    new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), TickType.Trade)).ToList();
                Assert.That(request, Is.Not.Null);
                Assert.That(request.IncludeExtendedMarketHours, Is.EqualTo(expected));
            }
            finally { Config.Set(key, previous); }
        }
    }
}
