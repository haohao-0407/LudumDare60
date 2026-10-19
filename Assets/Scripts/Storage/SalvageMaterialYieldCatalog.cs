using System;
using System.Collections.Generic;
using UnityEngine;

namespace LudumDare60.Storage
{
    /// <summary>Maps one recovered salvage item to configurable assembly materials.</summary>
    [CreateAssetMenu(fileName = "SalvageMaterialYields", menuName = "Storage/Salvage Material Yield Catalog")]
    public sealed class SalvageMaterialYieldCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string rewardId;
            [SerializeField] private List<InventoryCost> yields = new List<InventoryCost>();

            public string RewardId => rewardId;
            public IReadOnlyList<InventoryCost> Yields => yields;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Creates a scaled snapshot; invalid or missing mappings leave the reward unclaimed.</summary>
        public bool TryGetYields(string rewardId, int rewardQuantity, out IReadOnlyList<InventoryCost> yields)
        {
            yields = null;
            if (string.IsNullOrWhiteSpace(rewardId) || rewardQuantity <= 0 || entries == null) return false;
            foreach (Entry entry in entries)
            {
                if (entry == null || !string.Equals(entry.RewardId, rewardId, StringComparison.Ordinal)) continue;
                if (entry.Yields == null || entry.Yields.Count == 0) return false;
                var scaled = new List<InventoryCost>(entry.Yields.Count);
                foreach (InventoryCost yield in entry.Yields)
                {
                    if (string.IsNullOrWhiteSpace(yield.ItemId) || yield.Quantity <= 0
                        || yield.Quantity > int.MaxValue / rewardQuantity) return false;
                    scaled.Add(new InventoryCost(yield.ItemId, yield.Quantity * rewardQuantity));
                }
                yields = scaled.AsReadOnly();
                return true;
            }
            return false;
        }
    }
}
