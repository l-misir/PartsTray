using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LogiGap.Core;
using N = LogiGap.GestureNative;

namespace LogiGap;

internal sealed class GestureModule : IFeatureModule, IGestureEffects
{
    private sealed class Clock : IClock { public TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64); }
    private readonly Clock clock = new();
    private readonly ILog log;
    private readonly Action<Action> ui;
    private readonly Action<string> notify;
    private readonly WindowsGestureServices services;
    private readonly HiddenWindowManager hidden;
    private readonly GestureOverlay overlay;
    private readonly GestureEngine engine;
    private readonly INativeWheelBridge? wheelBridge;
    private readonly WheelTraceWriter wheelTrace;
    private readonly ConcurrentQueue<GestureEffect> actions = new(), decisions = new();
    private readonly ConcurrentQueue<(GestureEffect Effect, int Epoch)> commands = new();
    private readonly ConcurrentQueue<Action> replies = new();
    private readonly AutoResetEvent actionWake = new(false), decisionWake = new(false), commandWake = new(false);
    private readonly Thread actionThread, decisionThread, hookThread, commandThread;
    private readonly ManualResetEventSlim hookReady = new();
    private readonly N.HookProc callback;
    private readonly RawInputWindow raw;
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 5000 };
    private AppSettings settings;
    private uint hookThreadId;
    private nint hook;
    private long lastHook, lastRaw, lastReinstall, calls, totalTicks, maxTicks;
    private int stopping, workerStop, hookExit, userPaused, kazaguru, workerHealthy = 1;
    private int epoch;
    private bool notified;
    private long actionHeartbeat = Environment.TickCount64;
    private bool Enabled => Volatile.Read(ref stopping) == 0 && Volatile.Read(ref userPaused) == 0 && Volatile.Read(ref kazaguru) == 0 && Volatile.Read(ref workerHealthy) != 0;

    public GestureModule(AppSettings settings, ILog log, Action<Action> ui, Action<string> notify, INativeWheelBridge? wheelBridge = null)
    {
        this.settings = settings; this.log = log; this.ui = ui; this.notify = notify;
        this.wheelBridge = wheelBridge;
        wheelTrace = new(log);
        wheelTrace.SetEnabled(settings.Diagnostics.WheelTrace);
        services = new(log); hidden = new(log); overlay = new(settings.Gesture.Trail, log);
        engine = new(clock, this, settings); callback = Hook;
        CheckKazaguru(); engine.SetPaused(!Enabled);
        actionThread = new Thread(Actions) { IsBackground = true, Name = "LogiGap gesture input" };
        decisionThread = new Thread(Decisions) { IsBackground = true, Name = "LogiGap hit tests" };
        commandThread = new Thread(Commands) { IsBackground = true, Name = "LogiGap window commands" };
        hookThread = new Thread(HookLoop) { IsBackground = true, Name = "LogiGap mouse hook", Priority = ThreadPriority.Highest };
        actionThread.Start(); decisionThread.Start(); commandThread.Start(); hookThread.Start(); hookReady.Wait();
        raw = new RawInputWindow(() => Volatile.Write(ref lastRaw, Environment.TickCount64), log);
        monitor.Tick += (_, _) => { CheckKazaguru(); hidden.Prune(); }; monitor.Start();
    }
    public void Post(GestureEffect e)
    {
        if (e.Kind is GestureEffectKind.TrailBegin or GestureEffectKind.TrailPoint or GestureEffectKind.TrailShow or GestureEffectKind.TrailEnd or GestureEffectKind.Tooltip)
        { overlay.Post(e); return; }
        if (e.Kind == GestureEffectKind.Focus)
        {
            int startedEpoch = Volatile.Read(ref epoch);
            services.RequestFocus(e.Input.Position, () => Enabled && startedEpoch == Volatile.Read(ref epoch));
            return;
        }
        if (e.Kind is GestureEffectKind.HitTest or GestureEffectKind.Probe)
        { decisions.Enqueue(e); decisionWake.Set(); }
        else if (e.Kind is GestureEffectKind.Command or GestureEffectKind.MoveWindow)
        { commands.Enqueue((e, Volatile.Read(ref epoch))); commandWake.Set(); }
        else { actions.Enqueue(e); actionWake.Set(); }
    }
    private void Reply(Action action)
    {
        replies.Enqueue(action);
        if (hookThreadId != 0) N.PostThreadMessageW(hookThreadId, 0x8001, 0, 0);
    }
    private void HookLoop()
    {
        nuint timer = 0;
        try
        {
            hookThreadId = N.GetCurrentThreadId();
            N.PeekMessageW(out _, 0, 0, 0, 0); // create queue before another thread can post
            Install(); timer = N.SetTimer(0, 0, 20, 0);
            if (timer == 0) throw new InvalidOperationException("フック安全タイマーを作成できません");
            hookReady.Set();
            while (N.GetMessageW(out var message, 0, 0, 0) > 0)
            {
                while (replies.TryDequeue(out var reply))
                {
                    try { reply(); } catch (Exception ex) { Post(new(GestureEffectKind.Warning, Text: $"フック応答処理: {ex.Message}")); }
                }
                if (Volatile.Read(ref hookExit) != 0) break;
                if (message.Id == 0x113)
                {
                    long now = Environment.TickCount64;
                    if (now - Volatile.Read(ref lastRaw) < 500 && now - lastHook >= 2000 && now - lastReinstall >= 2000)
                    {
                        engine.HookLost(); Interlocked.Increment(ref epoch); Install();
                        Post(new(GestureEffectKind.Warning, Text: "Raw Input 生存監視: フックを付け直した"));
                    }
                    engine.Tick();
                    bool healthy = now - Volatile.Read(ref actionHeartbeat) < 2000;
                    if ((Volatile.Read(ref workerHealthy) != 0) != healthy)
                    {
                        Volatile.Write(ref workerHealthy, healthy ? 1 : 0); engine.SetPaused(!Enabled);
                        Interlocked.Increment(ref epoch);
                        Post(new(GestureEffectKind.Warning, Text: healthy ? "入力ワーカー復旧" : "入力ワーカー応答なし: 新しい入力の横取りを停止"));
                    }
                }
                N.TranslateMessage(ref message); N.DispatchMessageW(ref message);
            }
        }
        catch (Exception ex) { Post(new(GestureEffectKind.Warning, Text: $"マウスフック停止: {ex}")); }
        finally
        {
            engine.SetPaused(true);
            if (hook != 0) N.UnhookWindowsHookEx(hook);
            if (timer != 0) N.KillTimer(0, timer);
            hookReady.Set();
            log.Write($"フック処理時間 count={calls} average={(calls == 0 ? 0 : totalTicks * 1000.0 / Stopwatch.Frequency / calls):F4}ms max={maxTicks * 1000.0 / Stopwatch.Frequency:F4}ms");
        }
    }
    private void Install()
    {
        if (hook != 0) { N.UnhookWindowsHookEx(hook); hook = 0; }
        hook = N.SetWindowsHookExW(14, callback, N.GetModuleHandleW(null), 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        lastReinstall = lastHook = Environment.TickCount64;
    }
    private unsafe nint Hook(int code, nuint message, nint data)
    {
        if (code < 0) return N.CallNextHookEx(hook, code, message, data);
        long start = Stopwatch.GetTimestamp(); bool suppress = false;
        WheelTraceTaken taken = WheelTraceTaken.None;
        N.MouseHook native = default;
        bool captured = false;
        try
        {
            lastHook = Environment.TickCount64;
            native = *(N.MouseHook*)data;
            captured = true;
            if (native.Extra != KeyStroke.ExtraInfo)
            {
                PointerModifiers mods = 0;
                if ((N.GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= PointerModifiers.Shift;
                if ((N.GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= PointerModifiers.Control;
                var point = new PixelPoint(native.Point.X, native.Point.Y);
                PointerEvent? e = message switch
                {
                    0x200 => new(MouseEventKind.Move, point),
                    0x201 => new(MouseEventKind.Down, point, PointerButton.Left, Modifiers: mods),
                    0x202 => new(MouseEventKind.Up, point, PointerButton.Left),
                    0x204 => new(MouseEventKind.Down, point, PointerButton.Right, Modifiers: mods),
                    0x205 => new(MouseEventKind.Up, point, PointerButton.Right),
                    0x207 => new(MouseEventKind.Down, point, PointerButton.Middle, Modifiers: mods),
                    0x208 => new(MouseEventKind.Up, point, PointerButton.Middle),
                    0x20A => new(MouseEventKind.Wheel, point, Delta: unchecked((short)(native.Data >> 16))),
                    0x20E => new(MouseEventKind.HorizontalWheel, point, Delta: unchecked((short)(native.Data >> 16))),
                    _ => null
                };
                if (e.HasValue) suppress = engine.Handle(e.Value);
                if (suppress) taken = WheelTraceTaken.Gesture;
                // 0x1 = LLMHF_INJECTED, 0x2 = LLMHF_LOWER_IL_INJECTED。ほかのアプリが送った入力には触れない。
                if (!suppress && message == 0x20E && (native.Flags & 0x3) == 0 &&
                    wheelBridge?.TryTakeHorizontal(unchecked((short)(native.Data >> 16))) == true)
                { suppress = true; taken = WheelTraceTaken.Bridge; }
            }
        }
        catch { suppress = false; taken = WheelTraceTaken.None; Post(new(GestureEffectKind.Warning, Text: "フック判定例外: 入力を素通し")); }
        finally
        {
            // 記録の失敗は、すでに決めた入力の抑止・素通しを変えない。
            if (captured && message is 0x20E or 0x20A)
            {
                try { wheelTrace.Enqueue(new(start, unchecked((short)(native.Data >> 16)), native.Flags, native.Extra, taken, Vertical: message == 0x20A)); }
                catch { }
            }
            long duration = Stopwatch.GetTimestamp() - start;
            calls++; totalTicks += duration; maxTicks = Math.Max(maxTicks, duration);
            if (duration > Stopwatch.Frequency / 200) Post(new(GestureEffectKind.Warning, Text: "フック処理が5msを超過", At: TimeSpan.FromSeconds((double)duration / Stopwatch.Frequency)));
        }
        return suppress ? 1 : N.CallNextHookEx(hook, code, message, data);
    }
    private void Decisions()
    {
        while (Volatile.Read(ref workerStop) == 0)
        {
            while (decisions.TryDequeue(out var e))
            {
                try
                {
                    if (!Enabled) { if (e.Kind == GestureEffectKind.HitTest) Reply(() => engine.Resolve(e.Token, default)); continue; }
                    if (e.Kind == GestureEffectKind.HitTest)
                    {
                        if (clock.Now - e.At >= TimeSpan.FromSeconds(2)) continue;
                        var hit = services.HitTest(e.Input, Volatile.Read(ref settings));
                        Reply(() => engine.Resolve(e.Token, Enabled ? hit : default));
                    }
                    else if (e.Kind == GestureEffectKind.Probe)
                    {
                        if (clock.Now - e.At > TimeSpan.FromMilliseconds(100)) continue;
                        var cache = services.Probe(e.Input.Position, e.At);
                        Reply(() => engine.SetCache(cache));
                    }
                }
                catch (Exception ex)
                {
                    log.Write($"ヒットテスト/UIA: {ex}");
                    if (e.Kind == GestureEffectKind.HitTest) Reply(() => engine.Resolve(e.Token, default));
                }
            }
            decisionWake.WaitOne(100);
        }
    }
    private void Actions()
    {
        while (Volatile.Read(ref workerStop) == 0 || !actions.IsEmpty)
        {
            Volatile.Write(ref actionHeartbeat, Environment.TickCount64);
            while (actions.TryDequeue(out var e))
            {
                try
                {
                    if (e.Kind == GestureEffectKind.Replay)
                    {
                        bool ok = services.Replay(e.Input);
                        if (!ok && e.Input.Kind == MouseEventKind.Wheel) Reply(engine.WheelSendFailed);
                        if (e.Input.Kind == MouseEventKind.Down) Reply(() => engine.ReplayCompleted(e.Token));
                    }
                    else if (e.Kind == GestureEffectKind.Flush) Reply(() => Volatile.Write(ref hookExit, 1));
                    else if (e.Kind == GestureEffectKind.Warning) log.Write($"{e.Text} {e.At.TotalMilliseconds:F3}ms");
                }
                catch (Exception ex)
                {
                    log.Write($"ジェスチャー送信/操作: {ex}");
                    if (e.Kind == GestureEffectKind.Replay && e.Input.Kind == MouseEventKind.Wheel) Reply(engine.WheelSendFailed);
                }
                Volatile.Write(ref actionHeartbeat, Environment.TickCount64);
            }
            actionWake.WaitOne(100);
        }
    }
    private void Commands()
    {
        while (Volatile.Read(ref workerStop) == 0)
        {
            while (commands.TryDequeue(out var work))
            {
                try
                {
                    var e = work.Effect;
                    bool Allowed() => Enabled && work.Epoch == Volatile.Read(ref epoch);
                    if (!Allowed()) continue;
                    if (e.Kind == GestureEffectKind.Command && e.Text != null)
                    {
                        if (e.Text == "MinimizeToTray") ui(() => { if (Allowed()) hidden.Hide(e.Window); });
                        else services.Execute(e.Window, e.Text, Volatile.Read(ref settings).Gesture, Allowed);
                    }
                    else if (e.Kind == GestureEffectKind.MoveWindow && N.IsWindow((nint)e.Window))
                    {
                        if (!N.SetWindowPos((nint)e.Window, 0, e.Input.Position.X, e.Input.Position.Y, 0, 0, 0x15 | 0x4000)) log.Write("無効ウィンドウの移動が拒否されました");
                    }
                }
                catch (Exception ex) { log.Write($"ウィンドウ操作: {ex}"); }
            }
            commandWake.WaitOne(100);
        }
    }
    private void CheckKazaguru()
    {
        try
        {
            var processes = Process.GetProcessesByName("Kazaguru");
            bool running = processes.Length != 0; foreach (var p in processes) p.Dispose();
            bool changed = (Volatile.Read(ref kazaguru) != 0) != running;
            Volatile.Write(ref kazaguru, running ? 1 : 0);
            if (running && !notified) { notified = true; notify("かざぐるマウスが動作中のため、ジェスチャー機能を停止しています"); }
            if (changed) { Interlocked.Increment(ref epoch); log.Write(running ? "Kazaguru 動作中: 指示書Bの機能を停止" : "Kazaguru 終了: 指示書Bの機能を再開"); Reply(() => engine.SetPaused(!Enabled)); }
        }
        catch (Exception ex) { Volatile.Write(ref kazaguru, 1); Reply(() => engine.SetPaused(true)); log.Write($"Kazaguru 監視失敗: {ex}"); }
    }
    public void TogglePause()
    {
        Volatile.Write(ref userPaused, Volatile.Read(ref userPaused) == 0 ? 1 : 0);
        Interlocked.Increment(ref epoch);
        Reply(() => engine.SetPaused(!Enabled));
    }
    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        var pause = new ToolStripMenuItem("ジェスチャーを一時停止");
        pause.Click += (_, _) => TogglePause();
        pause.OwnerChanged += (_, _) =>
        {
            if (pause.Owner is ContextMenuStrip menu) menu.Opening += (_, _) =>
                pause.Text = Volatile.Read(ref kazaguru) != 0 ? "ジェスチャー（かざぐる動作中）" :
                    Volatile.Read(ref userPaused) != 0 ? "ジェスチャーを再開" : "ジェスチャーを一時停止";
        };
        yield return pause;
        var restore = new ToolStripMenuItem("隠したウィンドウをすべて戻す"); restore.Click += (_, _) => hidden.RestoreAll(); yield return restore;
    }
    public void SettingsChanged(AppSettings value)
    {
        wheelTrace.SetEnabled(value.Diagnostics.WheelTrace);
        Interlocked.Increment(ref epoch);
        Volatile.Write(ref settings, value); overlay.SettingsChanged(value.Gesture.Trail);
        Reply(() => { engine.UpdateSettings(value); engine.SetPaused(!Enabled); });
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0) return;
        monitor.Stop(); monitor.Dispose(); raw.Dispose();
        Reply(() => { engine.Shutdown(); Post(new(GestureEffectKind.Flush)); });
        if (!hookThread.Join(TimeSpan.FromSeconds(3))) log.Write("フック終了待ちがタイムアウトしました");
        wheelTrace.Dispose();
        Volatile.Write(ref workerStop, 1); actionWake.Set(); decisionWake.Set(); commandWake.Set();
        actionThread.Join(TimeSpan.FromSeconds(3)); decisionThread.Join(TimeSpan.FromSeconds(1));
        commandThread.Join(TimeSpan.FromSeconds(1));
        overlay.Dispose(); hidden.Dispose(); services.Dispose();
    }
    private sealed class RawInputWindow : NativeWindow, IDisposable
    {
        private readonly Action received;
        public RawInputWindow(Action received, ILog log)
        {
            this.received = received;
            CreateHandle(new CreateParams { Caption = "LogiGap Raw Input", Parent = new nint(-3) });
            N.RawDevice[] devices = [new() { Page = 1, Usage = 2, Flags = 0x100, Target = Handle }];
            if (!N.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<N.RawDevice>())) log.Write("Raw Input 登録失敗: 生存監視を利用できません");
        }
        protected override void WndProc(ref Message m)
        { if (m.Msg == 0xFF) received(); base.WndProc(ref m); }
        public void Dispose()
        {
            N.RegisterRawInputDevices([new() { Page = 1, Usage = 2, Flags = 1, Target = 0 }], 1, (uint)Marshal.SizeOf<N.RawDevice>());
            DestroyHandle();
        }
    }
}
