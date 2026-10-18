// Synchronize manifests and resume only confirmed existing Meshy tasks. Never creates geometry.
string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../SourceArt/MarsCabin/Jobs"));
var summaries = new System.Collections.Generic.List<object>();
foreach (string path in System.IO.Directory.GetFiles(folder, "*_job.json")) {
    var journal = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
    string taskId = (string)journal["provider_task_id"];
    string jobId = (string)journal["job_id"];
    string name = (string)journal["name"];
    if (string.IsNullOrWhiteSpace(taskId)) {
        summaries.Add(new { name, state = (string)journal["state"], message = "No confirmed task ID; automatic resubmission prohibited." });
        continue;
    }
    var job = MCPForUnity.Editor.Services.AssetGen.AssetGenJobManager.GetJob(jobId);
    if (job == null || job.State == MCPForUnity.Editor.Services.AssetGen.AssetGenJobState.Failed) {
        job = MCPForUnity.Editor.Services.AssetGen.AssetGenJobManager.RecoverMeshyTask(
            taskId, "image", "glb", name, "Assets/MarsCabin/Models/Meshy", (float?)journal["requested_size"] ?? 1f);
        journal["job_id"] = job.JobId;
    }
    journal["state"] = job.State.ToString().ToLowerInvariant();
    journal["progress"] = job.Progress;
    journal["asset_path"] = job.AssetPath;
    journal["error"] = job.Error;
    journal["last_checked_utc"] = System.DateTime.UtcNow.ToString("o");
    System.IO.File.WriteAllText(path, journal.ToString());
    summaries.Add(new { name, state = job.State.ToString(), progress = job.Progress, job_id = job.JobId, provider_task_id = taskId, asset_path = job.AssetPath, error = job.Error });
}
return summaries;
