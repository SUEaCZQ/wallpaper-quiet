using System.Runtime.InteropServices;

namespace WallpaperQuiet;

internal static class FullscreenPolicy
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L,T,R,B; }
    [DllImport("user32.dll")] static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint window,int attribute,out uint value,int size);

    internal readonly record struct WindowInfo(
        nint Handle,string ClassName,bool Visible,bool Minimized,bool Cloaked,
        bool Maximized,Rectangle Bounds,Rectangle Monitor,bool ClickThroughOverlay=false);

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

    internal static bool Blocks(WindowInfo window) =>
        window.Visible && !window.Minimized && !window.Cloaked && !window.ClickThroughOverlay
        && Blocks(IsDesktopSurface(window.ClassName),window.Maximized,window.Bounds,window.Monitor);

    internal static bool AnyWindowBlocksQuiet(IEnumerable<WindowInfo> windows) => windows.Any(Blocks);

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
            yield return new WindowInfo(w,cls,true,false,false,maximized,
                Rectangle.FromLTRB(r.L,r.T,r.R,r.B),Screen.FromHandle(w).Bounds);
        }
    }

    public static bool AnyWindowBlocksQuiet() => AnyWindowBlocksQuiet(ReadWindows());
}
