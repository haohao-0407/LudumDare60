using System;
using System.Collections.Generic;

namespace LudumDare60.Storage
{
    /// <summary>Single-threaded item storage without Unity, file or presentation dependencies.</summary>
    public sealed class Inventory
    {
        private readonly Dictionary<string, int> items = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<Guid> processedRewardIds = new HashSet<Guid>();
        private readonly HashSet<Guid> processedConsumptionIds = new HashSet<Guid>();

        public int DistinctItemCount => items.Count;
        public long TotalQuantity
        {
            get
            {
                long total = 0;
                foreach (var quantity in items.Values) total += quantity;
                return total;
            }
        }

        /// <summary>All mutations finish before notification. Listener failures cannot undo committed transactions.</summary>
        public event Action Changed;
        /// <summary>Reports a failed Changed listener without preventing other listeners or the transaction result.</summary>
        public event Action<Exception> NotificationFailed;
        /// <summary>Retains the latest listener failure even when no diagnostics listener is registered.</summary>
        public Exception LastNotificationException { get; private set; }

        public bool TryAdd(string itemId, int quantity)
        {
            if (!CanAdd(itemId, quantity)) return false;
            items[itemId] = GetQuantity(itemId) + quantity;
            NotifyChanged();
            return true;
        }

        /// <summary>Atomically adds a reward and remembers its ID for this inventory's lifetime.</summary>
        public bool TryAddOnce(Guid rewardId, string itemId, int quantity)
        {
            if (rewardId == Guid.Empty || processedRewardIds.Contains(rewardId) || !CanAdd(itemId, quantity))
                return false;
            items[itemId] = GetQuantity(itemId) + quantity;
            processedRewardIds.Add(rewardId);
            NotifyChanged();
            return true;
        }

        public bool HasProcessedReward(Guid rewardId) => processedRewardIds.Contains(rewardId);

        /// <summary>Adds every yield together, or none; shares reward deduplication with TryAddOnce.</summary>
        public bool TryAddBatchOnce(Guid rewardId, IReadOnlyList<InventoryCost> yields)
        {
            if (rewardId == Guid.Empty || processedRewardIds.Contains(rewardId)
                || !TryCombine(yields, out var combined, out _)) return false;

            foreach (var yield in combined)
                if (!CanAdd(yield.Key, yield.Value)) return false;

            foreach (var yield in combined)
                items[yield.Key] = GetQuantity(yield.Key) + yield.Value;
            processedRewardIds.Add(rewardId);
            NotifyChanged();
            return true;
        }

        /// <summary>Validates all costs, combining repeated IDs before checking available stock.</summary>
        public bool CanConsume(IReadOnlyList<InventoryCost> costs, out string reason)
        {
            if (!TryCombine(costs, out var combined, out reason)) return false;
            return HasRequiredStock(combined, out reason);
        }

        /// <summary>Commits all costs once. Failed checks never spend stock or claim the operation ID.</summary>
        public bool TryConsumeOnce(Guid operationId, IReadOnlyList<InventoryCost> costs, out string reason)
        {
            if (operationId == Guid.Empty)
            {
                reason = "A non-empty assembly operation ID is required.";
                return false;
            }
            if (processedConsumptionIds.Contains(operationId))
            {
                reason = "Materials have already been consumed for this operation.";
                return false;
            }
            if (!TryCombine(costs, out var combined, out reason) || !HasRequiredStock(combined, out reason))
                return false;

            foreach (var cost in combined)
            {
                int remaining = items[cost.Key] - cost.Value;
                if (remaining == 0) items.Remove(cost.Key);
                else items[cost.Key] = remaining;
            }
            processedConsumptionIds.Add(operationId);
            reason = string.Empty;
            NotifyChanged();
            return true;
        }

        public bool HasProcessedConsumption(Guid operationId) => processedConsumptionIds.Contains(operationId);

        /// <summary>Removes exactly the requested amount; insufficient stock leaves all data unchanged.</summary>
        public bool TryRemove(string itemId, int quantity)
        {
            if (quantity <= 0 || !TryGetItem(itemId, out var item) || item.Quantity < quantity) return false;
            return TrySetQuantity(itemId, item.Quantity - quantity);
        }

        /// <summary>Changes an existing stack. Zero deletes it; negative quantities are rejected.</summary>
        public bool TrySetQuantity(string itemId, int quantity)
        {
            if (quantity < 0 || !TryGetItem(itemId, out var item)) return false;
            if (quantity == item.Quantity) return true;
            if (quantity == 0) items.Remove(itemId);
            else items[itemId] = quantity;
            NotifyChanged();
            return true;
        }

        public bool TryDelete(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || !items.Remove(itemId)) return false;
            NotifyChanged();
            return true;
        }

        /// <summary>Clears stock, preserving transaction history so rewards and costs cannot be replayed.</summary>
        public void Clear()
        {
            if (items.Count == 0) return;
            items.Clear();
            NotifyChanged();
        }

        public int GetQuantity(string itemId)
            => itemId != null && items.TryGetValue(itemId, out var quantity) ? quantity : 0;

        public bool TryGetItem(string itemId, out InventoryItem item)
        {
            item = default;
            if (itemId == null || !items.TryGetValue(itemId, out var quantity)) return false;
            item = new InventoryItem(itemId, quantity);
            return true;
        }

        public IReadOnlyList<InventoryItem> GetItems() => FindItems(null);

        /// <summary>Case-insensitive ID substring search, sorted by ID. Empty text returns all stacks.</summary>
        public IReadOnlyList<InventoryItem> FindItems(string searchText)
        {
            var result = new List<InventoryItem>();
            foreach (var entry in items)
                if (string.IsNullOrEmpty(searchText) || entry.Key.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(new InventoryItem(entry.Key, entry.Value));
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.ItemId, right.ItemId));
            return result.AsReadOnly();
        }

        private bool CanAdd(string itemId, int quantity)
            => !string.IsNullOrWhiteSpace(itemId) && quantity > 0 && GetQuantity(itemId) <= int.MaxValue - quantity;

        private bool HasRequiredStock(Dictionary<string, int> costs, out string reason)
        {
            foreach (var cost in costs)
                if (GetQuantity(cost.Key) < cost.Value)
                {
                    reason = $"Insufficient {cost.Key}: {GetQuantity(cost.Key)}/{cost.Value}.";
                    return false;
                }
            reason = string.Empty;
            return true;
        }

        private static bool TryCombine(IReadOnlyList<InventoryCost> values,
            out Dictionary<string, int> combined, out string reason)
        {
            combined = new Dictionary<string, int>(StringComparer.Ordinal);
            if (values == null || values.Count == 0)
            {
                reason = "At least one item quantity is required.";
                return false;
            }
            for (int i = 0; i < values.Count; i++)
            {
                InventoryCost value = values[i];
                if (string.IsNullOrWhiteSpace(value.ItemId) || value.Quantity <= 0)
                {
                    reason = $"Item quantity at index {i} must have an ID and a positive quantity.";
                    return false;
                }
                combined.TryGetValue(value.ItemId, out int existing);
                if (existing > int.MaxValue - value.Quantity)
                {
                    reason = $"Combined quantity for {value.ItemId} exceeds the supported limit.";
                    return false;
                }
                combined[value.ItemId] = existing + value.Quantity;
            }
            reason = string.Empty;
            return true;
        }

        private void NotifyChanged()
        {
            Action listeners = Changed;
            if (listeners == null) return;
            foreach (Action listener in listeners.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception)
                {
                    LastNotificationException = exception;
                    Action<Exception> errorListeners = NotificationFailed;
                    if (errorListeners == null) continue;
                    foreach (Action<Exception> errorListener in errorListeners.GetInvocationList())
                    {
                        try { errorListener(exception); }
                        catch (Exception) { /* Reporting cannot invalidate a committed transaction. */ }
                    }
                }
            }
        }
    }
}
