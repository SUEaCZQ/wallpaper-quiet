using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace WallpaperQuiet;

internal interface IStartupStore
{
    string? Read();
    void Write(string command);
    void Remove();
}
internal sealed class RegistryStartupStore : IStartupStore
{
    const string SubKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName="WallpaperQuiet";
    public string? Read(){using var key=Registry.CurrentUser.OpenSubKey(SubKey);return key?.GetValue(ValueName) as string;}
    public void Write(string command){using var key=Registry.CurrentUser.CreateSubKey(SubKey);key.SetValue(ValueName,command,RegistryValueKind.String);}
    public void Remove(){using var key=Registry.CurrentUser.OpenSubKey(SubKey,true);key?.DeleteValue(ValueName,false);}
}

internal sealed class ScheduledStartupStore(string executable) : IStartupStore
{
    static string UserSid=>WindowsIdentity.GetCurrent().User!.Value;
    public static string TaskName=>"WallpaperQuiet-"+UserSid;
    static readonly XNamespace Ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
    internal static string Definition(string executable,string sid)
    {
        XElement E(string name,params object[] children)=>new(Ns+name,children);
        return new XDocument(E("Task",new XAttribute("version","1.2"),
            E("RegistrationInfo",E("Description","留景：登录后收起到托盘，并按设置自动开启静享。")),
            E("Triggers",E("LogonTrigger",E("Enabled","true"),E("UserId",sid),E("Delay","PT10S"))),
            E("Principals",E("Principal",new XAttribute("id","CurrentUser"),
                E("UserId",sid),E("LogonType","InteractiveToken"),E("RunLevel","LeastPrivilege"))),
            E("Settings",E("MultipleInstancesPolicy","IgnoreNew"),E("DisallowStartIfOnBatteries","false"),
                E("StopIfGoingOnBatteries","false"),E("AllowHardTerminate","false"),E("StartWhenAvailable","true"),
                E("AllowStartOnDemand","true"),E("Enabled","true"),E("Hidden","false"),
                E("ExecutionTimeLimit","PT0S"),E("RestartOnFailure",E("Interval","PT1M"),E("Count","3"))),
            E("Actions",new XAttribute("Context","CurrentUser"),E("Exec",
                E("Command",executable),E("Arguments","--startup"),E("WorkingDirectory",Path.GetDirectoryName(executable)!)))
        )).ToString();
    }
    sealed class Scheduler : IDisposable
    {
        readonly List<object> owned=[];
        public dynamic Service{get;}
        public dynamic Folder{get;}
        dynamic Own(object value){owned.Add(value);return value;}
        public Scheduler()
        {
            Service=Own(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!);
            try{Service.Connect();Folder=Own(Service.GetFolder(@"\"));
            }catch{Dispose();throw;}
        }
        public dynamic? Find()
        {
            try{return Own(Folder.GetTask(TaskName));}
            catch(Exception ex) when(ex.HResult==unchecked((int)0x80070002)){return null;}
        }
        public void Register(string xml)=>Own(Folder.RegisterTask(TaskName,xml,6,UserSid,null,3,null));
        public void Dispose(){for(int i=owned.Count-1;i>=0;i--)Marshal.ReleaseComObject(owned[i]);owned.Clear();}
    }
    public string? Read()
    {
        using var scheduler=new Scheduler();
        object? found=scheduler.Find();
        if(found is null)return null;
        dynamic task=found;
        if(!(bool)task.Enabled)return null;
        return ReadCommand((string)task.Xml);
    }
    internal static string? ReadCommand(string taskXml)
    {
        var xml=XDocument.Parse(taskXml);
        var trigger=xml.Root?.Element(Ns+"Triggers")?.Element(Ns+"LogonTrigger");
        // Task Scheduler omits default-valued Enabled=true when serializing a task.
        if(trigger==null || trigger.Element(Ns+"Enabled")?.Value is "false" or "0")return null;
        var action=xml.Root?.Element(Ns+"Actions")?.Element(Ns+"Exec");
        return action==null?null:"\""+action.Element(Ns+"Command")?.Value+"\" "+action.Element(Ns+"Arguments")?.Value;
    }
    public void Write(string command)
    {
        if(command!="\""+executable+"\" --startup")throw new ArgumentException("启动命令不匹配。");
        using var scheduler=new Scheduler();
        scheduler.Register(Definition(executable,UserSid));
        if(!string.Equals(Read(),command,StringComparison.OrdinalIgnoreCase))throw new IOException("Windows 未确认登录启动任务。");
        new RegistryStartupStore().Remove(); // Migrate only after the replacement is verified.
    }
    public void Remove()
    {
        using var scheduler=new Scheduler();
        if(scheduler.Find()!=null)scheduler.Folder.DeleteTask(TaskName,0);
        new RegistryStartupStore().Remove();
    }
}
internal sealed class StartupRegistration
{
    readonly IStartupStore store;
    public string Command{get;}
    public StartupRegistration(string executable,IStartupStore? store=null)
    {
        if(!Path.IsPathFullyQualified(executable)||executable.Contains('"'))throw new ArgumentException("启动路径无效。");
        Command="\""+executable+"\" --startup";
        this.store=store??new ScheduledStartupStore(executable);
    }
    public bool Enabled=>string.Equals(store.Read(),Command,StringComparison.OrdinalIgnoreCase);
    public string? RegisteredCommand=>store.Read();
    public void SetEnabled(bool enabled)
    {
        if(enabled)store.Write(Command);else store.Remove();
        if(Enabled!=enabled)throw new IOException("未能保存开机自启动设置。");
    }
}

internal static class StartupLog
{
    static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WallpaperQuiet");
    public static void Write(string message)
    {
        try{
            Directory.CreateDirectory(Folder);
            string path=Path.Combine(Folder,"startup.log");
            if(File.Exists(path)&&new FileInfo(path).Length>256*1024)File.Move(path,path+".previous",true);
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        }catch{ /* Diagnostic logging must never prevent startup or desktop recovery. */ }
    }
}
