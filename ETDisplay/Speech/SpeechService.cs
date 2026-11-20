using ETDisplay.Core;
using System;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;

namespace ETDisplay.Speech;

/// <summary>Single-owner backend: SS > ET/test > countdown. Lower priority is dropped, never backfilled.</summary>
public sealed class SpeechService : IDisposable
{
    private sealed record Request(SpeechKind Kind, EorzeaTime? Target = null, int Seconds = 0);
    private readonly object gate = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread? worker;
    private Request? pending;
    private Request? processing;
    private Prompt? activePrompt;
    private SpeechKind? activeKind;
    private Exception? pendingError;
    private SpeechSynthesizer? ownedEngine;
    private int volume;
    private bool cancelRequested;
    private bool resetRequested;
    private bool stopping;
    private bool unavailable;
    private long generation;
    private int disposed;
    private string status = "待命（首次播报时初始化）";
    private string? voiceName;
    private bool initializing;
    private bool presentationResetPending;
    public string Status => Volatile.Read(ref status);
    public SpeechPresentation Presentation
    {
        get
        {
            lock (gate)
            {
                var readiness = stopping || Volatile.Read(ref unavailable) ? SpeechReadiness.Unavailable
                    : presentationResetPending ? SpeechReadiness.Idle
                    : initializing ? SpeechReadiness.Initializing
                    : voiceName is not null ? SpeechReadiness.Ready : SpeechReadiness.Idle;
                return new(readiness, presentationResetPending ? null : voiceName);
            }
        }
    }

    public SpeechService(int volume = 100)
    {
        this.volume = Math.Clamp(volume, 0, 100);
        try
        {
            worker = new Thread(Run) { IsBackground = true, Name = "ETDisplay Speech" };
            worker.Start();
        }
        catch (Exception exception)
        {
            stopping = true;
            unavailable = true;
            status = "不可用：无法启动语音线程";
            signal.Dispose();
            Services.Log.Warning(exception, "ETDisplay语音线程启动失败，聊天和显示仍然可用。");
        }
    }

    public bool Say(EorzeaTime target) => Queue(new Request(SpeechKind.EtTime, target));
    public bool Test() => Queue(new Request(SpeechKind.Test));
    public bool SsAlert() => Queue(new Request(SpeechKind.SsAlert));
    public bool Countdown(int seconds) => seconds is >= 1 and <= 10 && Queue(new Request(SpeechKind.Countdown, Seconds: seconds));

    public void SetVolume(int value)
    {
        lock (gate)
        {
            if (stopping) return;
            volume = Math.Clamp(value, 0, 100);
            signal.Set(); // No cancellation/generation change: adjusting volume never restarts speech.
        }
    }

    private bool Queue(Request request)
    {
        lock (gate)
        {
            if (stopping || Volatile.Read(ref unavailable)) return false;
            if (!SpeechPriority.CanQueue(request.Kind, pending?.Kind, processing?.Kind, activeKind)) return false;
            pending = request;
            cancelRequested = true;
            generation++;
            signal.Set();
            return true;
        }
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (stopping) return;
            pending = null;
            cancelRequested = true;
            generation++;
            signal.Set();
        }
    }

    public void CancelCountdown()
    {
        lock (gate)
        {
            if (stopping) return;
            var pendingKind = pending?.Kind;
            if (pendingKind == SpeechKind.Countdown) pending = null;
            // A newer announcement is already responsible for replacing the old number.
            if (!SpeechPriority.CanCancelCountdown(pendingKind, processing?.Kind, activeKind)) return;
            generation++;
            cancelRequested = true;
            signal.Set();
        }
    }

    public void CancelSsAlert()
    {
        lock (gate)
        {
            if (stopping || !SpeechPriority.CanCancelSsAlert(pending?.Kind, processing?.Kind, activeKind)) return;
            if (pending?.Kind == SpeechKind.SsAlert) pending = null;
            generation++;
            cancelRequested = true;
            signal.Set();
        }
    }

    public void CancelBelowSs()
    {
        lock (gate)
        {
            if (stopping || !SpeechPriority.CanCancelBelowSs(pending?.Kind, processing?.Kind, activeKind)) return;
            pending = null;
            generation++;
            cancelRequested = true;
            signal.Set();
        }
    }

    public bool Retry()
    {
        lock (gate)
        {
            if (stopping) return false;
            pending = null;
            pendingError = null;
            resetRequested = true;
            presentationResetPending = true;
            cancelRequested = true;
            generation++;
            Volatile.Write(ref unavailable, false);
            signal.Set();
            return true;
        }
    }

    private void Run()
    {
        SpeechSynthesizer? synthesizer = null;
        var chinese = false;
        var appliedVolume = -1;
        try
        {
            while (true)
            {
                signal.WaitOne();
                Request? request;
                bool cancel;
                bool reset;
                Exception? playbackError;
                long requestGeneration;
                lock (gate)
                {
                    if (stopping) break;
                    request = pending;
                    pending = null;
                    processing = request;
                    cancel = cancelRequested;
                    reset = resetRequested;
                    playbackError = pendingError;
                    pendingError = null;
                    cancelRequested = false;
                    resetRequested = false;
                    requestGeneration = generation;
                }
                try
                {
                    if (reset)
                    {
                        DisposeEngine(ref synthesizer);
                        lock (gate) presentationResetPending = resetRequested; // Keep a later retry pending too.
                        appliedVolume = -1;
                        Volatile.Write(ref unavailable, false);
                        Volatile.Write(ref status, "待命（下次播报重新初始化）");
                    }
                    if (playbackError is not null && !reset) throw new InvalidOperationException("语音播放失败", playbackError);
                    if (cancel) synthesizer?.SpeakAsyncCancelAll();
                    int desiredVolume;
                    lock (gate) desiredVolume = volume;
                    if (synthesizer is not null && appliedVolume != desiredVolume)
                    {
                        synthesizer.Volume = desiredVolume;
                        appliedVolume = desiredVolume;
                    }
                    if (request is null || Volatile.Read(ref unavailable)) continue;
                    if (synthesizer is null)
                    {
                        lock (gate) initializing = true;
                        Volatile.Write(ref status, "正在初始化语音…");
                        synthesizer = CreateEngine(out chinese);
                        lock (gate) desiredVolume = volume;
                        synthesizer.Volume = desiredVolume;
                        appliedVolume = desiredVolume;
                        var currentVoice = synthesizer.Voice;
                        lock (gate) { voiceName = currentVoice.Name; initializing = false; }
                        Volatile.Write(ref status, $"就绪：{currentVoice.Name}（{currentVoice.Culture.Name}）");
                    }
                    var text = request.Kind switch
                    {
                        SpeechKind.EtTime => SpeechText.ForTime(request.Target!.Value, chinese),
                        SpeechKind.Countdown => SpeechText.ForCountdown(request.Seconds),
                        SpeechKind.SsAlert => SpeechText.ForSsAlert(chinese),
                        _ => SpeechText.Test(chinese),
                    };
                    var engine = synthesizer;
                    var prompt = new Prompt(text);
                    SpeechSubmission.Run(
                        () => { lock (gate) return !stopping && requestGeneration == generation; },
                        () =>
                        {
                            // Reserve identity BEFORE submission so completion cannot race its assignment.
                            lock (gate) { activePrompt = prompt; activeKind = request.Kind; }
                            engine.SpeakAsync(prompt);
                        },
                        () => engine.SpeakAsyncCancelAll());
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref unavailable, true);
                    Volatile.Write(ref status, $"不可用：{exception.Message}");
                    Services.Log.Warning(exception, "ETDisplay语音不可用，已降级为仅显示；可在设置中重试。");
                    DisposeEngine(ref synthesizer);
                    appliedVolume = -1;
                }
                finally
                {
                    lock (gate) processing = null;
                }
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref unavailable, true);
            Volatile.Write(ref status, "不可用：语音线程已退出，请重载插件");
            Services.Log.Warning(exception, "ETDisplay语音线程异常退出，聊天和显示仍然可用。");
        }
        finally
        {
            lock (gate) { stopping = true; pending = null; processing = null; }
            DisposeEngine(ref synthesizer);
            signal.Dispose();
        }
    }

    private SpeechSynthesizer CreateEngine(out bool chinese)
    {
        var engine = new SpeechSynthesizer();
        try
        {
            var voices = engine.GetInstalledVoices().Where(voice => voice.Enabled).Select(voice => voice.VoiceInfo).ToArray();
            if (voices.Length == 0) throw new InvalidOperationException("没有可用的Windows语音包");
            var selected = voices.FirstOrDefault(voice => voice.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                           ?? voices.FirstOrDefault(voice => voice.Culture.TwoLetterISOLanguageName == "zh")
                           ?? voices.FirstOrDefault(voice => voice.Name == engine.Voice.Name) ?? voices[0];
            engine.SelectVoice(selected.Name);
            engine.SetOutputToDefaultAudioDevice();
            chinese = selected.Culture.TwoLetterISOLanguageName == "zh";
            engine.SpeakCompleted += OnSpeakCompleted;
            Volatile.Write(ref ownedEngine, engine);
            return engine;
        }
        catch
        {
            try { engine.Dispose(); } catch { }
            throw;
        }
    }

    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs args)
    {
        lock (gate)
        {
            if (stopping || !ReferenceEquals(sender, Volatile.Read(ref ownedEngine))) return;
            // Late completion of a cancelled older prompt must not unlock a newer announcement.
            if (ReferenceEquals(args.Prompt, activePrompt)) { activePrompt = null; activeKind = null; }
            if (!SpeechCompletionPolicy.IsBackendFailure(args.Cancelled, args.Error)) return;
            pendingError = args.Error;
            pending = null;
            generation++;
            signal.Set();
        }
    }

    private void DisposeEngine(ref SpeechSynthesizer? engine)
    {
        var old = engine;
        engine = null;
        Volatile.Write(ref ownedEngine, null);
        lock (gate) { activePrompt = null; activeKind = null; voiceName = null; initializing = false; }
        if (old is null) return;
        old.SpeakCompleted -= OnSpeakCompleted;
        try { old.SpeakAsyncCancelAll(); } catch { }
        try { old.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lock (gate)
        {
            if (!stopping) { stopping = true; pending = null; generation++; signal.Set(); }
        }
        if (worker is { IsAlive: true } && Thread.CurrentThread != worker && !worker.Join(1500))
            Services.Log.Warning("ETDisplay语音后端清理耗时，后台线程将在设备调用返回后释放资源。");
    }
}
