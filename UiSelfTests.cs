namespace WallpaperQuiet;

internal static class UiSelfTests
{
    sealed class MemoryStartupStore:IStartupStore
    {
        public string? Command;
        public string? Read()=>Command;
        public void Write(string command)=>Command=command;
        public void Remove()=>Command=null;
    }
    static int count;
    static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);count++;}
    public static int Run()
    {
        try{
            var store=new MemoryStartupStore();
            var registration=new StartupRegistration(@"C:\Program Files\留景\WallpaperQuiet.exe",store);
            Check(!registration.Enabled,"startup initially absent");
            registration.SetEnabled(true);
            Check(store.Command=="\"C:\\Program Files\\留景\\WallpaperQuiet.exe\" --startup","quoted unicode startup command");
            Check(registration.Enabled,"startup enabled state");
            registration.SetEnabled(true);Check(registration.Enabled,"enabling startup is idempotent");
            registration.SetEnabled(false);Check(store.Command==null&&!registration.Enabled,"disable removes startup entry");
            bool rejected=false;try{_ =new StartupRegistration("relative.exe",store);}catch(ArgumentException){rejected=true;}
            Check(rejected,"relative executable path rejected");
            var taskXml=System.Xml.Linq.XDocument.Parse(ScheduledStartupStore.Definition(@"C:\Tools & Apps\留景\WallpaperQuiet.exe","S-1-5-21-123"));
            System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
            var taskRoot=taskXml.Root!;
            Check(taskRoot.Element(ns+"Triggers")!.Element(ns+"LogonTrigger")!.Element(ns+"UserId")!.Value=="S-1-5-21-123","login trigger is scoped to current user");
            Check(taskRoot.Element(ns+"Triggers")!.Element(ns+"LogonTrigger")!.Element(ns+"Delay")!.Value=="PT10S","login waits ten seconds for desktop");
            var principal=taskRoot.Element(ns+"Principals")!.Element(ns+"Principal")!;
            Check(principal.Element(ns+"LogonType")!.Value=="InteractiveToken"&&principal.Element(ns+"RunLevel")!.Value=="LeastPrivilege","startup needs neither password nor elevation");
            var taskSettings=taskRoot.Element(ns+"Settings")!;
            Check(taskSettings.Element(ns+"DisallowStartIfOnBatteries")!.Value=="false"&&taskSettings.Element(ns+"StopIfGoingOnBatteries")!.Value=="false","battery use does not prevent or stop startup");
            Check(taskSettings.Element(ns+"ExecutionTimeLimit")!.Value=="PT0S","background utility has no scheduler time limit");
            var action=taskRoot.Element(ns+"Actions")!.Element(ns+"Exec")!;
            Check(action.Element(ns+"Command")!.Value==@"C:\Tools & Apps\留景\WallpaperQuiet.exe"&&action.Element(ns+"Arguments")!.Value=="--startup","XML preserves Unicode and ampersands in startup path");
            Check(action.Element(ns+"WorkingDirectory")!.Value==@"C:\Tools & Apps\留景","startup working directory is explicit");
            taskRoot.Element(ns+"Triggers")!.Element(ns+"LogonTrigger")!.Element(ns+"Enabled")!.Remove();
            Check(ScheduledStartupStore.ReadCommand(taskXml.ToString())==new StartupRegistration(action.Element(ns+"Command")!.Value,store).Command,"Windows-normalized task XML with omitted default Enabled still registers");
            taskRoot.Element(ns+"Triggers")!.Element(ns+"LogonTrigger")!.Add(new System.Xml.Linq.XElement(ns+"Enabled","false"));
            Check(ScheduledStartupStore.ReadCommand(taskXml.ToString())==null,"explicitly disabled logon trigger reports startup off");
            using var icon=AppBrand.LoadIcon();Check(icon.Width>0&&icon.Height>0,"user-supplied icon embedded");
            using var toggle=new ToggleSwitch{AccessibleName="Test toggle"};
            toggle.Checked=true;Check(toggle.Checked&&toggle.AccessibleRole==AccessibleRole.CheckButton,"toggle retains checkbox semantics");
            toggle.Checked=false;Check(!toggle.Checked,"toggle can reverse state");
            using var stepper=new NumberStepper{Minimum=1,Maximum=3600};
            stepper.Value=0;Check(stepper.Value==1,"stepper clamps lower bound");
            stepper.Value=9000;Check(stepper.Value==3600,"stepper clamps upper bound");
            stepper.Value=42;Check(stepper.Value==42,"stepper retains editable value");
            using var tween=new UiTween(()=>{});
            tween.Snap(.4);tween.To(1,160,true);Check(tween.Value==1,"keyboard/reduced-motion transition is immediate");
            tween.Snap(.4);tween.To(0,160);tween.To(1,160);
            Check(tween.Value>.35&&tween.Value<.45,"rapid animation reversal retains current value");
            Check(UiTheme.Ease(0)==0&&UiTheme.Ease(1)==1,"UI easing exact endpoints");
            var defaults=new Settings();
            Check(defaults.StartupAutoStart,"startup defaults to automatic quiet mode");
            Check(defaults.IdleSeconds==5&&defaults.FadeMs==600&&defaults.Icons&&defaults.Taskbar&&!defaults.DesktopOnly&&defaults.Motion,
                "new installs use the tested five-second quiet configuration");
            Check(Program.OpenSignalName!=Program.SignalName&&Program.OpenSignalName!=Program.QuitSignalName,
                "opening settings has a separate signal and cannot pause quiet mode");
            var monitor=new Rectangle(0,0,1920,1080);
            Check(FullscreenPolicy.Blocks(false,true,new Rectangle(0,0,1920,1032),monitor),"maximized browser with reserved taskbar area blocks fade");
            Check(FullscreenPolicy.Blocks(false,false,monitor,monitor),"borderless full-screen blocks fade");
            Check(!FullscreenPolicy.Blocks(false,false,new Rectangle(20,20,1000,700),monitor),"normal browser window does not force full-screen protection");
            Check(!FullscreenPolicy.Blocks(true,true,monitor,monitor),"desktop shell is not treated as a full-screen app");
            var left=new Rectangle(-1920,0,1920,1080);
            Check(FullscreenPolicy.Blocks(false,false,left,left),"negative-coordinate secondary monitor");
            Check(!FullscreenPolicy.Blocks(false,false,new Rectangle(0,0,960,1080),monitor),"half-screen snapped window is not full-screen");
            Check(!FullscreenPolicy.Blocks(false,false,Rectangle.Empty,monitor),"invalid window bounds ignored");
            var chat=new FullscreenPolicy.WindowInfo(1,"ChatWindow",true,false,false,false,new Rectangle(400,100,800,850),monitor);
            var browser=new FullscreenPolicy.WindowInfo(2,"BrowserWindow",true,false,false,true,new Rectangle(0,0,1920,1032),monitor);
            Check(FullscreenPolicy.AnyWindowBlocksQuiet([chat,browser]),"REGRESSION: normal foreground chat over maximized background browser blocks fade");
            Check(FullscreenPolicy.AnyWindowBlocksQuiet([browser,chat]),"window stacking order does not change protection");
            var borderless=browser with { Maximized=false,Bounds=monitor };
            Check(FullscreenPolicy.AnyWindowBlocksQuiet([chat,borderless]),"background borderless full-screen app blocks fade");
            Check(FullscreenPolicy.AnyWindowBlocksQuiet([chat,borderless with {Bounds=left,Monitor=left}]),"background full-screen app on another monitor blocks fade globally");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,browser with {Minimized=true}]),"minimized browser no longer blocks even if maximized flag remains set");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,browser with {Visible=false}]),"hidden maximized browser does not block");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,browser with {Cloaked=true}]),"cloaked app on another virtual desktop does not block");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,borderless with {ClassName="WPEDesktopDX11Window"}]),"live wallpaper surface never blocks quiet mode");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,borderless with {ClassName="WorkerW"}]),"desktop host never blocks quiet mode");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,browser with {Maximized=false,Bounds=new Rectangle(0,0,1000,700)}]),"restoring the final maximized window releases protection");
            Check(FullscreenPolicy.AnyWindowBlocksQuiet([chat,browser with {Minimized=true},borderless]),"minimizing one blocker does not release another full-screen app");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([]),"closing all windows releases protection");
            Check(!FullscreenPolicy.AnyWindowBlocksQuiet([chat,borderless with {ClickThroughOverlay=true}]),"transparent click-through cursor overlay does not block desktop quiet mode");
            Check(FullscreenPolicy.AnyWindowBlocksQuiet([browser,borderless with {ClickThroughOverlay=true}]),"ignoring overlays still protects a maximized app underneath");
            CoverageTests();
            Console.WriteLine("SUCCESS: "+count+" UI/startup/fullscreen checks. No startup registry entries modified.");
            return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }

    sealed class CoverageWindow:Form
    {
        protected override bool ShowWithoutActivation=>true;
        public CoverageWindow(Rectangle bounds)
        {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual;Bounds=bounds;
        }
    }

    public static int RunNativeSplitScreen()
    {
        try{
            var screen=Screen.PrimaryScreen??Screen.AllScreens[0];
            var work=screen.WorkingArea;
            var display=new FullscreenPolicy.DisplayInfo(screen.Bounds,work);
            int middle=work.Left+work.Width/2;
            using var left=new CoverageWindow(Rectangle.FromLTRB(work.Left,work.Top,middle,work.Bottom));
            using var right=new CoverageWindow(Rectangle.FromLTRB(middle,work.Top,work.Right,work.Bottom));
            using var lower=new CoverageWindow(Rectangle.FromLTRB(middle,work.Top+work.Height/2,work.Right,work.Bottom));
            left.Show();right.Show();Application.DoEvents();
            var handles=new HashSet<nint>{left.Handle,right.Handle,lower.Handle};
            bool Covered()=>FullscreenPolicy.AnyWindowBlocksQuiet(FullscreenPolicy.ReadWindows().Where(w=>handles.Contains(w.Handle)),[display]);
            Check(Covered(),"native borderless half-screen windows jointly protect working area");
            right.Hide();Application.DoEvents();
            Check(!Covered(),"hiding a native split window releases protection");
            right.Bounds=Rectangle.FromLTRB(middle,work.Top,work.Right,work.Top+work.Height/2);
            right.Show();Application.DoEvents();
            Check(!Covered(),"native split with uncovered quadrant is not full coverage");
            lower.Show();Application.DoEvents();
            Check(Covered(),"three native windows complete coverage without maximization");
            Console.WriteLine("SUCCESS: native split-screen validation; only test windows were opened and disposed.");
            return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }

    static void CoverageTests()
    {
        var bounds=new Rectangle(0,0,1920,1080);
        var work=new Rectangle(0,0,1920,1032);
        var display=new FullscreenPolicy.DisplayInfo(bounds,work);
        FullscreenPolicy.WindowInfo App(Rectangle r,int id=1) => new(id,"TestApp",true,false,false,false,r,bounds);
        bool Protected(params FullscreenPolicy.WindowInfo[] windows) => FullscreenPolicy.AnyWindowBlocksQuiet(windows,[display]);
        var left=App(new Rectangle(0,0,960,1032));
        var right=App(new Rectangle(960,0,960,1032),2);
        Check(Protected(left,right),"REGRESSION: two snapped halves cover working area despite reserved taskbar space");
        Check(Protected(right,left),"split-screen coverage is independent of foreground and stacking order");
        Check(Protected(App(new Rectangle(0,0,1920,516)),App(new Rectangle(0,516,1920,516))),"top and bottom split protects desktop");
        Check(Protected(left,App(new Rectangle(960,0,960,516)),App(new Rectangle(960,516,960,516))),"three-window snap layout protects desktop");
        Check(Protected(App(new Rectangle(0,0,960,516)),App(new Rectangle(960,0,960,516)),App(new Rectangle(0,516,960,516)),App(new Rectangle(960,516,960,516))),"four quadrants protect desktop");
        Check(Protected(App(work)),"manually sized app filling work area protects without maximized flag");
        Check(!Protected(left),"one snapped half leaves wallpaper available");
        Check(!Protected(left,left,left),"overlapping copies of one half never add up to full coverage");
        Check(!Protected(App(new Rectangle(0,0,960,516)),App(new Rectangle(960,516,960,516))),"diagonal windows leave holes despite full bounding box");
        Check(!Protected(left,App(new Rectangle(972,0,948,1032))),"visible gap between split windows does not count as full coverage");
        Check(!Protected(left,right with {Minimized=true}),"minimizing one split window releases protection");
        Check(!Protected(left,right with {Visible=false}),"hidden split window does not fill missing half");
        Check(!Protected(left,right with {Cloaked=true}),"another virtual desktop cannot complete split coverage");
        Check(!Protected(left,right with {ClickThroughOverlay=true}),"click-through overlay cannot complete split coverage");
        Check(!Protected(left,right with {ClassName="WPEDesktopDX11Window"}),"wallpaper cannot complete split coverage");
        Check(!Protected(left,right with {ClassName="Shell_TrayWnd"}),"taskbar is not counted as an application");
        Check(Protected(App(new Rectangle(1,1,958,1030)),App(new Rectangle(961,1,958,1030))),"minor frame rounding does not break split-screen protection");
        var second=new FullscreenPolicy.DisplayInfo(new Rectangle(-1920,0,1920,1080),new Rectangle(-1920,0,1920,1032));
        Check(FullscreenPolicy.AnyWindowBlocksQuiet([App(new Rectangle(-1920,0,960,1032)),App(new Rectangle(-960,0,960,1032))],[display,second]),"split layout on negative-coordinate secondary display protects globally");
        Check(!FullscreenPolicy.AnyWindowBlocksQuiet([left,App(new Rectangle(-960,0,960,1032))],[display,second]),"halves on different screens cannot be added together");
        Check(FullscreenPolicy.AnyWindowBlocksQuiet([App(new Rectangle(-1920,0,2500,1032)),App(new Rectangle(580,0,1340,1032))],[display]),"window spanning screens is clipped into each display regardless of assigned monitor");
        var sideTaskbar=new FullscreenPolicy.DisplayInfo(bounds,new Rectangle(48,0,1872,1080));
        Check(FullscreenPolicy.AnyWindowBlocksQuiet([App(new Rectangle(48,0,936,1080)),App(new Rectangle(984,0,936,1080))],[sideTaskbar]),"vertical taskbar is excluded from required coverage");
        Check(FullscreenPolicy.AnyWindowBlocksQuiet([App(new Rectangle(0,0,960,1080)),App(new Rectangle(960,0,960,1080))],[new(bounds,bounds)]),"auto-hidden taskbar layout uses full monitor area");
        Check(!FullscreenPolicy.AreaCovered([Rectangle.Empty],work)&&!FullscreenPolicy.AreaCovered([work],Rectangle.Empty),"invalid coverage rectangles are ignored");

        // An independent pixel-grid oracle catches holes and overlapping intervals.
        var random=new Random(132);
        var grid=new Rectangle(0,0,16,12);
        bool agrees=true;
        for(int sample=0;sample<300;sample++){
            var rects=Enumerable.Range(0,random.Next(0,9)).Select(_=>new Rectangle(random.Next(-4,20),random.Next(-4,16),random.Next(1,20),random.Next(1,16))).ToArray();
            bool raster=true;
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)
                raster &= rects.Any(r=>x>=r.Left-2&&x<r.Right+2&&y>=r.Top-2&&y<r.Bottom+2);
            agrees &= raster==FullscreenPolicy.AreaCovered(rects,grid);
        }
        Check(agrees,"coverage matches independent pixel oracle in 300 overlapping and gapped layouts");
    }
}

