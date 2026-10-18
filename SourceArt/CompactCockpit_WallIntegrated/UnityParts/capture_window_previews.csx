var folder="SourceArt/CompactCockpit_WallIntegrated/Previews";
var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CompactCockpit_WallIntegrated/Prefabs/CompactCockpit_WallIntegrated.prefab");
var positions=new[]{new Vector3(0,1.49f,1.03f),new Vector3(.1f,1.47f,.74f),new Vector3(-.1f,1.47f,.74f)};
var targets=new[]{new Vector3(0,1.28f,-.4f),new Vector3(1.04f,1.43f,.16f),new Vector3(-1.04f,1.43f,.16f)};
var names=new[]{"Unity_TwinPorts","Unity_PortLeft","Unity_PortRight"};
var paths=new System.Collections.Generic.List<string>();
for(var i=0;i<3;i++){
 var preview=new PreviewRenderUtility();
 try {
  preview.ambientColor=new Color(.30f,.33f,.38f,1);
  preview.lights[0].intensity=1.25f; preview.lights[0].color=new Color(1,.88f,.70f);
  preview.lights[0].transform.rotation=Quaternion.Euler(35,190,0);
  preview.lights[1].intensity=.75f; preview.lights[1].color=new Color(.72f,.85f,1);
  preview.lights[1].transform.rotation=Quaternion.Euler(325,15,0);
  foreach(var light in preview.lights)light.shadows=LightShadows.None;
  var instance=preview.InstantiatePrefabInScene(source);
  preview.camera.orthographic=false;
  preview.cameraFieldOfView=i==0?102:59;
  preview.camera.transform.position=positions[i];
  preview.camera.transform.rotation=Quaternion.LookRotation(targets[i]-positions[i]);
  preview.camera.nearClipPlane=.015f;preview.camera.farClipPlane=12;
  preview.camera.clearFlags=CameraClearFlags.SolidColor;
  preview.camera.backgroundColor=new Color(.045f,.055f,.07f,1);
  preview.BeginStaticPreview(new Rect(0,0,1440,1080));
  preview.Render(true);
  var texture=preview.EndStaticPreview();
  var path=folder+"/"+names[i]+".png";
  System.IO.File.WriteAllBytes(path,ImageConversion.EncodeToPNG(texture));
  UnityEngine.Object.DestroyImmediate(texture);paths.Add(path);
 } finally {preview.Cleanup();}
}
return paths;
