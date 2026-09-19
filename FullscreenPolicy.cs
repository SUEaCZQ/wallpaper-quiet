using System.Runtime.InteropServices;

namespace WallpaperQuiet;

internal static class FullscreenPolicy
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L,T,R,B; }
    [DllImport("user32.dll")] static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint window,int attribute,out uint value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint window,int attribute,out Rect value,int size);

    internal readonly record struct WindowInfo(
        nint Handle,string ClassName,bool Visible,bool Minimized,bool Cloaked,
        bool Maximized,Rectangle Bounds,Rectangle Monitor,bool ClickThroughOverlay=false);

    internal readonly record struct DisplayInfo(Rectangle Bounds,Rectangle WorkingArea);

    public static bool Blocks(bool shell,bool maximized,Rectangle window,Rectangle monitor)
    {
        if(shell)return false;
        if(maximized)return true;
        if(window.Width<=0||window.Height<=0||monitor.Width<=0||monitor.Height<=0)return false;
        const int tolerance=2;
        return window.Left<=monitor.Left+tolerance&&window.Top<=monitor.Top+tolerance
            &&window.Right>=monitor.Right-tolerance&&window.Bottom>=monitor.Bottom-tolerance;
    }

    static bool IsDesktopSurface(string cls) =>
        cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "SHELLDLL_DefView" or "WPEDesktopDX11Window" or "WPEDesktopDX12Window"
            or "WPEDesktopOpenGLWindow" or "WPEDesktopVideoWindow";

    static bool OccupiesDesktop(WindowInfo window) =>
        window.Visible && !window.Minimized && !window.Cloaked && !window.ClickThroughOverlay
        && !IsDesktopSurface(window.ClassName);

    internal static bool Blocks(WindowInfo window) => OccupiesDesktop(window)
        && Blocks(false,window.Maximized,window.Bounds,window.Monitor);

    internal static bool AnyWindowBlocksQuiet(IEnumerable<WindowInfo> windows)
    {
        var snapshot=windows.ToArray();
        return AnyWindowBlocksQuiet(snapshot,snapshot.Select(w=>new DisplayInfo(w.Monitor,w.Monitor)).Distinct());
    }

    internal static bool AnyWindowBlocksQuiet(IEnumerable<WindowInfo> windows,IEnumerable<DisplayInfo> displays)
    {
        var occupied=windows.Where(OccupiesDesktop).ToArray();
        if(occupied.Any(Blocks))return true;
        return displays.Any(display=>DisplayCovered(occupied,display));
    }

    internal static bool DisplayCovered(IEnumerable<WindowInfo> windows,DisplayInfo display) =>
        AreaCovered(windows.Where(OccupiesDesktop).Select(w=>w.Bounds),
            display.WorkingArea.Width>0&&display.WorkingArea.Height>0 ? display.WorkingArea : display.Bounds);

    // Sweep vertical strips and merge their Y intervals. Summing window areas or
    // taking one bounding box would count overlapping windows and miss holes.
    internal static bool AreaCovered(IEnumerable<Rectangle> windows,Rectangle target)
    {
        if(target.Width<=0||target.Height<=0)return false;
        var rectangles=windows.Where(r=>r.Width>0&&r.Height>0).Select(r=>{
            // Allow two physical pixels for frame-edge rounding, not visible desktop gaps.
            r.Inflate(2,2);
            return Rectangle.Intersect(r,target);
        }).Where(r=>r.Width>0&&r.Height>0).ToArray();
        if(rectangles.Length==0)return false;
        var edges=rectangles.SelectMany(r=>new[]{r.Left,r.Right})
            .Append(target.Left).Append(target.Right).Distinct().Order().ToArray();
        for(int i=0;i<edges.Length-1;i++){
            int coveredTo=target.Top;
            foreach(var r in rectangles.Where(r=>r.Left<=edges[i]&&r.Right>=edges[i+1]).OrderBy(r=>r.Top)){
                if(r.Top>coveredTo)return false;
                coveredTo=Math.Max(coveredTo,r.Bottom);
                if(coveredTo>=target.Bottom)break;
            }
            if(coveredTo<target.Bottom)return false;
        }
        return true;
    }

    internal static DisplayInfo[] ReadDisplays() => Screen.AllScreens
        .Select(s=>new DisplayInfo(s.Bounds,s.WorkingArea)).ToArray();

    internal static IEnumerable<WindowInfo> ReadWindows()
    {
        var handles=new List<nint>();
        // Enumerate the entire desktop, including windows behind the foreground app.
        // WS_VISIBLE remains set when another app covers a window; that is intentional.
        if(!Native.EnumWindows((w,_)=>{handles.Add(w);return true;},0))
            throw new InvalidOperationException("无法检查应用窗口，已停止渐隐。");
        foreach(nint w in handles) {
            if(!Native.IsWindowVisible(w) || IsIconic(w))continue;
            // Layered, click-through surfaces are overlays, not occupied application windows.
            // Do not exclude tool windows alone: borderless apps can use that style too.
            const int clickThroughLayer=Native.Layered | 0x20;
            if((Native.GetWindowLong(w,-20)&clickThroughLayer)==clickThroughLayer)continue;
            string cls=Native.Class(w);
            if(IsDesktopSurface(cls))continue;
            // DWM-cloaked windows (for example on another virtual desktop) are not displayed.
            bool cloaked=DwmGetWindowAttribute(w,14,out uint cloakFlags,sizeof(uint))==0 && cloakFlags!=0;
            if(cloaked)continue;
            bool maximized=IsZoomed(w);
            if(!GetWindowRect(w,out var r))continue; // The window may have closed during the scan.
            // Ignore invisible resize borders so they cannot fill a real gap between apps.
            if(DwmGetWindowAttribute(w,9,out Rect frame,Marshal.SizeOf<Rect>())==0
                &&frame.R>frame.L&&frame.B>frame.T)r=frame;
            yield return new WindowInfo(w,cls,true,false,false,maximized,
                Rectangle.FromLTRB(r.L,r.T,r.R,r.B),Screen.FromHandle(w).Bounds);
        }
    }

    public static bool AnyWindowBlocksQuiet() => AnyWindowBlocksQuiet(ReadWindows(),ReadDisplays());
}
