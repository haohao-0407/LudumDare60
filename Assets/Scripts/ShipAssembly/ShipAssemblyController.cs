using System;
using System.Collections.Generic;
using LudumDare60.AssemblyDemo;
using LudumDare60.Storage;
using LudumDare60.UI;
using UnityEngine;

namespace LudumDare60.ShipAssembly
{
    /// <summary>
    /// Owns paid assembly sessions for this ship's lifetime. Put this component outside screen
    /// canvases: changing screens only changes input/visibility, never ownership or material cost.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipAssemblyController : MonoBehaviour
    {
        public InventoryController Inventory;
        public RepairScreenController RepairScreen;
        public AssemblyRecipe[] Recipes;
        public Transform Workspace;
        public Camera AssemblyCamera;
        [Range(0, 31)] public int WorkspaceLayer = 29;

        public bool RepairsAvailable { get; private set; } = true;
        public int SelectedRecipeIndex { get; private set; }
        public AssemblyRecipe SelectedRecipe => Recipes != null && SelectedRecipeIndex >= 0 && SelectedRecipeIndex < Recipes.Length
            ? Recipes[SelectedRecipeIndex] : null;
        public AssemblyDemoController ActivePuzzle => SelectedSession?.Puzzle;
        public bool HasActiveSession => SelectedSession != null;
        public bool IsSelectedCompleted => SelectedRecipe != null && DeviceIsRepaired((int)SelectedRecipe.Device);
        public float Progress => IsSelectedCompleted ? 1f : SelectedSession?.Progress ?? 0f;
        public string StatusMessage { get; private set; } = "Select a device and allocate materials to begin.";
        public bool InputEnabled
        {
            get
            {
                var session = SelectedSession;
                return RepairsAvailable && focused && isActiveAndEnabled && !operationInProgress && Application.isFocused && Time.timeScale > 0f &&
                       session != null && session.Puzzle != null && session.Puzzle.isActiveAndEnabled &&
                       !session.Completed && !DeviceIsRepaired((int)session.Recipe.Device) &&
                       RepairScreen != null && RepairScreen.CanBeginRepair((int)session.Recipe.Device);
            }
        }
        public event Action Changed;

        [Header("音效")]
        [Tooltip("留空则用本物体上的 AudioSource")]
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("组装完成（贴合 + 锁扣 + 确认）")]
        [SerializeField] private AudioClip assemblyCompleteClip;
        [SerializeField, Range(0f, 1f)] private float assemblyCompleteVolume = 1f;

        private sealed class Session
        {
            public Guid AttemptId;
            public AssemblyRecipe Recipe;
            public GameObject Root;
            public AssemblyDemoController Puzzle;
            public float Progress;
            public bool Completed;
            public Action ProgressHandler;
            public Action CompletionHandler;
        }

        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private readonly bool[] completedDevices = new bool[RepairScreenController.DeviceCount];
        private InventoryController subscribedInventory;
        private bool focused;
        private bool operationInProgress;
        private bool publishingChanged;
        private bool changePending;
        private bool lastInputEnabled;

        private Session SelectedSession
        {
            get
            {
                var recipe = SelectedRecipe;
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeId)) return null;
                return sessions.TryGetValue(recipe.RecipeId, out var session) ? session : null;
            }
        }

        private void OnEnable()
        {
            SubscribeInventory();
            RefreshVisibilityAndInput();
            NotifyChanged();
        }

        private void Start()
        {
            // Other scene objects may reset their editor preview in Awake after this view enabled.
            SynchronizeRepairScreen();
            RefreshVisibilityAndInput();
            NotifyChanged();
        }

        private void LateUpdate()
        {
            SubscribeInventory();
            SynchronizeRepairScreen();
            RefreshVisibilityAndInput();
            bool inputNow = InputEnabled;
            if (inputNow != lastInputEnabled)
            {
                lastInputEnabled = inputNow;
                NotifyChanged();
            }
        }

        private void OnDisable()
        {
            focused = false;
            RefreshVisibilityAndInput();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SetFocused(false);
        }

        private void SubscribeInventory()
        {
            if (subscribedInventory == Inventory) return;
            if (subscribedInventory != null) subscribedInventory.Changed -= NotifyChanged;
            subscribedInventory = Inventory;
            if (subscribedInventory != null) subscribedInventory.Changed += NotifyChanged;
        }

        public bool SelectRecipe(int index)
        {
            if (!RepairsAvailable || operationInProgress || publishingChanged || Recipes == null || index < 0 || index >= Recipes.Length || Recipes[index] == null)
                return false;
            if (index == SelectedRecipeIndex) return true;
            SelectedRecipeIndex = index;
            focused = false;
            StatusMessage = IsSelectedCompleted ? "Repair complete." : HasActiveSession
                ? "Assembly in progress. Materials already allocated." : "Allocate materials to begin assembly.";
            RefreshVisibilityAndInput();
            NotifyChanged();
            return true;
        }

        public void SetFocused(bool value)
        {
            focused = value && RepairsAvailable && isActiveAndEnabled;
            RefreshVisibilityAndInput();
        }

        /// <summary>Locks repair commands and puzzle input without changing device state or charging materials.</summary>
        public void SetRepairsAvailable(bool available)
        {
            if (RepairsAvailable == available) return;
            RepairsAvailable = available;
            if (!available) focused = false;
            StatusMessage = !available ? "Repairs are currently unavailable." : IsSelectedCompleted ? "Repair complete."
                : HasActiveSession ? "Assembly in progress. Materials already allocated." : "Allocate materials to begin assembly.";
            RefreshVisibilityAndInput();
            NotifyChanged();
        }

        /// <summary>
        /// Initializes an intact ship after scene Awake. It does not simulate repairs, spend inventory,
        /// or invoke puzzle completion; old sessions are discarded before publishing the complete snapshot.
        /// </summary>
        public bool InitializeOperationalDevices()
        {
            return ReplaceDeviceCondition(true);
        }

        /// <summary>Discards the old repair generation and marks every device damaged atomically.</summary>
        public bool ApplyCrashDamage()
        {
            return ReplaceDeviceCondition(false);
        }

        private bool ReplaceDeviceCondition(bool operational)
        {
            if (operationInProgress || publishingChanged) return false;
            if (RepairScreen == null)
            {
                StatusMessage = "The repair screen is not configured.";
                NotifyChanged();
                return false;
            }

            operationInProgress = true;
            try
            {
                focused = false;
                foreach (var session in sessions.Values) DestroySession(session);
                sessions.Clear();
                for (int device = 0; device < completedDevices.Length; device++)
                    completedDevices[device] = operational;
                SelectedRecipeIndex = 0;
                // SynchronizeRepairScreen is blocked during publication: listeners see the full new
                // snapshot, and LateUpdate can no longer restore a completion from before the crash.
                if (operational) RepairScreen.InitializeOperationalDevices();
                else RepairScreen.ResetAll();
                StatusMessage = operational ? "All ship systems are operational."
                    : RepairsAvailable ? "Crash damage detected. Allocate materials to begin repairs."
                    : "Ship systems damaged. Repairs are currently unavailable.";
                return true;
            }
            finally
            {
                operationInProgress = false;
                RefreshVisibilityAndInput();
                NotifyChanged();
            }
        }

        /// <summary>
        /// 找出"现在材料就够、可以开始组装"的配方下标，找不到返回 -1。
        /// 不改动当前选择，纯查询 —— 教学流程要轮询它来判断什么时候把镜头切到组装屏。
        /// </summary>
        public int FindCraftableRecipe()
        {
            if (Recipes == null || !RepairsAvailable || !isActiveAndEnabled) return -1;
            for (int i = 0; i < Recipes.Length; i++)
            {
                var recipe = Recipes[i];
                if (recipe == null) continue;
                int device = (int)recipe.Device;
                if (DeviceIsRepaired(device)) continue;
                if (RepairScreen != null && !RepairScreen.CanBeginRepair(device)) continue;
                if (Inventory == null) continue;
                string reason;
                if (Inventory.Items.CanConsume(recipe.Materials, out reason)) return i;
            }
            return -1;
        }

        public bool CanStartSelected(out string reason)
        {
            if (operationInProgress) return Fail("An assembly operation is already in progress.", out reason);
            if (!RepairsAvailable) return Fail("Repairs are currently unavailable.", out reason);
            if (!isActiveAndEnabled) return Fail("Assembly station is unavailable.", out reason);
            if (!ValidateConfiguration(out reason)) return false;
            var recipe = SelectedRecipe;
            int device = (int)recipe.Device;
            if (DeviceIsRepaired(device)) return Fail("This device is already repaired.", out reason);
            if (!RepairScreen.CanBeginRepair(device)) return Fail("This device is currently unavailable for repair.", out reason);
            if (SelectedSession != null)
            {
                if (SelectedSession.Puzzle == null) return Fail("The paid assembly instance is unavailable; materials will not be charged again.", out reason);
                reason = "Continue the existing assembly; materials have already been allocated.";
                return true;
            }
            return Inventory.Items.CanConsume(recipe.Materials, out reason);
        }

        public bool TryStartSelected()
        {
            if (operationInProgress || publishingChanged) return false;
            if (!RepairsAvailable)
            {
                StatusMessage = "Repairs are currently unavailable.";
                NotifyChanged();
                return false;
            }
            // Synchronize this device's saved session state before checking repair availability.
            SynchronizeRepairScreen();
            if (!CanStartSelected(out string reason))
            {
                StatusMessage = reason;
                NotifyChanged();
                return false;
            }

            operationInProgress = true;
            Session pending = null;
            try
            {
                if (SelectedSession != null)
                {
                    StatusMessage = "Assembly resumed. Materials already allocated.";
                    return true;
                }
                var recipe = SelectedRecipe;
                pending = CreateInitializedSession(recipe, out reason);
                if (pending == null)
                {
                    StatusMessage = reason;
                    return false;
                }
                // Awake and all required puzzle references have been checked before any deduction.
                if (!RepairsAvailable || !isActiveAndEnabled || !RepairScreen.CanBeginRepair((int)recipe.Device))
                {
                    StatusMessage = "Repair availability changed before assembly could begin.";
                    return false;
                }
                if (!Inventory.Items.TryConsumeOnce(pending.AttemptId, recipe.Materials, out reason))
                {
                    StatusMessage = reason;
                    return false;
                }

                sessions.Add(recipe.RecipeId, pending);
                // Capture a stable local; the temporary pending variable is cleared after commit.
                var committed = pending;
                pending.ProgressHandler = () => OnPuzzleProgress(committed);
                pending.CompletionHandler = () => OnPuzzleCompleted(committed);
                pending.Puzzle.ProgressChanged += pending.ProgressHandler;
                pending.Puzzle.Completed += pending.CompletionHandler;
                pending = null;
                StatusMessage = "Materials allocated. Select a part to begin assembly.";
                return true;
            }
            finally
            {
                if (pending != null) DestroySession(pending);
                operationInProgress = false;
                SynchronizeRepairScreen();
                RefreshVisibilityAndInput();
                NotifyChanged();
            }
        }

        public void ResetSelectedPuzzle()
        {
            if (operationInProgress || publishingChanged || !CanOperateSelected()) return;
            operationInProgress = true;
            try
            {
                var session = SelectedSession;
                session.Puzzle.InputBlocked = false;
                session.Puzzle.ResetDemo();
                StatusMessage = "Parts rearranged. Materials remain allocated to this assembly.";
            }
            finally
            {
                operationInProgress = false;
                RefreshVisibilityAndInput();
                SynchronizeRepairScreen();
                NotifyChanged();
            }
        }

        public void DetachSelected()
        {
            if (operationInProgress || publishingChanged || !CanOperateSelected()) return;
            operationInProgress = true;
            try
            {
                SelectedSession.Puzzle.InputBlocked = false;
                SelectedSession.Puzzle.DetachSelected();
            }
            finally
            {
                operationInProgress = false;
                RefreshVisibilityAndInput();
                SynchronizeRepairScreen();
                NotifyChanged();
            }
        }

        private bool CanOperateSelected()
        {
            var session = SelectedSession;
            return RepairsAvailable && isActiveAndEnabled && Application.isFocused && Time.timeScale > 0f && session != null && session.Puzzle != null &&
                   !IsSelectedCompleted && RepairScreen != null && RepairScreen.CanBeginRepair((int)session.Recipe.Device);
        }

        private bool ValidateConfiguration(out string reason)
        {
            if (SelectedRecipe == null) return Fail("No assembly recipe is selected.", out reason);
            if (Inventory == null || RepairScreen == null || Workspace == null || AssemblyCamera == null)
                return Fail("Assembly station references are incomplete.", out reason);
            if (!Workspace.gameObject.activeInHierarchy) return Fail("The assembly workspace is inactive.", out reason);
            if (WorkspaceLayer < 0 || WorkspaceLayer > 31) return Fail("The assembly render layer is invalid.", out reason);
            if (Quaternion.Angle(Workspace.rotation, Quaternion.identity) > 0.01f || (Workspace.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                return Fail("The assembly workspace must use world XY orientation and unit scale.", out reason);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var devices = new HashSet<RepairDeviceId>();
            foreach (var recipe in Recipes)
            {
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeId) || !ids.Add(recipe.RecipeId))
                    return Fail("Every assembly recipe requires a unique, nonempty ID.", out reason);
                int device = (int)recipe.Device;
                if (device < 0 || device >= RepairScreenController.DeviceCount || !devices.Add(recipe.Device))
                    return Fail("Every assembly recipe must target a different valid repair device.", out reason);
            }
            var selected = SelectedRecipe;
            if (selected.PuzzlePrefab == null || selected.PuzzlePrefab.GetComponentsInChildren<AssemblyDemoController>(true).Length != 1)
                return Fail("The selected recipe requires one assembly controller in its puzzle prefab.", out reason);
            if (selected.Materials == null || selected.Materials.Length == 0)
                return Fail("The selected recipe has no material requirements.", out reason);
            if (!Finite(selected.OrthographicSize) || selected.OrthographicSize <= 0f ||
                !Finite(selected.PuzzleOffset.x) || !Finite(selected.PuzzleOffset.y) || !Finite(selected.PuzzleOffset.z))
                return Fail("The selected recipe has invalid camera or puzzle placement settings.", out reason);
            reason = string.Empty;
            return true;
        }

        private Session CreateInitializedSession(AssemblyRecipe recipe, out string reason)
        {
            var session = new Session { AttemptId = Guid.NewGuid(), Recipe = recipe };
            try
            {
                // An inactive parent prevents Awake from capturing the prefab's original world pose.
                session.Root = new GameObject("Assembly session - " + recipe.RecipeId);
                session.Root.SetActive(false);
                session.Root.transform.SetParent(Workspace, false);
                var instance = Instantiate(recipe.PuzzlePrefab, session.Root.transform, false);
                instance.transform.localPosition = recipe.PuzzleOffset;
                instance.transform.localRotation = Quaternion.identity;
                instance.SetActive(true);
                session.Puzzle = instance.GetComponentInChildren<AssemblyDemoController>(true);
                session.Puzzle.ReadDeviceInput = false;
                session.Puzzle.InputBlocked = true;
                session.Puzzle.LockWhenComplete = true;
                session.Puzzle.ViewCamera = AssemblyCamera;
                foreach (var t in session.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = WorkspaceLayer;
                foreach (var camera in instance.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                session.Root.SetActive(true);
                if (!session.Puzzle.IsInitialized || !session.Puzzle.isActiveAndEnabled || session.Puzzle.Modules == null || session.Puzzle.Modules.Length < 2)
                    throw new InvalidOperationException("The assembly controller could not initialize its modules.");
                float planeZ = session.Puzzle.Modules[0] != null ? session.Puzzle.Modules[0].transform.position.z : 0f;
                var modules = new HashSet<AssemblyModule>();
                foreach (var module in session.Puzzle.Modules)
                {
                    if (module == null || !modules.Add(module) || !module.isActiveAndEnabled || !module.transform.IsChildOf(instance.transform) ||
                        !Finite(module.Position.x) || !Finite(module.Position.y) || !Finite(module.transform.position.z) ||
                        Mathf.Abs(module.transform.position.z - planeZ) > 0.001f ||
                        Vector3.Dot(module.transform.forward, Vector3.forward) < 0.99999f ||
                        (module.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                        throw new InvalidOperationException("The assembly requires active, referenced modules on a common XY plane.");
                }
                session.Root.SetActive(false);
                reason = string.Empty;
                return session;
            }
            catch (Exception exception)
            {
                DestroySession(session);
                reason = "Unable to create assembly: " + exception.Message;
                Debug.LogWarning("[ShipAssembly] " + reason, this);
                return null;
            }
        }

        private void OnPuzzleProgress(Session session)
        {
            if (!RepairsAvailable || !OwnsSession(session) || session.Completed || session.Puzzle == null) return;
            session.Progress = session.Puzzle.RequiredConnectionCount > 0
                ? Mathf.Clamp01((float)session.Puzzle.ConnectionCount / session.Puzzle.RequiredConnectionCount) : 0f;
            SynchronizeRepairScreen();
            NotifyChanged();
        }

        private void OnPuzzleCompleted(Session session)
        {
            if (!RepairsAvailable || !OwnsSession(session) || session.Completed || session.Puzzle == null || !session.Puzzle.IsAssemblyComplete) return;
            session.Completed = true;
            session.Progress = 1f;
            completedDevices[(int)session.Recipe.Device] = true;
            session.Puzzle.InputBlocked = true;
            if (session == SelectedSession) StatusMessage = "Repair complete.";
            PlayAssemblyComplete();
            SynchronizeRepairScreen();
            RefreshVisibilityAndInput();
            NotifyChanged();
        }

        private void PlayAssemblyComplete()
        {
            var source = sfxSource != null ? sfxSource : GetComponent<AudioSource>();
            if (source == null || assemblyCompleteClip == null) return;
            source.PlayOneShot(assemblyCompleteClip, assemblyCompleteVolume);
        }

        /// <summary>Synchronize each device's durable session state after a screen reset or re-enable.</summary>
        public void SynchronizeRepairScreen()
        {
            if (RepairScreen == null || operationInProgress || publishingChanged) return;
            operationInProgress = true;
            try
            {
                for (int device = 0; device < RepairScreenController.DeviceCount; device++)
                {
                    if (RepairScreen.GetState(device) == RepairDeviceState.Repaired)
                    {
                        if (!completedDevices[device]) changePending = true;
                        completedDevices[device] = true;
                    }
                    Session session = null;
                    foreach (var item in sessions.Values)
                        if ((int)item.Recipe.Device == device) { session = item; break; }
                    bool complete = completedDevices[device] || (session != null && session.Completed);
                    if (complete && session != null)
                    {
                        session.Completed = true;
                        session.Progress = 1f;
                        if (session.Puzzle != null) session.Puzzle.InputBlocked = true;
                    }
                    if (RepairScreen.GetState(device) == RepairDeviceState.Repaired || (!complete && session == null)) continue;
                    if (!RepairScreen.CanBeginRepair(device)) continue;
                    if (RepairScreen.GetState(device) != RepairDeviceState.Repairing && !RepairScreen.BeginRepair(device)) continue;
                    float progress = complete ? 1f : session.Progress;
                    if (!Mathf.Approximately(RepairScreen.GetProgress(device), progress)) RepairScreen.SetProgress(device, progress);
                    if (complete && !RepairScreen.CompleteRepair(device))
                    {
                        StatusMessage = "Assembly complete; waiting for repair screen synchronization.";
                        changePending = true;
                    }
                }
            }
            finally
            {
                operationInProgress = false;
            }
            if (changePending) NotifyChanged();
        }

        private bool DeviceIsRepaired(int device)
        {
            return device >= 0 && device < completedDevices.Length &&
                (completedDevices[device] || (RepairScreen != null && RepairScreen.GetState(device) == RepairDeviceState.Repaired));
        }

        private bool OwnsSession(Session session)
        {
            return session != null && session.Recipe != null && !string.IsNullOrEmpty(session.Recipe.RecipeId)
                && sessions.TryGetValue(session.Recipe.RecipeId, out var current) && ReferenceEquals(current, session);
        }

        private void RefreshVisibilityAndInput()
        {
            var selected = SelectedSession;
            foreach (var session in sessions.Values)
            {
                bool visible = RepairsAvailable && isActiveAndEnabled && session == selected;
                if (session.Root != null && session.Root.activeSelf != visible) session.Root.SetActive(visible);
                if (session.Puzzle != null) session.Puzzle.InputBlocked = !visible || !InputEnabled;
            }
            if (AssemblyCamera != null && SelectedRecipe != null)
            {
                AssemblyCamera.orthographic = true;
                AssemblyCamera.orthographicSize = Mathf.Max(0.1f, SelectedRecipe.OrthographicSize);
            }
        }

        private void NotifyChanged()
        {
            if (operationInProgress || publishingChanged) { changePending = true; return; }
            changePending = false;
            publishingChanged = true;
            try
            {
                Action listeners = Changed;
                if (listeners == null) return;
                foreach (Action listener in listeners.GetInvocationList())
                {
                    try { listener(); }
                    catch (Exception exception) { Debug.LogException(exception, this); }
                }
            }
            finally { publishingChanged = false; }
        }

        private void DestroySession(Session session)
        {
            if (session.Puzzle != null)
            {
                session.Puzzle.ProgressChanged -= session.ProgressHandler;
                session.Puzzle.Completed -= session.CompletionHandler;
                session.Puzzle.InputBlocked = true;
            }
            if (session.Root != null)
            {
                session.Root.SetActive(false);
                Destroy(session.Root);
            }
        }

        private void OnDestroy()
        {
            if (subscribedInventory != null) subscribedInventory.Changed -= NotifyChanged;
            foreach (var session in sessions.Values) DestroySession(session);
            sessions.Clear();
        }

        private static bool Fail(string message, out string reason) { reason = message; return false; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
