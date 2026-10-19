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
}
