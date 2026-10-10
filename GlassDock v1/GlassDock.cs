// GlassDock v4 - a macOS-style notch for Windows 10/11, pinned to the top-centre of your screen.
//  * Clear liquid glass: lightly blurred desktop snapshot + edge refraction + specular rim, no tint by default
//  * Spring / squash-stretch physics: the notch widens, drops, bulges and smears like liquid
//  * Tabs: Home (clock, mini player, focus timer, system) / Music / Apps / Tray / Notes
//  * React-Bits style touches: aurora, shiny + gradient text, count-up numbers, spotlight cards, dock magnify, click sparks
//  * Music card with album art + album-colour theme via Windows media controls
// Idle CPU ~0%: the animation loop only runs while something moves.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// ======================================================================= native
static class N
{
    [StructLayout(LayoutKind.Sequential)] public struct MEM { public uint Len, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }
    [StructLayout(LayoutKind.Sequential)] public struct PWR { public byte AC, Flag, Pct, Sys; public int Life, Full; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFI { public IntPtr hIcon; public int iIcon; public uint attrs; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string disp; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }

    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint b, out RECT r, uint c);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEM m);
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out PWR p);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint attrIn, out uint attrOut);
    [DllImport("shell32.dll")] public static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attr, ref SHFI fi, uint cb, uint flags);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
}

// ======================================================================= ui helpers
static class UI
{
    public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Display, Segoe UI");
    public static readonly SolidColorBrush Fg = new SolidColorBrush(Colors.White);   // island text colour (adapts to the wallpaper)

    public static TextBlock Ft(string t, double size, double op)
    {
        return new TextBlock { Text = t, FontSize = size, Foreground = Fg, Opacity = op, FontFamily = Font };
    }

    public static SolidColorBrush Br(byte a, byte r, byte g, byte b)
    {
        var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x;
    }

    public static Color Hex(string s)
    {
        try
        {
            s = s.TrimStart('#');
            return Color.FromRgb(Convert.ToByte(s.Substring(0, 2), 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16));
        }
        catch { return Colors.White; }
    }

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    public static TextBlock Tb(string t, double size, double op)
    {
        return new TextBlock { Text = t, FontSize = size, Foreground = Brushes.White, Opacity = op, FontFamily = Font };
    }

    public static Border Btn(string text, Action click)
    {
        var tb = Tb(text, 12.5, 1); tb.HorizontalAlignment = HorizontalAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Padding = new Thickness(14, 7, 14, 7), CornerRadius = new CornerRadius(10), Background = Br(36, 255, 255, 255),
                             BorderBrush = Br(50, 255, 255, 255), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = tb, Margin = new Thickness(0, 0, 8, 0) };
        b.MouseEnter += delegate { b.Background = Br(72, 255, 255, 255); };
        b.MouseLeave += delegate { b.Background = Br(36, 255, 255, 255); };
        b.MouseLeftButtonUp += delegate { click(); };
        return b;
    }

    public static TextBox Box(string text)
    {
        return new TextBox { Text = text, Background = Br(255, 32, 35, 44), Foreground = Brushes.White, CaretBrush = Brushes.White,
                             BorderBrush = Br(255, 70, 75, 92), Padding = new Thickness(7, 5, 7, 5), FontFamily = Font, FontSize = 13 };
    }
}

// ======================================================================= settings model
class Cfg
{
    public bool Clock24, MotionBlur = true, AlbumTheme = true, HoverOpen = true, CloseOnLaunch = true, Tooltips = true,
                Aurora = true, Sparks = true, CompactMedia = true, Equalizer = true, TextFx = true;
    public string Theme = "clear", TextMode = "auto", TintColor = "#FFFFFF", Accent = "#9DB8FF";
    public double XOff, Gap, Tint, Legib = 60, Blur = 30, Refract = 70, Rim = 80, Disp = 35, Radius = 30, IconSize = 46,
                  Elastic = 60, Speed = 100, MagnifyAmt = 60, OpenDelay, CloseDelay = 300, GlowAmt = 40, NotchW = 170, DayStart = 9, DayEnd = 18;

    public void Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                int i = line.IndexOf('='); if (i <= 0) continue;
                var f = typeof(Cfg).GetField(line.Substring(0, i).Trim()); if (f == null) continue;
                string v = line.Substring(i + 1).Trim();
                try
                {
                    if (f.FieldType == typeof(bool)) f.SetValue(this, v == "1" || v.ToLower() == "true");
                    else if (f.FieldType == typeof(double)) f.SetValue(this, double.Parse(v, CultureInfo.InvariantCulture));
                    else f.SetValue(this, v);
                }
                catch { }
            }
        }
        catch { }
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        foreach (var f in typeof(Cfg).GetFields())
        {
            object v = f.GetValue(this); string s;
            if (v is bool) s = (bool)v ? "1" : "0";
            else if (v is double) s = ((double)v).ToString("0.###", CultureInfo.InvariantCulture);
            else s = (string)v;
            sb.AppendLine(f.Name + "=" + s);
        }
        try { File.WriteAllText(path, sb.ToString()); } catch { }
    }
}

class Item
{
    public string Name = "", Target = "", Args = "", Icon = "";
    public bool Admin;
    public System.Windows.Media.ImageSource Img; public bool ImgTried;
    public string Expanded { get { return Environment.ExpandEnvironmentVariables(Target); } }
}

// ======================================================================= media (Windows media controls via reflection)
class MediaInfo
{
    public bool Has, Playing;
    public string Title = "", Artist = "", Album = "", Key = "";
    public double Pos, Dur;
    public byte[] Thumb;   // null = unchanged, empty = no art
}

class Media
{
    struct R { public object O; public Type T; }
    static MethodInfo asTask, asStream;
    Type tMgr; R mgr, ses; bool ready, hasSes; string lastKey = "";
    readonly object lk = new object();

    public bool Init()
    {
        try
        {
            tMgr = Type.GetType("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime");
            if (tMgr == null) return false;
            var rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            foreach (var m in rt.GetType("System.WindowsRuntimeSystemExtensions").GetMethods())
                if (m.Name == "AsTask" && m.IsGenericMethodDefinition)
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.Name == "IAsyncOperation`1") asTask = m;
                }
            foreach (var m in rt.GetType("System.IO.WindowsRuntimeStreamExtensions").GetMethods())
                if (m.Name == "AsStreamForRead" && m.GetParameters().Length == 1) asStream = m;
            ready = asTask != null && asStream != null;
            return ready;
        }
        catch { return false; }
    }

    static R CallM(R r, string name)
    {
        var m = r.T.GetMethod(name);
        var o = m.Invoke(r.O, null);
        var x = new R(); x.O = o; x.T = m.ReturnType; return x;
    }

    static R GetP(R r, string name)
    {
        var p = r.T.GetProperty(name);
        var x = new R(); x.O = p.GetValue(r.O, null); x.T = p.PropertyType; return x;
    }

    static R AwaitM(R r, string name)
    {
        var m = r.T.GetMethod(name);
        var op = m.Invoke(r.O, null);
        Type rt = m.ReturnType.GetGenericArguments()[0];
        var task = asTask.MakeGenericMethod(rt).Invoke(null, new object[] { op });
        if (!((System.Threading.Tasks.Task)task).Wait(4000)) throw new TimeoutException();
        var x = new R(); x.O = task.GetType().GetProperty("Result").GetValue(task, null); x.T = rt; return x;
    }

    public MediaInfo Poll()
    {
        try
        {
            if (!ready) return null;
            if (mgr.O == null)
            {
                var st = new R(); st.O = null; st.T = tMgr;
                mgr = AwaitM(st, "RequestAsync");
                if (mgr.O == null) return null;
            }
            var s = CallM(mgr, "GetCurrentSession");
            if (s.O == null) { lock (lk) { hasSes = false; } lastKey = ""; return new MediaInfo(); }
            lock (lk) { ses = s; hasSes = true; }

            var mi = new MediaInfo(); mi.Has = true;
            var props = AwaitM(s, "TryGetMediaPropertiesAsync");
            mi.Title = ((string)GetP(props, "Title").O) ?? "";
            mi.Artist = ((string)GetP(props, "Artist").O) ?? "";
            mi.Album = ((string)GetP(props, "AlbumTitle").O) ?? "";
            var pb = CallM(s, "GetPlaybackInfo");
            mi.Playing = Convert.ToInt32(GetP(pb, "PlaybackStatus").O) == 4;

            try
            {
                var tl = CallM(s, "GetTimelineProperties");
                TimeSpan pos = (TimeSpan)GetP(tl, "Position").O, end = (TimeSpan)GetP(tl, "EndTime").O, st0 = (TimeSpan)GetP(tl, "StartTime").O;
                DateTimeOffset up = (DateTimeOffset)GetP(tl, "LastUpdatedTime").O;
                double p = pos.TotalSeconds;
                if (mi.Playing) p += Math.Max(0, (DateTimeOffset.Now - up).TotalSeconds);
                mi.Dur = (end - st0).TotalSeconds;
                mi.Pos = Math.Max(0, p - st0.TotalSeconds);
                if (mi.Dur > 0) mi.Pos = Math.Min(mi.Pos, mi.Dur);
            }
            catch { }

            mi.Key = mi.Title + "|" + mi.Artist + "|" + mi.Album;
            if (mi.Key != lastKey)
            {
                lastKey = mi.Key;
                byte[] bytes = new byte[0];
                try
                {
                    var th = GetP(props, "Thumbnail");
                    if (th.O != null)
                    {
                        var rs = AwaitM(th, "OpenReadAsync");
                        var stream = asStream.Invoke(null, new object[] { rs.O }) as Stream;
                        if (stream != null)
                            using (stream) { var ms = new MemoryStream(); stream.CopyTo(ms); bytes = ms.ToArray(); }
                    }
                }
                catch { }
                mi.Thumb = bytes;
            }
            return mi;
        }
        catch { mgr = new R(); return null; }
    }

    public void Do(string name)
    {
        R s;
        lock (lk) { if (!hasSes) return; s = ses; }
        try { CallM(s, name); } catch { }
    }
}

// ======================================================================= glass renderer (clear liquid glass)
class GlassView : FrameworkElement
{
    public Rect R, TabRect, BackRect;
    public double Rad = 4, SmearX, SmearY, TabA, AuroraT;
    public Geometry Shape, TabShape;
    public ImageSource Back, Sharp;

    Brush tintBr, scrimBr, glowBr;
    Pen accPen, dispR, dispB;
    Color acc1, acc2, acc3;
    double rim = 0.8, refr = 0.7, disp = 0.3; bool aurora = true;
    static readonly Brush[] Shadow = new Brush[4];
    static readonly Brush Sheen, Fallback, Glint, Depth;
    static readonly Pen RimPen, Glow;

    static GlassView()
    {
        for (int i = 0; i < 4; i++) { var b = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)); b.Freeze(); Shadow[i] = b; }
        var sh = new LinearGradientBrush(new GradientStopCollection {
            new GradientStop(Color.FromArgb(0x3A, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(0x10, 255, 255, 255), 0.32),
            new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.6),
            new GradientStop(Color.FromArgb(0x1A, 255, 255, 255), 1) }, new Point(0.1, 0), new Point(0.7, 1));
        sh.Freeze(); Sheen = sh;
        var fb = new SolidColorBrush(Color.FromArgb(0xB4, 0x20, 0x22, 0x2C)); fb.Freeze(); Fallback = fb;
        var gl = new RadialGradientBrush(Color.FromArgb(0x50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)); gl.Freeze(); Glint = gl;
        var dp = new LinearGradientBrush(new GradientStopCollection {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0x2C, 0, 0, 0), 1) }, new Point(0, 0), new Point(0, 1));
        dp.Freeze(); Depth = dp;
        var rb = new LinearGradientBrush(new GradientStopCollection {
            new GradientStop(Color.FromArgb(0xFF, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(0x44, 255, 255, 255), 0.26),
            new GradientStop(Color.FromArgb(0x1C, 255, 255, 255), 0.60),
            new GradientStop(Color.FromArgb(0xD0, 255, 255, 255), 1) }, new Point(0, 0), new Point(1, 1));
        rb.Freeze(); RimPen = new Pen(rb, 1.5); RimPen.Freeze();
        var ib = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)); ib.Freeze(); Glow = new Pen(ib, 6); Glow.Freeze();
    }

    public GlassView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Configure(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 0, 0, 0), Color.FromRgb(150, 180, 255), 0.2, 0.8, 0.7, 0.3, true);
    }

    public void Configure(Color tint, Color scrim, Color accent, double glowA, double rimA, double refract, double dispA, bool aur)
    {
        var t = new SolidColorBrush(tint); t.Freeze(); tintBr = t;
        var s = new SolidColorBrush(scrim); s.Freeze(); scrimBr = s;
        rim = rimA; refr = refract; disp = dispA; aurora = aur;
        acc1 = accent; acc2 = Color.FromRgb(accent.B, accent.R, accent.G); acc3 = Color.FromRgb(accent.G, accent.B, accent.R);
        var c0 = Color.FromArgb((byte)(255 * Math.Min(0.9, Math.Max(0, glowA))), accent.R, accent.G, accent.B);
        var g = new RadialGradientBrush(c0, Color.FromArgb(0, accent.R, accent.G, accent.B));
        g.Center = new Point(0.5, 1.1); g.GradientOrigin = new Point(0.5, 1.1); g.RadiusX = 0.9; g.RadiusY = 0.7; g.Freeze(); glowBr = g;
        var ab = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)); ab.Freeze();
        accPen = new Pen(ab, 8); accPen.Freeze();
        var rr = new SolidColorBrush(Color.FromArgb((byte)(160 * dispA), 255, 70, 110)); rr.Freeze();
        var bb = new SolidColorBrush(Color.FromArgb((byte)(160 * dispA), 70, 140, 255)); bb.Freeze();
        dispR = new Pen(rr, 1.0); dispR.Freeze(); dispB = new Pen(bb, 1.0); dispB.Freeze();
    }

    void Smear(DrawingContext dc, double rad)
    {
        double a = Math.Min(0.28, (SmearX + SmearY) / 100.0);
        Color c0 = Color.FromArgb((byte)(a * 255), 255, 255, 255), c1 = Color.FromArgb(0, 255, 255, 255);
        if (SmearY > 1)
            dc.DrawRectangle(new LinearGradientBrush(c0, c1, new Point(0.5, 0), new Point(0.5, 1)), null, new Rect(R.X + rad * 0.8, R.Bottom - 1, Math.Max(1, R.Width - rad * 1.6), SmearY));
        if (SmearX > 1)
        {
            double hh = Math.Max(1, R.Height - rad * 1.2);
            dc.DrawRectangle(new LinearGradientBrush(c0, c1, new Point(1, 0.5), new Point(0, 0.5)), null, new Rect(R.X - SmearX + 1, R.Y + 2, SmearX, hh));
            dc.DrawRectangle(new LinearGradientBrush(c0, c1, new Point(0, 0.5), new Point(1, 0.5)), null, new Rect(R.Right - 1, R.Y + 2, SmearX, hh));
        }
    }

    // React-Bits "Aurora": soft colour blobs drifting inside the glass
    void DrawAurora(DrawingContext dc, Rect r)
    {
        Color[] cs = { acc1, acc2, acc3 };
        for (int i = 0; i < 3; i++)
        {
            double ph = AuroraT * (0.32 + 0.11 * i) + i * 2.1;
            var c = cs[i];
            var b = new RadialGradientBrush(Color.FromArgb(44, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            dc.DrawEllipse(b, null, new Point(r.X + r.Width * (0.5 + 0.36 * Math.Sin(ph)), r.Y + r.Height * (0.6 + 0.28 * Math.Cos(ph * 0.8))), r.Width * 0.32, r.Height * 0.8);
        }
    }

    void Glass(DrawingContext dc, Geometry shape, Rect r, double rad, bool island)
    {
        for (int i = 0; i < Shadow.Length; i++)
        {
            double g = 2 + i * 3.4;
            dc.DrawRoundedRectangle(Shadow[i], null, new Rect(r.X - g, r.Y - g + 6, r.Width + 2 * g, r.Height + 2 * g), rad + g, rad + g);
        }
        if (island && (SmearX > 1 || SmearY > 1)) Smear(dc, rad);

        dc.PushClip(shape);
        if (Back != null) dc.DrawImage(Back, BackRect); else dc.DrawRectangle(Fallback, null, r);
        dc.DrawRectangle(scrimBr, null, r);
        dc.DrawRectangle(tintBr, null, r);
        if (island && aurora) DrawAurora(dc, r);
        dc.DrawRectangle(Sheen, null, r);

        if (Sharp != null && refr > 0.03)     // lens refraction: sharper, magnified backdrop feathered into the rim
        {
            var ib = new ImageBrush(Sharp);
            ib.ViewportUnits = BrushMappingMode.Absolute; ib.Viewport = BackRect; ib.Stretch = Stretch.Fill;
            double sc = 1 + 0.16 * refr;
            ib.Transform = new ScaleTransform(sc, sc, r.X + r.Width / 2, r.Y + r.Height / 2);
            double[] ws = { 36, 22, 12, 5 };
            for (int i = 0; i < 4; i++)
            {
                dc.PushOpacity(0.14 + 0.20 * refr);
                dc.DrawGeometry(null, new Pen(ib, ws[i]), shape);
                dc.Pop();
            }
        }
        if (island) dc.DrawRectangle(glowBr, null, r);
        dc.DrawEllipse(Glint, null, new Point(r.X + rad * 1.3, r.Y + rad * 0.8), rad * 2.8, rad * 1.1);
        dc.DrawRectangle(Depth, null, r);
        dc.DrawGeometry(null, Glow, shape);
        dc.DrawGeometry(null, accPen, shape);
        dc.Pop();

        dc.PushOpacity(Math.Min(1.0, 0.3 + rim * 0.85));
        dc.DrawGeometry(null, RimPen, shape);
        dc.Pop();
        if (disp > 0.03)
        {
            dc.PushTransform(new TranslateTransform(-0.9, 0)); dc.DrawGeometry(null, dispR, shape); dc.Pop();
            dc.PushTransform(new TranslateTransform(0.9, 0)); dc.DrawGeometry(null, dispB, shape); dc.Pop();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // hit surface
        if (Shape == null || R.Width < 3 || R.Height < 3) return;
        Glass(dc, Shape, R, Math.Max(1.5, Rad), true);
        if (TabA > 0.02 && TabShape != null)
        {
            dc.PushOpacity(TabA);
            Glass(dc, TabShape, TabRect, TabRect.Height / 2, false);
            dc.Pop();
        }
    }
}

// React-Bits "ShinyText / GradientText": a highlight sweeping across text, driven by a brush transform.
class Flow
{
    public LinearGradientBrush B; public TranslateTransform T = new TranslateTransform();

    public void Build(Color a, Color b)
    {
        var br = new LinearGradientBrush(new GradientStopCollection {
            new GradientStop(a, 0), new GradientStop(a, 0.4), new GradientStop(b, 0.5), new GradientStop(a, 0.6), new GradientStop(a, 1) },
            new Point(0, 0), new Point(1, 0));
        br.RelativeTransform = T; B = br;
    }

    public void Start(double sec)
    {
        T.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-0.6, 0.6, TimeSpan.FromSeconds(sec)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    public void Stop() { T.BeginAnimation(TranslateTransform.XProperty, null); }
}

// React-Bits "CountUp": numbers tween to their new value.
class CountText : TextBlock
{
    public static readonly DependencyProperty VProp = DependencyProperty.Register("V", typeof(double), typeof(CountText), new PropertyMetadata(0.0, OnV));
    public string Suf = "", Fmt = "0";
    public double V { get { return (double)GetValue(VProp); } set { SetValue(VProp, value); } }

    static void OnV(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (CountText)d;
        c.Text = ((double)e.NewValue).ToString(c.Fmt, CultureInfo.InvariantCulture) + c.Suf;
    }

    public void To(double v)
    {
        BeginAnimation(VProp, new DoubleAnimation(v, TimeSpan.FromMilliseconds(650)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
    }
}

// Little equalizer bars (animated, not audio-reactive).
class EqBars : FrameworkElement
{
    public bool Active; double ph; double[] lv = new double[5]; static readonly Random Rnd = new Random();

    public void Step()
    {
        ph += 0.38;
        for (int i = 0; i < lv.Length; i++)
        {
            double t = 0.5 + 0.5 * Math.Sin(ph * (1.1 + 0.23 * i) + i * 1.7);
            lv[i] = Active ? 0.22 + 0.78 * (0.6 * t + 0.4 * Rnd.NextDouble()) : 0.14;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double bw = 3, gap = 2.5, H = ActualHeight;
        for (int i = 0; i < lv.Length; i++)
        {
            double hh = Math.Max(3, lv[i] * H);
            dc.DrawRoundedRectangle(UI.Fg, null, new Rect(i * (bw + gap), (H - hh) / 2, bw, hh), 1.5, 1.5);
        }
    }
}

// React-Bits "ClickSpark": a tiny burst of lines where you click.
class SparkLayer : FrameworkElement
{
    class P { public double X, Y, A, L; }
    List<P> ps = new List<P>(); bool on; public Color Col = Colors.White; static readonly Random Rnd = new Random();

    public void Burst(Point p)
    {
        for (int i = 0; i < 8; i++) { var q = new P(); q.X = p.X; q.Y = p.Y; q.A = i * Math.PI / 4 + Rnd.NextDouble() * 0.4; q.L = 0; ps.Add(q); }
        if (!on) { on = true; CompositionTarget.Rendering += Tick; }
    }

    void Tick(object s, EventArgs e)
    {
        for (int i = ps.Count - 1; i >= 0; i--) { ps[i].L += 0.06; if (ps[i].L >= 1) ps.RemoveAt(i); }
        InvalidateVisual();
        if (ps.Count == 0) { CompositionTarget.Rendering -= Tick; on = false; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ps.Count == 0) return;
        var pen = new Pen(new SolidColorBrush(Col), 1.7); pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round;
        foreach (var q in ps)
        {
            double t = q.L, e = 1 - (1 - t) * (1 - t), r0 = 6 + 24 * e, r1 = r0 + 9 * (1 - t);
            dc.PushOpacity(1 - t);
            dc.DrawLine(pen, new Point(q.X + Math.Cos(q.A) * r0, q.Y + Math.Sin(q.A) * r0), new Point(q.X + Math.Cos(q.A) * r1, q.Y + Math.Sin(q.A) * r1));
            dc.Pop();
        }
    }
}

// ======================================================================= notch window
class NotchWin : Window
{
    public Cfg C = new Cfg();
    public List<Item> Items = new List<Item>();
    List<Item> tray = new List<Item>();
    public bool Pinned;
    public static readonly string[] PresetNames = { "clear", "frost", "smoke", "aurora", "sunset", "rose" };

    const double TOPM = 16, SH = 24, OVR = 44, CapPad = 16, MaxW = 700, MaxH = 336, TabH = 46, TabGap = 10, TabW = 300, CompH = 34,
                 StatW = 180, DayW = 170, MusBarW = 240, HomeBarW = 204;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string PlayGeo = "M0,0 L11,6 L0,12 Z", PauseGeo = "M0,0 L3.6,0 L3.6,12 L0,12 Z M7.4,0 L11,0 L11,12 L7.4,12 Z",
                 PrevGeo = "M0,0 L2,0 L2,5 L11,0 L11,12 L2,7 L2,12 L0,12 Z", NextGeo = "M0,0 L9,5 L9,0 L11,0 L11,12 L9,12 L9,7 L0,12 Z";
    static readonly string[] TabIcons = {
        "M3,11 L12,3 L21,11 M5,9.5 L5,20.5 L19,20.5 L19,9.5",
        "M9,18 L9,5 L19,3 L19,16 M9,18 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M19,16 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0",
        "M4,4 h6 v6 h-6 z M14,4 h6 v6 h-6 z M4,14 h6 v6 h-6 z M14,14 h6 v6 h-6 z",
        "M3,13 L6,5 L18,5 L21,13 L21,19.5 L3,19.5 Z M3,13 L8,13 L9.5,16 L14.5,16 L16,13 L21,13",
        "M5,3 h14 v18 h-14 z M8.5,8 h7 M8.5,12 h7 M8.5,16 h4" };
    static readonly Color[] Palette = {
        Color.FromRgb(0x4F,0x8C,0xFF), Color.FromRgb(0xFF,0x6B,0x6B), Color.FromRgb(0x2E,0xC4,0xB6),
        Color.FromRgb(0xFF,0xA9,0x4D), Color.FromRgb(0x9B,0x6B,0xFF), Color.FromRgb(0x5C,0xC8,0x6B) };

    string itemsFile, cfgFile, notesFile, trayFile;
    double scale = 1, WinW = MaxW + 2 * OVR, WinH = TOPM + MaxH + TabGap + TabH + SH + 24, isW = 660, isH = 238;
    int state, tab, winX, winY, screenW, screenH, pomoSecs = 1500;
    bool animating, menuOpen, busy, dragging, dragMoved, goStaged, goExpand, settingsOpen, artOk, albumOn, mediaPlaying, compOn, pomoWork = true, pomoRun;
    volatile bool mediaWanted;
    double w, h, vw, vh, tw, th, sx, svx, sy, svy, dragVx, dragVy, goTime, last, auroraT;
    double lastCx, lastCy, lastCt, dragStartX, dragStartY, dragStartXOff, dragStartGap;
    Stopwatch sw = Stopwatch.StartNew();
    IntPtr hwnd;
    Color bgColor = Color.FromRgb(90, 90, 90), albumColor = Color.FromRgb(150, 180, 255), accentNow = Color.FromRgb(150, 180, 255);
    double bgLum = 0.3;
    SolidColorBrush accBr = new SolidColorBrush(Color.FromRgb(150, 180, 255));
    Flow shiny = new Flow(), grad = new Flow();

    GlassView view; Grid clipHost, host, root; StackPanel tabBar; SparkLayer sparks;
    Grid[] panels = new Grid[5]; Border[] tabBtns = new Border[6];
    Border compArt; EqBars compEq, musEq; TextBlock compNote;
    TextBlock clock, dateT, homeTitle, homeArtist, homeArtNote, musTitle, musArtist, musAlbum, musArtNote, musElapsed, musRemain, pomoT, pomoMode, trayHint;
    CountText dayPct, cpuV, ramV, batV;
    Border homeArt, musArt, homeProgFill, musProgFill, dayFill, cpuFill, ramFill, batFill;
    System.Windows.Shapes.Path homePlay, musPlay, pomoPlay;
    TextBox notes; WrapPanel appsWrap, trayWrap;
    DispatcherTimer leave, openT, wt, at, eqT, pt;
    List<Border> tileEls = new List<Border>();
    List<ScaleTransform> scs = new List<ScaleTransform>();
    long pi, pk, pu;
    Media media = new Media();
    System.Threading.AutoResetEvent pollEv = new System.Threading.AutoResetEvent(false);
    MediaInfo lastMi; ImageBrush artBrush;
    SettingsWin settingsWin;

    public NotchWin()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        itemsFile = System.IO.Path.Combine(dir, "apps.txt");
        cfgFile = System.IO.Path.Combine(dir, "settings.ini");
        notesFile = System.IO.Path.Combine(dir, "notes.txt");
        trayFile = System.IO.Path.Combine(dir, "tray.txt");
        C.Load(cfgFile);

        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = null;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = -5000; Top = 0; Width = 300; Height = 300;
        UseLayoutRounding = true; SnapsToDevicePixels = true; AllowDrop = true;

        LoadItems(); LoadTray();
        BuildUI();
        w = tw = CompW(); h = th = CompH;
        RebuildApps(); RebuildTray(); ShowPanel(0);

        leave = new DispatcherTimer();
        leave.Tick += delegate
        {
            leave.Stop();
            if (!Pinned && !busy && !menuOpen && !settingsOpen && !dragging && !IsMouseOver && !notes.IsKeyboardFocusWithin) SetState(0);
        };
        openT = new DispatcherTimer();
        openT.Tick += delegate { openT.Stop(); if (IsMouseOver) OpenNow(); };
        wt = new DispatcherTimer(); wt.Interval = TimeSpan.FromSeconds(1);
        wt.Tick += delegate { UpdateWidgets(); };
        at = new DispatcherTimer(); at.Interval = TimeSpan.FromMilliseconds(33);
        at.Tick += delegate { auroraT += 0.033; if (!animating) { view.AuroraT = auroraT; view.InvalidateVisual(); } };
        eqT = new DispatcherTimer(); eqT.Interval = TimeSpan.FromMilliseconds(70);
        eqT.Tick += delegate { if (state == 0) compEq.Step(); else if (musEq != null) musEq.Step(); };
        pt = new DispatcherTimer(); pt.Interval = TimeSpan.FromSeconds(1);
        pt.Tick += delegate { PomoTick(); };
        Cpu();

        MouseEnter += delegate { Expand(); };
        MouseLeave += delegate { openT.Stop(); StartLeave(); };
        Deactivated += delegate { StartLeave(); };
        MouseRightButtonUp += delegate { ShowMenu(MainMenu()); };
        DragEnter += OnDrag; DragOver += OnDrag;
        Drop += OnDropFiles;
        PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (C.Sparks && state == 1) sparks.Burst(e.GetPosition(sparks)); };
    }

    void StartLeave() { leave.Stop(); leave.Interval = TimeSpan.FromMilliseconds(Math.Max(60, C.CloseDelay)); leave.Start(); }
    double CompW() { return (compOn && C.CompactMedia) ? Math.Max(C.NotchW, 280) : C.NotchW; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd);
        if (src != null) scale = src.CompositionTarget.TransformToDevice.M11;
        int ex = N.GetWindowLong(hwnd, -20);
        N.SetWindowLong(hwnd, -20, ex | 0x80);                       // hide from Alt+Tab
        try { N.SetWindowDisplayAffinity(hwnd, 0x11); } catch { }    // keep the notch out of its own snapshot
        PlaceWindow(); SetRgnRect(RegionRect(0, 10)); ApplyVisuals(); Apply();
        Dispatcher.BeginInvoke(new Action(Capture), DispatcherPriority.ApplicationIdle);
        var t = new System.Threading.Thread(PollLoop); t.IsBackground = true; t.Start();
    }

    // ------------------------------------------------------------------ small ui helpers
    static SolidColorBrush Br(byte a, byte r, byte g, byte b) { return UI.Br(a, r, g, b); }

    System.Windows.Shapes.Path Glyph(string geo, double size)
    {
        return new System.Windows.Shapes.Path { Data = Geometry.Parse(geo), Fill = UI.Fg, Stretch = Stretch.Uniform, Width = size, Height = size,
                                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    static TextBlock Txt(string s, double size)
    {
        return new TextBlock { Text = s, FontSize = size, FontFamily = new FontFamily("Segoe UI Symbol"), Foreground = UI.Fg,
                               HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    CountText CT(string suf, double size, double op)
    {
        return new CountText { Suf = suf, FontSize = size, Foreground = UI.Fg, Opacity = op, FontFamily = UI.Font, Text = "0" + suf };
    }

    Grid Bar(double width, out Border fill)
    {
        var g = new Grid { Height = 4, Width = width, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Br(46, 255, 255, 255) });
        fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = accBr };
        g.Children.Add(fill);
        return g;
    }

    Border GBtn(UIElement content, double wd, double ht, Action click)
    {
        var b = new Border { Width = wd, Height = ht, CornerRadius = new CornerRadius(ht / 2), Background = Br(26, 255, 255, 255), Cursor = Cursors.Hand, Child = content, Margin = new Thickness(0, 0, 6, 0) };
        b.MouseEnter += delegate { b.Background = Br(62, 255, 255, 255); };
        b.MouseLeave += delegate { b.Background = Br(26, 255, 255, 255); };
        b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        b.MouseLeftButtonUp += delegate { click(); };
        return b;
    }

    // React-Bits "SpotlightCard": a soft light follows the cursor over the card.
    Border Card(UIElement content, Thickness pad)
    {
        var rb = new RadialGradientBrush(Color.FromArgb(64, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)); rb.RadiusX = 0.6; rb.RadiusY = 0.9;
        var spot = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(18), Opacity = 0, Background = rb };
        var g = new Grid();
        g.Children.Add(spot);
        g.Children.Add(new Border { Padding = pad, Child = content });
        var card = new Border { CornerRadius = new CornerRadius(18), Background = Br(20, 255, 255, 255), BorderBrush = Br(36, 255, 255, 255), BorderThickness = new Thickness(1), Child = g };
        card.MouseMove += delegate(object s, MouseEventArgs e)
        {
            var p = e.GetPosition(card);
            rb.Center = new Point(p.X / Math.Max(1, card.ActualWidth), p.Y / Math.Max(1, card.ActualHeight)); rb.GradientOrigin = rb.Center;
        };
        card.MouseEnter += delegate { spot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))); };
        card.MouseLeave += delegate { spot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))); };
        return card;
    }

    // React-Bits "BlurText"-style entrance: fade + rise with a staggered delay.
    static void Reveal(FrameworkElement el, double delayMs)
    {
        TranslateTransform tr = el.RenderTransform as TranslateTransform;
        if (tr == null)
        {
            var tg = el.RenderTransform as TransformGroup;
            if (tg != null) foreach (var t in tg.Children) { tr = t as TranslateTransform; if (tr != null) break; }
        }
        if (tr == null && el.RenderTransform == Transform.Identity) { tr = new TranslateTransform(); el.RenderTransform = tr; }
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        var bt = TimeSpan.FromMilliseconds(delayMs);
        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = bt, EasingFunction = ease });
        if (tr != null) tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(480)) { BeginTime = bt, EasingFunction = ease });
    }

    void RevealAll(Panel p)
    {
        int i = 0;
        foreach (UIElement c in p.Children)
        {
            var fe = c as FrameworkElement; if (fe == null) continue;
            var wp = fe as WrapPanel;
            if (wp != null) { foreach (UIElement t in wp.Children) { var te = t as FrameworkElement; if (te != null) Reveal(te, 40 + (i++) * 36); } }
            else Reveal(fe, 30 + (i++) * 55);
        }
    }

    // ------------------------------------------------------------------ build ui
    void BuildUI()
    {
        view = new GlassView();
        view.MouseLeftButtonDown += BeginDrag;
        clipHost = new Grid();
        host = new Grid { Width = MaxW, Height = MaxH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, TOPM, 0, 0), Opacity = 0 };
        panels[0] = BuildHome(); panels[1] = BuildMusicTab(); panels[2] = BuildAppsTab(); panels[3] = BuildTrayTab(); panels[4] = BuildNotesTab();
        foreach (var p in panels) { p.Visibility = Visibility.Collapsed; host.Children.Add(p); }
        clipHost.Children.Add(host);

        compNote = UI.Ft("\u266A", 12, 0.7); compNote.HorizontalAlignment = HorizontalAlignment.Center; compNote.VerticalAlignment = VerticalAlignment.Center;
        compArt = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Br(50, 255, 255, 255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                               Margin = new Thickness(0, TOPM + (CompH - 22) / 2, 0, 0), RenderTransform = new TranslateTransform(), IsHitTestVisible = false, Opacity = 0, Child = compNote };
        compEq = new EqBars { Width = 25, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                              Margin = new Thickness(0, TOPM + (CompH - 18) / 2, 0, 0), RenderTransform = new TranslateTransform(), IsHitTestVisible = false, Opacity = 0 };
        clipHost.Children.Add(compArt); clipHost.Children.Add(compEq);

        BuildTabBar();
        sparks = new SparkLayer { IsHitTestVisible = false };
        root = new Grid();
        root.Children.Add(view); root.Children.Add(clipHost); root.Children.Add(tabBar); root.Children.Add(sparks);
        Content = root;
    }

    void BuildTabBar()
    {
        tabBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                                  Height = TabH, Opacity = 0, RenderTransform = new TranslateTransform(), Background = Brushes.Transparent };
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            FrameworkElement ic;
            if (i < 5)
                ic = new System.Windows.Shapes.Path { Data = Geometry.Parse(TabIcons[i]), Stroke = UI.Fg, StrokeThickness = 1.7, Stretch = Stretch.Uniform, Width = 18, Height = 18,
                                                      StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                                                      HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else ic = Txt("\u2699", 18);
            var b = new Border { Width = 44, Height = 34, Margin = new Thickness(i == 0 ? 8 : 2, 6, i == 5 ? 8 : 2, 6), CornerRadius = new CornerRadius(17), Background = Brushes.Transparent,
                                 Cursor = Cursors.Hand, Child = ic, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate { if (idx == 5) OpenSettings(); else SetTab(idx); };
            tabBtns[i] = b; tabBar.Children.Add(b);
        }
        tabBar.MouseMove += delegate(object s, MouseEventArgs e) { MagTabs(e.GetPosition(tabBar).X); };
        tabBar.MouseLeave += delegate { MagTabs(-9999); };
    }

    // React-Bits "Dock": buttons swell as the cursor nears.
    void MagTabs(double x)
    {
        for (int i = 0; i < 6; i++)
        {
            double cx = 8 + i * 48 + 22, d = x - cx;
            double s = 1 + 0.28 * Math.Exp(-(d * d) / (2 * 38.0 * 38.0));
            var st = (ScaleTransform)tabBtns[i].RenderTransform; st.ScaleX = s; st.ScaleY = s;
        }
    }

    Grid BuildHome()
    {
        var g = new Grid { Width = 660, Height = 238, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        foreach (double r in new[] { 14.0, 150, 8, 52, 14 }) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r) });
        foreach (double c in new[] { 14.0, 198, 10, 232, 10, 182, 14 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c) });

        // clock + day progress
        clock = UI.Ft("", 40, 1); clock.FontWeight = FontWeights.Light;
        dateT = UI.Ft("", 12.5, 0.7);
        var dayRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        dayRow.Children.Add(UI.Ft("DAY PROGRESS", 10, 0.55));
        dayPct = CT("%", 11, 0.8); dayPct.HorizontalAlignment = HorizontalAlignment.Right; dayRow.Children.Add(dayPct);
        var sa = new StackPanel(); sa.Children.Add(clock); sa.Children.Add(dateT); sa.Children.Add(dayRow); sa.Children.Add(Bar(DayW, out dayFill));
        var cardA = Card(sa, new Thickness(14, 10, 14, 10));
        Grid.SetRow(cardA, 1); Grid.SetColumn(cardA, 1); g.Children.Add(cardA);

        // mini player
        homeArtNote = UI.Ft("\u266A", 22, 0.6); homeArtNote.HorizontalAlignment = HorizontalAlignment.Center; homeArtNote.VerticalAlignment = VerticalAlignment.Center;
        homeArt = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(13), Background = Br(34, 255, 255, 255), Child = homeArtNote };
        homeTitle = UI.Ft("Nothing playing", 13.5, 1); homeTitle.FontWeight = FontWeights.SemiBold; homeTitle.TextTrimming = TextTrimming.CharacterEllipsis; homeTitle.MaxWidth = 150;
        homeArtist = UI.Ft("Play something in Spotify", 11.5, 0.65); homeArtist.TextTrimming = TextTrimming.CharacterEllipsis; homeArtist.MaxWidth = 150;
        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(homeTitle); info.Children.Add(homeArtist);
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new ColumnDefinition());
        top.Children.Add(homeArt); Grid.SetColumn(info, 1); top.Children.Add(info);
        homePlay = Glyph(PlayGeo, 13);
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        ctl.Children.Add(GBtn(Glyph(PrevGeo, 12), 40, 28, delegate { MediaCmd("TrySkipPreviousAsync"); }));
        ctl.Children.Add(GBtn(homePlay, 44, 28, delegate { MediaCmd("TryTogglePlayPauseAsync"); }));
        ctl.Children.Add(GBtn(Glyph(NextGeo, 12), 40, 28, delegate { MediaCmd("TrySkipNextAsync"); }));
        var sb = new StackPanel(); sb.Children.Add(top); sb.Children.Add(Bar(HomeBarW, out homeProgFill)); sb.Children.Add(ctl);
        var cardB = Card(sb, new Thickness(14, 12, 14, 8));
        Grid.SetRow(cardB, 1); Grid.SetColumn(cardB, 3); g.Children.Add(cardB);

        // focus timer
        pomoMode = UI.Ft("WORK", 10, 0.6); pomoMode.HorizontalAlignment = HorizontalAlignment.Right;
        var hdr = new Grid(); hdr.Children.Add(UI.Ft("FOCUS", 10, 0.55)); hdr.Children.Add(pomoMode);
        pomoT = UI.Ft("25:00", 34, 1); pomoT.FontWeight = FontWeights.Light; pomoT.Margin = new Thickness(0, 8, 0, 12);
        pomoPlay = Glyph(PlayGeo, 12);
        var pc = new StackPanel { Orientation = Orientation.Horizontal };
        pc.Children.Add(GBtn(pomoPlay, 52, 32, delegate { PomoToggle(); }));
        pc.Children.Add(GBtn(Txt("\u21BA", 16), 40, 32, delegate { PomoReset(); }));
        var sc = new StackPanel(); sc.Children.Add(hdr); sc.Children.Add(pomoT); sc.Children.Add(pc);
        var cardC = Card(sc, new Thickness(14, 12, 14, 10));
        Grid.SetRow(cardC, 1); Grid.SetColumn(cardC, 5); g.Children.Add(cardC);

        // system strip
        var sg = new Grid();
        for (int i = 0; i < 3; i++) sg.ColumnDefinitions.Add(new ColumnDefinition());
        var s1 = Stat("CPU", "%", out cpuV, out cpuFill); var s2 = Stat("RAM", "%", out ramV, out ramFill); var s3 = Stat("BATTERY", "%", out batV, out batFill);
        Grid.SetColumn(s2, 1); Grid.SetColumn(s3, 2);
        sg.Children.Add(s1); sg.Children.Add(s2); sg.Children.Add(s3);
        var strip = Card(sg, new Thickness(14, 8, 0, 6));
        Grid.SetRow(strip, 3); Grid.SetColumn(strip, 1); Grid.SetColumnSpan(strip, 5); g.Children.Add(strip);
        return g;
    }

    StackPanel Stat(string label, string suf, out CountText v, out Border fill)
    {
        var row = new Grid(); row.Children.Add(UI.Ft(label, 10, 0.55));
        v = CT(suf, 11, 0.85); v.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(v);
        var sp = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        sp.Children.Add(row); sp.Children.Add(Bar(StatW, out fill));
        return sp;
    }

    Grid BuildMusicTab()
    {
        var g = new Grid { Width = 540, Height = 190, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        musArtNote = UI.Ft("\u266A", 46, 0.5); musArtNote.HorizontalAlignment = HorizontalAlignment.Center; musArtNote.VerticalAlignment = VerticalAlignment.Center;
        musArt = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(24), Background = Br(34, 255, 255, 255), Child = musArtNote, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(musArt, 1); g.Children.Add(musArt);

        musTitle = UI.Ft("Nothing playing", 19, 1); musTitle.FontWeight = FontWeights.SemiBold; musTitle.TextTrimming = TextTrimming.CharacterEllipsis; musTitle.MaxWidth = 270;
        musEq = new EqBars { Width = 25, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var tr = new Grid(); tr.ColumnDefinitions.Add(new ColumnDefinition()); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tr.Children.Add(musTitle); Grid.SetColumn(musEq, 1); tr.Children.Add(musEq);
        musArtist = UI.Ft("Play something in Spotify", 14, 0.8); musArtist.TextTrimming = TextTrimming.CharacterEllipsis; musArtist.MaxWidth = 300;
        musAlbum = UI.Ft("", 12, 0.55); musAlbum.TextTrimming = TextTrimming.CharacterEllipsis; musAlbum.MaxWidth = 300;
        musElapsed = UI.Ft("0:00", 10.5, 0.6); musRemain = UI.Ft("0:00", 10.5, 0.6);
        var pr = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        pr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); pr.ColumnDefinitions.Add(new ColumnDefinition()); pr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = Bar(MusBarW, out musProgFill); bar.Margin = new Thickness(0, 4, 0, 0);
        pr.Children.Add(musElapsed); Grid.SetColumn(bar, 1); pr.Children.Add(bar); musRemain.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(musRemain, 2); pr.Children.Add(musRemain);
        musPlay = Glyph(PlayGeo, 16);
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        ctl.Children.Add(GBtn(Glyph(PrevGeo, 14), 48, 34, delegate { MediaCmd("TrySkipPreviousAsync"); }));
        ctl.Children.Add(GBtn(musPlay, 60, 38, delegate { MediaCmd("TryTogglePlayPauseAsync"); }));
        ctl.Children.Add(GBtn(Glyph(NextGeo, 14), 48, 34, delegate { MediaCmd("TrySkipNextAsync"); }));
        var info = new StackPanel { Margin = new Thickness(18, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(tr); info.Children.Add(musArtist); info.Children.Add(musAlbum); info.Children.Add(pr); info.Children.Add(ctl);
        Grid.SetColumn(info, 2); g.Children.Add(info);
        return g;
    }

    Grid BuildAppsTab()
    {
        var g = new Grid { Width = 380, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        appsWrap = new WrapPanel { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 22, 0, 0) };
        appsWrap.MouseMove += delegate(object s, MouseEventArgs e) { MagApps(e.GetPosition(appsWrap)); };
        appsWrap.MouseLeave += delegate { ResetMag(); };
        g.Children.Add(appsWrap);
        return g;
    }

    Grid BuildTrayTab()
    {
        var g = new Grid { Width = 540, Height = 200, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        trayWrap = new WrapPanel { Background = Brushes.Transparent, Width = 492, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 22, 0, 0) };
        trayHint = UI.Ft("Drop files here to keep them handy", 13, 0.55);
        trayHint.HorizontalAlignment = HorizontalAlignment.Center; trayHint.VerticalAlignment = VerticalAlignment.Center;
        var clear = GBtn(UI.Ft("Clear", 11.5, 1), 60, 26, delegate { tray.Clear(); SaveTray(); RebuildTray(); });
        ((TextBlock)clear.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)clear.Child).VerticalAlignment = VerticalAlignment.Center;
        clear.HorizontalAlignment = HorizontalAlignment.Right; clear.VerticalAlignment = VerticalAlignment.Bottom; clear.Margin = new Thickness(0, 0, 20, 14);
        g.Children.Add(trayWrap); g.Children.Add(trayHint); g.Children.Add(clear);
        return g;
    }

    Grid BuildNotesTab()
    {
        var g = new Grid { Width = 480, Height = 230, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        notes = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Fg, CaretBrush = UI.Fg,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontFamily = UI.Font, Padding = new Thickness(10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 180
        };
        if (File.Exists(notesFile)) notes.Text = File.ReadAllText(notesFile);
        notes.LostKeyboardFocus += delegate { SaveNotes(); };
        var card = Card(notes, new Thickness(6)); card.Margin = new Thickness(20);
        g.Children.Add(card);
        return g;
    }

    // ------------------------------------------------------------------ tab sizes / apps / tray
    int AppCols()
    {
        double cell = Math.Round(C.IconSize) + 16; int n = Items.Count + 1;
        return Math.Max(4, Math.Min(n, (int)((MaxW - 48) / cell)));
    }

    Size TabSize(int t)
    {
        switch (t)
        {
            case 0: return new Size(660, 238);
            case 1: return new Size(540, 190);
            case 2:
                double cell = Math.Round(C.IconSize) + 16; int cols = AppCols();
                int rows = (int)Math.Ceiling((Items.Count + 1) / (double)cols);
                return new Size(Math.Max(380, cols * cell + 48), Math.Min(MaxH, Math.Max(150, 44 + rows * (cell + 4))));
            case 3: return new Size(540, 200);
            default: return new Size(480, 230);
        }
    }

    Border MakeTile(Item it, int idx, out ScaleTransform sc)
    {
        sc = new ScaleTransform(1, 1);
        var grp = new TransformGroup(); grp.Children.Add(sc); grp.Children.Add(new TranslateTransform(0, 0));
        double ic = Math.Round(C.IconSize);
        var tile = new Border { Width = ic, Height = ic, Margin = new Thickness(8), CornerRadius = new CornerRadius(ic * 0.3), Background = Brushes.Transparent,
                                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = grp, Cursor = Cursors.Hand };
        if (C.Tooltips) tile.ToolTip = it.Name;
        var img = GetIcon(it);
        if (img != null)
        {
            var im = new Image { Source = img, Width = ic * 0.72, Height = ic * 0.72, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            tile.Child = im;
        }
        else
        {
            string l = it.Name.Length > 0 ? it.Name.Substring(0, 1).ToUpper() : "?";
            tile.Child = new Border
            {
                Width = ic * 0.76, Height = ic * 0.76, CornerRadius = new CornerRadius(ic * 0.22), Background = new SolidColorBrush(Palette[l[0] % Palette.Length]),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = l, FontSize = ic * 0.4, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontFamily = UI.Font,
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        }
        var scc = sc;
        tile.MouseEnter += delegate { tile.Background = Br(34, 255, 255, 255); };
        tile.MouseLeave += delegate { tile.Background = Brushes.Transparent; };
        tile.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Press(scc); };
        tile.MouseLeftButtonUp += delegate { Launch(it); };
        tile.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var m = new ContextMenu();
            m.Items.Add(Mi("Open", delegate { Launch(it); }));
            m.Items.Add(Mi("Edit...", delegate { EditItem(it); }));
            m.Items.Add(Mi("Move earlier", delegate { Move(idx, -1); }));
            m.Items.Add(Mi("Move later", delegate { Move(idx, 1); }));
            m.Items.Add(new Separator());
            m.Items.Add(Mi("Remove", delegate { Items.RemoveAt(idx); ItemsChanged(); }));
            ShowMenu(m);
        };
        return tile;
    }

    Border MakeAddTile()
    {
        double ic = Math.Round(C.IconSize);
        var t = new Border { Width = ic, Height = ic, Margin = new Thickness(8), CornerRadius = new CornerRadius(ic * 0.3), Background = Br(18, 255, 255, 255),
                             BorderBrush = Br(80, 255, 255, 255), BorderThickness = new Thickness(1.2), Cursor = Cursors.Hand, Child = Txt("+", 22),
                             RenderTransform = new TranslateTransform() };
        t.MouseEnter += delegate { t.Background = Br(48, 255, 255, 255); };
        t.MouseLeave += delegate { t.Background = Br(18, 255, 255, 255); };
        t.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        t.MouseLeftButtonUp += delegate { AddInstalled(); };
        return t;
    }

    static void Press(ScaleTransform sc)
    {
        var a = new DoubleAnimation(sc.ScaleX * 0.86, TimeSpan.FromMilliseconds(70)) { AutoReverse = true, FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase() };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void MagApps(Point p)
    {
        double amt = C.MagnifyAmt / 100.0 * 0.5;
        if (amt < 0.01) return;
        for (int i = 0; i < tileEls.Count; i++)
        {
            var t = tileEls[i];
            Point c = t.TranslatePoint(new Point(t.ActualWidth / 2, t.ActualHeight / 2), appsWrap);
            double d2 = (p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y);
            double s = 1 + amt * Math.Exp(-d2 / (2 * 56.0 * 56.0));
            scs[i].ScaleX = s; scs[i].ScaleY = s;
        }
    }

    void ResetMag() { for (int i = 0; i < scs.Count; i++) { scs[i].ScaleX = 1; scs[i].ScaleY = 1; } }

    void RebuildApps()
    {
        appsWrap.Children.Clear(); tileEls.Clear(); scs.Clear();
        for (int i = 0; i < Items.Count; i++)
        {
            ScaleTransform sc;
            var t = MakeTile(Items[i], i, out sc);
            appsWrap.Children.Add(t); tileEls.Add(t); scs.Add(sc);
        }
        appsWrap.Children.Add(MakeAddTile());
        appsWrap.Width = AppCols() * (Math.Round(C.IconSize) + 16);
        var sz = TabSize(2); panels[2].Width = sz.Width; panels[2].Height = sz.Height;
    }

    void Rebuild()
    {
        RebuildApps();
        if (tab == 2)
        {
            var sz = TabSize(2); isW = sz.Width; isH = sz.Height;
            if (state == 1) { WideRgn(); Go(isW, isH, false); }
        }
        Apply();
    }

    void LoadTray()
    {
        tray.Clear();
        try
        {
            if (File.Exists(trayFile))
                foreach (var l in File.ReadAllLines(trayFile))
                    if (l.Trim().Length > 0) tray.Add(new Item { Name = System.IO.Path.GetFileName(l.Trim()), Target = l.Trim() });
        }
        catch { }
    }

    void SaveTray()
    {
        var sb = new StringBuilder();
        foreach (var it in tray) sb.AppendLine(it.Target);
        try { File.WriteAllText(trayFile, sb.ToString()); } catch { }
    }

    Border MakeChip(Item it, int idx)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var img = GetIcon(it);
        FrameworkElement ic;
        if (img != null) ic = new Image { Source = img, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        else ic = Txt("\u25A4", 18);
        var nm = UI.Ft(it.Name, 11.5, 1); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = 74; nm.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic); Grid.SetColumn(nm, 1); g.Children.Add(nm);
        var chip = new Border { Width = 118, Height = 44, Margin = new Thickness(4), CornerRadius = new CornerRadius(14), Background = Br(20, 255, 255, 255),
                                BorderBrush = Br(34, 255, 255, 255), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = g, ToolTip = it.Target };
        chip.MouseEnter += delegate { chip.Background = Br(44, 255, 255, 255); };
        chip.MouseLeave += delegate { chip.Background = Br(20, 255, 255, 255); };
        chip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        chip.MouseLeftButtonUp += delegate { Launch(it); };
        chip.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var m = new ContextMenu();
            m.Items.Add(Mi("Open", delegate { Launch(it); }));
            m.Items.Add(Mi("Remove from tray", delegate { tray.RemoveAt(idx); SaveTray(); RebuildTray(); }));
            ShowMenu(m);
        };
        return chip;
    }

    void RebuildTray()
    {
        trayWrap.Children.Clear();
        for (int i = 0; i < tray.Count && i < 16; i++) trayWrap.Children.Add(MakeChip(tray[i], i));
        trayHint.Visibility = tray.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnDrag(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true; leave.Stop();
        if (state == 0) { if (tab != 2) tab = 3; OpenNow(); }
        else if (tab != 2 && tab != 3) SetTab(3);
    }

    void OnDropFiles(object s, DragEventArgs e)
    {
        var f = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (f == null) return;
        if (tab == 2)
        {
            foreach (var p in f) Items.Add(new Item { Name = System.IO.Path.GetFileNameWithoutExtension(p), Target = p });
            ItemsChanged();
        }
        else
        {
            foreach (var p in f) tray.Add(new Item { Name = System.IO.Path.GetFileName(p), Target = p });
            SaveTray(); RebuildTray();
        }
    }

    // ------------------------------------------------------------------ tabs
    void ShowPanel(int t)
    {
        for (int i = 0; i < 5; i++)
        {
            tabBtns[i].Background = i == t ? Br(64, 255, 255, 255) : (Brush)Brushes.Transparent;
            panels[i].Visibility = i == t ? Visibility.Visible : Visibility.Collapsed;
        }
        var sz = TabSize(t); isW = sz.Width; isH = sz.Height; panels[t].Width = isW; panels[t].Height = isH;
        if (state == 1) RevealAll(panels[t]);
    }

    void WideRgn() { SetRgnRect(new Rect(0, 0, WinW, WinH)); }

    void SetTab(int t)
    {
        tab = t; ShowPanel(t);
        if (state == 1) { WideRgn(); Go(isW, isH, false); }
        UpdateTimers();
    }

    // ------------------------------------------------------------------ focus timer
    void PomoToggle() { pomoRun = !pomoRun; if (pomoRun) pt.Start(); else pt.Stop(); UpdatePomo(); }
    void PomoReset() { pomoRun = false; pt.Stop(); pomoWork = true; pomoSecs = 1500; UpdatePomo(); }

    void PomoTick()
    {
        pomoSecs--;
        if (pomoSecs <= 0)
        {
            pomoWork = !pomoWork; pomoSecs = pomoWork ? 1500 : 300;
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }
        UpdatePomo();
    }

    void UpdatePomo()
    {
        pomoT.Text = (pomoSecs / 60).ToString("00") + ":" + (pomoSecs % 60).ToString("00");
        pomoMode.Text = pomoWork ? "WORK" : "BREAK";
        pomoPlay.Data = Geometry.Parse(pomoRun ? PauseGeo : PlayGeo);
    }

    // ------------------------------------------------------------------ live widgets
    void UpdateWidgets()
    {
        var n = DateTime.Now;
        clock.Text = n.ToString(C.Clock24 ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture);
        dateT.Text = n.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        double hrs = n.Hour + n.Minute / 60.0, span = Math.Max(1, C.DayEnd - C.DayStart);
        double p = C01((hrs - C.DayStart) / span);
        dayPct.To(Math.Round(p * 100)); dayFill.Width = DayW * p;
        double c = Cpu(); cpuV.To(Math.Round(c * 100)); cpuFill.Width = StatW * c;
        var m = new N.MEM(); m.Len = (uint)Marshal.SizeOf(typeof(N.MEM));
        if (N.GlobalMemoryStatusEx(ref m)) { ramV.To(m.Load); ramFill.Width = StatW * (m.Load / 100.0); }
        N.PWR pw;
        if (N.GetSystemPowerStatus(out pw) && pw.Pct <= 100) { batV.To(pw.Pct); batFill.Width = StatW * (pw.Pct / 100.0); }
        else { batV.To(100); batFill.Width = StatW; }
    }

    void UpdateTimers()
    {
        bool open = state == 1;
        if (open) wt.Start(); else wt.Stop();
        if (open && C.Aurora) at.Start(); else at.Stop();
        compEq.Active = mediaPlaying && C.Equalizer; if (musEq != null) musEq.Active = mediaPlaying && C.Equalizer;
        bool eq = C.Equalizer && mediaPlaying && ((state == 0 && C.CompactMedia) || (state == 1 && tab == 1));
        if (eq) eqT.Start(); else { eqT.Stop(); compEq.Step(); if (musEq != null) musEq.Step(); }
        mediaWanted = open && (tab == 0 || tab == 1);
        SetFlows(open);
    }

    void SetFlows(bool on)
    {
        bool fx = on && C.TextFx, sh = fx && mediaPlaying;
        if (clock != null) clock.Foreground = fx ? (Brush)grad.B : UI.Fg;
        if (homeTitle != null) homeTitle.Foreground = sh ? (Brush)shiny.B : UI.Fg;
        if (musTitle != null) musTitle.Foreground = sh ? (Brush)shiny.B : UI.Fg;
        if (fx) grad.Start(6); else grad.Stop();
        if (sh) shiny.Start(2.6); else shiny.Stop();
    }

    // ------------------------------------------------------------------ media ui
    void MediaCmd(string name)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(delegate { media.Do(name); System.Threading.Thread.Sleep(250); pollEv.Set(); });
    }

    void OnMedia(MediaInfo m)
    {
        lastMi = m;
        if (m.Thumb != null) LoadArt(m.Thumb);
        bool was = albumOn, wasP = mediaPlaying;
        mediaPlaying = m.Has && m.Playing; albumOn = m.Has && artOk;
        compOn = mediaPlaying && C.CompactMedia;
        ShowMedia(m);
        if (was != albumOn || wasP != mediaPlaying || m.Thumb != null) ApplyVisuals();
        if (wasP != mediaPlaying) { if (state == 0) Go(CompW(), CompH, false); UpdateTimers(); }
        Apply();
    }

    static void SetT(TextBlock t, string s) { if (t != null) t.Text = s; }
    static string Fmt(double s) { int x = (int)Math.Max(0, s); return (x / 60) + ":" + (x % 60).ToString("00"); }

    static void SetArt(Border b, TextBlock note, Brush br, bool art)
    {
        if (b == null) return;
        b.Background = br;
        if (note != null) note.Visibility = art ? Visibility.Collapsed : Visibility.Visible;
    }

    void ShowMedia(MediaInfo m)
    {
        if (m == null) return;
        bool has = m.Has;
        string t = has ? (m.Title.Length > 0 ? m.Title : "Unknown") : "Nothing playing";
        string a = has ? (m.Artist.Length > 0 ? m.Artist : m.Album) : "Play something in Spotify";
        bool art = artOk && has && artBrush != null;
        Brush ab = art ? (Brush)artBrush : UI.Br(34, 255, 255, 255);
        SetT(homeTitle, t); SetT(homeArtist, a); SetT(musTitle, t); SetT(musArtist, a); SetT(musAlbum, has ? m.Album : "");
        double p = (has && m.Dur > 1) ? C01(m.Pos / m.Dur) : 0;
        homeProgFill.Width = HomeBarW * p; musProgFill.Width = MusBarW * p;
        musElapsed.Text = Fmt(has ? m.Pos : 0);
        musRemain.Text = (has && m.Dur > 1) ? "-" + Fmt(m.Dur - m.Pos) : "0:00";
        var geo = Geometry.Parse(has && m.Playing ? PauseGeo : PlayGeo);
        homePlay.Data = geo; musPlay.Data = geo.Clone();
        SetArt(homeArt, homeArtNote, ab, art); SetArt(musArt, musArtNote, ab, art);
        compArt.Background = ab; compNote.Visibility = art ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ theme (clear liquid glass; no tint by default)
    public void ApplyVisuals()
    {
        bool light = bgLum > 0.58;
        bool darkText = C.TextMode == "dark" || (C.TextMode == "auto" && light);
        if (C.TextMode == "light") darkText = false;
        Color fg = darkText ? Color.FromRgb(0x12, 0x14, 0x1A) : Colors.White;
        Color acc = UI.Hex(C.Accent);
        if (C.AlbumTheme && albumOn) acc = albumColor;
        Color tc = UI.Hex(C.TintColor); byte ta = (byte)(255 * C.Tint / 100.0 * 0.6);
        double lg = C.Legib / 100.0, sa; Color scc;
        if (!darkText) { scc = Colors.Black; sa = lg * 0.34 * C01((bgLum - 0.12) / 0.46); }
        else { scc = Colors.White; sa = lg * 0.26 * C01((bgLum - 0.55) / 0.35); }
        UI.Fg.Color = fg; accBr.Color = acc; accentNow = acc; sparks.Col = acc;
        double glow = C.GlowAmt / 100.0 * ((mediaPlaying && C.AlbumTheme) ? 0.7 : 0.25);
        view.Configure(Color.FromArgb(ta, tc.R, tc.G, tc.B), Color.FromArgb((byte)(255 * sa), scc.R, scc.G, scc.B), acc, glow, C.Rim / 100.0, C.Refract / 100.0, C.Disp / 100.0, C.Aurora);
        grad.Build(fg, UI.Mix(acc, Colors.White, 0.2));
        shiny.Build(Color.FromArgb(150, fg.R, fg.G, fg.B), fg);
        SetFlows(state == 1);
        view.InvalidateVisual();
    }

    // Snapshot of what is behind the notch (taken once as it opens), lightly blurred + a sharp copy for refraction.
    void Capture()
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            double capWd = MaxW + 2 * CapPad, capHd = MaxH + TabGap + TabH + 2 * CapPad;
            int cw = (int)Math.Round(capWd * scale), ch = (int)Math.Round(capHd * scale);
            int sx0 = winX + (int)Math.Round((WinW - capWd) / 2.0 * scale);
            int topPx = (int)Math.Round(C.Gap * scale);
            int sy0 = Math.Max(0, topPx - (int)Math.Round(CapPad * scale));
            double backY = TOPM - (topPx - sy0) / scale;
            int div = (int)Math.Round(2 + C.Blur / 100.0 * 10);
            int sw2 = Math.Max(8, cw / div), sh2 = Math.Max(8, ch / div);
            int mw = Math.Max(8, cw / 2), mh = Math.Max(8, ch / 2);
            byte[] px, sharpPx;
            using (var big = new System.Drawing.Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(big))
                    g.CopyFromScreen(sx0, sy0, 0, 0, new System.Drawing.Size(cw, ch), (System.Drawing.CopyPixelOperation)0x40CC0020);
                using (var mid = new System.Drawing.Bitmap(mw, mh, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (var gm = System.Drawing.Graphics.FromImage(mid))
                    {
                        gm.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        gm.DrawImage(big, new System.Drawing.Rectangle(0, 0, mw, mh));
                    }
                    var bm = mid.LockBits(new System.Drawing.Rectangle(0, 0, mw, mh), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                    sharpPx = new byte[mw * 4 * mh]; Marshal.Copy(bm.Scan0, sharpPx, 0, sharpPx.Length); mid.UnlockBits(bm);
                }
                using (var small = new System.Drawing.Bitmap(sw2, sh2, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (var g2 = System.Drawing.Graphics.FromImage(small))
                    {
                        g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g2.DrawImage(big, new System.Drawing.Rectangle(0, 0, sw2, sh2));
                    }
                    var bd = small.LockBits(new System.Drawing.Rectangle(0, 0, sw2, sh2), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                    px = new byte[sw2 * 4 * sh2]; Marshal.Copy(bd.Scan0, px, 0, px.Length); small.UnlockBits(bd);
                }
            }
            for (int it = 0; it < 2; it++) { BlurPass(px, sw2, sh2, 2, true); BlurPass(px, sw2, sh2, 2, false); }

            double lum = 0, ar = 0, ag = 0, ab = 0; int cnt = px.Length / 4;
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                ab += b; ag += g; ar += r;
                double gray = 0.299 * r + 0.587 * g + 0.114 * b;
                lum += gray;
                px[i] = Cl(gray + (b - gray) * 1.35); px[i + 1] = Cl(gray + (g - gray) * 1.35); px[i + 2] = Cl(gray + (r - gray) * 1.35);
                px[i + 3] = 255;
            }
            for (int i = 3; i < sharpPx.Length; i += 4) sharpPx[i] = 255;
            bgColor = Color.FromRgb((byte)(ar / cnt), (byte)(ag / cnt), (byte)(ab / cnt));
            bgLum = lum / cnt / 255.0;

            var bs = System.Windows.Media.Imaging.BitmapSource.Create(sw2, sh2, 96, 96, PixelFormats.Bgr32, null, px, sw2 * 4); bs.Freeze();
            var sp = System.Windows.Media.Imaging.BitmapSource.Create(mw, mh, 96, 96, PixelFormats.Bgr32, null, sharpPx, mw * 4); sp.Freeze();
            view.Back = bs; view.Sharp = sp;
            view.BackRect = new Rect((WinW - capWd) / 2, backY, capWd, capHd);
            ApplyVisuals();
        }
        catch { view.Back = null; view.Sharp = null; }
    }

    // ------------------------------------------------------------------ open / close / placement
    void Expand()
    {
        leave.Stop();
        if (state != 0 || !C.HoverOpen) return;
        if (C.OpenDelay > 0) { openT.Stop(); openT.Interval = TimeSpan.FromMilliseconds(C.OpenDelay); openT.Start(); }
        else OpenNow();
    }

    void OpenNow()
    {
        if (state != 0) return;
        PlaceWindow(); Capture(); SetState(1);
    }

    void SetState(int s)
    {
        int old = state; state = s;
        if (s != 0) ShowPanel(tab);
        SetRgnRect(Rect.Union(RegionRect(old, OVR), RegionRect(s, OVR)));
        if (s == 0) { SaveNotes(); Go(CompW(), CompH, true); }
        else { UpdateWidgets(); Go(isW, isH, true); }
        UpdateTimers(); pollEv.Set();
    }

    void PlaceWindow()
    {
        if (hwnd == IntPtr.Zero) return;
        screenW = N.GetSystemMetrics(0); screenH = N.GetSystemMetrics(1);
        int pw = (int)Math.Round(WinW * scale), ph = (int)Math.Round(WinH * scale);
        double maxX = Math.Max(0, (screenW - 300 * scale) / 2.0 / scale);
        C.XOff = Clamp(C.XOff, -maxX, maxX);
        C.Gap = Clamp(C.Gap, 0, screenH / scale / 3);
        winX = (int)Math.Round((screenW - pw) / 2.0 + C.XOff * scale);
        winY = (int)Math.Round((C.Gap - TOPM) * scale);
        N.SetWindowPos(hwnd, IntPtr.Zero, winX, winY, pw, ph, 0x0014);
    }

    Rect RegionRect(int st, double pad)
    {
        double wd, ht;
        if (st == 0) { wd = CompW(); ht = CompH; } else { wd = isW; ht = isH + TabGap + TabH; }
        var r = new Rect(WinW / 2 - wd / 2 - pad, TOPM - pad, wd + 2 * pad, ht + 2 * pad);
        r.Intersect(new Rect(0, 0, WinW, WinH));
        return r;
    }

    void SetRgnRect(Rect r)
    {
        if (hwnd == IntPtr.Zero) return;
        N.SetWindowRgn(hwnd, N.CreateRectRgn((int)Math.Floor(r.Left * scale), (int)Math.Floor(r.Top * scale), (int)Math.Ceiling(r.Right * scale), (int)Math.Ceiling(r.Bottom * scale)), true);
    }

    // ------------------------------------------------------------------ physics
    void Go(double nw, double nh, bool staged)
    {
        goStaged = staged; goExpand = nw + nh > tw + th;
        tw = nw; th = nh; goTime = sw.Elapsed.TotalSeconds;
        EnsureLoop();
    }

    void EnsureLoop()
    {
        if (!animating) { animating = true; last = sw.Elapsed.TotalSeconds; CompositionTarget.Rendering += Tick; }
    }

    static void Step(ref double x, ref double v, double t, double dt, double k, double c)
    {
        v += (k * (t - x) - c * v) * dt;
        x += v * dt;
    }

    static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }
    static double C01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    void Tick(object s, EventArgs e)
    {
        double now = sw.Elapsed.TotalSeconds, dt = Math.Min(now - last, 1.0 / 30); last = now;
        if (dt <= 0) return;
        double S = Math.Max(0.4, C.Speed / 100.0), E = C.Elastic / 100.0;
        double kw = 320 * S * S, kh = 480 * S * S, ks = 260;
        double cw = 2 * Math.Sqrt(kw) * (0.92 - 0.42 * E), chh = 2 * Math.Sqrt(kh) * (0.96 - 0.30 * E), cs = 2 * Math.Sqrt(ks) * 0.45;
        double age = now - goTime, delay = goStaged ? 0.06 / S : 0;
        bool stepW = !goStaged || goExpand || age >= delay;     // opening: width leads, then it drops; closing: height leads
        bool stepH = !goStaged || !goExpand || age >= delay;
        double dec = Math.Pow(0.01, dt);
        dragVx *= dec; dragVy *= dec;
        double sxT = Clamp(dragVx * 0.012, -12, 12), syT = Clamp(dragVy * 0.012, -12, 12);
        for (int i = 0; i < 2; i++)
        {
            double d2 = dt / 2;
            if (stepW) Step(ref w, ref vw, tw, d2, kw, cw);
            if (stepH) Step(ref h, ref vh, th, d2, kh, chh);
            Step(ref sx, ref svx, sxT, d2, ks, cs);
            Step(ref sy, ref svy, syT, d2, ks, cs);
        }
        bool done = age > delay + 0.02 && !dragging && Math.Abs(w - tw) < 0.2 && Math.Abs(h - th) < 0.2 && Math.Abs(vw) < 3 && Math.Abs(vh) < 3
                    && Math.Abs(sx) < 0.05 && Math.Abs(sy) < 0.05 && Math.Abs(svx) < 0.5 && Math.Abs(svy) < 0.5 && Math.Abs(dragVx) < 2 && Math.Abs(dragVy) < 2;
        if (done) { w = tw; h = th; vw = 0; vh = 0; sx = 0; sy = 0; svx = 0; svy = 0; dragVx = 0; dragVy = 0; }
        Apply();
        if (done)
        {
            CompositionTarget.Rendering -= Tick; animating = false;
            SetRgnRect(RegionRect(state, state == 0 ? 10 : 18));
        }
    }

    // One frame: liquid shape (bulges with velocity), smear, compact pill content, tab bar, content fade.
    void Apply()
    {
        if (hwnd == IntPtr.Zero) return;
        double ww = Math.Min(Math.Max(w, 8), WinW - 6), hh = Math.Min(Math.Max(h, 12), MaxH + 30);
        double x = (WinW - ww) / 2, y = TOPM;
        double rb = Math.Min(C.Radius, Math.Min(ww, hh) / 2), rt = C.Gap < 1.5 ? 0 : rb;
        double E = 0.3 + C.Elastic / 100.0;
        double bs = Clamp(vw * 0.012 * E, -14, 14), bb = Clamp(vh * 0.010 * E, -14, 14) + sy;
        var rc = new Rect(x, y, ww, hh);
        var shape = MakeShape(rc, rt, rb, bb, bs - sx, bs + sx);
        view.R = rc; view.Rad = rb; view.Shape = shape; view.AuroraT = auroraT;

        double prog = C01((hh - CompH) / Math.Max(1, isH - CompH));
        var tr = new Rect((WinW - TabW) / 2, TOPM + hh + TabGap, TabW, TabH);
        var tg = new RectangleGeometry(tr, TabH / 2, TabH / 2); tg.Freeze();
        view.TabRect = tr; view.TabShape = tg; view.TabA = C01((hh - CompH - 24) / 70);
        view.SmearY = C.MotionBlur ? Math.Min(30, Math.Abs(vh) * 0.04) : 0;
        view.SmearX = C.MotionBlur ? Math.Min(40, Math.Abs(vw) * 0.04) : 0;
        view.InvalidateVisual();
        clipHost.Clip = shape;

        host.Opacity = C01(prog * 1.8 - 0.55);
        double comp = compOn ? C01(1 - (hh - CompH) / 18) : 0;
        compArt.Opacity = comp; compEq.Opacity = comp;
        ((TranslateTransform)compArt.RenderTransform).X = -(ww / 2 - 23);
        ((TranslateTransform)compEq.RenderTransform).X = ww / 2 - 24.5;
        tabBar.Opacity = view.TabA;
        ((TranslateTransform)tabBar.RenderTransform).Y = TOPM + hh + TabGap;
    }

    static void Edge(StreamGeometryContext c, Point ctrl, Point end, Point cur)
    {
        double dx = end.X - cur.X, dy = end.Y - cur.Y;
        if (dx * dx + dy * dy < 1) c.LineTo(end, true, false); else c.QuadraticBezierTo(ctrl, end, true, false);
    }

    // Notch shape: flush flat top (or rounded when floating), rounded bottom, edges that can bulge outward.
    static Geometry MakeShape(Rect r, double rt, double rb, double bb, double bl, double br)
    {
        double m = Math.Min(r.Width, r.Height) / 2 - 0.01;
        rb = Math.Max(1, Math.Min(rb, m)); rt = Math.Min(rt, m); if (rt < 0.5) rt = 0;
        double x0 = r.Left, y0 = r.Top, x1 = r.Right, y1 = r.Bottom, mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0 + rt, y0), true, true);
            c.LineTo(new Point(x1 - rt, y0), true, false);
            if (rt > 0) c.ArcTo(new Point(x1, y0 + rt), new Size(rt, rt), 0, false, SweepDirection.Clockwise, true, false);
            Edge(c, new Point(x1 + 2 * br, my), new Point(x1, y1 - rb), new Point(x1, y0 + rt));
            c.ArcTo(new Point(x1 - rb, y1), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
            Edge(c, new Point(mx, y1 + 2 * bb), new Point(x0 + rb, y1), new Point(x1 - rb, y1));
            c.ArcTo(new Point(x0, y1 - rb), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
            Edge(c, new Point(x0 - 2 * bl, my), new Point(x0, y0 + rt), new Point(x0, y1 - rb));
            if (rt > 0) c.ArcTo(new Point(x0 + rt, y0), new Size(rt, rt), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    // ------------------------------------------------------------------ drag to move (X along the top, Y to float it)
    void BeginDrag(object s, MouseButtonEventArgs e)
    {
        if (dragging) return;
        N.POINT p; N.GetCursorPos(out p);
        dragging = true; dragMoved = false;
        dragStartX = p.X; dragStartY = p.Y; dragStartXOff = C.XOff; dragStartGap = C.Gap; lastCx = p.X; lastCy = p.Y; lastCt = sw.Elapsed.TotalSeconds;
        leave.Stop(); CaptureMouse(); e.Handled = true; EnsureLoop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        N.POINT p; N.GetCursorPos(out p);
        double t = sw.Elapsed.TotalSeconds, dtv = Math.Max(0.001, t - lastCt);
        dragVx = dragVx * 0.6 + ((p.X - lastCx) / scale / dtv) * 0.4;
        dragVy = dragVy * 0.6 + ((p.Y - lastCy) / scale / dtv) * 0.4;
        lastCx = p.X; lastCy = p.Y; lastCt = t;
        if (Math.Abs(p.X - dragStartX) + Math.Abs(p.Y - dragStartY) > 4 * scale) dragMoved = true;
        C.XOff = dragStartXOff + (p.X - dragStartX) / scale;
        double g = dragStartGap + (p.Y - dragStartY) / scale;
        C.Gap = g < 14 ? 0 : g;                       // magnetic snap to the top edge
        if (Math.Abs(C.XOff) < 14) C.XOff = 0;        // magnetic snap to centre
        PlaceWindow();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragging) EndDrag();
    }

    void EndDrag()
    {
        dragging = false; ReleaseMouseCapture(); SaveCfg();
        if (!dragMoved && state == 0 && !C.HoverOpen) { OpenNow(); return; }
        if (dragMoved) Capture();
        StartLeave(); EnsureLoop();
    }

    // ------------------------------------------------------------------ menus / settings
    MenuItem Mi(string t, Action a) { return Mi(t, a, false); }
    MenuItem Mi(string t, Action a, bool chk)
    {
        var m = new MenuItem { Header = t, IsChecked = chk };
        m.Click += delegate { a(); };
        return m;
    }

    ContextMenu MainMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(Mi("Settings...", delegate { OpenSettings(); }));
        m.Items.Add(Mi("Add installed app...", delegate { AddInstalled(); }));
        m.Items.Add(Mi("Keep open", delegate { Pinned = !Pinned; if (!Pinned) StartLeave(); }, Pinned));
        m.Items.Add(Mi("Reset position", delegate { ResetPosition(); }));
        m.Items.Add(new Separator());
        m.Items.Add(Mi("Quit GlassDock", delegate { SaveNotes(); SaveCfg(); Application.Current.Shutdown(); }));
        return m;
    }

    void ShowMenu(ContextMenu m)
    {
        m.PlacementTarget = this;
        m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        m.Opened += delegate { menuOpen = true; };
        m.Closed += delegate { menuOpen = false; StartLeave(); };
        m.IsOpen = true;
    }

    public void OpenSettings()
    {
        if (settingsWin != null) { settingsWin.Activate(); return; }
        settingsOpen = true;
        settingsWin = new SettingsWin(this);
        settingsWin.Closed += delegate { settingsWin = null; settingsOpen = false; StartLeave(); };
        settingsWin.Show();
    }

    public static bool StartupOn()
    {
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("GlassDock") != null; }
        catch { return false; }
    }

    public static void SetStartup(bool on)
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (on) k.SetValue("GlassDock", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                else k.DeleteValue("GlassDock", false);
            }
        }
        catch { }
    }

    public void SaveCfg() { C.Save(cfgFile); }
    void SaveNotes() { try { File.WriteAllText(notesFile, notes.Text); } catch { } }

    public void ApplyPreset(string n)
    {
        C.Theme = n;
        switch (n)
        {
            case "frost": C.TintColor = "#FFFFFF"; C.Tint = 28; C.Accent = "#BFD4FF"; break;
            case "smoke": C.TintColor = "#05070D"; C.Tint = 40; C.Accent = "#8AB4FF"; break;
            case "aurora": C.TintColor = "#2EC4B6"; C.Tint = 14; C.Accent = "#5CF2C8"; break;
            case "sunset": C.TintColor = "#FF7A59"; C.Tint = 14; C.Accent = "#FFA070"; break;
            case "rose": C.TintColor = "#FF6B9A"; C.Tint = 14; C.Accent = "#FF8FB5"; break;
            default: C.TintColor = "#FFFFFF"; C.Tint = 0; C.Accent = "#9DB8FF"; break;
        }
        Refresh(1);
    }

    // flags: 1 visuals, 2 recapture backdrop, 4 rebuild apps, 16 re-place / resize pill
    public void Refresh(int f)
    {
        SaveCfg();
        if ((f & 4) != 0) Rebuild();
        if ((f & 16) != 0) { PlaceWindow(); if (state == 0) Go(CompW(), CompH, false); }
        if ((f & 2) != 0) Capture();
        UpdateTimers(); ApplyVisuals(); Apply();
    }

    public void ResetPosition() { C.XOff = 0; C.Gap = 0; PlaceWindow(); SaveCfg(); Capture(); Apply(); }

    public void Replay()
    {
        SetState(0);
        var t = new DispatcherTimer(); t.Interval = TimeSpan.FromMilliseconds(560);
        t.Tick += delegate { t.Stop(); OpenNow(); };
        t.Start();
    }

    // ------------------------------------------------------------------ entry
    [STAThread]
    static void Main()
    {
        bool created;
        var mx = new System.Threading.Mutex(true, "GlassDock_SingleInstance", out created);
        if (!created) return;
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Run(new NotchWin());
        GC.KeepAlive(mx);
    }

    // ------------------------------------------------------------------ reused from v3: apps, icons, launching, media polling, blur helpers
    // ------------------------------------------------------------------ items + icons
    void LoadItems()
    {
        Items.Clear();
        string[] lines = new string[0];
        try { if (File.Exists(itemsFile)) lines = File.ReadAllLines(itemsFile); } catch { }
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
            var p = line.Split('|');
            if (p.Length < 2 || p[0].Trim().Length == 0 || p[1].Trim().Length == 0) continue;
            var it = new Item { Name = p[0].Trim(), Target = p[1].Trim() };
            if (p.Length > 2) it.Args = p[2].Trim();
            if (p.Length > 3) it.Icon = p[3].Trim();
            if (p.Length > 4) it.Admin = p[4].Trim() == "1";
            Items.Add(it);
        }
    }

    public void SaveItems()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GlassDock apps:  Name | target | arguments | icon image | runAsAdmin(0/1)");
        foreach (var it in Items)
            sb.AppendLine(it.Name.Replace('|', '/') + "|" + it.Target.Replace('|', '/') + "|" + it.Args.Replace('|', '/') + "|" + it.Icon.Replace('|', '/') + "|" + (it.Admin ? "1" : "0"));
        try { File.WriteAllText(itemsFile, sb.ToString(), Encoding.UTF8); } catch { }
    }

    public void ItemsChanged() { SaveItems(); Rebuild(); }

    void Move(int i, int d)
    {
        int j = i + d; if (j < 0 || j >= Items.Count) return;
        var t = Items[i]; Items[i] = Items[j]; Items[j] = t;
        ItemsChanged();
    }

    public void EditItem(Item it)
    {
        busy = true;
        var ew = new EditWin(it);
        if (ew.ShowDialog() == true) { it.Img = null; it.ImgTried = false; ItemsChanged(); }
        busy = false;
    }

    public void AddCustom()
    {
        busy = true;
        var it = new Item();
        var ew = new EditWin(it);
        if (ew.ShowDialog() == true) { Items.Add(it); ItemsChanged(); }
        busy = false;
    }

    public void AddFile()
    {
        busy = true;
        var d = new Microsoft.Win32.OpenFileDialog();
        d.Title = "Choose an app, shortcut or file";
        d.Filter = "Apps and shortcuts|*.exe;*.lnk;*.url|All files|*.*";
        if (d.ShowDialog() == true) { Items.Add(new Item { Name = System.IO.Path.GetFileNameWithoutExtension(d.FileName), Target = d.FileName }); ItemsChanged(); }
        busy = false;
    }

    public void AddInstalled()
    {
        busy = true;
        var pk2 = new AppPicker();
        if (pk2.ShowDialog() == true) { foreach (var it in pk2.Picked) Items.Add(it); ItemsChanged(); }
        busy = false;
    }

    static string Resolve(string t)
    {
        if (File.Exists(t) || Directory.Exists(t)) return t;
        if (t.IndexOf('\\') >= 0 || t.IndexOf(':') >= 0) return null;
        string paths = Environment.SystemDirectory + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
        foreach (var d in paths.Split(';'))
        {
            try
            {
                if (d.Length == 0) continue;
                var f = System.IO.Path.Combine(d, t);
                if (File.Exists(f)) return f;
                if (File.Exists(f + ".exe")) return f + ".exe";
            }
            catch { }
        }
        return null;
    }

    static ImageSource ShellIcon(string name)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            uint at;
            if (N.SHParseDisplayName(name, IntPtr.Zero, out pidl, 0, out at) != 0 || pidl == IntPtr.Zero) return null;
            var fi = new N.SHFI();
            N.SHGetFileInfo(pidl, 0, ref fi, (uint)Marshal.SizeOf(typeof(N.SHFI)), 0x108);
            if (fi.hIcon == IntPtr.Zero) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(fi.hIcon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze(); N.DestroyIcon(fi.hIcon);
            return bs;
        }
        catch { return null; }
        finally { if (pidl != IntPtr.Zero) N.CoTaskMemFree(pidl); }
    }

    static ImageSource GetIcon(Item it)
    {
        if (it.Img != null || it.ImgTried) return it.Img;
        it.ImgTried = true;
        try
        {
            if (it.Icon.Length > 0 && File.Exists(it.Icon))
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri(it.Icon); bi.DecodePixelWidth = 128; bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                it.Img = bi; return bi;
            }
        }
        catch { }
        string t = it.Expanded;
        if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) { it.Img = ShellIcon(t); return it.Img; }
        try
        {
            string p = Resolve(t);
            if (p != null)
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(p))
                    if (ic != null)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze(); it.Img = bs;
                    }
        }
        catch { }
        return it.Img;
    }

    // Launch on a separate STA thread so the dock UI can never block.
    void Launch(Item it)
    {
        string t = it.Expanded, a = Environment.ExpandEnvironmentVariables(it.Args), name = it.Name; bool adm = it.Admin;
        var th2 = new System.Threading.Thread(delegate()
        {
            try
            {
                var psi = new ProcessStartInfo(t); psi.UseShellExecute = true;
                if (a.Length > 0) psi.Arguments = a;
                if (adm) psi.Verb = "runas";
                if (File.Exists(t)) psi.WorkingDirectory = System.IO.Path.GetDirectoryName(t);
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                Dispatcher.BeginInvoke(new Action(delegate { busy = true; MessageBox.Show("Couldn't open " + name + "\n\n" + msg, "GlassDock"); busy = false; }));
            }
        });
        th2.SetApartmentState(System.Threading.ApartmentState.STA);
        th2.IsBackground = true; th2.Start();
        if (C.CloseOnLaunch && !Pinned) SetState(0);
    }

    double Cpu()
    {
        long i, k, u; N.GetSystemTimes(out i, out k, out u);
        long di = i - pi, dk = k - pk, du = u - pu; pi = i; pk = k; pu = u;
        long tot = dk + du;
        return tot <= 0 ? 0 : Math.Max(0, Math.Min(1, 1.0 - (double)di / tot));
    }

    void PollLoop()
    {
        bool ok = media.Init();
        while (true)
        {
            if (ok)
            {
                var mi = media.Poll();
                if (mi != null) Dispatcher.BeginInvoke(new Action(delegate { OnMedia(mi); }));
            }
            pollEv.WaitOne(mediaWanted ? 900 : 3500);
        }
    }

    void LoadArt(byte[] bytes)
    {
        artOk = false; artBrush = null;
        if (bytes.Length == 0) return;
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit(); bi.StreamSource = new MemoryStream(bytes); bi.DecodePixelWidth = 200; bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
            var ib = new ImageBrush(bi); ib.Stretch = Stretch.UniformToFill; ib.Freeze(); artBrush = ib;

            var sm = new System.Windows.Media.Imaging.BitmapImage();
            sm.BeginInit(); sm.StreamSource = new MemoryStream(bytes); sm.DecodePixelWidth = 24; sm.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; sm.EndInit();
            var fc = new System.Windows.Media.Imaging.FormatConvertedBitmap(sm, PixelFormats.Bgra32, null, 0);
            int pw = fc.PixelWidth, ph = fc.PixelHeight, stride = pw * 4;
            byte[] px = new byte[stride * ph]; fc.CopyPixels(px, stride, 0);
            double sr = 0, sg = 0, sb = 0, sw2 = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                double sat = mx <= 0 ? 0 : (mx - mn) / mx;
                double wt = sat * sat * (0.3 + mx / 255.0) + 0.01;
                sr += r * wt; sg += g * wt; sb += b * wt; sw2 += wt;
            }
            double rr = sr / sw2, gg = sg / sw2, bb = sb / sw2, top = Math.Max(rr, Math.Max(gg, bb));
            if (top > 0 && top < 170) { double f = 170 / top; rr = Math.Min(255, rr * f); gg = Math.Min(255, gg * f); bb = Math.Min(255, bb * f); }
            albumColor = Color.FromRgb((byte)rr, (byte)gg, (byte)bb);
            artOk = true;
        }
        catch { artOk = false; artBrush = null; }
    }

    static byte Cl(double v) { return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v)); }

    static void BlurPass(byte[] p, int w, int h, int r, bool horiz)
    {
        var s = (byte[])p.Clone();
        int len = horiz ? w : h, lines = horiz ? h : w;
        for (int l = 0; l < lines; l++)
            for (int i = 0; i < len; i++)
            {
                int b = 0, g = 0, rr = 0, n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int j = i + k; if (j < 0) j = 0; else if (j >= len) j = len - 1;
                    int idx = horiz ? (l * w + j) * 4 : (j * w + l) * 4;
                    b += s[idx]; g += s[idx + 1]; rr += s[idx + 2]; n++;
                }
                int o = horiz ? (l * w + i) * 4 : (i * w + l) * 4;
                p[o] = (byte)(b / n); p[o + 1] = (byte)(g / n); p[o + 2] = (byte)(rr / n); p[o + 3] = 255;
            }
    }

}

// ======================================================================= edit dialog
class EditWin : Window
{
    TextBox name, target, args, icon; CheckBox admin; Item it;

    public EditWin(Item item)
    {
        it = item;
        Title = item.Name.Length == 0 ? "Add to dock" : "Edit item";
        Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UI.Br(255, 20, 22, 29); Foreground = Brushes.White; FontFamily = UI.Font; FontSize = 13;
        var sp = new StackPanel { Margin = new Thickness(18) };
        name = UI.Box(item.Name); target = UI.Box(item.Target); args = UI.Box(item.Args); icon = UI.Box(item.Icon);
        Row(sp, "Name", name, false, false);
        Row(sp, "App, file, folder, web link or shell:AppsFolder\\...", target, true, false);
        Row(sp, "Arguments (optional)", args, false, false);
        Row(sp, "Custom icon image (optional, png/ico/jpg)", icon, true, true);
        admin = new CheckBox { Content = "Run as administrator", Foreground = Brushes.White, IsChecked = item.Admin, Margin = new Thickness(0, 12, 0, 0) };
        sp.Children.Add(admin);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        bar.Children.Add(UI.Btn("Cancel", delegate { DialogResult = false; }));
        bar.Children.Add(UI.Btn("Save", Save));
        sp.Children.Add(bar);
        Content = sp;
    }

    void Row(StackPanel sp, string label, TextBox tb, bool browse, bool image)
    {
        var l = UI.Tb(label, 12, 0.7); l.Margin = new Thickness(0, 10, 0, 4); sp.Children.Add(l);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(tb);
        if (browse)
        {
            var b = UI.Btn("Browse", delegate
            {
                var d = new Microsoft.Win32.OpenFileDialog();
                if (image) d.Filter = "Images|*.png;*.ico;*.jpg;*.jpeg;*.bmp|All files|*.*";
                if (d.ShowDialog(this) == true) { tb.Text = d.FileName; if (!image && name.Text.Trim().Length == 0) name.Text = System.IO.Path.GetFileNameWithoutExtension(d.FileName); }
            });
            b.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(b, 1); g.Children.Add(b);
        }
        sp.Children.Add(g);
    }

    void Save()
    {
        if (target.Text.Trim().Length == 0) { MessageBox.Show("Please enter what this should open.", "GlassDock"); return; }
        it.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : System.IO.Path.GetFileNameWithoutExtension(target.Text.Trim());
        it.Target = target.Text.Trim(); it.Args = args.Text.Trim(); it.Icon = icon.Text.Trim(); it.Admin = admin.IsChecked == true;
        DialogResult = true;
    }
}

// ======================================================================= installed-apps picker (incl. Store apps)
class AppPicker : Window
{
    public List<Item> Picked = new List<Item>();
    List<KeyValuePair<string, string>> all = new List<KeyValuePair<string, string>>();
    TextBox search; ListBox lb; TextBlock status;

    public AppPicker()
    {
        Title = "Add installed apps"; Width = 460; Height = 560; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UI.Br(255, 20, 22, 29); Foreground = Brushes.White; FontFamily = UI.Font; FontSize = 13;
        var g = new Grid { Margin = new Thickness(16) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        search = UI.Box(""); search.TextChanged += delegate { Fill(); };
        status = UI.Tb("Loading installed apps...", 12, 0.65); status.Margin = new Thickness(2, 8, 0, 8);
        lb = new ListBox { Background = UI.Br(255, 28, 31, 40), Foreground = Brushes.White, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended };
        lb.MouseDoubleClick += delegate { Accept(); };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        bar.Children.Add(UI.Btn("Cancel", delegate { DialogResult = false; }));
        bar.Children.Add(UI.Btn("Add selected", Accept));
        g.Children.Add(search);
        Grid.SetRow(status, 1); g.Children.Add(status);
        Grid.SetRow(lb, 2); g.Children.Add(lb);
        Grid.SetRow(bar, 3); g.Children.Add(bar);
        Content = g;
        var t = new System.Threading.Thread(LoadApps); t.IsBackground = true; t.Start();
    }

    void LoadApps()
    {
        var res = new List<KeyValuePair<string, string>>();
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + '|' + $_.AppID }\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.UTF8;
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
                foreach (var line in o.Split('\n'))
                {
                    string l = line.Trim(); int i = l.LastIndexOf('|');
                    if (i > 0 && i < l.Length - 1) res.Add(new KeyValuePair<string, string>(l.Substring(0, i), l.Substring(i + 1)));
                }
            }
        }
        catch { }
        res.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
        Dispatcher.BeginInvoke(new Action(delegate { all = res; Fill(); }));
    }

    void Fill()
    {
        string q = search.Text.Trim().ToLower();
        lb.Items.Clear();
        foreach (var kv in all) if (q.Length == 0 || kv.Key.ToLower().Contains(q)) lb.Items.Add(kv.Key);
        status.Text = all.Count == 0 ? "Loading installed apps..." : lb.Items.Count + " apps. Pick one or more, then Add.";
    }

    void Accept()
    {
        foreach (var o in lb.SelectedItems)
        {
            string n = (string)o;
            foreach (var kv in all)
                if (kv.Key == n) { Picked.Add(new Item { Name = kv.Key, Target = "shell:AppsFolder\\" + kv.Value }); break; }
        }
        if (Picked.Count > 0) DialogResult = true;
    }
}

// ======================================================================= settings window
class SettingsWin : Window
{
    NotchWin d; Cfg C; StackPanel page, tabs; string tab = "Look";
    static readonly string[] Tabs = { "Look", "Motion", "Behavior", "Widgets", "Apps" };
    static readonly string[] Swatches = { "#FFFFFF", "#B4BED6", "#9DB8FF", "#6C8CFF", "#5CF2C8", "#2EC4B6", "#FFA070", "#FF6B9A", "#C28BFF", "#0A0E22", "#000000" };

    public SettingsWin(NotchWin dock)
    {
        d = dock; C = dock.C;
        Title = "GlassDock settings"; Width = 620; Height = 720; MinWidth = 540; MinHeight = 500;
        Background = UI.Br(255, 20, 22, 29); Foreground = Brushes.White; FontFamily = UI.Font; FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        var root = new DockPanel();
        tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 6) };
        DockPanel.SetDock(tabs, System.Windows.Controls.Dock.Top);
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 0, 16, 16) };
        page = new StackPanel(); sv.Content = page;
        root.Children.Add(tabs); root.Children.Add(sv);
        Content = root;
        ShowTab(tab);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int v = 1; N.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref v, 4);   // dark title bar
    }

    void ShowTab(string t)
    {
        tab = t; tabs.Children.Clear(); page.Children.Clear();
        foreach (var name in Tabs)
        {
            string n = name;
            var b = new Border { Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0),
                                 Background = n == t ? UI.Br(64, 255, 255, 255) : Brushes.Transparent, Child = UI.Tb(n, 13, n == t ? 1 : 0.6) };
            b.MouseLeftButtonUp += delegate { ShowTab(n); };
            tabs.Children.Add(b);
        }
        if (t == "Look") Look();
        else if (t == "Motion") Motion();
        else if (t == "Behavior") Behavior();
        else if (t == "Widgets") Widgets();
        else Apps();
    }

    // ---- row helpers
    void Head(string t)
    {
        var tb = UI.Tb(t.ToUpper(), 11, 0.55); tb.FontWeight = FontWeights.SemiBold; tb.Margin = new Thickness(0, 18, 0, 6);
        page.Children.Add(tb);
    }

    void Note(string t)
    {
        var tb = UI.Tb(t, 12, 0.55); tb.TextWrapping = TextWrapping.Wrap; tb.Margin = new Thickness(0, 2, 0, 6);
        page.Children.Add(tb);
    }

    void SliderRow(string label, double min, double max, double val, string unit, Action<double> set, int flags)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        var l = UI.Tb(label, 13, 0.9); l.VerticalAlignment = VerticalAlignment.Center;
        var vt = UI.Tb(((int)Math.Round(val)) + unit, 12, 0.7); vt.VerticalAlignment = VerticalAlignment.Center; vt.HorizontalAlignment = HorizontalAlignment.Right;
        var s = new Slider { Minimum = min, Maximum = max, Value = val, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += delegate(object o, RoutedPropertyChangedEventArgs<double> e)
        {
            set(e.NewValue); vt.Text = ((int)Math.Round(e.NewValue)) + unit; d.Refresh(flags);
        };
        g.Children.Add(l); Grid.SetColumn(s, 1); g.Children.Add(s); Grid.SetColumn(vt, 2); g.Children.Add(vt);
        page.Children.Add(g);
    }

    void ToggleRow(string label, bool val, Action<bool> set, int flags)
    {
        var cb = new CheckBox { Content = label, IsChecked = val, Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 5) };
        cb.Checked += delegate { set(true); d.Refresh(flags); };
        cb.Unchecked += delegate { set(false); d.Refresh(flags); };
        page.Children.Add(cb);
    }

    void ChoiceRow(string label, string[] opts, string cur, Action<string> set, int flags)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = UI.Tb(label, 13, 0.9); l.VerticalAlignment = VerticalAlignment.Center;
        var row = new WrapPanel();
        var segs = new List<Border>();
        Action style = delegate
        {
            for (int i = 0; i < segs.Count; i++) segs[i].Background = opts[i] == cur ? UI.Br(80, 255, 255, 255) : UI.Br(26, 255, 255, 255);
        };
        for (int i = 0; i < opts.Length; i++)
        {
            string o = opts[i];
            var b = new Border { Padding = new Thickness(11, 5, 11, 5), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 2, 6, 2), Cursor = Cursors.Hand, Child = UI.Tb(o, 12.5, 1) };
            b.MouseLeftButtonUp += delegate { cur = o; set(o); style(); d.Refresh(flags); };
            segs.Add(b); row.Children.Add(b);
        }
        style();
        g.Children.Add(l); Grid.SetColumn(row, 1); g.Children.Add(row);
        page.Children.Add(g);
    }

    void SwatchRow(string label, string cur, Action<string> set, int flags)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = UI.Tb(label, 13, 0.9); l.VerticalAlignment = VerticalAlignment.Center;
        var row = new WrapPanel();
        foreach (var hx in Swatches)
        {
            string h2 = hx;
            var b = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 2, 7, 2), Cursor = Cursors.Hand,
                                 Background = new SolidColorBrush(UI.Hex(h2)), BorderThickness = new Thickness(2),
                                 BorderBrush = string.Equals(h2, cur, StringComparison.OrdinalIgnoreCase) ? Brushes.White : UI.Br(60, 255, 255, 255) };
            b.MouseLeftButtonUp += delegate { set(h2); d.Refresh(flags); ShowTab(tab); };
            row.Children.Add(b);
        }
        var hb = UI.Box(cur); hb.Width = 84; hb.Margin = new Thickness(4, 0, 0, 0);
        hb.LostFocus += delegate { string v = hb.Text.Trim(); if (v.Length == 7 && v[0] == '#') { set(v); d.Refresh(flags); ShowTab(tab); } };
        row.Children.Add(hb);
        g.Children.Add(l); Grid.SetColumn(row, 1); g.Children.Add(row);
        page.Children.Add(g);
    }

    // ---- tabs
    void Look()
    {
        Head("Glass style");
        var wp = new WrapPanel();
        foreach (var nm in NotchWin.PresetNames)
        {
            string n = nm;
            var b = new Border { Padding = new Thickness(13, 7, 13, 7), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                                 Background = C.Theme == n ? UI.Br(90, 255, 255, 255) : UI.Br(28, 255, 255, 255), Child = UI.Tb(n.Substring(0, 1).ToUpper() + n.Substring(1), 12.5, 1) };
            b.MouseLeftButtonUp += delegate { d.ApplyPreset(n); ShowTab("Look"); };
            wp.Children.Add(b);
        }
        page.Children.Add(wp);
        Note("Clear is real transparent liquid glass with no tint. Frost and Smoke add a tint on top of it.");

        Head("Glass");
        SliderRow("Blur", 0, 100, C.Blur, "%", delegate(double v) { C.Blur = v; }, 3);
        SliderRow("Tint strength", 0, 100, C.Tint, "%", delegate(double v) { C.Tint = v; }, 1);
        SliderRow("Edge refraction", 0, 100, C.Refract, "%", delegate(double v) { C.Refract = v; }, 1);
        SliderRow("Rim light", 0, 100, C.Rim, "%", delegate(double v) { C.Rim = v; }, 1);
        SliderRow("Colour fringe", 0, 100, C.Disp, "%", delegate(double v) { C.Disp = v; }, 1);
        SliderRow("Ambient glow", 0, 100, C.GlowAmt, "%", delegate(double v) { C.GlowAmt = v; }, 1);
        SliderRow("Corner roundness", 8, 44, C.Radius, "px", delegate(double v) { C.Radius = v; }, 1);

        Head("Text and contrast");
        ChoiceRow("Text colour", new[] { "Auto", "Light", "Dark" }, C.TextMode.Substring(0, 1).ToUpper() + C.TextMode.Substring(1), delegate(string v) { C.TextMode = v.ToLower(); }, 1);
        SliderRow("Legibility assist", 0, 100, C.Legib, "%", delegate(double v) { C.Legib = v; }, 1);
        Note("Auto switches to dark text over bright wallpapers. Legibility assist adds a very light scrim only when the background is busy.");

        Head("Colours");
        SwatchRow("Glass tint", C.TintColor, delegate(string v) { C.TintColor = v; }, 1);
        SwatchRow("Accent", C.Accent, delegate(string v) { C.Accent = v; }, 1);
        ToggleRow("Take the accent from the album art", C.AlbumTheme, delegate(bool v) { C.AlbumTheme = v; }, 1);

        Head("Size");
        SliderRow("App icon size", 32, 64, C.IconSize, "px", delegate(double v) { C.IconSize = v; }, 4);
    }

    void Motion()
    {
        Head("Physics");
        SliderRow("Elasticity (jelly)", 0, 100, C.Elastic, "%", delegate(double v) { C.Elastic = v; }, 0);
        SliderRow("Animation speed", 50, 200, C.Speed, "%", delegate(double v) { C.Speed = v; }, 0);
        SliderRow("Icon magnify", 0, 100, C.MagnifyAmt, "%", delegate(double v) { C.MagnifyAmt = v; }, 0);
        ToggleRow("Motion blur / smear", C.MotionBlur, delegate(bool v) { C.MotionBlur = v; }, 0);

        Head("Effects");
        ToggleRow("Aurora glow drifting inside the glass", C.Aurora, delegate(bool v) { C.Aurora = v; }, 1);
        ToggleRow("Click sparks", C.Sparks, delegate(bool v) { C.Sparks = v; }, 0);
        ToggleRow("Shiny / gradient text", C.TextFx, delegate(bool v) { C.TextFx = v; }, 1);
        ToggleRow("Equalizer bars while music plays", C.Equalizer, delegate(bool v) { C.Equalizer = v; }, 1);
        page.Children.Add(new Border { Height = 10 });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(UI.Btn("Replay animation", delegate { d.Replay(); }));
        page.Children.Add(row);
        Note("The equalizer is a visual flourish, not tied to the real audio. If your PC feels busy, turn off Aurora and the equalizer.");
    }

    void Behavior()
    {
        Head("Position");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(UI.Btn("Reset position", delegate { d.ResetPosition(); }));
        page.Children.Add(row);
        Note("Drag any empty glass area to move the notch. Slide it sideways along the top, or pull it down to float it as an island. It snaps back to the top centre.");

        Head("Collapsed notch");
        SliderRow("Notch width", 100, 260, C.NotchW, "px", delegate(double v) { C.NotchW = v; }, 17);
        ToggleRow("Show album art and equalizer when music plays", C.CompactMedia, delegate(bool v) { C.CompactMedia = v; }, 17);

        Head("Opening and closing");
        ToggleRow("Open when the mouse touches the notch", C.HoverOpen, delegate(bool v) { C.HoverOpen = v; }, 0);
        SliderRow("Open delay", 0, 600, C.OpenDelay, "ms", delegate(double v) { C.OpenDelay = v; }, 0);
        SliderRow("Close delay", 80, 2000, C.CloseDelay, "ms", delegate(double v) { C.CloseDelay = v; }, 0);
        ToggleRow("Close after launching an app", C.CloseOnLaunch, delegate(bool v) { C.CloseOnLaunch = v; }, 0);
        ToggleRow("Keep open (pinned)", d.Pinned, delegate(bool v) { d.Pinned = v; }, 0);
        ToggleRow("Show app name tooltips", C.Tooltips, delegate(bool v) { C.Tooltips = v; }, 4);

        Head("System");
        ToggleRow("Start with Windows", NotchWin.StartupOn(), delegate(bool v) { NotchWin.SetStartup(v); }, 0);
    }

    void Widgets()
    {
        Head("Clock and day");
        ToggleRow("24-hour clock", C.Clock24, delegate(bool v) { C.Clock24 = v; }, 0);
        SliderRow("Workday starts", 0, 12, C.DayStart, "h", delegate(double v) { C.DayStart = v; }, 0);
        SliderRow("Workday ends", 12, 24, C.DayEnd, "h", delegate(double v) { C.DayEnd = v; }, 0);
        Note("Music works with anything that shows in Windows media controls: Spotify, YouTube Music in Chrome, Apple Music, VLC and more.");
        Note("Tray: drag files onto the notch to park them on the Tray tab. Drop them while the Apps tab is open to add them as apps instead.");
    }

    void Apps()
    {
        Head("Your apps");
        var bar = new WrapPanel();
        bar.Children.Add(UI.Btn("Installed apps...", delegate { d.AddInstalled(); ShowTab("Apps"); }));
        bar.Children.Add(UI.Btn("Add file...", delegate { d.AddFile(); ShowTab("Apps"); }));
        bar.Children.Add(UI.Btn("Custom...", delegate { d.AddCustom(); ShowTab("Apps"); }));
        page.Children.Add(bar);
        Note("You can also drag any app, shortcut, file or folder onto the open Apps tab.");
        if (d.Items.Count == 0) { Note("Nothing here yet. Add your first app above."); return; }
        for (int i = 0; i < d.Items.Count; i++)
        {
            int idx = i; var it = d.Items[i];
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(UI.Tb(it.Name, 13.5, 1));
            var sub = UI.Tb(it.Target, 11, 0.5); sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.MaxWidth = 300;
            info.Children.Add(sub);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(Small("Edit", delegate { d.EditItem(it); ShowTab("Apps"); }));
            btns.Children.Add(Small("\u25B2", delegate { Swap(idx, -1); }));
            btns.Children.Add(Small("\u25BC", delegate { Swap(idx, 1); }));
            btns.Children.Add(Small("\u2715", delegate { d.Items.RemoveAt(idx); d.ItemsChanged(); ShowTab("Apps"); }));
            Grid.SetColumn(btns, 1);
            row.Children.Add(info); row.Children.Add(btns);
            page.Children.Add(row);
        }
    }

    Border Small(string t, Action a)
    {
        var b = UI.Btn(t, a); b.Padding = new Thickness(10, 5, 10, 5); b.Margin = new Thickness(0, 0, 4, 0); return b;
    }

    void Swap(int i, int dlt)
    {
        int j = i + dlt; if (j < 0 || j >= d.Items.Count) return;
        var t = d.Items[i]; d.Items[i] = d.Items[j]; d.Items[j] = t;
        d.ItemsChanged(); ShowTab("Apps");
    }
}
