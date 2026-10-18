using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LudumDare60.ReturnWindow.Editor
{
    /// <summary>Idempotent generation of editable physical props. All roots face local +Z, with +Y up.</summary>
    public static class ReturnPropBuilder
    {
        public const string ArtRoot = "Assets/ReturnWindow/Art";
        public const string CalendarPrefabPath = ArtRoot + "/Prefabs/ReturnCalendar.prefab";
        public const string CountdownPrefabPath = ArtRoot + "/Prefabs/ReturnCountdown.prefab";
        private static readonly Color InkColor = new Color(.075f, .10f, .09f);
        private static readonly Color RedColor = new Color(.58f, .055f, .025f);
        private static TMP_FontAsset font;
        private static Material redInk;
        private static Material darkInk;
        private static Material amberLit;
        private static Material amberOff;
        private static Mesh segmentMesh;

        [MenuItem("Tools/Return Window/Build Physical Props")]
        public static void BuildPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building props.");
            foreach (string folder in new[] { "Materials", "Prefabs", "Meshes", "Audio" }) Directory.CreateDirectory(ArtRoot + "/" + folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            if (!font) throw new FileNotFoundException("The existing terminal font asset is required.");
            redInk = FlatMaterial("RW_Dynamic_RedInk", RedColor);
            darkInk = FlatMaterial("RW_Dynamic_DarkInk", InkColor);
            amberLit = FlatMaterial("RW_Dynamic_AmberLit", new Color(1.6f, .42f, .055f));
            amberOff = FlatMaterial("RW_Dynamic_AmberOff", new Color(.075f, .032f, .012f));
            segmentMesh = BuildSegmentMesh();
            WriteSound("PencilCross", true); WriteSound("WindowWarning", false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.NewPreviewScene();
            try { BuildCalendar(scene); BuildCountdown(scene); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            AssetDatabase.SaveAssets();
            Debug.Log("Return Window physical prefabs generated: " + CalendarPrefabPath + ", " + CountdownPrefabPath);
        }

        [MenuItem("Tools/Return Window/Render Physical Prop Review")]
        public static void RenderPreviews()
        {
            const string output = "SourceArt/ReturnWindow/Previews";
            Directory.CreateDirectory(output);
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null; Texture2D pixels = null; Camera camera = null;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var calendar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CalendarPrefabPath),scene);
                var countdown = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CountdownPrefabPath),scene);
                calendar.transform.position = new Vector3(.24f,0,0);
                countdown.transform.position = new Vector3(-.18f,.075f,0);
                camera = NewRoot("Review camera",scene).AddComponent<Camera>();
                // Preview scenes occupy their own scene-culling bit. A default game camera
                // only sees ordinary scenes, even when it is itself inside this preview scene.
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.transform.position = new Vector3(0,.18f,1.7f);
                camera.transform.rotation = Quaternion.Euler(0,180,0);
                camera.orthographic = true; camera.orthographicSize = .29f; camera.aspect = 1.6f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.048f,.052f);
                camera.nearClipPlane = .05f; camera.farClipPlane = 5f;
                var key = NewRoot("Review key light",scene).AddComponent<Light>();
                key.type = LightType.Directional; key.intensity = 1.6f; key.transform.rotation = Quaternion.Euler(25,210,0);
                var fill = NewRoot("Review fill light",scene).AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = .55f; fill.transform.rotation = Quaternion.Euler(-15,145,0);
                target = new RenderTexture(1600,1000,24); camera.targetTexture = target;
                pixels = new Texture2D(1600,1000,TextureFormat.RGB24,false);
                var calendarView = calendar.GetComponent<ReturnCalendarView>(); var countdownView = countdown.GetComponent<ReturnCountdownView>();
                for (int state = 0; state < 3; state++)
                {
                    if (state == 0) { calendarView.Render(0,1,false,0); countdownView.Render(20*86400d,false,false,false); }
                    if (state == 1)
                    {
                        // Reinstantiate the calendar for a static late-state snapshot (live crosses animate).
                        Object.DestroyImmediate(calendar);
                        calendar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CalendarPrefabPath),scene);
                        calendar.transform.position = new Vector3(.24f,0,0); calendarView = calendar.GetComponent<ReturnCalendarView>();
                        calendarView.Render(15,16,false,1); countdownView.Render(5*86400d,false,false,true);
                    }
                    if (state == 2) { calendarView.Render(20,20,true,1); countdownView.Render(0,true,false,true); }
                    foreach (var text in calendar.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
                    foreach (var text in countdown.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
                    // Compile only the variants actually used by these two props before capture.
                    camera.Render(); camera.Render(); var previous = RenderTexture.active;
                    try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,1600,1000),0,0); pixels.Apply(); }
                    finally { RenderTexture.active = previous; }
                    var colors = pixels.GetPixels32();
                    var background = colors[0]; int distinct = 0;
                    for (int i = 0; i < colors.Length; i += 71)
                        if (Math.Abs(colors[i].r - background.r) + Math.Abs(colors[i].g - background.g) + Math.Abs(colors[i].b - background.b) > 20) distinct++;
                    if (distinct < 100) throw new InvalidOperationException("Physical prop preview is blank; check preview scene culling and render pipeline.");
                    File.WriteAllBytes(output + "/Unity_Props_" + new[] { "Ready", "Warning", "Sowing" }[state] + ".png",pixels.EncodeToPNG());
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                if (camera) camera.targetTexture = null;
                if (pixels) Object.DestroyImmediate(pixels);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("Return Window physical prop reviews saved to " + output);
        }

        private static void BuildCalendar(Scene scene)
        {
            GameObject root = NewRoot("ReturnCalendar", scene);
            try
            {
                var geometry = ImportGeometry("ReturnCalendar", root.transform, scene);
                // The portable model includes editable date artwork. Unity replaces that
                // separate mesh with configured dates, crosses and one live deadline ring.
                var datePrint = geometry.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("CalendarDatePrint", StringComparison.Ordinal));
                if (datePrint) Object.DestroyImmediate(datePrint.gameObject);
                var normal = Child("Active mission schedule", root.transform);
                var print = geometry.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("CalendarPlanPrint", StringComparison.Ordinal));
                if (!print) throw new InvalidDataException("Calendar GLB is missing its separately hideable CalendarPlanPrint mesh.");
                print.SetParent(normal.transform, true);
                var caption = Text("Current mission day", "25 AUG 1981", root.transform, new Vector3(0, .263f, .0128f), .009f, .23f, InkColor);
                caption.fontStyle = FontStyles.Bold;
                var labels = new TMP_Text[ReturnCalendarView.CellsPerPage]; var first = new LineRenderer[ReturnCalendarView.CellsPerPage]; var second = new LineRenderer[ReturnCalendarView.CellsPerPage];
                for (int i = 0; i < ReturnCalendarView.CellsPerPage; i++)
                {
                    var center = ReturnCalendarView.CellPosition(i) + new Vector3(0, 0, .0129f);
                    labels[i] = Text("Date " + (i + 1), MissionSchedule.CellLabel(new DateTime(1981,8,25).AddDays(i)), normal.transform, center, .012f, .054f, InkColor);
                    first[i] = Line("Cross day " + (i + 1) + " stroke 1", normal.transform, new[] {
                        center + new Vector3(.023f,.010f,.0005f), center + new Vector3(-.023f,-.010f,.0005f) }, .0013f, redInk);
                    second[i] = Line("Cross day " + (i + 1) + " stroke 2", normal.transform, new[] {
                        center + new Vector3(-.023f,.010f,.0006f), center + new Vector3(.023f,-.010f,.0006f) }, .0013f, redInk);
                    first[i].enabled = false; second[i].enabled = false;
                }
                var marker = Line("Current day underline", normal.transform, new[] { new Vector3(-.019f,0,0), new Vector3(.019f,0,0) }, .0015f, redInk).transform;
                marker.localPosition = ReturnCalendarView.CellPosition(0) + new Vector3(0,-.0115f,.013f);
                var circlePoints = new Vector3[49];
                for (int i=0;i<circlePoints.Length;i++) { float a = i * Mathf.PI * 2f / 48f; circlePoints[i] = new Vector3(Mathf.Cos(a)*.025f, Mathf.Sin(a)*.013f, 0); }
                var finalCircle = Line("Final day hand drawn ring", normal.transform, circlePoints, .0015f, redInk).transform;
                finalCircle.localPosition = ReturnCalendarView.CellPosition(19) + new Vector3(0,0,.013f);
                var ticks = new GameObject[4];
                for (int i = 0; i < ticks.Length; i++)
                {
                    var p = new Vector3(.113f,.066f-i*.009f,.013f);
                    ticks[i] = Line("Checklist tick " + i, normal.transform, new[] { p + new Vector3(.003f,0,0), p + new Vector3(0,-.003f,0), p + new Vector3(-.005f,.004f,0) }, .0009f, darkInk).gameObject;
                    ticks[i].SetActive(false);
                }
                var sowing = Child("Sowing epilogue page", root.transform);
                Text("Sowing note", "NEXT: GREENHOUSE / SOWING", sowing.transform, new Vector3(0,.084f,.013f), .006f, .25f, RedColor);
                sowing.SetActive(false);
                var audio = Audio(root, "PencilCross", .14f);
                root.AddComponent<ReturnCalendarView>().Configure(caption, labels, first, second, marker, finalCircle, normal, sowing, ticks, audio);
                Save(root, CalendarPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildCountdown(Scene scene)
        {
            GameObject root = NewRoot("ReturnCountdown", scene);
            try
            {
                ImportGeometry("ReturnCountdown", root.transform, scene);
                var digits = Child("Dynamic seven segment display", root.transform);
                var renderers = new Renderer[42];
                // Seven-segment bit order: top, upper right, lower right, bottom, lower left, upper left, middle.
                Vector2[] offsets = { new Vector2(0,.018f), new Vector2(.010f,.009f), new Vector2(.010f,-.009f),
                    new Vector2(0,-.018f),new Vector2(-.010f,-.009f),new Vector2(-.010f,.009f),Vector2.zero };
                int[] initialMasks = { 0x5b,0x3f,0x3f,0x3f,0x3f,0x3f };
                for (int digit = 0; digit < 6; digit++)
                {
                    float groupX = -.105f + (digit / 2) * .105f;
                    float x = groupX + (digit % 2 == 0 ? -.015f : .015f);
                    for (int s = 0; s < 7; s++)
                    {
                        var segment = Child("Digit " + digit + " segment " + s, digits.transform);
                        segment.transform.localPosition = new Vector3(-(x + offsets[s].x), .088f + offsets[s].y, .0654f);
                        segment.transform.localRotation = Quaternion.Euler(0,0,s == 1 || s == 2 || s == 4 || s == 5 ? 90 : 0);
                        segment.AddComponent<MeshFilter>().sharedMesh = segmentMesh;
                        var renderer = segment.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = (initialMasks[digit] & (1 << s)) != 0 ? amberLit : amberOff;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false; renderers[digit * 7 + s] = renderer;
                    }
                }
                var status = Text("Window status", "TIME TO LAST DEPARTURE", root.transform, new Vector3(0,.058f,.0655f), .006f, .28f, new Color(1f,.58f,.16f));
                status.fontStyle = FontStyles.Bold;
                // A small disk sits just above the static lamp lens, so only its state changes.
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lamp.name = "Dynamic caution lamp"; Object.DestroyImmediate(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(root.transform,false); lamp.transform.localPosition = new Vector3(-.171f,.136f,.0658f);
                lamp.transform.localRotation = Quaternion.Euler(90,0,0); lamp.transform.localScale = new Vector3(.0074f,.00025f,.0074f);
                var lampRenderer = lamp.GetComponent<Renderer>(); lampRenderer.sharedMaterial = amberOff;
                root.AddComponent<ReturnCountdownView>().Configure(renderers, amberLit, amberOff, lampRenderer, status, Audio(root,"WindowWarning",.16f));
                Save(root, CountdownPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject ImportGeometry(string name, Transform parent, Scene scene)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ArtRoot + "/Models/" + name + ".glb");
            if (!asset) throw new FileNotFoundException("Import GLB before building: " + name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Static geometry"; instance.transform.SetParent(parent,false);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i]) continue;
                    string path = ArtRoot + "/Materials/" + materials[i].name.Replace(" (Instance)", "") + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!material) { material = new Material(materials[i]); AssetDatabase.CreateAsset(material,path); }
                    ConfigurePhysicalMaterial(material);
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
            return instance;
        }

        private static void ConfigurePhysicalMaterial(Material material)
        {
            // glTF's shared PBR shader turns the glossy, near-black cover into a bright
            // environment reflection in the cockpit. Keep printed information and the
            // smoked display optically dark; use editable native URP materials elsewhere.
            bool flat = false; Color color; float metal = 0f, smooth = .25f;
            switch (material.name)
            {
                case "RW_Ivory_Enamel": color = new Color(.73f,.71f,.61f); metal=.25f; smooth=.3f; break;
                case "RW_Aged_Paper": color = new Color(.79f,.72f,.53f); smooth=.08f; break;
                case "RW_Safety_Orange": color = new Color(.72f,.29f,.105f); metal=.15f; smooth=.3f; break;
                case "RW_Black_Rubber": color = new Color(.018f,.023f,.022f); flat=true; break;
                case "RW_Brushed_Metal": color = new Color(.38f,.42f,.42f); metal=.65f; smooth=.42f; break;
                case "RW_Printed_Ink": color = new Color(.035f,.05f,.042f); flat=true; break;
                case "RW_Red_Ink": color = RedColor; flat=true; break;
                case "RW_Smoked_Glass": color = new Color(.014f,.029f,.026f); flat=true; break;
                case "RW_Amber_LED": color = new Color(1.6f,.42f,.055f); flat=true; break;
                case "RW_Amber_Unlit": color = new Color(.06f,.025f,.01f); flat=true; break;
                default: return;
            }
            var shader = Shader.Find(flat ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("Native URP prop shader was not found.");
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.SetColor("_BaseColor",color); material.SetFloat("_Surface",0); material.SetFloat("_Cull",2);
            if (!flat)
            {
                material.SetFloat("_Metallic",metal); material.SetFloat("_Smoothness",smooth);
                material.SetFloat("_SpecularHighlights", material.name == "RW_Aged_Paper" ? 0 : 1);
                material.SetFloat("_EnvironmentReflections", material.name == "RW_Aged_Paper" ? 0 : 1);
            }
            material.renderQueue = 2000;
            EditorUtility.SetDirty(material);
        }

        private static GameObject NewRoot(string name, Scene scene)
        {
            var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root,scene); return root;
        }
        private static GameObject Child(string name, Transform parent)
        {
            var result = new GameObject(name); result.transform.SetParent(parent,false); return result;
        }
        private static TMP_Text Text(string name,string body,Transform parent,Vector3 p,float height,float width,Color color,float boxHeight = .035f)
        {
            var go = Child(name,parent); var text = go.AddComponent<TextMeshPro>();
            go.transform.localPosition = p; go.transform.localRotation = Quaternion.Euler(0,180,0);
            text.font = font; text.text = body; text.fontSize = height * 14f;
            text.color = color; text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(width,boxHeight);
            text.enableAutoSizing = false; text.richText = false;
            text.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return text;
        }
        private static LineRenderer Line(string name,Transform parent,Vector3[] points,float width,Material material)
        {
            var go = Child(name,parent); var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.positionCount = points.Length; line.SetPositions(points);
            line.startWidth = line.endWidth = width; line.numCapVertices = 3; line.numCornerVertices = 3;
            line.alignment = LineAlignment.TransformZ; line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        private static Material FlatMaterial(string name,Color color)
        {
            string path = ArtRoot + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (!shader) throw new InvalidOperationException("No unlit shader available.");
                material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material,path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",color);
            if (material.HasProperty("_Color")) material.SetColor("_Color",color);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull",0);
            EditorUtility.SetDirty(material); return material;
        }
        private static Mesh BuildSegmentMesh()
        {
            string path = ArtRoot + "/Meshes/SevenSegmentBar.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = "Tapered seven segment bar" }; AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(-.008f,-.0015f,0),new Vector3(.008f,-.0015f,0),new Vector3(.010f,0,0),new Vector3(.008f,.0015f,0),new Vector3(-.008f,.0015f,0),new Vector3(-.010f,0,0) };
            mesh.triangles = new[] {0,1,2,0,2,3,0,3,4,0,4,5};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
        private static AudioSource Audio(GameObject root,string name,float volume)
        {
            var audio = root.AddComponent<AudioSource>(); audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ArtRoot + "/Audio/" + name + ".wav");
            audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 1; audio.volume = volume;
            audio.minDistance = .3f; audio.maxDistance = 3f; return audio;
        }
        private static void WriteSound(string name,bool pencil)
        {
            string path = ArtRoot + "/Audio/" + name + ".wav";
            const int rate = 22050; int count = (int)(rate * (pencil ? .48f : .18f));
            using (var stream = File.Create(path)) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count*2);
                var random = new System.Random(218); double filtered = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)rate, phase = (i / (double)count * 2) % 1;
                    filtered = filtered * .6 + (random.NextDouble() * 2 - 1) * .4;
                    double envelope = Math.Sin(Math.PI * Math.Min(1,pencil ? phase : i/(double)count));
                    double sample = pencil ? filtered * envelope * .6 : Math.Sin(t * 2 * Math.PI * 880) * envelope * .25;
                    writer.Write((short)(sample*short.MaxValue));
                }
            }
        }
        private static void Save(GameObject root,string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root,path,out bool success);
            if (!success) throw new IOException("Could not save " + path);
        }
    }
}
