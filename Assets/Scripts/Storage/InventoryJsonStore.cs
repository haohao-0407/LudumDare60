using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace LudumDare60.Storage
{
    /// <summary>Versioned JSON persistence. A failed read never replaces the live inventory.</summary>
    public sealed class InventoryJsonStore
    {
        public string FilePath { get; }

        public InventoryJsonStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
        }

        public bool TryLoad(Inventory inventory, out string error)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            try
            {
                var data = JsonUtility.FromJson<InventorySaveData>(File.ReadAllText(FilePath, Encoding.UTF8));
                return inventory.TryRestore(data, out error);
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TrySave(Inventory inventory, out string error)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var temporaryPath = FilePath + ".tmp";
            try
            {
                var json = JsonUtility.ToJson(inventory.CreateSaveData(), true);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                // Complete the temporary file before replacing the last valid save.
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, FilePath + ".bak");
                else File.Move(temporaryPath, FilePath);
                error = null;
                return true;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception exception) when (IsStorageError(exception)) { /* Preserve the original save result. */ }
            }
        }

        private static bool IsStorageError(Exception exception)
            => exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException
               || exception is NotSupportedException || exception is System.Security.SecurityException;
    }
}
