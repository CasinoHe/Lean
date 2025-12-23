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

using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;

namespace QuantConnect.Interfaces
{
    /// <summary>
    /// Provides algorithm-facing access to add/remove data feed subscriptions.
    /// This is implemented by the engine data manager and is intentionally kept in QuantConnect.Common
    /// so algorithm code can request additional subscriptions without referencing engine types.
    /// </summary>
    public interface IAlgorithmDataFeedSubscriptionManager
    {
        /// <summary>
        /// Ensures the requested subscription exists in the data feed.
        /// </summary>
        /// <param name="request">Defines the subscription request</param>
        /// <returns>True if the subscription exists after the call, false otherwise</returns>
        bool EnsureSubscription(SubscriptionRequest request);

        /// <summary>
        /// Removes a subscription request for the specified universe/configuration.
        /// </summary>
        /// <param name="configuration">The subscription configuration to remove</param>
        /// <param name="universe">Universe requesting removal. Null removes all universes</param>
        /// <returns>True if the subscription was successfully removed, false otherwise</returns>
        bool RemoveSubscription(SubscriptionDataConfig configuration, Universe universe = null);
    }
}

