using System;

namespace LudumDare60.Storage
{
    /// <summary>A configurable positive item quantity used by costs and material yields.</summary>
    [Serializable]
    public struct InventoryCost
    {
        public string ItemId;
        public int Quantity;

        public InventoryCost(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    /// <summary>An immutable snapshot of one item stack. IDs resolve through the item catalog.</summary>
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
}
