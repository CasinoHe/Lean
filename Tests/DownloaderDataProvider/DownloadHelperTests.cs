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
 *
*/

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Logging;
using QuantConnect.Interfaces;
using QuantConnect.Data.Market;
using System.Collections.Generic;
using QuantConnect.DownloaderDataProvider.Launcher;
using QuantConnect.DownloaderDataProvider.Launcher.Models;
using NodaTime;
using QuantConnect.Securities;
using QuantConnect.Configuration;

namespace QuantConnect.Tests.DownloaderDataProvider
{
    [TestFixture]
    [NonParallelizable]
    public class DownloadHelperTests
    {
        private IDataCacheProvider _cacheProvider;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _cacheProvider = new DiskDataCacheProvider();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_cacheProvider != null)
            {
                _cacheProvider.Dispose();
            }
        }

        /// <summary>
        /// Temporary data download directory
        /// </summary>
        private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        [TestCase(TickType.Trade, Resolution.Daily)]
        public void RunDownload(TickType tickType, Resolution resolution)
        {
            var startDate = new DateTime(2024, 01, 01);
            var tradeDate = new DateTime(2024, 01, 10);
            var endDate = new DateTime(2024, 02, 02);
            var symbol = Symbol.CreateCanonicalOption(Symbols.AAPL);

            var downloadDataConfig = new DataDownloadConfig(tickType, SecurityType.Option, resolution, startDate, endDate, Market.USA, new List<Symbol>() { symbol });

            var optionContracts = GenerateOptionContracts(Symbols.AAPL, 100, new DateTime(2024, 03, 16), expiryAddDay: 30);
            var generateOptionContactFileName = optionContracts.ToList(contract => LeanData.GenerateZipEntryName(contract, contract.ID.Date, resolution, tickType));

            Assert.That(optionContracts.Distinct().Count(), Is.EqualTo(optionContracts.Count));

            var mockBaseDate = GenerateTradeBarByEachSymbol(optionContracts, tradeDate);

            var downloader = new DataDownloaderTest(mockBaseDate);

            Program.RunDownload(downloader, downloadDataConfig, _dataDirectory, _cacheProvider);

            Assert.AreEqual(startDate, downloader.LastParameters.StartUtc);
            Assert.AreEqual(endDate, downloader.LastParameters.EndUtc);

            var filePath = LeanData.GenerateZipFilePath(_dataDirectory, symbol, startDate, resolution, tickType);
            var fileNames = _cacheProvider.GetZipEntries(filePath);
            var unZipData = QuantConnect.Compression.Unzip(filePath).ToDictionary(x => x.Key, x => x.Value.ToList());

            Assert.AreEqual(fileNames.Count, unZipData.Count);
            Assert.AreEqual(fileNames.Count, optionContracts.Count);

            foreach (var dataInZip in unZipData)
            {
                Assert.IsTrue(generateOptionContactFileName.Contains(dataInZip.Key));
                Assert.Greater(dataInZip.Value.Count, 0);
                Assert.IsTrue(dataInZip.Value.All(row => row.Length > 0));
            }
        }

        private static IEnumerable<BaseData> GenerateTradeBarByEachSymbol(IEnumerable<Symbol> symbols, DateTime tradeDateTime)
        {
            var multiplier = 100;
            foreach (var option in symbols)
            {
                yield return new TradeBar(tradeDateTime, option, multiplier, multiplier, multiplier, multiplier, multiplier);
                multiplier *= 2;
            }
        }

        private static List<Symbol> GenerateOptionContracts(Symbol underlying, decimal strikePrice, DateTime expiryDate, int strikeMultiplier = 2, int expiryAddDay = 1, int count = 2)
        {
            var contracts = new List<Symbol>();
            for (int i = 0; i < count; i++)
            {
                contracts.Add(Symbol.CreateOption(underlying, underlying.ID.Market, OptionStyle.American, OptionRight.Put, strikePrice, expiryDate));
                expiryDate = expiryDate.AddDays(expiryAddDay);
                strikePrice *= strikeMultiplier;
            }
            return contracts;
        }

        [Test]
        public void RunDownloadLogsWarningWhenDownloaderReturnsEmptyData()
        {
            var startDate = new DateTime(2015, 7, 20);
            var endDate = new DateTime(2016, 1, 1);
            var symbol = Symbols.SPY;

            var downloadDataConfig = new DataDownloadConfig(TickType.Trade, SecurityType.Equity, Resolution.Daily, startDate, endDate, Market.USA, new List<Symbol> { symbol });

            var logHandler = new QueueLogHandler();
            var originalHandler = Log.LogHandler;
            Log.LogHandler = logHandler;

            try
            {
                var downloader = new DataDownloaderTest(Enumerable.Empty<BaseData>());
                Program.RunDownload(downloader, downloadDataConfig, _dataDirectory, _cacheProvider);

                var logs = logHandler.Logs.Select(x => x.Message).ToList();
                Assert.IsTrue(logs.Any(msg => msg.Contains("No data found", StringComparison.OrdinalIgnoreCase)),
                    $"Expected a warning log when the downloader returns no data, but got: {string.Join(", ", logs)}");
            }
            finally
            {
                Log.LogHandler = originalHandler;
            }
        }

        [TestCase("20260701", "20260702", 4, 4, Resolution.Hour)]
        [TestCase("20260701", "20260702", 4, 4, Resolution.Minute)]
        [TestCase("20260701", "20260702", 4, 4, Resolution.Second)]
        [TestCase("20261102", "20261103", 5, 5, Resolution.Hour)]
        [TestCase("20261102", "20261103", 5, 5, Resolution.Minute)]
        [TestCase("20261102", "20261103", 5, 5, Resolution.Second)]
        [TestCase("20260307", "20260310", 5, 4, Resolution.Minute)]
        [TestCase("20261031", "20261103", 4, 5, Resolution.Second)]
        [TestCase("20261224", "20261228", 5, 5, Resolution.Hour, "20261224", 17)]
        [TestCase("20261224", "20261228", 5, 5, Resolution.Minute, "20261224", 17)]
        [TestCase("20261224", "20261228", 5, 5, Resolution.Second, "20261224", 17)]
        public void ExchangeLocalDatesUseDstAndExclusiveEnd(string startText, string endText,
            int startUtcHour, int endUtcHour, Resolution resolution, string tailDateText = null, int tailCloseHour = 20)
        {
            var start = DateTime.ParseExact(startText, "yyyyMMdd", null);
            var end = DateTime.ParseExact(endText, "yyyyMMdd", null);
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Config.Set("data-type", "Trade");
            Config.Set("resolution", resolution.ToString());
            Config.Set("security-type", "Equity");
            Config.Set("market", "usa");
            Config.Set("tickers", "{\"SPY\":\"\"}");
            Config.Set("start-date", startText);
            Config.Set("end-date", endText);
            Config.Set("download-date-time-zone", "America/New_York");
            try
            {
                var config = new DataDownloadConfig();
                var period = resolution.ToTimeSpan();
                var tailDate = tailDateText == null ? end.AddDays(-1) : DateTime.ParseExact(tailDateText, "yyyyMMdd", null);
                var lastBar = tailDate.AddHours(tailCloseHour).Subtract(period);
                var times = new[] { start.AddTicks(-1), start, lastBar, end.AddTicks(-1), end };
                var downloader = new DataDownloaderTest(times.Select(time => (BaseData)new TradeBar(
                    time, Symbols.SPY, 10, 10, 10, 10, 100, period)).ToList());
                Program.RunDownload(downloader, config, directory, _cacheProvider, mapSymbol: false);
                Assert.AreEqual(start.AddHours(startUtcHour), downloader.LastParameters.StartUtc);
                Assert.AreEqual(end.AddHours(endUtcHour).AddTicks(-1), downloader.LastParameters.EndUtc);
                var files = Directory.GetFiles(directory, "*.zip", SearchOption.AllDirectories);
                var rows = files.SelectMany(file => QuantConnect.Compression.Unzip(file))
                    .SelectMany(member => member.Value).ToList();
                Assert.AreEqual(3, rows.Count, "Include both range tails, exclude prior and next local dates");
                var expectedTail = resolution == Resolution.Hour
                    ? lastBar.ToString("yyyyMMdd HH:mm")
                    : ((long)lastBar.TimeOfDay.TotalMilliseconds).ToString();
                Assert.IsTrue(rows.Any(row => row.StartsWith(expectedTail + ",", StringComparison.Ordinal)),
                    "Retain the preceding session's final raw after-hours bar across holidays");
            }
            finally
            {
                Config.Reset();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void EarlyClosePostmarketSurvivesLeanSessionTruncation()
        {
            var date = new DateTime(2026, 11, 27);
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var zone = DateTimeZoneProviders.Tzdb["America/New_York"];
            var hours = new SecurityExchangeHours(zone, new HashSet<DateTime>(),
                new Dictionary<DayOfWeek, LocalMarketHours>
                {
                    [DayOfWeek.Friday] = new LocalMarketHours(DayOfWeek.Friday,
                        TimeSpan.FromHours(4), TimeSpan.FromHours(9.5), TimeSpan.FromHours(16), TimeSpan.FromHours(20))
                }, new Dictionary<DateTime, TimeSpan> { [date] = TimeSpan.FromHours(13) },
                new Dictionary<DateTime, TimeSpan>());
            Assert.IsFalse(hours.IsOpen(date.AddHours(16), extendedMarketHours: true),
                "LEAN's early close truncates even the postmarket segment");
            Config.Set("data-type", "Trade");
            Config.Set("resolution", "Minute");
            Config.Set("security-type", "Equity");
            Config.Set("market", "usa");
            Config.Set("tickers", "{\"SPY\":\"\"}");
            Config.Set("start-date", "20261127");
            Config.Set("end-date", "20261128");
            Config.Set("download-date-time-zone", "America/New_York");
            try
            {
                var downloader = new DataDownloaderTest(new[]
                {
                    new TradeBar(date.AddHours(16).AddMinutes(59), Symbols.SPY, 10, 10, 10, 10, 100, TimeSpan.FromMinutes(1))
                });
                Program.RunDownload(downloader, new DataDownloadConfig(), directory, _cacheProvider, mapSymbol: false);
                Assert.AreEqual(date.AddDays(1).AddHours(5).AddTicks(-1), downloader.LastParameters.EndUtc);
                Assert.AreEqual(1, Directory.GetFiles(directory, "*.zip", SearchOption.AllDirectories).Length);
            }
            finally
            {
                Config.Reset();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void EmptyOrReversedLocalRangeFailsBeforeCallingProvider(int days)
        {
            var date = new DateTime(2026, 11, 2);
            var config = new DataDownloadConfig(TickType.Trade, SecurityType.Equity, Resolution.Minute,
                date, date.AddDays(days), Market.USA, new List<Symbol> { Symbols.SPY })
            {
                DownloadDateTimeZone = DateTimeZoneProviders.Tzdb["America/New_York"]
            };
            var downloader = new DataDownloaderTest(Enumerable.Empty<BaseData>());
            Assert.Throws<ArgumentException>(() => Program.RunDownload(downloader, config, _dataDirectory, _cacheProvider));
            Assert.IsNull(downloader.LastParameters);
        }

        private class DataDownloaderTest : IDataDownloader
        {
            public IEnumerable<BaseData> Data { get; }
            public DataDownloaderGetParameters LastParameters { get; private set; }

            public DataDownloaderTest(IEnumerable<BaseData> data)
            {
                Data = data;
            }

            public IEnumerable<BaseData> Get(DataDownloaderGetParameters dataDownloaderGetParameters)
            {
                LastParameters = dataDownloaderGetParameters;
                return Data.Select(x => x);
            }
        }
    }
}
