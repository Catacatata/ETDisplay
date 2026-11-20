using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ETDisplay.Core;
using ETDisplay.Rendering;
using System;
using System.Diagnostics;
using System.Numerics;

namespace ETDisplay.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private enum Page { General, Voice, Appearance, Tests, Advanced }
    private readonly Plugin plugin;
    private readonly OverlayPreview preview;
    private Page page = Page.General;
    private string testInput = "ET 15.30";
    private string testResult = string.Empty;
    private string speechResult = string.Empty;
    private string debugResult = string.Empty;
    private long speechFeedbackAt;
    private bool testSucceeded;

    public ConfigWindow(Plugin plugin) : base("ETDisplay 设置###ETDisplayConfigV2")
    {
        this.plugin = plugin;
        preview = new OverlayPreview(plugin.Configuration);
        Size = new Vector2(780, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(650, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
    }

    public override void OnOpen() => page = Page.General;

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(S(12), S(10)));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(S(8), S(5)));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, S(4));
        try
        {
            var origin = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail();
            var navigationWidth = S(132);
            var navVisible = ImGui.BeginChild("Navigation", new Vector2(navigationWidth, 0), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            try
            {
                if (navVisible)
                {
                    ImGui.TextUnformatted("ETDisplay");
                    ImGui.TextDisabled("设置");
                    ImGui.Dummy(new Vector2(0, S(14)));
                    Navigation("常规", Page.General);
                    Navigation("语音", Page.Voice);
                    Navigation("外观", Page.Appearance);
                    Navigation("测试", Page.Tests);
                    Navigation("高级", Page.Advanced);
                    ImGui.Dummy(new Vector2(0, S(16)));
                    ImGui.TextDisabled("RC2 · API 15");
                }
            }
            finally { ImGui.EndChild(); }
            var lineX = origin.X + navigationWidth + S(10);
            ImGui.GetWindowDrawList().AddLine(new Vector2(lineX, origin.Y), new Vector2(lineX, origin.Y + available.Y), ImGui.GetColorU32(ImGuiCol.Separator));
            ImGui.SameLine(0, S(22));
            var contentVisible = ImGui.BeginChild("Content-" + page, Vector2.Zero, false);
            try
            {
                if (contentVisible)
                {
                    switch (page)
                    {
                        case Page.General: DrawGeneral(); break;
                        case Page.Voice: DrawVoice(); break;
                        case Page.Appearance: DrawAppearance(); break;
                        case Page.Tests: DrawTests(); break;
                        case Page.Advanced: DrawAdvanced(); break;
                    }
                }
            }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.PopStyleVar(3); }
    }

    private void Navigation(string label, Page target)
    {
        if (ImGui.Selectable(label, page == target, ImGuiSelectableFlags.None, new Vector2(0, S(36)))) page = target;
    }

    private void DrawGeneral()
    {
        Header("常规", "主要功能与提醒开关");
        var config = plugin.Configuration;
        var enabled = config.Enabled;
        if (ToggleRow("启用 ETDisplay", ref enabled, "关闭后停止识别，并清空ET、倒计时、SS与语音。")) plugin.SetEnabled(enabled);
        ImGui.Separator();
        ImGui.BeginDisabled(!config.Enabled);
        try
        {
            Section("主要功能", first: true);
            var countdown = config.CountdownEnabled;
            if (ToggleRow("显示 ET 倒计时", ref countdown, "关闭倒计时不清除ET文字。")) plugin.SetCountdownEnabled(countdown);
            var ss = config.SSAlertEnabled;
            if (ToggleRow("启用 SS 侦察提示", ref ss, "识别系统侦察提示；玩家或Echo复制不触发。")) plugin.SetSsAlertEnabled(ss);
            var tts = config.TtsEnabled;
            if (ToggleRow("启用语音提醒", ref tts, "ET报时、末10秒读秒与SS的语音总开关。")) plugin.SetTtsEnabled(tts);
            Section("自动清理");
            var sanctuary = config.AutoClearOnSanctuary;
            if (ToggleRow("进入休息区时清空提醒", ref sanctuary, "仅匹配系统休息区提示，清空文字、计时和语音。")) plugin.SetAutoClearOnSanctuary(sanctuary);
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawVoice()
    {
        Header("语音", "Windows 系统语音");
        var config = plugin.Configuration;
        var presentation = plugin.SpeechPresentation;
        ImGui.TextUnformatted("语音状态");
        ImGui.SameLine(S(112));
        var statusColor = presentation.Readiness switch
        {
            SpeechReadiness.Ready => new Vector4(0.4f, 0.82f, 0.6f, 1),
            SpeechReadiness.Unavailable => new Vector4(0.9f, 0.57f, 0.4f, 1),
            _ => new Vector4(0.65f, 0.68f, 0.72f, 1),
        };
        ImGui.TextColored(statusColor, presentation.StatusText);
        KeyValue("当前声音", presentation.DisplayVoice);
        Section("音量");
        var volume = config.SpeechVolume;
        RowLabel("语音音量");
        if (ImGui.SliderInt("##Volume", ref volume, 0, 100, "%d%%")) plugin.SetSpeechVolume(volume);
        Tip("仅调整本插件；0为静音，不改变游戏或系统主音量。");
        Section("播报内容");
        var readout = config.CountdownTtsEnabled;
        if (ToggleRow("末10秒读秒", ref readout, "需同时开启倒计时和语音总开关。")) plugin.SetCountdownTtsEnabled(readout);
        var ssTts = config.SSTtsEnabled;
        if (ToggleRow("SS语音", ref ssTts, "需同时开启SS提示和语音总开关。")) plugin.SetSsTtsEnabled(ssTts);
        ImGui.Dummy(new Vector2(0, S(8)));
        ImGui.BeginDisabled(!config.Enabled || !config.TtsEnabled);
        try
        {
            if (ImGui.Button("试听", new Vector2(S(100), 0)))
            {
                speechResult = plugin.TestSpeech() ? "已提交试听" : "未播放";
                speechFeedbackAt = Stopwatch.GetTimestamp();
            }
            Tip("使用当前音量试听；SS播报期间普通试听不会打断它。");
        }
        finally { ImGui.EndDisabled(); }
        if (!config.Enabled || !config.TtsEnabled) ImGui.TextDisabled(!config.Enabled ? "插件已关闭" : "语音总开关已关闭");
        else if (presentation.Readiness != SpeechReadiness.Unavailable && speechResult.Length > 0 && Stopwatch.GetElapsedTime(speechFeedbackAt).TotalSeconds < 4)
            ImGui.TextDisabled(speechResult);
    }

    private void DrawAppearance()
    {
        Header("外观", "文字样式与窗口位置");
        var config = plugin.Configuration;
        Section("实时预览", first: true);
        preview.Draw(config);
        Tip("静态三行示例，不改变真实提醒，不发送聊天或播报。");
        Section("文字样式");
        var size = config.FontSize;
        RowLabel("字号");
        if (ImGui.SliderFloat("##FontSize", ref size, 20, 96, "%.0f")) { config.FontSize = size; config.Save(); }
        Tip("按实际字号生成高清字体；调整稳定后约200ms更新。");
        var textColor = config.TextColor;
        if (ColorRow("ET文字颜色", ref textColor)) { config.TextColor = textColor; config.Save(); }
        var ssColor = config.SSAlertColor;
        if (ColorRow("SS文字颜色", ref ssColor)) { config.SSAlertColor = ssColor; config.Save(); }
        var thickness = config.OutlineThickness;
        RowLabel("描边粗细");
        if (ImGui.SliderFloat("##Outline", ref thickness, 0, 4, "%.1f")) { config.OutlineThickness = thickness; config.Save(); }
        Section("位置与操作");
        var locked = config.IsLocked;
        if (ToggleRow("锁定显示窗口", ref locked, "锁定后鼠标穿透；解锁后拖动实际浮层的标题栏。")) { config.IsLocked = locked; config.Save(); }
        if (ImGui.Button("重置位置")) plugin.ResetPosition();
        Tip("作用于实际提醒浮层，下次显示时回到主视口左上附近；不改变预览。");
    }

    private void DrawTests()
    {
        Header("测试", "测试不会向游戏发送聊天");
        var config = plugin.Configuration;
        Section("提醒测试", first: true);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##TestInput", ref testInput, 256);
        Tip("ET显示测试输入；例如ET 15.30。只用于手动测试，不修改真实聊天。");
        ImGui.BeginDisabled(!config.Enabled);
        try
        {
            if (ImGui.Button("ET显示测试")) SetTestResult(plugin.ShowTest(testInput), "已更新ET显示", "未找到合法ET时间");
            Tip("仅显示文字；不启动倒计时或语音，也不取消高优先级SS。");
            ImGui.BeginDisabled(!config.CountdownEnabled);
            try
            {
                if (ImGui.Button("倒计时测试")) SetTestResult(plugin.ShowCountdownTest(), "已设置约15–18秒后的ET目标", "倒计时未启用");
                Tip("走真实ET换算路径，遵守校准、音量与读秒开关。");
            }
            finally { ImGui.EndDisabled(); }
            ImGui.BeginDisabled(!config.SSAlertEnabled);
            try
            {
                if (ImGui.Button("SS提示测试")) SetTestResult(plugin.ShowSsTest(), "已触发SS提示", "未触发，可能处于10秒冷却中");
                Tip("遵守10秒冷却与语音开关；不模拟真实系统消息。");
            }
            finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
        Section("操作");
        if (ImGui.Button("清空当前提醒")) { plugin.Clear(); SetTestResult(true, "已清空全部提醒与语音", string.Empty); }
        if (testResult.Length > 0)
        {
            Section("上次测试结果");
            ImGui.TextColored(testSucceeded ? new Vector4(0.4f, 0.82f, 0.6f, 1) : new Vector4(0.9f, 0.65f, 0.4f, 1), testResult);
        }
        Section("当前提醒");
        KeyValue("ET", plugin.Snapshot.IsVisible ? plugin.Snapshot.Text : "无");
        KeyValue("倒计时", plugin.Countdown.IsVisible ? plugin.Countdown.Text : "无");
        KeyValue("SS", plugin.SsAlert.IsVisible ? "显示中" : "无");
    }

    private void DrawAdvanced()
    {
        Header("高级", "计时参数与排错工具");
        var config = plugin.Configuration;
        Section("计时", first: true);
        KeyValue("当前ET", plugin.CurrentEt.ToString());
        var calibration = config.TimeCalibrationOffset;
        RowLabel("校准偏移");
        if (ImGui.SliderFloat("##Calibration", ref calibration, -20, 20, "%.1f 秒")) plugin.SetCalibration(calibration);
        Tip("真实秒：负数提前、正数延后。调整未到期截止时间，已到期不会复活。");
        Section("SS提示");
        var duration = config.SSAlertDuration;
        RowLabel("显示时长");
        if (ImGui.SliderFloat("##SsDuration", ref duration, 1, 30, "%.1f 秒")) plugin.SetSsDuration(duration);
        Tip("仅影响后续触发，不延长当前提示；10秒固定冷却不变。");
        ImGui.Dummy(new Vector2(0, S(18)));
        if (ImGui.CollapsingHeader("排错工具"))
        {
            ImGui.TextDisabled($"Build: {Plugin.BuildId}");
            ImGui.TextWrapped("语音后端：" + plugin.SpeechStatus);
            ImGui.TextWrapped("字体：" + plugin.FontStatus);
            if (ImGui.Button("重试语音后端")) debugResult = plugin.RetrySpeech() ? "已重置，请到语音页试听" : "后端已停止，请重载插件";
            Tip("显式重置整个语音后端，可能停止正在播报的SS。不会自动播放声音。");
            if (ImGui.Button("开启60秒诊断")) { plugin.StartDiagnostics(); debugResult = "诊断已开启，仅记录有限内存元数据"; }
            if (ImGui.Button("输出诊断结果")) { plugin.PrintDiagnostics(); debugResult = "结果已输出到本地聊天，诊断已停止"; }
            Tip("不保存正文或人名；最多16条元数据，不向其他玩家发送消息。");
            if (debugResult.Length > 0) ImGui.TextWrapped(debugResult);
        }
    }

    private void SetTestResult(bool succeeded, string success, string failure)
    {
        testSucceeded = succeeded;
        testResult = succeeded ? success : failure;
    }

    private static void Header(string title, string subtitle)
    {
        ImGui.TextUnformatted(title);
        ImGui.TextDisabled(subtitle);
        ImGui.Dummy(new Vector2(0, S(8)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, S(6)));
    }

    private static void Section(string title, bool first = false)
    {
        ImGui.Dummy(new Vector2(0, S(first ? 4 : 14)));
        ImGui.TextDisabled(title);
        ImGui.Dummy(new Vector2(0, S(2)));
    }

    private static void RowLabel(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(S(112));
        ImGui.SetNextItemWidth(-1);
    }

    private static void KeyValue(string label, string value)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine(S(112));
        ImGui.PushTextWrapPos(0);
        try { ImGui.TextUnformatted(value); }
        finally { ImGui.PopTextWrapPos(); }
    }

    private static bool ColorRow(string label, ref uint value)
    {
        RowLabel(label);
        var color = ImGui.ColorConvertU32ToFloat4(value);
        var changed = ImGui.ColorEdit4("##" + label, ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar);
        if (changed) value = ImGui.ColorConvertFloat4ToU32(color);
        ImGui.SameLine();
        ImGui.TextDisabled($"#{value & 0xff:X2}{(value >> 8) & 0xff:X2}{(value >> 16) & 0xff:X2}");
        return changed;
    }

    private static bool ToggleRow(string label, ref bool value, string tooltip)
    {
        ImGui.PushID(label);
        try
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(label);
            Tip(tooltip);
            ImGui.SameLine();
            var width = S(38);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - width));
            var start = ImGui.GetCursorScreenPos();
            var height = ImGui.GetFrameHeight();
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
            bool changed;
            try { changed = ImGui.Button("##Toggle", new Vector2(width, height)); }
            finally { ImGui.PopStyleColor(3); }
            if (changed) value = !value;
            var trackHeight = S(20);
            var min = start + new Vector2(0, (height - trackHeight) / 2);
            var max = min + new Vector2(width, trackHeight);
            var draw = ImGui.GetWindowDrawList();
            var track = ImGui.GetColorU32(value ? ImGuiCol.CheckMark : ImGui.IsItemHovered() ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
            draw.AddRectFilled(min, max, track, trackHeight / 2);
            var radius = trackHeight / 2 - S(3);
            var center = new Vector2(value ? max.X - trackHeight / 2 : min.X + trackHeight / 2, min.Y + trackHeight / 2);
            draw.AddCircleFilled(center, radius, ImGui.GetColorU32(ImGuiCol.Text));
            Tip(tooltip);
            return changed;
        }
        finally { ImGui.PopID(); }
    }

    private static void Tip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.BeginTooltip();
        try { ImGui.PushTextWrapPos(S(340)); try { ImGui.TextUnformatted(text); } finally { ImGui.PopTextWrapPos(); } }
        finally { ImGui.EndTooltip(); }
    }

    private static float S(float value) => value * ImGuiHelpers.GlobalScale;
    public void Dispose() => preview.Dispose();
}
