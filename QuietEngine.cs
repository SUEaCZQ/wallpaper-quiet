using System.Diagnostics;
using System.Text.Json;

namespace WallpaperQuiet;

internal sealed class Settings
{
    // The out-of-box behavior matches the tested configuration used by the app.
    public int IdleSeconds { get; set; } = 5;
    public int FadeMs { get; set; } = 600;
    public int Version { get; set; } = 3;
    public bool StartupAutoStart { get; set; } = true;
    public bool Icons { get; set; } = true;
    public bool Taskbar { get; set; } = true;
    // Allow quiet mode while the settings window is open. Full-screen and
    // maximized applications are still protected by FullscreenPolicy.
    public bool DesktopOnly { get; set; } = false;
    public bool Motion { get; set; } = true;
    static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WallpaperQuiet","settings.json");
    public static Settings Load()
    {
        try {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? new();
            using var json=JsonDocument.Parse(File.ReadAllText(PathName));
            if(!json.RootElement.TryGetProperty("Version",out _) && s.FadeMs==250) s.FadeMs=600;
            s.IdleSeconds = Math.Clamp(s.IdleSeconds,1,3600); s.FadeMs = Math.Clamp(s.FadeMs,100,2000);
            return s;
        } catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName + ".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true }));
        File.Move(PathName + ".tmp",PathName,true);
    }
}

internal sealed class Fade
{
    double start=1,end=1;
    long began;
    int duration;
    public double Value { get; private set; }=1;
    public double Target=>end;
    public double Sample(long now)
    {
        double t=duration==0 ? 1 : Math.Clamp((double)(now-began)/duration,0,1);
        // easeInOutSine: an ambient fade, without the old near-instant initial drop.
        double eased=t<=0 ? 0 : t>=1 ? 1 : -(Math.Cos(Math.PI*t)-1)/2;
        return Value=start+(end-start)*eased;
    }
    public void To(double target,long now,int milliseconds)
    {
        Sample(now);start=Value;end=target;began=now;duration=milliseconds;
        if(milliseconds==0) Value=target;
    }
    public void Reset() { start=end=Value=1;duration=0; }
}

internal sealed class QuietEngine : IDisposable
{
    public Settings Settings { get; }
    readonly RecoveryGuard guard;
    readonly Fade fade=new();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer timer=new() { Interval=40 };
    List<WindowState> windows=[];
    uint input;
    long eligibleSince, nextValidation;
    bool disposed;
    public bool Enabled { get; private set; }
    public bool SettingsOpen { get; set; }=true;
    public string Status { get; private set; }="准备就绪 · 实时渐隐";
    public int Remaining { get; private set; }=10;
    public event Action? Changed;
    public event Action<string>? Failed;
    public QuietEngine(Settings settings)
    {
        Settings=settings; guard=new(); input=Native.LastInput();
        timer.Tick+=(_,_)=>Tick(); timer.Start();
    }
    void PrepareWindows()
    {
        windows=Native.Targets(Settings.Icons,Settings.Taskbar)
            .Where(Native.IsWindowVisible).Select(WindowState.Capture).ToList();
        if(windows.Count==0) throw new InvalidOperationException("没有找到可渐隐的桌面元素。");
        guard.Save(windows); // Recovery owns the original state before any style changes.
        foreach(var w in windows) w.Prepare();
        nextValidation=clock.ElapsedMilliseconds+1000;
    }
    public void Enable()
    {
        Pause();
        try {
            // Prepare once while settings are still open, never at a fade endpoint.
            PrepareWindows(); input=Native.LastInput();
            Enabled=true; eligibleSince=clock.ElapsedMilliseconds;
            Status="等待空闲"; Changed?.Invoke();
        } catch { Restore(); throw; }
    }
    public void Pause()
    {
        Enabled=false; Restore(); Status="已暂停 · 桌面已恢复"; Changed?.Invoke();
    }
    public void Reveal()
    {
        foreach(var w in windows) if(w.Prepared) w.Apply(1);
        fade.Reset();eligibleSince=clock.ElapsedMilliseconds;
    }
    public void Restore()
    {
        foreach(var w in windows) w.Restore();
        windows.Clear(); fade.Reset();
        if(guard.Alive) guard.Save(windows);
        eligibleSince=clock.ElapsedMilliseconds;
    }
    public static long IdleMilliseconds(uint ticks,uint last,long now,long eligibleStart) =>
        Math.Min(unchecked(ticks-last),Math.Max(0,now-eligibleStart));

    void Tick()
    {
        try {
            if(!Enabled) return;
            if(!guard.Alive) throw new Exception("恢复保护进程已退出，已停止自动渐隐。");
            long now=clock.ElapsedMilliseconds;
            uint latest=Native.LastInput();
            bool activity=latest!=input;
            input=latest;
            bool fullscreen=FullscreenPolicy.AnyWindowBlocksQuiet();
            if(fullscreen && (fade.Target!=1 || fade.Value!=1)) Reveal();
            bool eligible=!SettingsOpen && !fullscreen && (!Settings.DesktopOnly || Native.DesktopForeground());
            if(activity || !eligible) eligibleSince=now;
            long idle=IdleMilliseconds(unchecked((uint)Environment.TickCount),latest,now,eligibleSince);
            int remaining=Math.Max(0,Settings.IdleSeconds-(int)(idle/1000));
            bool hide=eligible && !activity && idle>=Settings.IdleSeconds*1000L;
            if(now>=nextValidation && windows.Count>0) {
                nextValidation=now+1000;
                if(windows.Any(w=>!w.Valid())) { Restore();hide=false; }
            }
            double target=hide ? 0 : 1;
            if(target!=fade.Target) {
                if(hide && windows.Count==0) PrepareWindows();
                bool animations=Settings.Motion;
                if(Native.SystemParametersInfo(0x1042,0,out bool systemMotion,0)) animations &= systemMotion;
                fade.To(target,clock.ElapsedMilliseconds,animations ? (hide ? Settings.FadeMs : 250) : 0);
            }
            bool animating=fade.Value!=fade.Target;
            if(animating || target!=1) {
                double value=fade.Sample(clock.ElapsedMilliseconds);
                foreach(var w in windows) w.Apply(value);
            } else if(windows.Count>0) {
                // The first tick after an instant/finished restore still sets the endpoint.
                foreach(var w in windows) w.Apply(1);
            }
            string status=SettingsOpen ? "设置期间保持显示" : fullscreen ? "屏幕被应用占满，保持显示" : !eligible ? "等待回到桌面"
                : fade.Target==0 ? (fade.Value==0 ? "正在欣赏壁纸" : "正在渐隐")
                : fade.Value<1 ? "正在渐现" : "等待空闲";
            if(Status!=status || Remaining!=remaining) {
                Status=status; Remaining=remaining; Changed?.Invoke();
            }
            timer.Interval=fade.Value!=fade.Target ? 15 : 40;
        } catch(Exception ex) {
            Enabled=false;
            try { Restore(); } catch { }
            Status="已停止 · "+ex.Message; Changed?.Invoke(); Failed?.Invoke(ex.Message);
        }
    }
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        timer.Stop();timer.Dispose();
        try { Restore(); } finally { guard.Dispose(); }
    }
}
