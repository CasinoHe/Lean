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
        [TestCase("2026-03-08T07:30:00Z", "27000000")]
        [TestCase("2026-11-01T07:30:00Z", "27000000")]
        public void SecondDownloadSerializationPreservesUtcStartAcrossDst(string utc, string expectedSlot)
        {
            var start = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            var symbol = Symbol.CreateFuture("ES", Market.CME, new DateTime(2026, 12, 18));
            var bar = new TradeBar(start.ConvertFromUtc(TimeZones.NewYork), symbol, 100m, 102m, 99m, 101m, 3m, TimeSpan.FromSeconds(1));
            var grouped = QuantConnect.Lean.Engine.DataFeeds.DownloaderDataProvider.FilterAndGroupDownloadDataBySymbol(
                new[] { bar }, symbol, typeof(TradeBar), TimeZones.NewYork, NodaTime.DateTimeZone.Utc,
                start.AddSeconds(-1), start.AddSeconds(2)).Single().Single();
            var line = QuantConnect.Util.LeanData.GenerateLine(grouped, SecurityType.Future, Resolution.Second);
            Assert.That(line, Is.EqualTo(expectedSlot + ",100,102,99,101,3"));
        }

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

        [TestCase(SecurityType.Future, Resolution.Second, "2026-10-01T20:45:00Z", "2026-10-01T21:00:00Z", 20, 45, 21)]
        [TestCase(SecurityType.Future, Resolution.Minute, "2026-10-01T20:45:00Z", "2026-10-01T21:00:00Z", 20, 45, 21)]
        [TestCase(SecurityType.Equity, Resolution.Minute, "2026-10-01T20:45:00Z", "2026-10-01T21:00:00Z", 0, 0, 0)]
        public void IntradayWindowIsOnlyAppliedToFutures(SecurityType type, Resolution resolution, string start, string end, int hour, int minute, int endHour)
        {
            const string firstKey = "data-downloader-futures-start-utc", lastKey = "data-downloader-futures-end-utc";
            var firstPrevious = Config.Get(firstKey, null); var lastPrevious = Config.Get(lastKey, null);
            try
            {
                Config.Set(firstKey, start); Config.Set(lastKey, end);
                HistoryRequest request = null;
                var brokerage = new Mock<IBrokerage>();
                brokerage.Setup(b => b.GetHistory(It.IsAny<HistoryRequest>()))
                    .Callback<HistoryRequest>(r => request = r).Returns(Enumerable.Empty<BaseData>());
                var downloader = (Downloader)RuntimeHelpers.GetUninitializedObject(typeof(Downloader));
                typeof(Downloader).GetField("_brokerage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(downloader, brokerage.Object);
                typeof(Downloader).GetField("_marketHoursDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(downloader, MarketHoursDatabase.FromDataFolder());
                var symbol = type == SecurityType.Future
                    ? Symbol.CreateFuture("ES", Market.CME, new DateTime(2026, 12, 18))
                    : Symbol.Create("SPY", SecurityType.Equity, Market.USA);
                downloader.Get(new DataDownloaderGetParameters(symbol, resolution,
                    new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), TickType.Trade)).ToList();
                Assert.That(request.StartTimeUtc, Is.EqualTo(new DateTime(2026, 10, 1, hour, minute, 0)));
                Assert.That(request.EndTimeUtc, Is.EqualTo(type == SecurityType.Future
                    ? new DateTime(2026, 10, 1, endHour, 0, 0) : new DateTime(2026, 10, 2)));
            }
            finally { Config.Set(firstKey, firstPrevious); Config.Set(lastKey, lastPrevious); }
        }

        [TestCase("2026-10-01T20:45:00Z", null)]
        [TestCase("2026-10-01T21:00:00Z", "2026-10-01T20:45:00Z")]
        [TestCase("2026-10-01T20:45:00", "2026-10-01T21:00:00Z")]
        [TestCase("2026-10-01T20:45:00-04:00", "2026-10-02T01:00:00Z")]
        public void RejectsInvalidFuturesWindowBeforeCallingBrokerage(string start, string end)
        {
            const string firstKey = "data-downloader-futures-start-utc", lastKey = "data-downloader-futures-end-utc";
            var previousStart = Config.Get(firstKey, null); var previousEnd = Config.Get(lastKey, null);
            try
            {
                Config.Set(firstKey, start); Config.Set(lastKey, end);
                var downloader = (Downloader)RuntimeHelpers.GetUninitializedObject(typeof(Downloader));
                var symbol = Symbol.CreateFuture("ES", Market.CME, new DateTime(2026, 12, 18));
                Assert.Throws<ArgumentException>(() => downloader.Get(new DataDownloaderGetParameters(symbol,
                    Resolution.Minute, new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), TickType.Trade)).ToList());
            }
            finally { Config.Set(firstKey, previousStart); Config.Set(lastKey, previousEnd); }
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
