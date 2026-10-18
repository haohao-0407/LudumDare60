using System;
using System.Collections.Generic;
using UnityEngine;

namespace LudumDare60.Salvage
{
    /// <summary>Weighted set of salvage targets. One entry is drawn per attempt.</summary>
    [CreateAssetMenu(fileName = "SalvagePool", menuName = "Salvage/Pool")]
    public sealed class SalvagePool : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private SalvageDefinition definition;
            [SerializeField, Min(0f)] private float weight = 1f;

            public SalvageDefinition Definition => definition;
            public float Weight => weight;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Draws by weight. Entries without a definition or with zero weight never win.</summary>
        public SalvageDefinition Pick(int? randomSeed = null)
        {
            var total = 0f;
            foreach (var entry in entries)
                if (IsEligible(entry)) total += entry.Weight;
            if (total <= 0f) return null;

            var random = randomSeed.HasValue ? new System.Random(randomSeed.Value) : new System.Random();
            var roll = (float)random.NextDouble() * total;
            foreach (var entry in entries)
            {
                if (!IsEligible(entry)) continue;
                roll -= entry.Weight;
                if (roll <= 0f) return entry.Definition;
            }

            // Float rounding can leave the roll marginally above zero; fall back to the last eligible entry.
            for (var i = entries.Count - 1; i >= 0; i--)
                if (IsEligible(entries[i])) return entries[i].Definition;
            return null;
        }

        private static bool IsEligible(Entry entry) => entry != null && entry.Definition != null && entry.Weight > 0f;
    }
}
