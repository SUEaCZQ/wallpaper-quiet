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
            Console.WriteLine("SUCCESS: "+count+" UI/startup/fullscreen checks. No startup registry entries modified.");
            return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}

