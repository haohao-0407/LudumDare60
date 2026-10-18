using System;
using System.IO;
using System.Linq;
using LudumDare60.UI;
using LudumDare60.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

[InitializeOnLoad]
public static class EscMenuValidation
{
    private const string RunKey = "EscMenuValidation.Run";
    private static int frames;
    static EscMenuValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false)) { frames = 0; EditorApplication.update += Check; } };
    }
    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.1f, 0.14f, 0.16f);
            EscMenuBuilder.Build();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EscMenuBuilder.PrefabPath);
            Require(prefab != null, "Prefab missing.");
            Require(prefab.GetComponentsInChildren<Camera>(true).Length == 0 && prefab.GetComponentsInChildren<EventSystem>(true).Length == 0, "Prefab contains scene-owned objects.");
            Require(!prefab.transform.Find("Menu Overlay").gameObject.activeSelf, "Prefab should start closed.");
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            Require(actions.FindAction("ESCMenu/Toggle").bindings.Any(binding => binding.path == "<Keyboard>/escape"), "ESC binding missing from Input System asset.");
            foreach (var text in prefab.GetComponentsInChildren<TMP_Text>(true))
                Require(text.font != null && text.font.name == "Glass_TTY_VT220 SDF" && text.text.All(character => character < 128), "Incorrect font or non-English menu text.");
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Require(component != null, "Missing script.");
                var iterator = new SerializedObject(component).GetIterator();
                while (iterator.Next(true))
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue is Component reference)
                        Require(reference.transform == prefab.transform || reference.transform.IsChildOf(prefab.transform), "External scene reference in prefab.");
            }
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/EscMenuValidation.unity");
            EscMenuBuilder.Install(); EscMenuBuilder.Install();
            Require(UnityEngine.Object.FindObjectsByType<EscMenuController>().Length == 1, "Install duplicated the menu.");
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>().Length == 1, "Install duplicated EventSystem.");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/EscMenuValidation.unity");
            SessionState.SetBool(RunKey, true); EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Check()
    {
        if (++frames < 40) return;
        EditorApplication.update -= Check;
        var initialVolume = AudioListener.volume;
        try
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<EscMenuController>();
            Require(menu != null && !menu.IsOpen, "Menu should start closed.");
            // The isolated batch Editor has no focused Game view; route synthetic device input to the player loop.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            InputSystem.EnableDevice(keyboard); Release(keyboard);
            Time.timeScale = 0.35f;
            Press(keyboard); Require(menu.IsOpen && Time.timeScale == 0, "Input System ESC did not open/pause.");
            Release(keyboard);
            var root = menu.transform.Find("Menu Overlay");
            Require(root.gameObject.activeSelf && Cursor.visible, "Menu/cursor hidden after opening.");
            var resume = Find<Button>(menu, "Resume");
            Require(EventSystem.current.currentSelectedGameObject == resume.gameObject, "Resume was not selected for keyboard navigation.");
            Canvas.ForceUpdateCanvases();
            Capture(menu, 1920, 1080, "ESC-Menu-Preview.png");
            Capture(menu, 1280, 1024, "ESC-Menu-Preview-4x3.png");
            Press(keyboard); Release(keyboard); Require(!menu.IsOpen && Mathf.Approximately(Time.timeScale, 0.35f), "ESC did not close/restore previous time scale.");
            Press(keyboard); Release(keyboard);
            Find<Button>(menu, "Settings").onClick.Invoke();
            Require(menu.transform.Find("Menu Overlay/Menu Window/Settings Page").gameObject.activeSelf, "Settings page did not open.");
            var slider = Find<Slider>(menu, "Master Volume"); slider.value = 0.37f;
            Require(Mathf.Abs(AudioListener.volume - 0.37f) < 0.001f && Find<TMP_Text>(menu, "Value").text == "37%", "Volume slider is not connected to audio.");
            Find<Button>(menu, "Reset Audio").onClick.Invoke(); Require(AudioListener.volume == 1, "Reset audio failed.");
            Find<Button>(menu, "Back").onClick.Invoke();
            Find<Button>(menu, "Quit").onClick.Invoke();
            Require(menu.transform.Find("Menu Overlay/Menu Window/Quit Confirmation").gameObject.activeSelf && !resume.IsInteractable(), "Quit confirmation did not block underlying keyboard controls.");
            Find<Button>(menu, "Cancel Quit").onClick.Invoke(); Require(resume.IsInteractable(), "Quit cancel did not restore controls.");
            resume.onClick.Invoke(); Require(!menu.IsOpen && Mathf.Approximately(Time.timeScale, 0.35f), "Resume did not close the menu.");
            menu.Open(); menu.enabled = false;
            Require(!menu.IsOpen && Mathf.Approximately(Time.timeScale, 0.35f), "Disabling menu left the game paused.");
            menu.enabled = true; Press(keyboard); Release(keyboard); Require(menu.IsOpen, "ESC did not work after re-enabling.");
            menu.Close();
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>().Length == 1, "Runtime EventSystem duplicated.");
            AudioListener.volume = initialVolume; Time.timeScale = 1;
            File.WriteAllText(Path.Combine(Output, "EscMenuValidation.txt"), "PASS: independent prefab, authored ESC input binding, hidden by default, ESC open/close/reopen, time-scale restoration, pointer raycast, keyboard selection, settings/back, master volume/reset, quit/cancel modal blocking, safe disable/re-enable, English Glass TTY font, 16:9 and 4:3 render previews.");
            SessionState.SetBool(RunKey, false); Debug.Log("ESC_MENU_VALIDATION_OK"); EditorApplication.Exit(0);
        }
        catch (Exception error) { AudioListener.volume = initialVolume; Time.timeScale = 1; Fail(error); }
    }
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "Logs"));
    private static T Find<T>(Component root, string name) where T : Component => root.GetComponentsInChildren<T>(true).Single(item => item.name == name);
    private static void Press(Keyboard keyboard) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update(); }
    private static void Release(Keyboard keyboard) { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); }
    private static void Capture(EscMenuController menu, int width, int height, string file)
    {
        var camera = Camera.main; var canvas = menu.GetComponent<Canvas>(); var oldAspect = camera.aspect;
        var texture = new RenderTexture(width, height, 24); texture.Create();
        camera.targetTexture = texture; camera.aspect = (float)width / height;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        // Rendering assigns uGUI's graphic depths. In a hidden batch Editor they are unset before this render.
        var resume = Find<Button>(menu, "Resume"); var buttonRect = (RectTransform)resume.transform;
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(camera, buttonRect.TransformPoint(buttonRect.rect.center)) }, hits);
        Require(hits.Count > 0 && hits[0].gameObject == resume.gameObject,
            "Menu button cannot receive pointer input after rendering: " + string.Join(",", hits.Select(hit => hit.gameObject.name)) + "; depth=" + resume.GetComponent<UnityEngine.UI.Image>().depth);
        var window = (RectTransform)menu.transform.Find("Menu Overlay/Menu Window"); var corners = new Vector3[4]; window.GetWorldCorners(corners);
        foreach (var corner in corners) { var point = camera.WorldToViewportPoint(corner); Require(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1, "Menu cropped at " + width + "x" + height); }
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(Output, file), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; camera.aspect = oldAspect;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
        UnityEngine.Object.Destroy(image); texture.Release(); UnityEngine.Object.Destroy(texture); Canvas.ForceUpdateCanvases();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception error) { SessionState.SetBool(RunKey, false); EditorApplication.update -= Check; Debug.LogException(error); EditorApplication.Exit(1); }
}
