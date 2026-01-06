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

using NodaTime;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using HistoryRequest = QuantConnect.Data.HistoryRequest;

namespace QuantConnect.Lean.Engine.HistoricalData
{
    /// <summary>
    /// History provider for live mode that reads from local files for any period strictly before today (exchange time zone),
    /// and uses the brokerage history provider for the portion of the request that overlaps today.
    /// </summary>
    public class HybridLocalFileBrokerageHistoryProvider : HistoryProviderBase, IBrokerageHistoryProvider
    {
        private readonly SubscriptionDataReaderHistoryProvider _localFileHistoryProvider = new();
        private readonly BrokerageHistoryProvider _brokerageHistoryProvider = new();
        private bool _initialized;
        private bool _brokerageSet;

        /// <summary>
        /// Gets the total number of data points emitted by this history provider
        /// </summary>
        public override int DataPointCount => _localFileHistoryProvider.DataPointCount + _brokerageHistoryProvider.DataPointCount;

        /// <summary>
        /// Sets the brokerage to be used for history requests covering today
        /// </summary>
        public void SetBrokerage(IBrokerage brokerage)
        {
            _brokerageHistoryProvider.SetBrokerage(brokerage);
            _brokerageSet = true;
        }

        /// <summary>
        /// Initializes this history provider to work for the specified job
        /// </summary>
        public override void Initialize(HistoryProviderInitializeParameters parameters)
        {
            if (_initialized)
            {
                throw new InvalidOperationException($"{nameof(HybridLocalFileBrokerageHistoryProvider)} can only be initialized once");
            }
            _initialized = true;

            if (!_brokerageSet)
            {
                throw new InvalidOperationException($"{nameof(HybridLocalFileBrokerageHistoryProvider)} requires a brokerage via {nameof(SetBrokerage)} before initialization");
            }

            WireEvents(_localFileHistoryProvider);
            WireEvents(_brokerageHistoryProvider);

            _localFileHistoryProvider.Initialize(parameters);
            _brokerageHistoryProvider.Initialize(parameters);
        }

        /// <summary>
        /// Gets the history for the requested securities, routing pre-today requests to local files and today's portion to the brokerage
        /// </summary>
        public override IEnumerable<Slice> GetHistory(IEnumerable<HistoryRequest> requests, DateTimeZone sliceTimeZone)
        {
            if (requests == null)
            {
                return null;
            }

            var requestList = requests as IReadOnlyList<HistoryRequest> ?? requests.ToList();
            if (requestList.Count == 0)
            {
                return Enumerable.Empty<Slice>();
            }

            var localRequests = new List<HistoryRequest>(requestList.Count);
            var brokerageRequests = new List<HistoryRequest>(requestList.Count);

            foreach (var request in requestList)
            {
                if (TrySplitRequestAtTodayStart(request, out var beforeToday, out var todayPortion))
                {
                    if (beforeToday != null)
                    {
                        localRequests.Add(beforeToday);
                    }
                    if (todayPortion != null)
                    {
                        brokerageRequests.Add(todayPortion);
                    }
                }
                else
                {
                    localRequests.Add(request);
                }
            }

            var enumerators = new List<IEnumerator<Slice>>(2);

            if (localRequests.Count > 0)
            {
                var history = _localFileHistoryProvider.GetHistory(localRequests, sliceTimeZone);
                if (history != null)
                {
                    enumerators.Add(history.GetEnumerator());
                }
            }

            if (brokerageRequests.Count > 0)
            {
                var history = _brokerageHistoryProvider.GetHistory(brokerageRequests, sliceTimeZone);
                if (history != null)
                {
                    enumerators.Add(history.GetEnumerator());
                }
            }

            if (enumerators.Count == 0)
            {
                return Enumerable.Empty<Slice>();
            }

            return EnumerateMerged(enumerators);
        }

        private static IEnumerable<Slice> EnumerateMerged(List<IEnumerator<Slice>> enumerators)
        {
            using var synchronizer = new SynchronizingSliceEnumerator(enumerators);
            Slice latestMergeSlice = null;
            while (synchronizer.MoveNext())
            {
                if (synchronizer.Current == null)
                {
                    continue;
                }

                if (latestMergeSlice == null)
                {
                    latestMergeSlice = synchronizer.Current;
                    continue;
                }

                if (synchronizer.Current.UtcTime > latestMergeSlice.UtcTime)
                {
                    yield return latestMergeSlice;
                    latestMergeSlice = synchronizer.Current;
                }
                else
                {
                    latestMergeSlice.MergeSlice(synchronizer.Current);
                }
            }

            if (latestMergeSlice != null)
            {
                yield return latestMergeSlice;
            }
        }

        private static bool TrySplitRequestAtTodayStart(HistoryRequest request, out HistoryRequest beforeToday, out HistoryRequest todayPortion)
        {
            beforeToday = null;
            todayPortion = null;

            if (request == null)
            {
                return false;
            }

            var exchangeTimeZone = request.ExchangeHours?.TimeZone ?? TimeZones.Utc;
            var nowLocal = DateTime.UtcNow.ConvertFromUtc(exchangeTimeZone);
            var todayStartLocal = nowLocal.Date;
            var todayStartUtc = todayStartLocal.ConvertToUtc(exchangeTimeZone);

            if (request.EndTimeUtc <= todayStartUtc)
            {
                beforeToday = request;
                return true;
            }

            if (request.StartTimeUtc >= todayStartUtc)
            {
                todayPortion = request;
                return true;
            }

            // Request spans across midnight. Split into [start, todayStart) and [todayStart, end).
            beforeToday = new HistoryRequest(request, request.Symbol, request.StartTimeUtc, todayStartUtc);
            todayPortion = new HistoryRequest(request, request.Symbol, todayStartUtc, request.EndTimeUtc);
            return true;
        }

        private void WireEvents(IHistoryProvider provider)
        {
            provider.InvalidConfigurationDetected += (sender, args) => { OnInvalidConfigurationDetected(args); };
            provider.NumericalPrecisionLimited += (sender, args) => { OnNumericalPrecisionLimited(args); };
            provider.StartDateLimited += (sender, args) => { OnStartDateLimited(args); };
            provider.DownloadFailed += (sender, args) => { OnDownloadFailed(args); };
            provider.ReaderErrorDetected += (sender, args) => { OnReaderErrorDetected(args); };
        }
    }
}
