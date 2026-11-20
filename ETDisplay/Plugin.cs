using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ETDisplay.Core;
using ETDisplay.Speech;
using ETDisplay.Windows;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ETDisplay;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/etdisplay";
    public const string ShortCommandName = "/etd";
    public const string BuildId = "settings-ui-rc2";
    private readonly SsAlertSession ssAlert = new();
    private readonly CountdownSession countdown = new();
    private readonly ChatProbe chatProbe = new();
    private readonly object eventGate = new();
    private readonly EtSpeechPolicy speechPolicy = new();
    private readonly long clockStartedAt = Stopwatch.GetTimestamp();
    private readonly WindowSystem windowSystem = new("ETDisplay");
    private readonly DisplaySession session;
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly SpeechService speech;
    private bool primaryRegistered;
    private bool aliasRegistered;
    private int disposed;

    public Configuration Configuration { get; }
    public DisplaySnapshot Snapshot => session.Snapshot;
    public string FontStatus => mainWindow.FontStatus;
    public string SpeechStatus => speech.Status;
    public SpeechPresentation SpeechPresentation => speech.Presentation;
    public CountdownSnapshot Countdown => countdown.Snapshot;
    public SsAlertSnapshot SsAlert => ssAlert.Snapshot;
    public EorzeaTime CurrentEt => EorzeaClock.At(DateTimeOffset.UtcNow);

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Version < 4) BackupOldConfig(pluginInterface);
        Configuration.Validate();
        session = new DisplaySession(Configuration.Enabled);
        mainWindow = new MainWindow(Configuration, session, countdown, ssAlert);
        configWindow = new ConfigWindow(this);
        speech = new SpeechService(Configuration.SpeechVolume);

        try
        {
            windowSystem.AddWindow(mainWindow);
            windowSystem.AddWindow(configWindow);
            primaryRegistered = Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "设置；test 显示；countdowntest 倒计时；sstest SS提示；tts 语音；clear 清空",
            });
            if (!primaryRegistered)
                throw new InvalidOperationException("/etdisplay 已被占用，请先停用旧版 ETDisplay。");

            aliasRegistered = Services.CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "/etdisplay 的简写：设置、test、countdowntest、sstest、tts、clear、reset",
            });
            if (!aliasRegistered) Services.Log.Warning("/etd 已被占用，仍可使用 /etdisplay。");

            Services.ChatGui.ChatMessage += OnChatMessage;
            Services.Framework.Update += OnFrameworkUpdate;
            pluginInterface.UiBuilder.Draw += DrawUi;
            pluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
            pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static void BackupOldConfig(IDalamudPluginInterface pi)
    {
        try
        {
            var file = pi.ConfigFile;
            var backup = file.FullName + ".pre-phase4.bak";
            if (file.Exists && !File.Exists(backup)) File.Copy(file.FullName, backup);
        }
        catch (Exception exception)
        {
            Services.Log.Warning(exception, "ETDisplay旧配置备份失败，未主动覆盖旧配置；后续设置保存仍请留意备份。");
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        // Not a flags enum. SystemMessage routes SS/sanctuary independently, never sets an ET target.
        var channel = message.LogKind switch
        {
            XivChatType.Say => ChatChannel.Say,
            XivChatType.Yell => ChatChannel.Yell,
            XivChatType.Shout => ChatChannel.Shout,
            XivChatType.Echo => ChatChannel.Echo,
            XivChatType.SystemMessage => ChatChannel.SystemMessage,
            _ => ChatChannel.Other,
        };
        var now = DateTimeOffset.UtcNow;
        var diagnosing = chatProbe.IsActive(now);
        if (channel == ChatChannel.Other && !diagnosing) return;
        var original = message.OriginalMessage.ExtractText();
        var accepted = false;
        var ssAccepted = false;
        lock (eventGate)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            if (Configuration.Enabled && Configuration.AutoClearOnSanctuary && SanctuaryMessage.IsEntry(channel, original))
                ClearCore();
            else
            {
                if (SsAlertMessage.IsScouting(channel, original))
                    ssAccepted = TriggerSsCore(Stopwatch.GetElapsedTime(clockStartedAt));
                accepted = session.Receive(channel, original, out var target);
                if (accepted)
                {
                    var elapsed = Stopwatch.GetElapsedTime(clockStartedAt);
                    if (Configuration.CountdownEnabled && countdown.Start(target, DateTimeOffset.UtcNow, elapsed, Configuration.TimeCalibrationOffset))
                        speech.CancelCountdown(); // A previous target's queued number is no longer valid.
                    if (speechPolicy.TryAnnounce(target, elapsed, Configuration.TtsEnabled)) speech.Say(target);
                }
            }
        }
        if (diagnosing)
            chatProbe.Observe((ushort)message.LogKind, original, message.Message.TextValue, accepted, now,
                ssTextMatched: SsAlertMessage.IsScouting(ChatChannel.SystemMessage, original), ssAccepted: ssAccepted);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        lock (eventGate)
        {
            if (Volatile.Read(ref disposed) != 0 || !Configuration.Enabled) return;
            var elapsed = Stopwatch.GetElapsedTime(clockStartedAt);
            ssAlert.Update(elapsed);
            if (!ssAlert.Snapshot.IsVisible) speech.CancelSsAlert();
            if (!Configuration.CountdownEnabled) return;
            var tick = countdown.Tick(elapsed, Configuration.TtsEnabled && Configuration.CountdownTtsEnabled);
            if (tick.CancelPendingReadout) speech.CancelCountdown();
            else if (tick.Readout.HasValue) speech.Countdown(tick.Readout.Value);
        }
    }

    private void DrawUi()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        mainWindow.UpdateFont();
        windowSystem.Draw();
    }

    private void OpenConfigUi() => configWindow.IsOpen = true;

    public void SetEnabled(bool enabled)
    {
        lock (eventGate)
        {
            Configuration.Enabled = enabled;
            session.SetEnabled(enabled);
            countdown.Clear();
            ssAlert.Clear();
            speechPolicy.Reset();
            speech.Cancel();
        }
        Configuration.Save();
    }

    public void SetTtsEnabled(bool enabled)
    {
        lock (eventGate)
        {
            Configuration.TtsEnabled = enabled;
            speechPolicy.Reset();
            speech.Cancel();
        }
        Configuration.Save();
    }

    public void SetAutoClearOnSanctuary(bool enabled)
    {
        lock (eventGate) Configuration.AutoClearOnSanctuary = enabled;
        Configuration.Save();
    }

    public void SetSpeechVolume(int volume)
    {
        lock (eventGate)
        {
            Configuration.SpeechVolume = Math.Clamp(volume, 0, 100);
            speech.SetVolume(Configuration.SpeechVolume);
        }
        Configuration.Save();
    }

    public void SetCountdownEnabled(bool enabled)
    {
        lock (eventGate)
        {
            Configuration.CountdownEnabled = enabled;
            countdown.Clear();
            speech.CancelCountdown();
            if (enabled && Configuration.Enabled && session.Snapshot.Target is { } target)
                countdown.Start(target, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(clockStartedAt), Configuration.TimeCalibrationOffset);
        }
        Configuration.Save();
    }

    public void SetCountdownTtsEnabled(bool enabled)
    {
        lock (eventGate)
        {
            Configuration.CountdownTtsEnabled = enabled;
            if (!enabled) speech.CancelCountdown();
        }
        Configuration.Save();
    }

    public void SetCalibration(float offset)
    {
        lock (eventGate)
        {
            Configuration.TimeCalibrationOffset = float.IsFinite(offset) ? Math.Clamp(offset, -20, 20) : 0;
            countdown.AdjustCalibration(Configuration.TimeCalibrationOffset, Stopwatch.GetElapsedTime(clockStartedAt));
            speech.CancelCountdown();
        }
        Configuration.Save();
    }

    public void SetSsAlertEnabled(bool enabled)
    {
        lock (eventGate)
        {
            Configuration.SSAlertEnabled = enabled;
            ssAlert.Clear();
            speech.CancelSsAlert();
        }
        Configuration.Save();
    }

    public void SetSsTtsEnabled(bool enabled)
    {
        lock (eventGate)
        {
            Configuration.SSTtsEnabled = enabled;
            if (!enabled) speech.CancelSsAlert();
        }
        Configuration.Save();
    }

    public void SetSsDuration(float seconds)
    {
        lock (eventGate)
            Configuration.SSAlertDuration = float.IsFinite(seconds) ? Math.Clamp(seconds, 1, 30) : 5;
        Configuration.Save(); // Existing alert keeps its original deadline.
    }

    private bool TriggerSsCore(TimeSpan elapsed)
    {
        if (!Configuration.Enabled || !Configuration.SSAlertEnabled ||
            !ssAlert.TryTrigger(elapsed, TimeSpan.FromSeconds(Configuration.SSAlertDuration))) return false;
        if (Configuration.TtsEnabled && Configuration.SSTtsEnabled) speech.SsAlert();
        return true;
    }

    public bool ShowSsTest()
    {
        // Same cooldown and priority as a system event, without sending or simulating game chat.
        lock (eventGate) return TriggerSsCore(Stopwatch.GetElapsedTime(clockStartedAt));
    }

    private void ClearCore()
    {
        session.Clear();
        countdown.Clear();
        ssAlert.Clear();
        speechPolicy.Reset();
        speech.Cancel();
    }

    public void Clear()
    {
        lock (eventGate) ClearCore();
    }

    public void ResetPosition() => mainWindow.RequestPositionReset();

    public bool ShowTest(string text = "ET 15.30")
    {
        // Display-only test never leaves a previous target's timer/audio attached to its new text.
        lock (eventGate)
        {
            if (!session.Receive(ChatChannel.Shout, text)) return false;
            countdown.Clear();
            speechPolicy.Reset();
            speech.CancelBelowSs();
            return true;
        }
    }

    public bool ShowCountdownTest()
    {
        lock (eventGate)
        {
            if (!Configuration.Enabled || !Configuration.CountdownEnabled) return false;
            var now = DateTimeOffset.UtcNow;
            // Quantized to a real ET minute: same production path, about 15-18 real seconds ahead.
            var target = EorzeaClock.At(now.AddSeconds(18));
            session.Receive(ChatChannel.Echo, $"ET {target}");
            countdown.Clear();
            countdown.Start(target, now, Stopwatch.GetElapsedTime(clockStartedAt), Configuration.TimeCalibrationOffset);
            speechPolicy.Reset();
            speech.CancelBelowSs();
            return true;
        }
    }

    public bool TestSpeech()
    {
        lock (eventGate)
            return Configuration.Enabled && Configuration.TtsEnabled && speech.Test();
    }

    public bool RetrySpeech()
    {
        lock (eventGate)
        {
            speechPolicy.Reset();
            return speech.Retry();
        }
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        var separator = trimmed.IndexOf(' ');
        var operation = (separator >= 0 ? trimmed[..separator] : trimmed).ToLowerInvariant();
        var payload = separator >= 0 ? trimmed[(separator + 1)..].Trim() : string.Empty;
        switch (operation)
        {
            case "":
            case "settings":
            case "config": configWindow.Toggle(); break;
            case "test":
                if (!ShowTest(string.IsNullOrEmpty(payload) ? "ET 15.30" : payload))
                    Services.ChatGui.Print("[ETDisplay] 测试未更新：请启用插件并输入合法ET报时。");
                break;
            case "sstest":
                if (!ShowSsTest()) Services.ChatGui.Print("[ETDisplay] SS提示未触发：请启用插件和SS提示，或等待10秒冷却。");
                break;
            case "countdowntest":
                if (!ShowCountdownTest()) Services.ChatGui.Print("[ETDisplay] 请先启用插件和倒计时。");
                break;
            case "tts":
                if (!TestSpeech()) Services.ChatGui.Print("[ETDisplay] 语音测试未提交：请检查总开关、SS高优先级播报或语音后端状态。");
                break;
            case "speechretry": RetrySpeech(); break;
            case "diag": StartDiagnostics(); break;
            case "status": PrintDiagnostics(); break;
            case "clear": Clear(); break;
            case "reset": ResetPosition(); break;
            default:
                Services.ChatGui.Print("[ETDisplay] /etd 设置；test 显示测试；countdowntest 倒计时测试；sstest SS提示；tts 语音测试；speechretry 重试语音；clear 清空；reset 重置位置。");
                break;
        }
    }

    public void StartDiagnostics()
    {
        chatProbe.Arm(DateTimeOffset.UtcNow);
        Services.ChatGui.Print($"[ETDisplay诊断] {BuildId} 已开启60秒。输入 /echo ET 12:34 ETD_ECHO_PROBE，再输入 /etd status。也可观测SS系统事件后输入status；只记录内存元数据，不保存正文。");
    }

    public void PrintDiagnostics()
    {
        chatProbe.Stop();
        var samples = chatProbe.Snapshot();
        Services.ChatGui.Print($"[ETDisplay诊断] Build={BuildId}; Enabled={session.Snapshot.Enabled}; SsEnabled={Configuration.SSAlertEnabled}; SsVisible={ssAlert.Snapshot.IsVisible}; Samples={samples.Length}。诊断已停止。");
        foreach (var sample in samples)
            Services.ChatGui.Print($"[ETDisplay诊断] Type={(XivChatType)sample.LogKind}({sample.LogKind}); Len={sample.OriginalLength}/{sample.CurrentLength}; Probe={sample.ProbeMarkerFound}; Parse={sample.OriginalParsed}/{sample.CurrentParsed}; Accepted={sample.Accepted}; SsText={sample.SsTextMatched}; SsAccepted={sample.SsAccepted}");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Services.ChatGui.ChatMessage -= OnChatMessage;
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.PluginInterface.UiBuilder.Draw -= DrawUi;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        if (aliasRegistered) Services.CommandManager.RemoveHandler(ShortCommandName);
        if (primaryRegistered) Services.CommandManager.RemoveHandler(CommandName);
        chatProbe.Stop();
        lock (eventGate)
        {
            session.SetEnabled(false);
            countdown.Clear();
            ssAlert.Clear();
            speechPolicy.Reset();
            speech.Cancel();
        }
        speech.Dispose();
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        configWindow.Dispose();
    }
}
