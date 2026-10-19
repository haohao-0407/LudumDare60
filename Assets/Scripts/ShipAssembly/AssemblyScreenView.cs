using System.Text;
using LudumDare60.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudumDare60.ShipAssembly
{
    /// <summary>Renders configurable recipes and a paid session without owning inventory or puzzle state.</summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyScreenView : MonoBehaviour
    {
        public ShipAssemblyController Controller;
        public ItemCatalog Catalog;
        public TMP_Text Title, Requirements, Status, ProgressLabel, StartLabel, Hint;
        public Button StartButton, PreviousButton, NextButton, ResetButton, DetachButton;
        public RawImage PuzzleImage;
        public GameObject RequirementsPanel, PuzzlePanel;

        private ShipAssemblyController subscribedController;
        private InventoryController subscribedInventory;
        private readonly StringBuilder materialText = new StringBuilder(512);

        private void OnEnable()
        {
            if (StartButton != null) StartButton.onClick.AddListener(StartOrResume);
            if (PreviousButton != null) PreviousButton.onClick.AddListener(PreviousRecipe);
            if (NextButton != null) NextButton.onClick.AddListener(NextRecipe);
            if (ResetButton != null) ResetButton.onClick.AddListener(ResetPuzzle);
            if (DetachButton != null) DetachButton.onClick.AddListener(DetachModule);
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (StartButton != null) StartButton.onClick.RemoveListener(StartOrResume);
            if (PreviousButton != null) PreviousButton.onClick.RemoveListener(PreviousRecipe);
            if (NextButton != null) NextButton.onClick.RemoveListener(NextRecipe);
            if (ResetButton != null) ResetButton.onClick.RemoveListener(ResetPuzzle);
            if (DetachButton != null) DetachButton.onClick.RemoveListener(DetachModule);
            Unsubscribe();
            if (Controller != null) Controller.SetFocused(false);
        }

        private void LateUpdate()
        {
            if (subscribedController != Controller || subscribedInventory != (Controller != null ? Controller.Inventory : null))
            {
                Subscribe();
                Refresh();
            }
            // The offscreen camera/texture may be initialized after this view's OnEnable.
            if (PuzzleImage != null && Controller != null && Controller.AssemblyCamera != null
                && PuzzleImage.texture != Controller.AssemblyCamera.targetTexture)
                PuzzleImage.texture = Controller.AssemblyCamera.targetTexture;
        }

        private void Subscribe()
        {
            Unsubscribe();
            subscribedController = Controller;
            if (subscribedController == null) return;
            subscribedController.Changed += Refresh;
            subscribedInventory = subscribedController.Inventory;
            if (subscribedInventory != null) subscribedInventory.Changed += Refresh;
        }

        private void Unsubscribe()
        {
            if (subscribedController != null) subscribedController.Changed -= Refresh;
            if (subscribedInventory != null) subscribedInventory.Changed -= Refresh;
            subscribedController = null;
            subscribedInventory = null;
        }

        public void Refresh()
        {
            var recipe = Controller != null ? Controller.SelectedRecipe : null;
            bool available = Controller != null && Controller.RepairsAvailable;
            bool started = Controller != null && Controller.HasActiveSession;
            bool completed = Controller != null && Controller.IsSelectedCompleted;
            string reason = "Assembly controller is not configured.";
            bool canStart = Controller != null && Controller.CanStartSelected(out reason);

            SetText(Title, recipe != null ? recipe.DisplayName : "SHIP ASSEMBLY");
            SetText(ProgressLabel, "ASSEMBLY " + Mathf.RoundToInt((Controller != null ? Controller.Progress : 0f) * 100f) + "%");
            SetText(StartLabel, !available ? "UNAVAILABLE" : completed ? "REPAIRED" : started ? "RESUME" : "START ASSEMBLY");
            if (StartButton != null) StartButton.interactable = !completed && canStart;
            bool canChangeRecipe = available && Controller.Recipes != null && Controller.Recipes.Length > 1;
            if (PreviousButton != null) PreviousButton.interactable = canChangeRecipe;
            if (NextButton != null) NextButton.interactable = canChangeRecipe;
            if (ResetButton != null) ResetButton.interactable = available && started && !completed;
            if (DetachButton != null) DetachButton.interactable = available && started && !completed;

            if (RequirementsPanel != null) RequirementsPanel.SetActive(available && !started);
            if (PuzzlePanel != null) PuzzlePanel.SetActive(available && started);
            if (PuzzleImage != null && Controller != null && Controller.AssemblyCamera != null)
                PuzzleImage.texture = Controller.AssemblyCamera.targetTexture;

            materialText.Clear();
            if (recipe != null && recipe.Materials != null)
                foreach (var cost in recipe.Materials)
                {
                    int owned = Controller.Inventory != null ? Controller.Inventory.Items.GetQuantity(cost.ItemId) : 0;
                    string name = Catalog != null ? Catalog.GetDisplayName(cost.ItemId) : cost.ItemId;
                    if (materialText.Length > 0) materialText.Append('\n');
                    materialText.Append(name).Append('\n').Append("  ")
                        .Append(owned >= cost.Quantity ? "<color=#9CF0BA>" : "<color=#F2AA78>")
                        .Append("OWNED ").Append(owned).Append("  /  REQUIRED ").Append(cost.Quantity).Append("</color>");
                }
            SetText(Requirements, materialText.Length > 0 ? materialText.ToString() : "No recipe materials configured.");

            string message = Controller != null ? Controller.StatusMessage : reason;
            if (!started && !canStart && !string.IsNullOrEmpty(reason)) message = reason;
            if (!available) message = "REPAIR STATION UNAVAILABLE";
            else if (completed) message = "REPAIR COMPLETE - SYSTEM RESTORED";
            SetText(Status, message);
            var puzzle = Controller != null ? Controller.ActivePuzzle : null;
            int moduleCount = puzzle != null && puzzle.Modules != null ? puzzle.Modules.Length : 0;
            string selectionKeys = moduleCount > 0 ? "1-" + Mathf.Min(moduleCount, 5) + " / TAB SELECT" : "TAB SELECT";
            SetText(Hint, !available ? "REPAIR CONTROLS ARE LOCKED"
                : completed ? "REPAIR RECORDED  /  CHOOSE ANOTHER SYSTEM"
                : !started ? "MATERIALS ARE CONSUMED ONCE WHEN ASSEMBLY STARTS"
                : Controller.InputEnabled
                    ? "DRAG / WASD MOVE   Q/E ROTATE   SHIFT FINE\n" + selectionKeys + "   X DETACH   R RESET   ESC PAUSE"
                    : "CLICK PUZZLE TO OPERATE  /  RIGHT MOUSE TO LOOK\nPROGRESS IS KEPT WHEN YOU SWITCH SCREENS");
        }

        private void StartOrResume()
        {
            if (Controller == null) return;
            Controller.SetFocused(false);
            Controller.TryStartSelected();
            Refresh();
        }

        private void PreviousRecipe() => ChangeRecipe(-1);
        private void NextRecipe() => ChangeRecipe(1);
        private void ChangeRecipe(int direction)
        {
            if (Controller == null || Controller.Recipes == null || Controller.Recipes.Length == 0) return;
            Controller.SetFocused(false);
            int count = Controller.Recipes.Length;
            Controller.SelectRecipe((Controller.SelectedRecipeIndex + direction + count) % count);
        }

        private void ResetPuzzle() { if (Controller != null) Controller.ResetSelectedPuzzle(); }
        private void DetachModule() { if (Controller != null) Controller.DetachSelected(); }
        private static void SetText(TMP_Text label, string value)
        { if (label != null) label.text = value ?? string.Empty; }
    }
}
