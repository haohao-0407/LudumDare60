using UnityEngine;

namespace LudumDare60.Salvage
{
    /// <summary>Create one asset per salvage target/difficulty. IDs belong to the future world/inventory system.</summary>
    [CreateAssetMenu(fileName = "SalvageDefinition", menuName = "Salvage/Definition")]
    public sealed class SalvageDefinition : ScriptableObject
    {
        [SerializeField] private string targetId = "salvage-target";
        [SerializeField] private string rewardId = "scrap";
        [SerializeField, Min(1)] private int rewardQuantityMin = 1;
        [SerializeField, Min(1)] private int rewardQuantityMax = 3;
        [SerializeField] private SalvageSettings settings = new SalvageSettings();

        public string TargetId => targetId;
        public string RewardId => rewardId;

        public SalvageSettings CreateSettings() => settings.CreateValidatedCopy();
        public SalvageTarget CreateTarget() => new SalvageTarget(targetId, rewardId, rewardQuantityMin, rewardQuantityMax);
    }
}
