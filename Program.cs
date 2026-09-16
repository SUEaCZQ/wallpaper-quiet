using System.Diagnostics;

namespace WallpaperQuiet;

internal static class Program
{
    public static string SignalName=>@"Local\WallpaperQuiet.Restore."+Environment.UserName;
    public static string OpenSignalName=>@"Local\WallpaperQuiet.Open."+Environment.UserName;
    public static string QuitSignalName=>@"Local\WallpaperQuiet.Quit."+Environment.UserName;
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Contains("--guardian")){RecoveryGuard.Run();return 0;}
        if(args.Contains("--configure-startup")){
            try{
                var registration=new StartupRegistration(Environment.ProcessPath!);
                registration.SetEnabled(!args.Contains("--disable"));
                var settings=Settings.Load();settings.StartupAutoStart=!args.Contains("--manual");settings.Save();
                Console.WriteLine(registration.RegisteredCommand??"Disabled");return 0;
            }catch(Exception ex){Console.WriteLine(ex.Message);return 1;}
        }
        if(args.Contains("--restore")||args.Contains("--quit")){
            try{using var signal=EventWaitHandle.OpenExisting(args.Contains("--quit")?QuitSignalName:SignalName);signal.Set();}
            catch(WaitHandleCannotBeOpenedException){if(!args.Contains("--quit"))MessageBox.Show("留景当前没有运行。","留景");}
            return 0;
        }
        bool loginLaunch=args.Contains("--startup")||args.Contains("--startup-smoke");
        if(loginLaunch)StartupLog.Write("Launch entered; version="+Application.ProductVersion);
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>StartupLog.Write("Unhandled: "+e.ExceptionObject);
        ApplicationConfiguration.Initialize();
        if(args.Contains("--ui-tests"))return UiSelfTests.Run();
        if(args.Contains("--validate-live"))return LiveValidation.Run();
        if(args.Contains("--self-test"))return SelfTests.Run();
        if(args.Contains("--diagnose-protection")){
            var windows=FullscreenPolicy.ReadWindows().ToArray();
            var foreground=Native.GetAncestor(Native.GetForegroundWindow(),2);
            var blockers=windows.Where(FullscreenPolicy.Blocks).ToArray();
            Console.WriteLine("Protection active: "+(blockers.Length>0));
            Console.WriteLine("Foreground handle: "+foreground);
            foreach(var w in blockers)
                Console.WriteLine($"Blocker: handle={w.Handle} class={w.ClassName} maximized={w.Maximized} foreground={w.Handle==foreground} bounds={w.Bounds} monitor={w.Monitor}");
            var watch=Stopwatch.StartNew();
            for(int i=0;i<100;i++)FullscreenPolicy.AnyWindowBlocksQuiet();
            Console.WriteLine($"Mean scan time: {watch.Elapsed.TotalMilliseconds/100:F3} ms");
            return 0;
        }
        if(args.Contains("--diagnose")){
            foreach(var h in Native.Targets(true,true))Console.WriteLine(Native.Class(h)+" | visible="+Native.IsWindowVisible(h)+" | handle="+h);
            Console.WriteLine("Input API: "+Native.LastInput());return 0;
        }
        if(args.Length==2&&args[0]=="--render"){
            UiTheme.AllowMotion=false;
            using var form=new MainForm(preview:true);
            form.Show();Application.DoEvents();form.ValidateLayout();
            using var bitmap=new Bitmap(form.Width,form.Height);
            form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1]);form.Exit();return 0;
        }
        using var mutex=new Mutex(true,@"Local\WallpaperQuiet.Instance."+Environment.UserName,out bool fresh);
        if(!fresh){
            if(loginLaunch)StartupLog.Write("Existing instance retained.");
            // Opening an already-running app should show its settings without
            // interrupting quiet mode. --restore retains its pause behavior.
            if(!args.Contains("--startup"))try{using var signal=EventWaitHandle.OpenExisting(OpenSignalName);signal.Set();}catch{}
            return 0;
        }
        MainForm? main=null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>{
            StartupLog.Write("UI error: "+e.Exception);
            try{main?.Exit();}catch{}
            MessageBox.Show("留景遇到问题，正在恢复桌面。\n"+e.Exception.Message,"留景");
        };
        try{
            bool smoke=args.Contains("--startup-smoke");
            main=new MainForm(args.Contains("--startup")||smoke);
            int result=0;
            using var smokeTimer=new System.Windows.Forms.Timer{Interval=4000};
            if(smoke){
                smokeTimer.Tick+=(_,_)=>{
                    smokeTimer.Stop();
                    bool hidden=!Native.IsWindowVisible(main.Handle);
                    bool enabled=main.IsQuietEnabled;
                    Console.WriteLine("Startup window hidden: "+hidden);
                    Console.WriteLine("Automatic quiet mode enabled: "+enabled);
                    result=hidden&&enabled?0:1;main.Exit();
                };
                smokeTimer.Start();
            }
            Application.Run(main);main.Dispose();if(loginLaunch)StartupLog.Write("Exited normally.");return result;
        }catch(Exception ex){
            StartupLog.Write("Startup failure: "+ex);
            try{main?.Dispose();}catch{}
            MessageBox.Show(ex.Message,"留景无法启动");return 1;
        }finally{mutex.ReleaseMutex();}
    }
}

internal static class SelfTests
{
    static int count;
    static void Check(bool condition,string name)
    {
        if(!condition) throw new Exception("FAIL: "+name);
        Console.WriteLine("PASS: "+name);count++;
    }
    public static int Run()
    {
        try {
            var fade=new Fade();fade.To(0,0,600);
            Check(fade.Sample(60)>.95,"fade does not jump at its start");
            Check(Math.Abs(fade.Sample(300)-.5)<.00001,"half-time is half-opacity");
            double half=fade.Value;fade.To(1,300,250);
            Check(Math.Abs(fade.Value-half)<.00001,"reversal preserves current opacity");
            Check(fade.Sample(425)>.5,"reversal progresses toward visible");
            Check(fade.Sample(550)==1,"restore exact endpoint");
            fade.To(0,600,0);Check(fade.Value==0,"reduced motion exact endpoint");
            Check(QuietEngine.IdleMilliseconds(10000,0,10000,0)==10000,"idle threshold");
            Check(QuietEngine.IdleMilliseconds(10000,9999,10000,0)==1,"input resets idle");
            Check(QuietEngine.IdleMilliseconds(10000,0,10000,10000)==0,"foreground change resets idle");
            Check(QuietEngine.IdleMilliseconds(5,uint.MaxValue,100,0)==6,"tick rollover");

            using var window=new Form { ShowInTaskbar=false,Text="留景隔离测试",Size=new Size(120,80),Location=new Point(-2000,-2000),StartPosition=FormStartPosition.Manual };
            using var child=new ListView { Dock=DockStyle.Fill };
            window.Controls.Add(child);window.Show();Application.DoEvents();
            var original=WindowState.Capture(window.Handle);
            int childStyle=Native.GetWindowLong(child.Handle,-20);
            try {
                original.Prepare();
                int preparedStyle=Native.GetWindowLong(window.Handle,-20);
                original.Apply(.5);
                Check(Native.GetLayeredWindowAttributes(window.Handle,out _,out byte alpha,out _) && alpha==128,"half opacity reaches compositor");
                original.Apply(0);
                Check(Native.IsWindowVisible(window.Handle),"zero opacity never hides the window");
                Check(Native.GetLayeredWindowAttributes(window.Handle,out _,out alpha,out _) && alpha==0,"zero opacity reaches compositor");
                original.Apply(1);
                Check(Native.GetWindowLong(window.Handle,-20)==preparedStyle,"restore keeps compositor style");
                for(int i=0;i<100;i++) { original.Apply(0);original.Apply(1); }
                Check(Native.GetWindowLong(window.Handle,-20)==preparedStyle && Native.IsWindowVisible(window.Handle),"100 cycles keep visibility and style stable");
                Check(Native.GetWindowLong(child.Handle,-20)==childStyle,"parent fade never modifies child list style");
            } finally { original.Restore(); }
            Check(Native.GetWindowLong(window.Handle,-20)==original.Style,"exit restores original style");
            Native.SetWindowLong(window.Handle,-20,original.Style | Native.Layered);
            Native.SetLayeredWindowAttributes(window.Handle,0,173,2);
            var layered=WindowState.Capture(window.Handle);
            try { layered.Prepare();layered.Apply(0);layered.Apply(1); }
            finally { layered.Restore(); }
            Check(Native.GetLayeredWindowAttributes(window.Handle,out _,out byte restored,out _) && restored==173,"pre-existing Wallpaper Engine alpha preserved");
            Native.SetWindowLong(window.Handle,-20,original.Style);
            var saved=WindowState.Capture(window.Handle);
            using(var guard=new RecoveryGuard()) {
                guard.Save([saved]);saved.Prepare();saved.Apply(0);
                Check(Native.IsWindowVisible(window.Handle),"guardian covers alpha-zero visible window");
            }
            Check(Native.IsWindowVisible(window.Handle) && Native.GetWindowLong(window.Handle,-20)==saved.Style,"guardian restores original compositor state");
            window.Hide();
            var hidden=WindowState.Capture(window.Handle);hidden.Prepare();hidden.Apply(0);hidden.Restore(true);
            Check(!Native.IsWindowVisible(window.Handle),"pre-existing hidden window preserved");
            Check(Native.LastInput()!=0,"live input timing available");
            foreach(var target in Native.Targets(true,false))
                Check(Native.Class(target)=="SHELLDLL_DefView","desktop target is the composition parent");
            Console.WriteLine("SUCCESS: "+count+" checks. Desktop windows were only read, not changed.");
            return 0;
        } catch(Exception ex) { Console.WriteLine(ex);return 1; }
    }
}
