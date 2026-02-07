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

namespace QuantConnect.Lean.Engine.DataFeeds
{
    /// <summary>
    /// A time provider which updates 'now' time based on the current data emit time of all subscriptions
    /// </summary>
    /// <remarks>This class is not thread safe but there is no need for it to be since it's only consumed by the
    /// <see cref="SubscriptionSynchronizer"/></remarks>
    public class SubscriptionFrontierTimeProvider : ITimeProvider
    {
        private static readonly long MaxDateTimeTicks = DateTime.MaxValue.Ticks;
        private DateTime _utcNow;
        private readonly IDataFeedSubscriptionManager _subscriptionManager;

        /// <summary>
        /// Creates a new instance of the SubscriptionFrontierTimeProvider
        /// </summary>
        /// <param name="utcNow">Initial UTC now time</param>
        /// <param name="subscriptionManager">Subscription manager. Will be used to obtain current subscriptions</param>
        public SubscriptionFrontierTimeProvider(DateTime utcNow, IDataFeedSubscriptionManager subscriptionManager)
        {
            _utcNow = utcNow;
            _subscriptionManager = subscriptionManager;
        }

        /// <summary>
        /// Gets the current time in UTC
        /// </summary>
        /// <returns>The current time in UTC</returns>
        public DateTime GetUtcNow()
        {
            UpdateCurrentTime();
            return _utcNow;
        }

        /// <summary>
        /// Sets the current time calculated as the minimum current data emit time of all the subscriptions.
        /// If there are no subscriptions current time will remain unchanged
        /// </summary>
        private void UpdateCurrentTime()
        {
            long earlyBirdTicks = MaxDateTimeTicks;
            foreach (var subscription in _subscriptionManager.DataFeedSubscriptions)
            {
                if (subscription.Current == null && !subscription.IsUniverseSelectionSubscription)
                {
                    // If the subscription start time is in the future, let it contribute to the frontier.
                    // This prevents the frontier from being anchored by coarser subscriptions (for example, hourly)
                    // and then ingesting all intraday data in a single catch-up slice.
                    if (subscription.UtcStartTime > _utcNow)
                    {
                        earlyBirdTicks = Math.Min(earlyBirdTicks, subscription.UtcStartTime.Ticks);
                    }
                    // this if should just be 'subscription.Current == null' but its affected by GH issue 3914
                    // for non-universe subscriptions we only prime when the start time has been reached
                    else
                    {
                        subscription.MoveNext();
                    }
                }
                else if (subscription.Current == null
                    // UserDefinedUniverse, through the AddData calls
                    // will add new universe selection data points when it has to
                    && subscription.IsUniverseSelectionSubscription)
                {
                    subscription.MoveNext();
                }

                if (subscription.Current != null)
                {
                    if (earlyBirdTicks == MaxDateTimeTicks)
                    {
                        earlyBirdTicks = subscription.Current.EmitTimeUtc.Ticks;
                    }
                    else
                    {
                        // take the earliest between the next piece of data or the current earliest bird
                        earlyBirdTicks = Math.Min(earlyBirdTicks, subscription.Current.EmitTimeUtc.Ticks);
                    }
                }
            }

            if (earlyBirdTicks != MaxDateTimeTicks)
            {
                _utcNow = new DateTime(Math.Max(earlyBirdTicks, _utcNow.Ticks), DateTimeKind.Utc);
            }
        }
    }
}
