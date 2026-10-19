using System.Collections.Generic;
using System.Globalization;
using TMPro;
using Pioneer60.Localization;
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
            Loc.LanguageChanged -= HandleLanguageChanged;
            Unsubscribe();
            subscribedInventory = inventoryController;
            if (subscribedInventory != null)
            {
                subscribedInventory.Changed += Refresh;
            }
            Refresh();
            Loc.LanguageChanged += HandleLanguageChanged;
        }

        private void OnDisable() { Loc.LanguageChanged -= HandleLanguageChanged; Unsubscribe(); }

        private void HandleLanguageChanged(string code) => Refresh();

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
            Pioneer60.Localization.LocAutoTranslate.Refresh(gameObject);
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
            // 过去这里有个「含非 ASCII 字符就退回大写物品 ID」的兜底，是因为旧字体图集只有 ASCII。
            // 字体换过之后它会把中文物品名整个吃掉（HEAT RESISTANT CERAMIC INSULATORS 就是这么来的），已删除。
            // 保留 ToUpper：终端字体是小写缺失的端子字体，中文不受影响。
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
