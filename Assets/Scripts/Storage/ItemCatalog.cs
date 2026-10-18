using System;
using System.Collections.Generic;
using UnityEngine;

namespace LudumDare60.Storage
{
    /// <summary>Static item metadata for the string IDs stored in <see cref="Inventory"/>. Inventory data stays ID-only.</summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Storage/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string itemId = "item";
            [SerializeField] private string displayName = "Item";
            [SerializeField, TextArea(2, 4)] private string description = string.Empty;
            [SerializeField] private Sprite icon;

            public string ItemId => itemId;
            public string DisplayName => displayName;
            public string Description => description;
            public Sprite Icon => icon;
        }

        [SerializeField] private List<Entry> items = new List<Entry>();

        public IReadOnlyList<Entry> Entries => items;

        public bool TryGetEntry(string itemId, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(itemId)) return false;
            foreach (var candidate in items)
                if (candidate != null && string.Equals(candidate.ItemId, itemId, StringComparison.Ordinal))
                {
                    entry = candidate;
                    return true;
                }
            return false;
        }

        /// <summary>Falls back to the raw ID so missing catalog entries never produce empty UI text.</summary>
        public string GetDisplayName(string itemId)
        {
            if (!TryGetEntry(itemId, out var entry) || string.IsNullOrWhiteSpace(entry.DisplayName)) return itemId;
            return entry.DisplayName;
        }

        public Sprite GetIcon(string itemId) => TryGetEntry(itemId, out var entry) ? entry.Icon : null;

        public string GetDescription(string itemId) => TryGetEntry(itemId, out var entry) ? entry.Description : string.Empty;
    }
}
