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
            [SerializeField, Tooltip("英文/默认名称")] private string displayName = "Item";
            [SerializeField, Tooltip("中文名称。留空则回退到英文")] private string displayNameZh = string.Empty;
            [SerializeField, TextArea(2, 4), Tooltip("英文/默认描述")] private string description = string.Empty;
            [SerializeField, TextArea(2, 4), Tooltip("中文描述。留空则回退到英文")] private string descriptionZh = string.Empty;
            [SerializeField] private Sprite icon;

            public string ItemId => itemId;
            public string DisplayName => displayName;
            public string DisplayNameZh => displayNameZh;
            public string Description => description;
            public string DescriptionZh => descriptionZh;
            public Sprite Icon => icon;

            // 加语言就在这两处各加一列（并在 LocalizationManager 的语言表里登记）。
            public string DisplayNameFor(string language)
                => PickLocalized(displayName, displayNameZh, language);

            public string DescriptionFor(string language)
                => PickLocalized(description, descriptionZh, language);
        }

        private static string PickLocalized(string english, string chinese, string language)
        {
            bool wantChinese = !string.IsNullOrEmpty(language)
                && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            if (wantChinese && !string.IsNullOrWhiteSpace(chinese)) return chinese;
            if (!string.IsNullOrWhiteSpace(english)) return english;
            return chinese;
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
            if (!TryGetEntry(itemId, out var entry)) return itemId;
            var name = entry.DisplayNameFor(Pioneer60.Localization.Loc.Language);
            return string.IsNullOrWhiteSpace(name) ? itemId : name;
        }

        public Sprite GetIcon(string itemId) => TryGetEntry(itemId, out var entry) ? entry.Icon : null;

        public string GetDescription(string itemId)
            => TryGetEntry(itemId, out var entry) ? entry.DescriptionFor(Pioneer60.Localization.Loc.Language) : string.Empty;
    }
}
