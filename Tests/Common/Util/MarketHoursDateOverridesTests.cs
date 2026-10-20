using System;
using System.Collections.Generic;
using System.Globalization;
using NodaTime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QuantConnect.Securities;

namespace QuantConnect.Tests.Common.Util
{
    [TestFixture]
    public class MarketHoursDateOverridesTests
    {
        // Use Lean's UTC -> LocalTimeKeeper -> SecurityExchange path. These are
        // synthetic dated sessions, not claims about Sunday futures trading.
        [TestCase("America/Chicago", "2026-01-02", "17:00", "2026-01-02T22:59:59", "2026-01-02T16:59:59", false)]
        [TestCase("America/Chicago", "2026-01-02", "17:00", "2026-01-02T23:00:00", "2026-01-02T17:00:00", true)]
        [TestCase("America/New_York", "2026-07-02", "18:00", "2026-07-02T22:00:00", "2026-07-02T18:00:00", true)]
        [TestCase("America/New_York", "2026-07-02", "18:00", "2026-07-03T03:59:59", "2026-07-02T23:59:59", true)]
        [TestCase("America/New_York", "2026-07-02", "18:00", "2026-07-03T04:00:00", "2026-07-03T00:00:00", false)]
        [TestCase("America/Chicago", "2026-03-08", "01:00", "2026-03-08T07:59:59", "2026-03-08T01:59:59", true)]
        [TestCase("America/Chicago", "2026-03-08", "01:00", "2026-03-08T08:00:00", "2026-03-08T03:00:00", false)]
        [TestCase("America/Chicago", "2026-11-01", "01:00", "2026-11-01T06:30:00", "2026-11-01T01:30:00", true)]
        [TestCase("America/Chicago", "2026-11-01", "01:00", "2026-11-01T07:30:00", "2026-11-01T01:30:00", true)]
        [TestCase("America/Chicago", "2026-11-01", "01:00", "2026-11-01T09:00:00", "2026-11-01T03:00:00", false)]
        public void DatedSessionsUseExchangeLocalDatesThroughLeanTimeKeeper(string zoneId, string date,
            string open, string utc, string expectedLocal, bool expectedOpen)
        {
            var zone = DateTimeZoneProviders.Tzdb[zoneId];
            var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var weekly = new Dictionary<DayOfWeek, LocalMarketHours>();
            foreach (DayOfWeek weekday in Enum.GetValues(typeof(DayOfWeek)))
                weekly[weekday] = LocalMarketHours.ClosedAllDay(weekday);
            var hours = new SecurityExchangeHours(zone, new[] { day }, weekly,
                new Dictionary<DateTime, TimeSpan>(), new Dictionary<DateTime, TimeSpan>(), null,
                new Dictionary<DateTime, LocalMarketHours>
                {
                    [day] = new LocalMarketHours(day.DayOfWeek, TimeSpan.Parse(open, CultureInfo.InvariantCulture),
                        open == "01:00" ? TimeSpan.FromHours(3) : Time.OneDay)
                });
            var keeper = new TimeKeeper(DateTime.Parse(utc, CultureInfo.InvariantCulture), zone);
            var exchange = new SecurityExchange(hours);
            exchange.SetLocalDateTimeFrontierProvider(keeper.GetLocalTimeKeeper(zone));
            Assert.AreEqual(DateTime.Parse(expectedLocal, CultureInfo.InvariantCulture), exchange.LocalTime);
            Assert.AreEqual(expectedOpen, exchange.ExchangeOpen);
        }

        [TestCase(2006, 3, 20, 23)] // US DST started in April before 2007.
        [TestCase(2007, 3, 20, 22)]
        [TestCase(2026, 1, 20, 23)]
        [TestCase(2026, 7, 20, 22)]
        public void CentralScheduleConversionUsesHistoricalTzdbRules(int year, int month, int day, int utcHour)
        {
            var central = new DateTime(year, month, day, 17, 0, 0);
            var chicago = DateTimeZoneProviders.Tzdb["America/Chicago"];
            var newYork = DateTimeZoneProviders.Tzdb["America/New_York"];
            var eastern = central.ConvertTo(chicago, newYork, strict: true);
            Assert.AreEqual(central.AddHours(1), eastern);
            Assert.AreEqual(new DateTime(year, month, day, utcHour, 0, 0), eastern.ConvertToUtc(newYork, strict: true));
        }

        private static MarketHoursDatabase CreateDatabase(Action<JObject> configure = null)
        {
            // A dated Globex evening must reopen despite an inherited holiday.
            var common = new JObject
            {
                ["dataTimeZone"] = "UTC", ["exchangeTimeZone"] = "America/Chicago",
                ["holidays"] = new JArray("12/25/2023"),
                ["bankHolidays"] = new JArray("12/25/2023"),
                ["monday"] = JArray.Parse("[{\"start\":\"00:00:00\",\"end\":\"16:00:00\",\"state\":\"market\"}]")
            };
            var specific = (JObject)common.DeepClone();
            specific["holidays"] = new JArray();
            specific["earlyCloses"] = JObject.Parse("{\"12/25/2023\":\"12:00:00\"}");
            specific["lateOpens"] = JObject.Parse("{\"12/25/2023\":\"20:00:00\"}");
            specific["marketHoursOverrides"] = JObject.Parse(@"{
                '12/25/2023': [{'start':'17:00:00','end':'1.00:00:00','state':'postmarket'}],
                '12/26/2023': [{'start':'00:00:00','end':'12:00:00','state':'market'}],
                '1/1/2024': []
            }");
            var json = new JObject { ["entries"] = new JObject
            {
                ["Future-cbot-[*]"] = common, ["Future-cbot-ZN"] = specific
            }};
            configure?.Invoke(json);
            return json.ToObject<MarketHoursDatabase>();
        }

        [Test]
        public void DatedEveningOverridesInheritedClosureWithoutChangingExpiryExclusions()
        {
            var db = CreateDatabase();
            var h = db.GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours;
            var day = new DateTime(2023, 12, 25);
            Assert.IsFalse(h.IsOpen(day.AddHours(17).AddTicks(-1), true));
            Assert.IsTrue(h.IsOpen(day.AddHours(17), true));
            Assert.IsFalse(h.IsOpen(day.AddHours(17), false));
            Assert.IsTrue(h.IsOpen(day.AddHours(23), true));
            Assert.IsTrue(h.Holidays.Contains(day));
            Assert.IsTrue(h.BankHolidays.Contains(day));
            Assert.IsFalse(db.GetEntry("cbot", "ZB", SecurityType.Future).ExchangeHours.IsOpen(day.AddHours(18), true));
            Assert.IsTrue(db.GetEntry("cbot", "OZN", SecurityType.FutureOption).ExchangeHours.IsOpen(day.AddHours(18), true));
        }

        [Test]
        public void DatedHoursDriveNextOpenCloseAndMidnightContinuity()
        {
            var h = CreateDatabase().GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours;
            var day = new DateTime(2023, 12, 25);
            Assert.AreEqual(day.AddHours(17), h.GetNextMarketOpen(day.AddHours(12), true));
            Assert.AreEqual(day.AddDays(1).AddHours(12), h.GetNextMarketClose(day.AddHours(18), true));
            Assert.IsTrue(h.IsOpen(day.AddDays(1), true));
            Assert.IsTrue(h.IsOpen(day.AddDays(1).AddHours(12).AddTicks(-1), true));
            Assert.IsFalse(h.IsOpen(day.AddDays(1).AddHours(12), true));
            Assert.IsFalse(h.IsDateOpen(new DateTime(2024, 1, 1), true));
        }

        [Test]
        public void SerializationRetainsDatedHoursAndExplicitClosedDay()
        {
            var original = CreateDatabase();
            var restored = JsonConvert.DeserializeObject<MarketHoursDatabase>(JsonConvert.SerializeObject(original));
            var h = restored.GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours;
            Assert.IsTrue(h.IsOpen(new DateTime(2023, 12, 25, 18, 0, 0), true));
            Assert.IsFalse(h.IsDateOpen(new DateTime(2024, 1, 1), true));
            Assert.IsTrue(h.BankHolidays.Contains(new DateTime(2023, 12, 25)));
        }

        [Test]
        public void RegularOnlyOpenDoesNotContinueFromPreviousExtendedSegment()
        {
            var h = CreateDatabase().GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours;
            Assert.AreEqual(new DateTime(2023, 12, 26), h.GetNextMarketOpen(new DateTime(2023, 12, 25, 18, 0, 0), false));
        }

        [Test]
        public void RegularOnlyCloseDoesNotContinueIntoNextExtendedSegment()
        {
            var h = CreateDatabase(json =>
            {
                var dated = json["entries"]["Future-cbot-ZN"]["marketHoursOverrides"];
                dated["12/25/2023"][0]["state"] = "market";
                dated["12/26/2023"][0]["state"] = "premarket";
            }).GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours;
            Assert.AreEqual(new DateTime(2023, 12, 26), h.GetNextMarketClose(new DateTime(2023, 12, 25, 18, 0, 0), false));
        }

        [Test]
        public void WeeklyScheduleEditsAndCachedUpdatesRetainDatedHours()
        {
            var source = CreateDatabase().GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours;
            var exchange = new SecurityExchange(source);
            exchange.SetMarketHours(new[] { new MarketHoursSegment(MarketHoursState.Market, TimeSpan.FromHours(9), TimeSpan.FromHours(16)) }, DayOfWeek.Monday);
            var day = new DateTime(2023, 12, 25);
            Assert.IsTrue(exchange.Hours.IsOpen(day.AddHours(18), true));
            Assert.IsTrue(exchange.Hours.BankHolidays.Contains(day));
            var target = SecurityExchangeHours.AlwaysOpen(source.TimeZone);
            Assert.IsTrue(target.IsOpen(day.AddHours(12), true));
            target.Update(source);
            Assert.IsFalse(target.IsOpen(day.AddHours(12), true));
            Assert.IsTrue(target.IsOpen(day.AddHours(18), true));
            Assert.IsTrue(target.BankHolidays.Contains(day));
        }

        [Test]
        public void DatedClosuresPreventAlwaysOpenShortcut()
        {
            var db = CreateDatabase(json =>
            {
                var entry = (JObject)json["entries"]["Future-cbot-ZN"];
                foreach (var day in new[] { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" })
                    entry[day] = JArray.Parse("[{'start':'00:00:00','end':'1.00:00:00','state':'market'}]");
            });
            Assert.IsFalse(db.GetEntry("cbot", "ZN", SecurityType.Future).ExchangeHours.IsMarketAlwaysOpen);
        }

        [TestCase("[{'start':'18:00:00','end':'17:00:00','state':'market'}]")]
        [TestCase("[{'start':'00:00:00','end':'1.01:00:00','state':'market'}]")]
        [TestCase("[{'start':'17:00:00','end':'20:00:00','state':'market'},{'start':'18:00:00','end':'21:00:00','state':'market'}]")]
        [TestCase("null")]
        public void InvalidDatedSegmentsFailRatherThanFallingBackToWildcard(string segments)
        {
            Assert.Catch(() => CreateDatabase(json =>
                json["entries"]["Future-cbot-ZN"]["marketHoursOverrides"]["12/25/2023"] = JToken.Parse(segments)));
        }
    }
}
