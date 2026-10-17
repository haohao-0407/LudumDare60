using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Project-scoped bridge for the explicit T2.5 endpoint, added after the installed MCP catalog.
// Credentials remain in the editor secure store; manifests contain no credentials or image bytes.
public static class MarsBaseMeshySubmit
{
    public static string Submit(string name, string reference, float size, string texturePrompt)
    {
        if (name != "Rover_T25" && name != "LifeSupport_T25") throw new ArgumentException("Unexpected asset name.");
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../SourceArt/MarsBase"));
        Directory.CreateDirectory(folder);
        string manifest = Path.Combine(folder, name + "_job.json");
        if (File.Exists(manifest)) return "Submission already recorded; recover the saved provider task instead: " + manifest;
        if (!File.Exists(reference)) throw new FileNotFoundException("Reference image missing", reference);
        if (!SecureKeyStore.Current.TryGet("meshy", out string apiKey) || string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Meshy credential is not configured.");
        var request = new JObject
        {
            ["ai_model"] = "meshy-t2.5", ["model_type"] = "smart-topology",
            ["topology"] = "triangle", ["target_polycount"] = 30000,
            ["should_texture"] = true, ["enable_pbr"] = true, ["texture_resolution"] = "4k",
            ["texture_prompt"] = texturePrompt, ["target_formats"] = new JArray("glb")
        };
        var journal = new JObject
        {
            ["name"] = name, ["provider"] = "meshy", ["mode"] = "image",
            ["reference_path"] = reference, ["requested_size"] = size,
            ["submitted_utc"] = DateTime.UtcNow.ToString("o"),
            ["state"] = "submission_pending", ["request"] = request.DeepClone()
        };
        File.WriteAllText(manifest, journal.ToString());
        request["image_url"] = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(reference));
        SubmitAsync(name, size, apiKey, request, journal, manifest);
        return "T2.5 submission started; inspect " + manifest;
    }

    private static async void SubmitAsync(string name, float size, string key, JObject request, JObject journal, string path)
    {
        try
        {
            var spec = new HttpRequestSpec
            {
                Method = "POST", Url = "https://api.meshy.ai/openapi/v1/image-to-3d",
                ContentType = "application/json", Body = Encoding.UTF8.GetBytes(request.ToString()),
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + key }
            };
            var response = await new UnityWebRequestTransport().SendAsync(spec, CancellationToken.None);
            journal["http_status"] = response.Status;
            if (!response.Ok)
            {
                journal["state"] = response.Status >= 400 && response.Status < 500 ? "rejected" : "submission_unknown";
                journal["error"] = (response.Text ?? "No response").Replace(key, "[REDACTED]");
                File.WriteAllText(path, journal.ToString());
                return;
            }
            string taskId = JObject.Parse(response.Text)["result"]?.Value<string>();
            if (string.IsNullOrEmpty(taskId)) throw new InvalidOperationException("Response did not contain a task ID.");
            journal["provider_task_id"] = taskId;
            journal["state"] = "submitted";
            File.WriteAllText(path, journal.ToString());
            var job = AssetGenJobManager.RecoverMeshyTask(taskId, "image", "glb", name, "Assets/MarsBase/Models/Meshy", size);
            journal["job_id"] = job.JobId;
            journal["state"] = "polling_import";
            File.WriteAllText(path, journal.ToString());
        }
        catch (Exception e)
        {
            journal["state"] = journal["provider_task_id"] == null ? "submission_unknown" : "recovery_needed";
            journal["error"] = e.Message.Replace(key, "[REDACTED]");
            File.WriteAllText(path, journal.ToString());
        }
    }
}
