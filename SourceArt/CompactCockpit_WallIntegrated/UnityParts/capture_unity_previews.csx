// Run as an Editor execute_code body. Uses isolated preview scenes only.
var folder = "SourceArt/CompactCockpit_WallIntegrated/Previews";
System.IO.Directory.CreateDirectory(folder);
var assetRoot = "Assets/CompactCockpit_WallIntegrated/Prefabs/";
var outputs = new System.Collections.Generic.List<string>();
foreach (var contactSheet in new[] {false, true})
{
    var preview = new UnityEditor.PreviewRenderUtility();
    try
    {
        preview.ambientColor = new UnityEngine.Color(.30f,.33f,.38f,1);
        preview.lights[0].intensity = 1.25f;
        preview.lights[0].color = new UnityEngine.Color(1,.88f,.70f);
        preview.lights[0].transform.rotation = UnityEngine.Quaternion.Euler(35,190,0);
        preview.lights[1].intensity = .75f;
        preview.lights[1].color = new UnityEngine.Color(.72f,.85f,1);
        preview.lights[1].transform.rotation = UnityEngine.Quaternion.Euler(325,15,0);
        foreach(var light in preview.lights) light.shadows=UnityEngine.LightShadows.None;
        if (contactSheet)
        {
            var names=new[]{"RADAR","ATTITUDE","NAVIGATION","COMMUNICATIONS","SYSTEMS"};
            var positions=new[]{new UnityEngine.Vector3(.60f,.45f,0),new UnityEngine.Vector3(-.60f,.45f,0),new UnityEngine.Vector3(0,-.32f,0),new UnityEngine.Vector3(.98f,-.32f,0),new UnityEngine.Vector3(-.98f,-.32f,0)};
            for(int i=0;i<names.Length;i++)
            {
                var source=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(assetRoot+"Screens/Screen_"+names[i]+".prefab");
                var instance=preview.InstantiatePrefabInScene(source);
                instance.transform.position=positions[i];
            }
            preview.camera.orthographic=true;
            preview.camera.orthographicSize=.94f;
            preview.camera.transform.position=new UnityEngine.Vector3(0,.08f,3);
            preview.camera.transform.rotation=UnityEngine.Quaternion.LookRotation(UnityEngine.Vector3.back);
        }
        else
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(assetRoot+"CompactCockpit_WallIntegrated.prefab");
            var instance=preview.InstantiatePrefabInScene(source);
            preview.camera.orthographic=false;
            preview.cameraFieldOfView=58;
            preview.camera.transform.position=new UnityEngine.Vector3(0,1.49f,.98f);
            preview.camera.transform.rotation=UnityEngine.Quaternion.LookRotation(new UnityEngine.Vector3(0,1.21f,-.72f)-preview.camera.transform.position);
        }
        preview.camera.nearClipPlane=.015f;
        preview.camera.farClipPlane=12;
        preview.camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
        preview.camera.backgroundColor=new UnityEngine.Color(.045f,.055f,.07f,1);
        preview.BeginStaticPreview(new UnityEngine.Rect(0,0,1440,contactSheet?960:1080));
        preview.Render(true);
        var texture=preview.EndStaticPreview();
        var path=folder+(contactSheet?"/Unity_ScreenPrefabs.png":"/Unity_CockpitInterior.png");
        System.IO.File.WriteAllBytes(path,UnityEngine.ImageConversion.EncodeToPNG(texture));
        UnityEngine.Object.DestroyImmediate(texture);
        outputs.Add(path);
    }
    finally {preview.Cleanup();}
}
return outputs;
