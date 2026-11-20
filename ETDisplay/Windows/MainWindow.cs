using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;
using ETDisplay.Core;
using ETDisplay.Rendering;
using System;
using System.Numerics;

namespace ETDisplay.Windows;

/// <summary>Read-only snapshot renderer; neither business time nor speech runs in Draw.</summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly DisplaySession session;
    private readonly CountdownSession countdown;
    private readonly SsAlertSession ssAlert;
    private readonly OverlayFont overlayFont;
    private readonly OverlayFont countdownFont;
    private readonly OverlayFont ssFont;
    private bool resetPosition;
    private bool resetApplied;
    private string frameEt = string.Empty;
    private string frameCountdown = string.Empty;
    private string frameSs = string.Empty;

    public MainWindow(Configuration config, DisplaySession session, CountdownSession countdown, SsAlertSession ssAlert) : base("ETDisplay（拖动标题栏移动）###ETDisplayOverlay")
    {
        this.config = config;
        this.session = session;
        this.countdown = countdown;
        this.ssAlert = ssAlert;
        overlayFont = new OverlayFont(config.FontSize);
        countdownFont = new OverlayFont(config.FontSize * 0.65f, includeChinese: true);
        ssFont = new OverlayFont(config.FontSize, includeChinese: true);
        IsOpen = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        AllowPinning = false;
        AllowClickthrough = false;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(600, 100);
    }

    public string FontStatus => $"ET：{overlayFont.Status}；" +
        (config.CountdownEnabled ? $"倒计时：{countdownFont.Status}；" : "倒计时已关闭；") +
        (config.SSAlertEnabled ? $"SS：{ssFont.Status}" : "SS已关闭");

    public void UpdateFont()
    {
        overlayFont.Update(config.FontSize);
        if (config.CountdownEnabled) countdownFont.Update(config.FontSize * 0.65f);
        if (config.SSAlertEnabled) ssFont.Update(config.FontSize);
    }

    public void RequestPositionReset() => resetPosition = true;
    public override bool DrawConditions()
    {
        var display = session.Snapshot;
        return display.Enabled && (display.IsVisible || (config.SSAlertEnabled && ssAlert.Snapshot.IsVisible));
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing;
        if (config.IsLocked)
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove;
        var display = session.Snapshot;
        frameEt = display.IsVisible ? display.Text : string.Empty;
        frameCountdown = frameEt.Length > 0 && config.CountdownEnabled ? countdown.Snapshot.Text : string.Empty;
        frameSs = display.Enabled && config.SSAlertEnabled ? ssAlert.Snapshot.Text : string.Empty;
        var top = config.IsLocked ? 12 : ImGui.GetFrameHeight() + 12;
        float width = 0;
        float height = 0;
        MeasureLine(frameEt, overlayFont, ref width, ref height);
        MeasureLine(frameCountdown, countdownFont, ref width, ref height);
        MeasureLine(frameSs, ssFont, ref width, ref height);
        Size = new Vector2(MathF.Max(300, width + 40), top + height + 22) / ImGuiHelpers.GlobalScale;
        if (resetPosition)
        {
            Position = ImGui.GetMainViewport().WorkPos + new Vector2(80, 100);
            PositionCondition = ImGuiCond.Always;
            resetPosition = false;
            resetApplied = true;
        }
    }

    private static void MeasureLine(string text, OverlayFont font, ref float width, ref float height)
    {
        if (text.Length == 0) return;
        using (font.Push())
        {
            width = MathF.Max(width, ImGui.CalcTextSize(text).X);
            height += (height > 0 ? 8 : 0) + ImGui.GetFontSize();
        }
    }

    public override void PostDraw()
    {
        if (!resetApplied) return;
        Position = null;
        PositionCondition = ImGuiCond.FirstUseEver;
        resetApplied = false;
    }

    public override void Draw()
    {
        if (!session.Snapshot.Enabled) return;
        var y = config.IsLocked ? 12 : ImGui.GetFrameHeight() + 12;
        if (frameEt.Length > 0)
            y += DrawLine(frameEt, y, overlayFont, config.TextColor, config.OutlineThickness) + 8;
        if (frameCountdown.Length > 0)
            y += DrawLine(frameCountdown, y, countdownFont, config.TextColor, config.OutlineThickness * 0.75f) + 8;
        if (frameSs.Length > 0)
            DrawLine(frameSs, y, ssFont, config.SSAlertColor, config.OutlineThickness);
    }

    private float DrawLine(string text, float top, OverlayFont handle, uint color, float thickness)
    {
        using var fontScope = handle.Push();
        var font = ImGui.GetFont();
        var renderedSize = ImGui.GetFontSize();
        var measured = ImGui.CalcTextSize(text);
        var pos = AlignToPixels(ImGui.GetWindowPos() + new Vector2((ImGui.GetWindowSize().X - measured.X) / 2, top));
        var drawList = ImGui.GetWindowDrawList();
        if (thickness > 0)
        {
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                        drawList.AddText(font, renderedSize, AlignToPixels(pos + new Vector2(dx, dy) * thickness), config.OutlineColor, text);
        }
        drawList.AddText(font, renderedSize, pos, color, text);
        return renderedSize;
    }

    private static Vector2 AlignToPixels(Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));
    public void Dispose()
    {
        overlayFont.Dispose();
        countdownFont.Dispose();
        ssFont.Dispose();
    }
}
