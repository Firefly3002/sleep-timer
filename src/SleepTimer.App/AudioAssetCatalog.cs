using System.IO;
using SleepTimer.Core;

namespace SleepTimer.Desktop;

internal sealed record AudioAsset(string Id, string Name, string FileName);

internal static class AudioAssetCatalog
{
    public static IReadOnlyList<AudioAsset> MusicTracks { get; } =
    [
        new(AudioSelectionIds.MoonlitAmbient, "Moonlit ambient", "moonlit-ambient.mp3"),
        new(AudioSelectionIds.SoftPiano, "Soft piano", "soft-piano.mp3"),
        new(AudioSelectionIds.RainyNight, "Gentle rain", "rainy-night.mp3"),
        new(AudioSelectionIds.NightForest, "Night forest", "night-forest.mp3"),
        new(AudioSelectionIds.OceanWaves, "Ocean waves", "ocean-waves.mp3")
    ];

    public static IReadOnlyList<AudioAsset> EndSounds { get; } =
    [
        new(AudioSelectionIds.SoftChime, "Soft chime", "soft-chime.wav"),
        new(AudioSelectionIds.WarmBell, "Warm bell", "warm-bell.wav"),
        new(AudioSelectionIds.NightBird, "Night bird", "night-bird.wav"),
        new(AudioSelectionIds.MoonSparkle, "Moon sparkle", "moon-sparkle.wav"),
        new(AudioSelectionIds.Stardust, "Stardust", "stardust.wav"),
        new(AudioSelectionIds.DreamPortal, "Dream portal", "dream-portal.wav")
    ];

    public static string? ResolveMusic(AppSettings settings)
        => Resolve(settings.SleepMusicSelectionId, settings.SleepMusicFilePath, MusicTracks);

    public static string? ResolveMusic(string selectionId, string filePath)
        => Resolve(selectionId, filePath, MusicTracks);

    public static string? ResolveEndSound(AppSettings settings)
        => Resolve(settings.EndSoundSelectionId, settings.EndSoundFilePath, EndSounds);

    public static string? ResolveEndSound(string selectionId, string filePath)
        => Resolve(selectionId, filePath, EndSounds);

    public static string? ResolveBuiltIn(string id, IReadOnlyList<AudioAsset> assets)
    {
        var asset = assets.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault();
        return asset is null ? null : Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", asset.FileName);
    }

    private static string? Resolve(string selectionId, string filePath, IReadOnlyList<AudioAsset> assets)
    {
        if (string.Equals(selectionId, AudioSelectionIds.CustomFile, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        return ResolveBuiltIn(selectionId, assets);
    }
}
