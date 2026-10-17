var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/MarsCabin/Scenes/MarsCabin_Interior.unity") throw new System.InvalidOperationException("Expected cabin scene");
var roots=scene.GetRootGameObjects();
var controls=roots.SelectMany(r=>r.GetComponentsInChildren<LudumDare60.MarsCabin.CabinInteractable>(true)).ToArray();
var checks=new System.Collections.Generic.List<object>();
var failures=new System.Collections.Generic.List<string>();
var everyTransform=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
foreach(var retired in new[]{"NavConsole_T25","ThrottlePod_T25","JoystickPod_T25"})
  if(everyTransform.Any(t=>t.name==retired&&t.gameObject.activeInHierarchy))failures.Add("Retired generated foreground still active: "+retired);
var hero=everyTransform.FirstOrDefault(t=>t.name=="Cabin_HeroConsole");
if(hero==null)failures.Add("Missing authored hero console");
else
{
 var heroFilters=hero.GetComponentsInChildren<MeshFilter>(true);
 foreach(var f in heroFilters)
 {
  var asset=AssetDatabase.GetAssetPath(f.sharedMesh);
  if(asset.EndsWith(".glb",System.StringComparison.OrdinalIgnoreCase))failures.Add("Generated mesh used in hero console: "+f.name);
 }
 var heroScreens=hero.GetComponentsInChildren<LudumDare60.MarsCabin.CabinScreenPanel>(true);
 if(heroScreens.Length!=3)failures.Add("Expected three independent authored CRT displays");
}
var meshyRoot=everyTransform.FirstOrDefault(t=>t.name=="Meshy_Cabin_Props");
if(meshyRoot!=null&&meshyRoot.GetComponentsInChildren<LudumDare60.MarsCabin.CabinInteractable>(true).Length!=0)
  failures.Add("Meshy decorations still contain interaction components");
foreach(var c in controls)
{
  var hit=c.GetComponent<Collider>();
  var pivot=c.MotionPivot;
  float initial=c.NormalizedValue;var initialJoy=c.JoystickValue;
  var p0=pivot!=null?pivot.position:Vector3.zero;var q0=pivot!=null?pivot.rotation:Quaternion.identity;
  float move=0,angle=0;
  if(c.Kind!=LudumDare60.MarsCabin.CabinControlKind.Device)
  {
    if(pivot==null){ failures.Add(c.StableId+": missing pivot");continue;}
    if(c.Kind==LudumDare60.MarsCabin.CabinControlKind.Joystick)c.SetJoystick(new Vector2(.65f,.45f));
    else c.SetNormalizedValue(initial>.5f?0:1);
    move=Vector3.Distance(p0,pivot.position);angle=Quaternion.Angle(q0,pivot.rotation);
    c.SetNormalizedValue(initial);c.SetJoystick(initialJoy);
    if(move<.0001f&&angle<.1f)failures.Add(c.StableId+": did not articulate");
    if(Vector3.Distance(p0,pivot.position)>.0001f||Quaternion.Angle(q0,pivot.rotation)>.05f)failures.Add(c.StableId+": did not restore");
  }
  if(hit==null||!hit.enabled)failures.Add(c.StableId+": no enabled collider");
  var size=hit!=null?hit.bounds.size:Vector3.zero;
  if(hit!=null&&Mathf.Max(size.x,size.y,size.z)>3)failures.Add(c.StableId+": oversized collider");
  checks.Add(new {id=c.StableId,kind=c.Kind.ToString(),colliderSize=new[]{size.x,size.y,size.z},motionMetres=move,motionDegrees=angle});
}
Physics.SyncTransforms();
var labelChecks=new System.Collections.Generic.List<object>();
foreach(var c in controls.Where(c=>c.name.StartsWith("INT_BUTTON_L_")||c.name.StartsWith("INT_BUTTON_R_")))
{
 var label=c.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.StartsWith("Label_"));
 if(label==null){failures.Add(c.StableId+": missing moving numeric label");continue;}
 var p0=c.MotionPivot.position;var l0=label.position;var initial=c.NormalizedValue;
 c.SetNormalizedValue(initial>.5f?0:1);
 var error=((label.position-l0)-(c.MotionPivot.position-p0)).magnitude;
 c.SetNormalizedValue(initial);
 var restored=Vector3.Distance(l0,label.position)<.0001f;
 if(error>.0001f||!restored)failures.Add(c.StableId+": label did not follow and restore");
 labelChecks.Add(new {id=c.StableId,label=label.name,errorMetres=error,restored});
}
if(labelChecks.Count!=18)failures.Add("Expected 18 bound numeric labels");
Physics.SyncTransforms();
var ceilingHits=new System.Collections.Generic.List<object>();
foreach(var x in new[]{-1.5f,0,1.5f})foreach(var z in new[]{-1.5f,0,1.5f})
{
 var origin=new Vector3(x,1.8f,z);RaycastHit hit;
 bool found=Physics.Raycast(origin,Vector3.up,out hit,4,~0,QueryTriggerInteraction.Ignore);
 ceilingHits.Add(new {x,z,found,hit=found?hit.collider.name:"",height=found?hit.point.y:0});
 if(!found)failures.Add("Ceiling gap at "+x+","+z);
}
var ids=controls.Select(c=>c.StableId).ToArray();
if(ids.Distinct().Count()!=ids.Length)failures.Add("Duplicate control ids");
var badMaterials=roots.SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m==null||m.shader==null||!m.shader.isSupported).Count();
if(badMaterials>0)failures.Add("Missing/unsupported materials");
return new {scene=scene.path,controls=controls.Length,interactiveChecks=checks,labelChecks,ceilingChecks=ceilingHits,failures,playMode=EditorApplication.isPlaying};

