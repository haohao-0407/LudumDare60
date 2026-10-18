// Execute as a Unity execute_code method body after replacing __JOB_NAME__.
// No scene objects are created; the existing asset pipeline imports the returned GLB only.
string name = "__JOB_NAME__";
string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../SourceArt/MarsCabin/Jobs"));
string specificationPath = System.IO.Path.Combine(folder, name + "_request.json");
string journalPath = System.IO.Path.Combine(folder, name + "_job.json");
if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$"))
    throw new System.ArgumentException("Invalid asset name.");
if (!System.IO.File.Exists(specificationPath))
    return new { state = "not_submitted", reason = "Request specification missing", specificationPath };
if (System.IO.File.Exists(journalPath))
    return new { state = "already_recorded", journalPath, message = "Recover the existing task; do not resubmit." };
var specification = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(specificationPath));
if ((bool?)specification["reference_inspected"] != true)
    throw new System.InvalidOperationException("Inspect the isolated reference before submission.");
string reference = (string)specification["reference_path"];
if (!System.IO.File.Exists(reference)) throw new System.IO.FileNotFoundException("Reference missing", reference);
byte[] imageBytes = System.IO.File.ReadAllBytes(reference);
if (imageBytes.Length > 20 * 1024 * 1024) throw new System.ArgumentException("Reference exceeds Meshy's 20 MB limit.");
string mime;
switch (System.IO.Path.GetExtension(reference).ToLowerInvariant()) {
    case ".png": mime = "image/png"; break;
    case ".jpg": case ".jpeg": mime = "image/jpeg"; break;
    case ".webp": mime = "image/webp"; break;
    default: throw new System.ArgumentException("Unsupported reference image format.");
}
if (!MCPForUnity.Editor.Security.SecureKeyStore.Current.TryGet("meshy", out string apiKey) || string.IsNullOrEmpty(apiKey))
    throw new System.InvalidOperationException("Meshy credential is not configured.");
string texturePrompt = (string)specification["texture_prompt"] ?? "Preserve the reference materials, colors and fine surface details.";
if (texturePrompt.Length > 800) throw new System.ArgumentException("texture_prompt exceeds 800 characters.");
var request = new Newtonsoft.Json.Linq.JObject {
    ["ai_model"] = "meshy-t2.5", ["model_type"] = "smart-topology",
    ["topology"] = "triangle", ["target_polycount"] = 30000,
    ["should_texture"] = true, ["enable_pbr"] = true, ["texture_resolution"] = "4k",
    ["texture_prompt"] = texturePrompt,
    ["target_formats"] = new Newtonsoft.Json.Linq.JArray("glb")
};
string digest;
using (var sha = System.Security.Cryptography.SHA256.Create())
    digest = System.BitConverter.ToString(sha.ComputeHash(imageBytes)).Replace("-", "").ToLowerInvariant();
var journal = new Newtonsoft.Json.Linq.JObject {
    ["name"] = name, ["provider"] = "meshy", ["mode"] = "image",
    ["reference_path"] = reference, ["reference_sha256"] = digest,
    ["reference_inspected"] = true,
    ["requested_target_polycount"] = specification["requested_target_polycount"]?.DeepClone(),
    ["provider_limit_note"] = "Meshy T2.5 supports 100 to 30000 faces; explicit maximum used.",
    ["requested_size"] = (float?)specification["requested_size"] ?? 1f,
    ["submitted_utc"] = System.DateTime.UtcNow.ToString("o"),
    ["state"] = "submission_pending", ["request"] = request.DeepClone(),
    ["output_folder"] = "Assets/MarsCabin/Models/Meshy"
};
// CreateNew is the duplicate-submission guard, including uncertain remote outcomes.
using (var stream = new System.IO.FileStream(journalPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
using (var writer = new System.IO.StreamWriter(stream)) writer.Write(journal.ToString());
request["image_url"] = "data:" + mime + ";base64," + System.Convert.ToBase64String(imageBytes);

async System.Threading.Tasks.Task SubmitAndRecover() {
    try {
        var spec = new MCPForUnity.Editor.Services.AssetGen.Http.HttpRequestSpec {
            Method = "POST", Url = "https://api.meshy.ai/openapi/v1/image-to-3d",
            ContentType = "application/json", Body = System.Text.Encoding.UTF8.GetBytes(request.ToString()),
            Headers = new System.Collections.Generic.Dictionary<string, string> { ["Authorization"] = "Bearer " + apiKey }
        };
        var response = await new MCPForUnity.Editor.Services.AssetGen.Http.UnityWebRequestTransport().SendAsync(spec, System.Threading.CancellationToken.None);
        journal["http_status"] = response.Status;
        if (!response.Ok) {
            journal["state"] = response.Status >= 400 && response.Status < 500 ? "rejected" : "submission_unknown";
            journal["error"] = MCPForUnity.Editor.Security.SecretRedactor.Scrub(response.Text ?? "No response", apiKey);
            System.IO.File.WriteAllText(journalPath, journal.ToString());
            return;
        }
        string taskId = (string)Newtonsoft.Json.Linq.JObject.Parse(response.Text)["result"];
        if (string.IsNullOrWhiteSpace(taskId)) throw new System.InvalidOperationException("No confirmed provider task ID in response.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, name + "_task_id.txt"), taskId);
        journal["provider_task_id"] = taskId;
        journal["state"] = "submitted";
        System.IO.File.WriteAllText(journalPath, journal.ToString());
        var job = MCPForUnity.Editor.Services.AssetGen.AssetGenJobManager.RecoverMeshyTask(
            taskId, "image", "glb", name, "Assets/MarsCabin/Models/Meshy", (float)journal["requested_size"]);
        journal["job_id"] = job.JobId;
        journal["state"] = "polling_import";
        System.IO.File.WriteAllText(journalPath, journal.ToString());
    } catch (System.Exception error) {
        journal["state"] = journal["provider_task_id"] == null ? "submission_unknown" : "recovery_needed";
        journal["error"] = MCPForUnity.Editor.Security.SecretRedactor.Scrub(error.Message, apiKey);
        System.IO.File.WriteAllText(journalPath, journal.ToString());
    }
}
_ = SubmitAndRecover();
return new { state = "submission_started", name, journalPath, model = "meshy-t2.5", target_polycount = 30000, texture_resolution = "4k" };
