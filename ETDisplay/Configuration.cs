using Dalamud.Configuration;
using System;

namespace ETDisplay;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public bool SSAlertEnabled { get; set; } = true;
    public bool SSTtsEnabled { get; set; } = true;
    public float SSAlertDuration { get; set; } = 5;
    public uint SSAlertColor { get; set; } = 0xFF00FFFF; // AABBGGRR: yellow
    public int SpeechVolume { get; set; } = 100;
    public bool CountdownEnabled { get; set; } = true;
    public bool CountdownTtsEnabled { get; set; } = true;
    public float TimeCalibrationOffset { get; set; } = 0;
    public bool TtsEnabled { get; set; } = true;
    public bool AutoClearOnSanctuary { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public bool IsLocked { get; set; } = true;
    public float FontSize { get; set; } = 48;
    public uint TextColor { get; set; } = 0xFFFFFF00; // AABBGGRR: aqua
    public uint OutlineColor { get; set; } = 0xFF000000;
    public float OutlineThickness { get; set; } = 2;

    public void Validate()
    {
        // Preserve existing visual, TTS, volume and calibration preferences.
        if (Version < 4) Version = 4;
        SSAlertDuration = float.IsFinite(SSAlertDuration) ? Math.Clamp(SSAlertDuration, 1, 30) : 5;
        SpeechVolume = Math.Clamp(SpeechVolume, 0, 100);
        TimeCalibrationOffset = float.IsFinite(TimeCalibrationOffset) ? Math.Clamp(TimeCalibrationOffset, -20, 20) : 0;
        FontSize = float.IsFinite(FontSize) ? Math.Clamp(FontSize, 20, 96) : 48;
        OutlineThickness = float.IsFinite(OutlineThickness) ? Math.Clamp(OutlineThickness, 0, 4) : 2;
    }

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
