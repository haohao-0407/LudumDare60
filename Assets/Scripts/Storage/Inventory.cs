using System;
using System.Collections.Generic;

namespace LudumDare60.Storage
{
    /// <summary>Single-threaded item storage without Unity, file or presentation dependencies.</summary>
    public sealed class Inventory
    {
        private readonly Dictionary<string, int> items = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<Guid> processedRewardIds = new HashSet<Guid>();

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

        /// <summary>All mutations finish before notification. Listeners must not throw.</summary>
        public event Action Changed;

        public bool TryAdd(string itemId, int quantity)
        {
            if (!CanAdd(itemId, quantity)) return false;
            items[itemId] = GetQuantity(itemId) + quantity;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Atomically adds a reward and remembers its ID for this inventory's lifetime.</summary>
        public bool TryAddOnce(Guid rewardId, string itemId, int quantity)
        {
            if (rewardId == Guid.Empty || processedRewardIds.Contains(rewardId) || !CanAdd(itemId, quantity))
                return false;
            items[itemId] = GetQuantity(itemId) + quantity;
            processedRewardIds.Add(rewardId);
            Changed?.Invoke();
            return true;
        }

        public bool HasProcessedReward(Guid rewardId) => processedRewardIds.Contains(rewardId);

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
            Changed?.Invoke();
            return true;
        }

        public bool TryDelete(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || !items.Remove(itemId)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Clears stock, preserving reward history so spent/discarded rewards cannot be claimed again.</summary>
        public void Clear()
        {
            if (items.Count == 0) return;
            items.Clear();
            Changed?.Invoke();
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
    }
}
