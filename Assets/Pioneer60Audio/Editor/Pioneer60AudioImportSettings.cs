using UnityEditor;
using UnityEngine;

// Applies only to this audio pack. PCM preserves the authored loop boundaries.
public sealed class Pioneer60AudioImportSettings : AssetPostprocessor
{
    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Pioneer60Audio/Audio/", System.StringComparison.Ordinal))
            return;

        // 音乐目录例外：整首曲子几MB到几十MB，按 PCM 解压会整段驻留内存，
        // 而且歌曲不需要"样本级循环点"，用流式压缩更合适。
        // 其它目录（01_Loops / 02_Single_Shots / 03_Composites）继续走 PCM。
        if (assetPath.StartsWith("Assets/Pioneer60Audio/Audio/04_Music/", System.StringComparison.Ordinal))
        {
            var musicImporter = (AudioImporter)assetImporter;
            musicImporter.forceToMono = false;
            musicImporter.loadInBackground = true;
            var musicSettings = musicImporter.defaultSampleSettings;
            musicSettings.compressionFormat = AudioCompressionFormat.Vorbis;
            musicSettings.loadType = AudioClipLoadType.Streaming;
            musicSettings.quality = 0.7f;
            musicSettings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            musicSettings.preloadAudioData = false;
            musicImporter.defaultSampleSettings = musicSettings;
            return;
        }

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

    public override uint GetVersion() => 2;
}
