using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudumDare60.Storage
{
    /// <summary>Read-only cargo manifest backed by the shared in-memory inventory.</summary>
    [DisallowMultipleComponent]
    public sealed class InventoryScreenView : MonoBehaviour
    {
        [SerializeField] private InventoryController inventoryController;
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private RectTransform content;
        [SerializeField] private RectTransform rowTemplate;
        [SerializeField] private TMP_Text emptyLabel;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private TMP_Text statusLabel;
        private readonly List<Row> rows = new List<Row>();
        private InventoryController subscribedInventory;

        private sealed class Row
        {
            public RectTransform Root;
            public TMP_Text Index, Name, Quantity;
        }

        public InventoryController InventoryController => inventoryController;

        public void Configure(InventoryController inventory, ItemCatalog catalog, RectTransform list,
            RectTransform template, TMP_Text empty, TMP_Text summary, TMP_Text status)
        {
            Unsubscribe();
            inventoryController = inventory;
            itemCatalog = catalog;
            content = list;
            rowTemplate = template;
            emptyLabel = empty;
            summaryLabel = summary;
            statusLabel = status;
            if (Application.IsPlaying(gameObject) && isActiveAndEnabled) OnEnable();
        }

        private void OnEnable()
        {
            Unsubscribe();
            subscribedInventory = inventoryController;
            if (subscribedInventory != null)
            {
                subscribedInventory.Changed += Refresh;
            }
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void Unsubscribe()
        {
            if (subscribedInventory != null)
            {
                subscribedInventory.Changed -= Refresh;
            }
            subscribedInventory = null;
        }

        /// <summary>Refreshes from a read-only snapshot, also used when the screen is reopened.</summary>
        public void Refresh()
        {
            var inventory = inventoryController != null ? inventoryController.Items : null;
            var items = inventory?.GetItems();
            var count = items?.Count ?? 0;
            if (content != null && rowTemplate != null)
            {
                for (var i = 0; i < count; i++)
                {
                    if (i == rows.Count) rows.Add(CreateRow());
                    var row = rows[i];
                    row.Root.gameObject.SetActive(true);
                    row.Index.text = (i + 1).ToString("00", CultureInfo.InvariantCulture);
                    row.Name.text = DisplayName(items[i].ItemId);
                    row.Quantity.text = items[i].Quantity.ToString("N0", CultureInfo.InvariantCulture);
                }
            }
            for (var i = count; i < rows.Count; i++) rows[i].Root.gameObject.SetActive(false);
            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(count == 0);
                emptyLabel.text = inventory == null ? "INVENTORY OFFLINE\nCHECK CARGO LINK"
                    : "NO RECOVERED ITEMS\nSUCCESSFUL SALVAGE WILL APPEAR HERE";
            }
            if (summaryLabel != null)
                summaryLabel.text = string.Format(CultureInfo.InvariantCulture, "{0:00} ITEM TYPES  /  {1:N0} UNITS",
                    inventory?.DistinctItemCount ?? 0, inventory?.TotalQuantity ?? 0);
            RefreshStatus();
        }

        private Row CreateRow()
        {
            var root = Instantiate(rowTemplate, content, false);
            root.name = "Inventory Row " + (rows.Count + 1);
            return new Row
            {
                Root = root,
                Index = root.Find("Index").GetComponent<TMP_Text>(),
                Name = root.Find("Name").GetComponent<TMP_Text>(),
                Quantity = root.Find("Quantity").GetComponent<TMP_Text>()
            };
        }

        private string DisplayName(string itemId)
        {
            var name = itemCatalog != null ? itemCatalog.GetDisplayName(itemId) : itemId;
            // Glass TTY's authored atlas contains printable ASCII only, matching the other screens.
            foreach (var character in name)
                if (character < 32 || character > 126) return itemId.Replace('_', ' ').ToUpperInvariant();
            return name.Replace('_', ' ').ToUpperInvariant();
        }

        private void RefreshStatus()
        {
            if (statusLabel == null) return;
            var connected = inventoryController != null;
            statusLabel.text = connected ? "[ ONLINE ]" : "[ OFFLINE ]";
            statusLabel.color = connected ? new Color(0.48f, 0.83f, 0.67f) : new Color(1f, 0.67f, 0.3f);
        }
    }
}
