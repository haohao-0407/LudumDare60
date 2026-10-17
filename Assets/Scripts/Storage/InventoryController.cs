using System;
using System.IO;
using UnityEngine;

namespace LudumDare60.Storage
{
    /// <summary>Optional Unity adapter. Use one owner per save file; access CRUD through Items.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Storage/Inventory Controller")]
    public sealed class InventoryController : MonoBehaviour
    {
        [SerializeField] private string saveFileName = "inventory.json";
        [SerializeField] private bool loadOnAwake = true;
        [SerializeField] private bool autoSave = true;
        private Inventory inventory;
        private InventoryJsonStore store;
        private bool canSave = true;
        private bool loading;

        public Inventory Items { get { EnsureInitialized(); return inventory; } }
        public string SavePath { get { EnsureInitialized(); return store?.FilePath; } }
        public string LastError { get; private set; }
        public bool HasUnsavedChanges { get; private set; }
        public bool CanSave { get { EnsureInitialized(); return canSave; } }
        public event Action Changed;
        public event Action<string> PersistenceFailed;

        private void Awake() => EnsureInitialized();

        public bool Save()
        {
            EnsureInitialized();
            if (!canSave) return ReportFailure(LastError ?? "Saving is blocked until the existing save is recovered and loaded.");
            if (!store.TrySave(inventory, out var error)) return ReportFailure(error);
            HasUnsavedChanges = false;
            LastError = null;
            return true;
        }

        /// <summary>Explicitly reloads disk data, discarding unsaved changes only after a valid read.</summary>
        public bool Load()
        {
            EnsureInitialized();
            if (store == null) return false;
            loading = true;
            bool loaded;
            string error;
            try { loaded = store.TryLoad(inventory, out error); }
            finally { loading = false; }
            if (!loaded)
            {
                // Do not overwrite an unreadable/unsupported file with a new or partial inventory.
                if (File.Exists(store.FilePath)) canSave = false;
                return ReportFailure(error);
            }
            canSave = true;
            LastError = null;
            HasUnsavedChanges = false;
            Changed?.Invoke();
            return true;
        }

        private void EnsureInitialized()
        {
            if (inventory != null) return;
            inventory = new Inventory();
            try
            {
                if (string.IsNullOrWhiteSpace(saveFileName) || Path.IsPathRooted(saveFileName)
                    || saveFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || saveFileName == "." || saveFileName == ".." || saveFileName.Contains("/") || saveFileName.Contains("\\"))
                    throw new ArgumentException("Save File Name must be a local file name, such as inventory.json.");
                store = new InventoryJsonStore(Path.Combine(Application.persistentDataPath, saveFileName));
                if (loadOnAwake && File.Exists(store.FilePath))
                {
                    canSave = store.TryLoad(inventory, out var error);
                    if (!canSave) ReportFailure(error);
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException
                || exception is IOException || exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
            {
                canSave = false;
                ReportFailure(exception.Message);
            }
            inventory.Changed += OnInventoryChanged;
        }

        private void OnInventoryChanged()
        {
            if (loading) return;
            HasUnsavedChanges = true;
            if (autoSave && canSave) Save();
            Changed?.Invoke();
        }

        private bool ReportFailure(string error)
        {
            LastError = error;
            Debug.LogError($"Inventory persistence failed: {error}", this);
            PersistenceFailed?.Invoke(error);
            return false;
        }

        private void SavePendingChanges()
        {
            if (inventory != null && autoSave && canSave && HasUnsavedChanges) Save();
        }

        private void OnApplicationPause(bool paused) { if (paused) SavePendingChanges(); }
        private void OnApplicationQuit() => SavePendingChanges();
        private void OnDestroy()
        {
            SavePendingChanges();
            if (inventory != null) inventory.Changed -= OnInventoryChanged;
        }
    }
}
