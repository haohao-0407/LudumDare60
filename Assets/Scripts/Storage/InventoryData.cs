using System;
using System.Collections.Generic;

namespace LudumDare60.Storage
{
    /// <summary>An immutable snapshot of one item stack. IDs match SalvageResult.RewardId.</summary>
    public readonly struct InventoryItem
    {
        public string ItemId { get; }
        public int Quantity { get; }

        internal InventoryItem(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    /// <summary>JSON transfer data only; changing this object does not change a live inventory.</summary>
    [Serializable]
    public sealed class InventorySaveData
    {
        public const int CurrentVersion = 1;
        public const string FormatName = "LudumDare60.Inventory";
        public string Format;
        public int Version;
        public List<InventoryItemData> Items;
        public List<string> ProcessedRewardIds;
    }

    [Serializable]
    public sealed class InventoryItemData
    {
        public string ItemId;
        public int Quantity;
    }
}
