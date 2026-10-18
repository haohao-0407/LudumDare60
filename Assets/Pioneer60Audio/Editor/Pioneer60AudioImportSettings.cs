using UnityEditor;
using UnityEngine;

// Applies only to this audio pack. PCM preserves the authored loop boundaries.
public sealed class Pioneer60AudioImportSettings : AssetPostprocessor
{
    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Pioneer60Audio/Audio/", System.StringComparison.Ordinal))
            return;

        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = false;
        importer.loadInBackground = false;

        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
    }

    public override uint GetVersion() => 1;
}
