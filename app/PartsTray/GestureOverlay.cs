using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PartsTray.Core;
using Microsoft.Win32;
using N = PartsTray.GestureNative;

namespace PartsTray;

/// <summary>Independent STA renderer. Hook/decision threads only enqueue value records.
/// A batch appends new segments; only a color change rebuilds the accumulated DIB.</summary>
internal sealed class GestureOverlay : IDisposable
{
    private readonly ConcurrentQueue<GestureEffect> queue = new();
    private readonly Thread thread;
    private readonly ILog log;
    private readonly List<Surface> surfaces = [];
    private readonly List<PixelPoint> points = [];
    private readonly ManualResetEventSlim ready = new();
    private Control? dispatcher;
    private TipWindow? tooltip;
    private TrailSettings settings;
    private long token;
    private bool visible, valid, renderedValid;
    private int posted, stopped, drawn;

    public GestureOverlay(TrailSettings settings, ILog log)
    {
        this.settings = settings; this.log = log;
        thread = new Thread(Run) { Name = "PartsTray overlay", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
    }
    private void Run()
    {
        try
        {
            dispatcher = new Control(); _ = dispatcher.Handle;
            tooltip = new TipWindow();
            Rebuild(); SystemEvents.DisplaySettingsChanged += DisplayChanged;
            ready.Set(); Application.Run();
        }
        catch (Exception ex) { log.Write($"軌跡スレッド: {ex}"); }
        finally
        {
            ready.Set(); SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            foreach (var surface in surfaces) surface.Dispose();
            tooltip?.Dispose(); dispatcher?.Dispose();
        }
    }
    private void DisplayChanged(object? sender, EventArgs args) => Dispatch(() => { Rebuild(); Draw(true); });
    private void Rebuild()
    {
        foreach (var s in surfaces) s.Dispose(); surfaces.Clear();
        foreach (var screen in Screen.AllScreens) surfaces.Add(new Surface(screen.Bounds));
        drawn = 0;
    }
    public void SettingsChanged(TrailSettings value) => Dispatch(() => { settings = value; Draw(true); });
    public void Post(GestureEffect effect)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        queue.Enqueue(effect);
        if (Interlocked.Exchange(ref posted, 1) == 0) Dispatch(Drain);
    }
    private void Dispatch(Action action)
    {
        try { if (dispatcher is { IsDisposed: false }) dispatcher.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }
    private void Drain()
    {
        try
        {
            int count = 0;
            while (count++ < 8192 && queue.TryDequeue(out var e))
            {
                if (e.Kind == GestureEffectKind.TrailBegin)
                {
                    Hide(); token = e.Token; points.Clear(); points.Add(e.Input.Position); drawn = 0;
                    foreach (var s in surfaces) s.Clear();
                }
                else if (e.Token == token || e.Token == 0)
                {
                    switch (e.Kind)
                    {
                        case GestureEffectKind.TrailPoint: points.Add(e.Input.Position); valid = e.Valid; break;
                        case GestureEffectKind.TrailShow: visible = true; valid = e.Valid; break;
                        case GestureEffectKind.TrailEnd: Hide(); points.Clear(); drawn = 0; break;
                        case GestureEffectKind.Tooltip:
                            if (e.Text == null) tooltip?.Hide();
                            else if (visible) tooltip?.ShowText(e.Text, e.Input.Position);
                            break;
                    }
                }
            }
            Draw(false);
        }
        catch (Exception ex) { Hide(); log.Write($"軌跡描画失敗: {ex}"); }
        finally
        {
            Interlocked.Exchange(ref posted, 0);
            if (!queue.IsEmpty && Interlocked.Exchange(ref posted, 1) == 0) Dispatch(Drain);
        }
    }
    private void Hide()
    {
        visible = false; tooltip?.Hide();
        foreach (var s in surfaces) s.Hide();
    }
    private void Draw(bool force)
    {
        if (!visible || !settings.Enabled) { foreach (var s in surfaces) s.Hide(); return; }
        bool rebuild = force || drawn == 0 || valid != renderedValid;
        var color = ColorTranslator.FromHtml(valid ? settings.ValidColor : settings.InvalidColor);
        using var pen = new Pen(color, settings.WidthPx) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        foreach (var s in surfaces)
        {
            if (rebuild) s.Clear();
            for (int i = rebuild ? 1 : Math.Max(1, drawn); i < points.Count; i++) s.Line(points[i - 1], points[i], pen);
            s.Present();
        }
        drawn = points.Count; renderedValid = valid;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        Dispatch(() => { Hide(); Application.ExitThread(); });
        thread.Join(TimeSpan.FromSeconds(3));
    }

    private sealed class TipWindow : Form
    {
        private readonly Label label = new() { AutoSize = true, Padding = new Padding(8, 5, 8, 5), BackColor = Color.LightYellow, ForeColor = Color.Black };
        public TipWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
            Controls.Add(label);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20 | 8; return p; }
        }
        public void ShowText(string text, PixelPoint p)
        {
            label.Text = text; ClientSize = label.PreferredSize;
            var area = Screen.FromPoint(new(p.X, p.Y)).WorkingArea;
            Location = new(Math.Clamp(p.X + 16, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(p.Y + 20, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            Show(); N.SetWindowPos(Handle, -1, Left, Top, Width, Height, 0x10 | 0x40);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = -1; return; }
            if (m.Msg == 0x21) { m.Result = 3; return; }
            base.WndProc(ref m);
        }
    }
    private sealed class Surface : NativeWindow, IDisposable
    {
        private readonly Rectangle bounds;
        private readonly nint dc, dib, previous;
        private readonly Bitmap bitmap;
        private readonly Graphics graphics;
        private Rectangle dirty;
        private bool shown;
        public Surface(Rectangle bounds)
        {
            this.bounds = bounds;
            CreateHandle(new CreateParams { Caption = "PartsTray trail", Style = unchecked((int)0x80000000),
                ExStyle = 0x80000 | 0x20 | 8 | 0x80 | 0x08000000,
                X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height });
            dc = N.CreateCompatibleDC(0);
            var info = new N.BitmapInfo { Size = (uint)Marshal.SizeOf<N.BitmapInfo>(), Width = bounds.Width,
                Height = -bounds.Height, Planes = 1, BitCount = 32 };
            dib = N.CreateDIBSection(dc, ref info, 0, out nint bits, 0, 0);
            if (dc == 0 || dib == 0)
            {
                if (dib != 0) N.DeleteObject(dib);
                if (dc != 0) N.DeleteDC(dc);
                DestroyHandle(); throw new Win32Exception("軌跡DIBを作成できません");
            }
            previous = N.SelectObject(dc, dib);
            bitmap = new(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
            graphics = Graphics.FromImage(bitmap); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Clear();
        }
        public void Clear()
        {
            graphics.Clear(Color.Transparent); dirty = new(0, 0, bounds.Width, bounds.Height);
        }
        public void Line(PixelPoint a, PixelPoint b, Pen pen)
        {
            int pad = (int)Math.Ceiling(pen.Width / 2) + 2;
            var area = Rectangle.FromLTRB(Math.Min(a.X, b.X) - bounds.X - pad, Math.Min(a.Y, b.Y) - bounds.Y - pad,
                Math.Max(a.X, b.X) - bounds.X + pad + 1, Math.Max(a.Y, b.Y) - bounds.Y + pad + 1);
            area.Intersect(new Rectangle(0, 0, bounds.Width, bounds.Height));
            if (area.Width <= 0 || area.Height <= 0 || a == b) return;
            graphics.DrawLine(pen, a.X - bounds.X, a.Y - bounds.Y, b.X - bounds.X, b.Y - bounds.Y);
            dirty = dirty.IsEmpty ? area : Rectangle.Union(dirty, area);
        }
        public unsafe void Present()
        {
            if (dirty.IsEmpty) return;
            graphics.Flush(FlushIntention.Sync);
            var destination = new N.Point(bounds.X, bounds.Y); var source = new N.Point(0, 0);
            var size = new N.Size { Width = bounds.Width, Height = bounds.Height };
            var blend = new N.Blend { Alpha = 255, Format = 1 };
            var rect = new N.Rect { Left = dirty.Left, Top = dirty.Top, Right = dirty.Right, Bottom = dirty.Bottom };
            var update = new N.LayeredInfo { Size = (uint)Marshal.SizeOf<N.LayeredInfo>(), Destination = (nint)(&destination),
                Dimensions = (nint)(&size), SourceDC = dc, Source = (nint)(&source), Blend = (nint)(&blend), Flags = 2, Dirty = (nint)(&rect) };
            if (!N.UpdateLayeredWindowIndirect(Handle, ref update)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!shown) { N.SetWindowPos(Handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40); shown = true; }
            dirty = Rectangle.Empty;
        }
        public void Hide() { if (shown) { N.ShowWindow(Handle, 0); shown = false; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = -1; return; }
            if (m.Msg == 0x21) { m.Result = 3; return; }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            Hide(); graphics.Dispose(); bitmap.Dispose(); N.SelectObject(dc, previous);
            N.DeleteObject(dib); N.DeleteDC(dc); DestroyHandle();
        }
    }
}
