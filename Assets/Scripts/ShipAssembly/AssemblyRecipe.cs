using LudumDare60.Storage;
using UnityEngine;

namespace LudumDare60.ShipAssembly
{
    public enum RepairDeviceId
    {
        PowerUnit = 0,
        Navigation = 1,
        PropulsionEngine = 2,
        LifeSupport = 3
    }

    [CreateAssetMenu(fileName = "AssemblyRecipe", menuName = "Ship/Assembly Recipe")]
    public sealed class AssemblyRecipe : ScriptableObject
    {
        public string RecipeId;
        public string DisplayName;
        public RepairDeviceId Device;
        public InventoryCost[] Materials;
        public GameObject PuzzlePrefab;
        [Min(0.1f)] public float OrthographicSize = 5.4f;
        public Vector3 PuzzleOffset = new Vector3(0f, -1.4f, 0f);
    }
}
