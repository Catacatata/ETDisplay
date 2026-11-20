using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Diagnostics;
using System.IO;

namespace ETDisplay.Rendering;

/// <summary>
/// Owns rasterized font handles. All methods are called on the UI draw thread.
/// Keep the previous font alive until its replacement is ready, and debounce slider changes.
/// </summary>
public sealed class OverlayFont : IDisposable
{
    private static readonly ushort[] AsciiGlyphs = [0x20, 0x7E, 0];
    // The countdown and SS characters only, sorted: 余 出 分 剩 时 现 秒 ！.
    private static readonly ushort[] ChineseOverlayGlyphs = [0x20, 0x7E, 0x4F59, 0x4F59, 0x51FA, 0x51FA, 0x5206, 0x5206, 0x5269, 0x5269, 0x65F6, 0x65F6, 0x73B0, 0x73B0, 0x79D2, 0x79D2, 0xFF01, 0xFF01, 0];
    private readonly ushort[] glyphs;
    private readonly float minimumSize;
    private readonly string? fontPath;
    private readonly string fontName;
    private IFontHandle? active;
    private IFontHandle? pending;
    private float activeSize;
    private float pendingSize;
    private float requestedSize;
    private float failedSize = float.NaN;
    private long requestChangedAt;
    private bool pendingUsesDefault;
    private bool activeUsesDefault;
    private bool disposed;
    private string? failure;

    public OverlayFont(float size, bool includeChinese = false)
    {
        glyphs = includeChinese ? ChineseOverlayGlyphs : AsciiGlyphs;
        minimumSize = includeChinese ? 14 : 20;
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var candidates = includeChinese
            ? new[] { ("msyhbd.ttc", "Microsoft YaHei Bold"), ("msyh.ttc", "Microsoft YaHei"), ("simhei.ttf", "SimHei") }
            : new[] { ("segoeuib.ttf", "Segoe UI Bold"), ("arialbd.ttf", "Arial Bold") };
        foreach (var (file, name) in candidates)
        {
            var path = Path.Combine(fonts, file);
            if (!File.Exists(path)) continue;
            fontPath = path;
            fontName = name;
            break;
        }
        fontName ??= "Dalamud默认字体";
        requestedSize = Normalize(size);
        requestChangedAt = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
    }

    public string Status
    {
        get
        {
            if (failure is not null) return $"字体降级：{failure}";
            if (active is null) return $"正在生成字体（{requestedSize:0}px）…";
            var source = activeUsesDefault ? "Dalamud默认字体（回退）" : fontName;
            var rebuilding = pending is not null || requestedSize != activeSize ? "，更新中…" : string.Empty;
            return $"{source} · 原生栅格化 {activeSize:0}px{rebuilding}";
        }
    }

    public void Update(float size)
    {
        if (disposed) return;
        var desired = Normalize(size);
        if (desired != requestedSize)
        {
            requestedSize = desired;
            // A previous request must not become active after the user has chosen another size.
            pending?.Dispose();
            pending = null;
            requestChangedAt = Stopwatch.GetTimestamp();
            failedSize = float.NaN;
            failure = null;
        }

        if (pending is not null)
        {
            if (pending.LoadException is { } exception)
            {
                var fallback = !pendingUsesDefault;
                var failedRequestSize = pendingSize;
                pending.Dispose();
                pending = null;
                Services.Log.Warning($"ETDisplay字体生成失败：{exception.Message}");
                if (fallback) CreatePending(failedRequestSize, useDefault: true);
                else
                {
                    failedSize = failedRequestSize;
                    failure = "字体生成失败，暂保留已可用字体";
                }
            }
            else if (pending.Available)
            {
                // No Push scope is active here. Existing draw scopes have finished in prior frames.
                var previous = active;
                active = pending;
                activeSize = pendingSize;
                activeUsesDefault = pendingUsesDefault;
                pending = null;
                previous?.Dispose();
            }
        }

        if (active is not null && activeSize == requestedSize) return;
        if (pending is not null && pendingSize == requestedSize) return;
        if (failedSize == requestedSize) return;
        // At most one new rasterization after the requested size has settled for 200ms.
        if (Stopwatch.GetElapsedTime(requestChangedAt).TotalMilliseconds < 200) return;
        CreatePending(requestedSize, useDefault: fontPath is null);
    }

    private void CreatePending(float size, bool useDefault)
    {
        pending?.Dispose();
        pending = null;
        pendingSize = size;
        pendingUsesDefault = useDefault;
        // Capture immutable request values, never mutable Configuration fields in a build callback.
        var path = fontPath;
        var glyphRanges = glyphs;
        try
        {
            pending = Services.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            {
                var font = useDefault || path is null
                    ? tk.AddDalamudDefaultFont(size, glyphRanges)
                    : tk.AddFontFromFile(path, new SafeFontConfig
                    {
                        SizePx = size,
                        GlyphRanges = glyphRanges,
                        OversampleH = 2,
                        OversampleV = 1,
                        Name = $"ETDisplay HD {size:0}px",
                    });
                // The overlay uses pixel-sized text; do not stretch the baked glyphs with global scale.
                tk.SetFontScaleMode(font, FontScaleMode.UndoGlobalScale);
                tk.Font = font;
            }));
        }
        catch (Exception exception)
        {
            failedSize = size;
            failure = "字体资源创建失败，暂保留已可用字体";
            Services.Log.Warning($"ETDisplay字体句柄创建失败：{exception.Message}");
        }
    }

    /// <summary>The scope locks the font until every measurement and AddText call has finished.</summary>
    public IDisposable? Push() => active?.Push();

    private float Normalize(float size) => float.IsFinite(size) ? MathF.Round(Math.Clamp(size, minimumSize, 96)) : 48;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pending?.Dispose();
        pending = null;
        active?.Dispose();
        active = null;
    }
}
