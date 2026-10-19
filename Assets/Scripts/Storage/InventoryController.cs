using System;
using UnityEngine;

namespace LudumDare60.Storage
{
    /// <summary>Owns an in-memory inventory for this object's lifetime. Access CRUD through Items.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Storage/Inventory Controller")]
    public sealed class InventoryController : MonoBehaviour
    {
        private Inventory inventory;

        public Inventory Items { get { EnsureInitialized(); return inventory; } }
        public event Action Changed;

        private void Awake() => EnsureInitialized();

        private void EnsureInitialized()
        {
            if (inventory != null) return;
            inventory = new Inventory();
            inventory.Changed += OnInventoryChanged;
        }

        private void OnInventoryChanged() => Changed?.Invoke();

        private void OnDestroy()
        {
            if (inventory != null) inventory.Changed -= OnInventoryChanged;
        }
    }
}
