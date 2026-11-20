using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace ETDisplay.Rendering;

/// <summary>
/// Independent, read-only appearance preview. The owning settings window must dispose it.
/// Draw (including font updates) and Dispose must run on the UI thread.
/// </summary>
public sealed class OverlayPreview : IDisposable
{
    private const string EtText = "ET 12:34";
    private const string CountdownText = "剩余10秒";
    private const string SsText = "SS出现！";
    private const float LineGap = 8;
    private readonly OverlayFont etFont;
    private readonly OverlayFont countdownFont;
    private readonly OverlayFont ssFont;
    private bool disposed;

    public OverlayPreview(Configuration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        // These handles belong only to the preview; construction does not update the atlas.
        etFont = new OverlayFont(config.FontSize);
        countdownFont = new OverlayFont(config.FontSize * 0.65f, includeChinese: true);
        ssFont = new OverlayFont(config.FontSize, includeChinese: true);
    }

    public void Draw(Configuration config)
    {
        if (disposed) return;
        ArgumentNullException.ThrowIfNull(config);

        // Update before any Push scope. OverlayFont owns the existing 200ms debounce
        // and keeps the previous rasterized size alive while the new one is building.
        etFont.Update(config.FontSize);
        countdownFont.Update(config.FontSize * 0.65f);
        ssFont.Update(config.FontSize);

        var et = MeasureLine(EtText, etFont);
        var countdown = MeasureLine(CountdownText, countdownFont);
        var ss = MeasureLine(SsText, ssFont);
        var style = ImGui.GetStyle();
        var thickness = float.IsFinite(config.OutlineThickness)
            ? Math.Clamp(config.OutlineThickness, 0, 4) : 2;
        // UI spacing follows global scale; font sizes and outline remain actual pixels,
        // just as in the overlay. Leave room on every side for all outline samples.
        var inset = MathF.Ceiling(12 * ImGuiHelpers.GlobalScale + thickness + 1);
        var availableWidth = MathF.Max(1, ImGui.GetContentRegionAvail().X
            - 2 * style.WindowPadding.X - 2 * style.ChildBorderSize);
        var contentWidth = MathF.Ceiling(MathF.Max(availableWidth,
            MathF.Max(et.X, MathF.Max(countdown.X, ss.X)) + 2 * inset));
        var contentHeight = MathF.Ceiling(et.Y + countdown.Y + ss.Y + 2 * LineGap + 2 * inset);
        // Reserve a scrollbar's height even when not needed, avoiding a vertical
        // scrollbar when the horizontal bar appears in a narrow settings window.
        var childHeight = MathF.Ceiling(contentHeight + 2 * style.WindowPadding.Y
            + 2 * style.ChildBorderSize + style.ScrollbarSize);

        ImGui.SetNextWindowContentSize(new Vector2(contentWidth, contentHeight));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.5f, 0.5f, 0.5f, 0.25f));
        try
        {
            var visible = ImGui.BeginChild("##ETDisplayAppearancePreview", new Vector2(0, childHeight),
                true, ImGuiWindowFlags.HorizontalScrollbar);
            try
            {
                if (!visible) return;
                var origin = ImGui.GetCursorScreenPos();
                var y = inset;
                DrawLine(EtText, etFont, origin, contentWidth, y, et.X,
                    config.TextColor, config.OutlineColor, thickness);
                y += et.Y + LineGap;
                DrawLine(CountdownText, countdownFont, origin, contentWidth, y, countdown.X,
                    config.TextColor, config.OutlineColor, thickness * 0.75f);
                y += countdown.Y + LineGap;
                DrawLine(SsText, ssFont, origin, contentWidth, y, ss.X,
                    config.SSAlertColor, config.OutlineColor, thickness);
                // Draw-list text does not advance layout. Register the entire native-size
                // canvas so even the widest line and its outline can be scrolled into view.
                ImGui.Dummy(new Vector2(contentWidth, contentHeight));
            }
            finally
            {
                // ImGui requires EndChild even when BeginChild returns false.
                ImGui.EndChild();
            }
        }
        finally
        {
            ImGui.PopStyleColor();
        }
    }

    private static Vector2 MeasureLine(string text, OverlayFont handle)
    {
        using var scope = handle.Push();
        return new Vector2(ImGui.CalcTextSize(text).X, ImGui.GetFontSize());
    }

    private static void DrawLine(string text, OverlayFont handle, Vector2 origin,
        float width, float top, float measuredWidth, uint color, uint outlineColor, float thickness)
    {
        using var scope = handle.Push();
        var font = ImGui.GetFont();
        var renderedSize = ImGui.GetFontSize();
        var position = AlignToPixels(origin + new Vector2((width - measuredWidth) / 2, top));
        var drawList = ImGui.GetWindowDrawList();
        if (thickness > 0)
        {
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                        drawList.AddText(font, renderedSize,
                            AlignToPixels(position + new Vector2(dx, dy) * thickness), outlineColor, text);
        }
        drawList.AddText(font, renderedSize, position, color, text);
    }

    private static Vector2 AlignToPixels(Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        etFont.Dispose();
        countdownFont.Dispose();
        ssFont.Dispose();
    }
}
