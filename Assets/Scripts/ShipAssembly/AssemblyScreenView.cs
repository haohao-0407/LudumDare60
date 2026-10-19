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
        private AssemblyKeyGuide keyGuide;

        private void OnEnable()
        {
            if (PuzzleImage != null) keyGuide = AssemblyKeyGuide.Ensure(transform,Hint != null ? Hint.font : null);
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
            UpdateGuide();
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
            bool started = Controller != null && Controller.HasActiveSession;
            bool completed = Controller != null && Controller.IsSelectedCompleted;
            string reason = "Assembly controller is not configured.";
            bool canStart = Controller != null && Controller.CanStartSelected(out reason);

            SetText(Title, recipe != null ? recipe.DisplayName : "SHIP ASSEMBLY");
            SetText(ProgressLabel, "ASSEMBLY " + Mathf.RoundToInt((Controller != null ? Controller.Progress : 0f) * 100f) + "%");
            SetText(StartLabel, completed ? "REPAIRED" : started ? "RESUME" : "START ASSEMBLY");
            if (StartButton != null) StartButton.interactable = !completed && canStart;
            bool canChangeRecipe = Controller != null && Controller.Recipes != null && Controller.Recipes.Length > 1;
            if (PreviousButton != null) PreviousButton.interactable = canChangeRecipe;
            if (NextButton != null) NextButton.interactable = canChangeRecipe;
            if (ResetButton != null) ResetButton.interactable = started && !completed;
            if (DetachButton != null) DetachButton.interactable = started && !completed;

            if (RequirementsPanel != null) RequirementsPanel.SetActive(!started);
            if (PuzzlePanel != null) PuzzlePanel.SetActive(started);
            if (keyGuide != null)
            {
                keyGuide.gameObject.SetActive(started);
                keyGuide.ShowInput = Controller != null && Controller.InputEnabled;
            }
            LayoutControls(started);
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
            if (completed) message = "REPAIR COMPLETE - SYSTEM RESTORED";
            SetText(Status, message);
            SetText(Hint, completed ? "REPAIR RECORDED  /  CHOOSE ANOTHER SYSTEM"
                : !started ? "MATERIALS ARE CONSUMED ONCE WHEN ASSEMBLY STARTS" : string.Empty);
            UpdateGuide();
        }

        private void UpdateGuide()
        {
            var puzzle = Controller != null ? Controller.ActivePuzzle : null;
            if (keyGuide == null || puzzle == null) return;
            keyGuide.ShowInput = Controller.InputEnabled;
            keyGuide.SetState(puzzle.Selected != null ? puzzle.Selected.moduleLabel : "BODY", puzzle.Modules.Length,
                puzzle.ConnectionCount, puzzle.RequiredConnectionCount, Controller.IsSelectedCompleted, Controller.InputEnabled);
        }

        private void LayoutControls(bool started)
        {
            // Reserve a footer for the diagram without placing it over movable parts or mouse buttons.
            if (PuzzlePanel != null) SetVertical(PuzzlePanel.transform, started ? .325f : .25f, .82f);
            if (Status != null) SetVertical(Status.transform, started ? .28f : .19f, started ? .32f : .245f);
            if (ProgressLabel != null) ProgressLabel.gameObject.SetActive(!started);
            if (Hint != null) Hint.gameObject.SetActive(!started);
            foreach (var button in new[] { StartButton, ResetButton, DetachButton })
                if (button != null) SetVertical(button.transform, started ? .23f : .075f, started ? .275f : .14f);
        }

        private static void SetVertical(Transform target, float min, float max)
        {
            var rect = target as RectTransform;
            if (rect == null) return;
            rect.anchorMin = new Vector2(rect.anchorMin.x, min);
            rect.anchorMax = new Vector2(rect.anchorMax.x, max);
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
