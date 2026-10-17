using LudumDare60.Salvage;
using UnityEngine;

namespace LudumDare60.Storage
{
    /// <summary>Credits successful salvage attempts to an explicitly assigned inventory.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SalvageController))]
    [AddComponentMenu("Storage/Salvage Inventory Bridge")]
    public sealed class SalvageInventoryBridge : MonoBehaviour
    {
        [SerializeField] private SalvageController salvageController;
        [SerializeField] private InventoryController inventoryController;

        private void OnEnable()
        {
            if (salvageController == null) salvageController = GetComponent<SalvageController>();
            if (inventoryController == null)
            {
                Debug.LogError("Assign an Inventory Controller before enabling the salvage inventory bridge.", this);
                return;
            }
            salvageController.Completed += OnCompleted;
            // Re-enabling can recover the retained completion. Attempt IDs prevent double credit.
            OnCompleted(salvageController.Result);
        }

        private void OnDisable()
        {
            if (salvageController != null) salvageController.Completed -= OnCompleted;
        }

        public bool TryCollect(SalvageResult result)
        {
            if (result == null || !result.IsSuccess || inventoryController == null) return false;
            // A failed load blocks rewards as well as writes: its transaction history is unavailable.
            if (!inventoryController.CanSave) return false;
            return inventoryController.Items.TryAddOnce(result.AttemptId, result.RewardId, result.RewardQuantity);
        }

        private void OnCompleted(SalvageResult result)
        {
            if (result == null || !result.IsSuccess || inventoryController == null) return;
            if (inventoryController.Items.HasProcessedReward(result.AttemptId)) return;
            if (!TryCollect(result))
                Debug.LogError($"Could not store salvage reward for attempt {result.AttemptId}. Check inventory persistence and quantity limits.", this);
        }
    }
}
