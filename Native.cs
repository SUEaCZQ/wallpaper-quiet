using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WallpaperQuiet;

internal static class Native
{
    public const int Layered = 0x80000;
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Size, Time; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref Input input);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, nint data);
    public delegate bool EnumProc(nint window, nint data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", SetLastError=true)] public static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", SetLastError=true)] public static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint update);

    public static string Class(nint window) { var b = new StringBuilder(256); GetClassName(window,b,b.Capacity); return b.ToString(); }
    public static uint LastInput()
    {
        var input = new Input { Size = (uint)Marshal.SizeOf<Input>() };
        if (!GetLastInputInfo(ref input)) throw new InvalidOperationException("无法读取空闲状态，已恢复桌面。");
        return input.Time;
    }
    public static List<nint> Targets(bool icons, bool taskbar)
    {
        var result = new HashSet<nint>();
        EnumWindows((w,_) => {
            string cls = Class(w);
            if (taskbar && (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")) result.Add(w);
            if (icons && (cls == "Progman" || cls == "WorkerW")) {
                nint view = FindWindowEx(w,0,"SHELLDLL_DefView",null);
                if (view != 0) {
                    nint list = FindWindowEx(view,0,"SysListView32",null);
                    if (list != 0) result.Add(view);
                }
            }
            return true;
        },0);
        return result.ToList();
    }
    public static bool DesktopForeground()
    {
        string cls = Class(GetAncestor(GetForegroundWindow(),2));
        return cls == "Progman" || cls == "WorkerW";
    }
}

internal sealed class WindowState
{
    public long Handle { get; set; }
    public uint Pid { get; set; }
    public long ProcessStarted { get; set; }
    public string ClassName { get; set; } = "";
    public bool Visible { get; set; }
    public int Style { get; set; }
    public uint Key { get; set; }
    public byte Alpha { get; set; } = 255;
    public uint Flags { get; set; } = 2;
    public bool OriginalAttributesKnown { get; set; } = true;
    public bool Touched { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public nint Window => (nint)Handle;
    [System.Text.Json.Serialization.JsonIgnore] public bool Prepared { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] byte? lastAlpha;

    public static WindowState Capture(nint window)
    {
        Native.GetWindowThreadProcessId(window,out uint pid);
        var s = new WindowState {
            Handle = (long)window, Pid = pid,
            ProcessStarted = Process.GetProcessById((int)pid).StartTime.ToUniversalTime().Ticks,
            ClassName = Native.Class(window), Visible = Native.IsWindowVisible(window),
            Style = Native.GetWindowLong(window,-20)
        };
        if ((s.Style & Native.Layered) != 0) {
            s.OriginalAttributesKnown = Native.GetLayeredWindowAttributes(window,out uint key,out byte alpha,out uint flags);
            if (s.OriginalAttributesKnown) { s.Key=key; s.Alpha=alpha; s.Flags=flags; }
        }
        return s;
    }
    public bool Valid()
    {
        if (!Native.IsWindow(Window) || Native.Class(Window)!=ClassName) return false;
        Native.GetWindowThreadProcessId(Window,out uint pid);
        if (pid!=Pid) return false;
        try { using var p=Process.GetProcessById((int)pid); return p.StartTime.ToUniversalTime().Ticks==ProcessStarted; }
        catch { return false; }
    }
    public void Prepare()
    {
        if (Prepared || !Visible) return;
        if (!Valid()) throw new InvalidOperationException("桌面窗口已更新，请重新开启静享。");
        if (!OriginalAttributesKnown) throw new InvalidOperationException("此桌面层不支持安全渐变，已保留原始显示。");
        Touched=true;
        int style=Native.GetWindowLong(Window,-20);
        if ((style & Native.Layered)==0) {
            Native.SetWindowLong(Window,-20,style | Native.Layered);
            if ((Native.GetWindowLong(Window,-20)&Native.Layered)==0)
                throw new InvalidOperationException("无法为此任务栏开启透明合成。");
        }
        Prepared=true;
        Apply(1);
    }
    public void Apply(double opacity)
    {
        if (!Visible) return;
        if (!Prepared) throw new InvalidOperationException("必须先准备透明合成层。");
        byte baseAlpha=(Flags & 2)!=0 ? Alpha : (byte)255;
        byte alpha=(byte)Math.Round(baseAlpha*Math.Clamp(opacity,0,1));
        if (alpha==lastAlpha) return;
        if (!Native.SetLayeredWindowAttributes(Window,Key,alpha,(Flags & 1)|2))
            throw new InvalidOperationException("实时透明合成不可用，已停止静享并恢复桌面。");
        lastAlpha=alpha;
        // Keep WS_VISIBLE and WS_EX_LAYERED unchanged at both endpoints.
        // Explorer continues rendering; Wallpaper Engine never gets a hide/show event.
    }
    public void Restore(bool guardian=false)
    {
        if ((!Touched && !guardian) || !Visible || !Valid()) return;
        int current=Native.GetWindowLong(Window,-20);
        if ((Style & Native.Layered)==0) {
            if ((current & Native.Layered)!=0) {
                Native.SetLayeredWindowAttributes(Window,0,255,2);
                Native.SetWindowLong(Window,-20,current & ~Native.Layered);
            }
        } else if (OriginalAttributesKnown) {
            if ((current & Native.Layered)==0) Native.SetWindowLong(Window,-20,current | Native.Layered);
            Native.SetLayeredWindowAttributes(Window,Key,Alpha,Flags);
        }
        // No ShowWindow and no forced RedrawWindow, including normal recovery.
        Prepared=false; Touched=false; lastAlpha=null;
    }
}

internal sealed class RecoveryGuard : IDisposable
{
    readonly Process process;
    bool disposed;
    public bool Alive => !disposed && !process.HasExited;
    public RecoveryGuard()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,
            RedirectStandardInput=true, RedirectStandardOutput=true, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--guardian");
        process = Process.Start(info) ?? throw new Exception("恢复保护进程未能启动。");
        Reply();
    }
    void Reply()
    {
        var reply = process.StandardOutput.ReadLineAsync();
        if (!reply.Wait(3000) || reply.Result != "OK") throw new Exception("恢复保护未就绪，自动隐藏已停止。");
    }
    public void Save(List<WindowState> states)
    {
        process.StandardInput.WriteLine(JsonSerializer.Serialize(states));
        process.StandardInput.Flush();
        Reply(); // No shell window is changed until the independent process owns its snapshot.
    }
    public void Dispose()
    {
        if(disposed) return;
        disposed=true;
        try { process.StandardInput.Close(); process.WaitForExit(3000); } catch { }
        process.Dispose();
    }
    public static void Run()
    {
        List<WindowState> snapshots = [];
        try {
            Console.WriteLine("OK"); Console.Out.Flush();
            string? line;
            while ((line = Console.ReadLine()) != null) {
                snapshots = JsonSerializer.Deserialize<List<WindowState>>(line) ?? [];
                Console.WriteLine("OK"); Console.Out.Flush();
            }
        } finally {
            foreach (var s in snapshots) try { s.Restore(true); } catch { }
        }
    }
}

