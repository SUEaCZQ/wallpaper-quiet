using System.Diagnostics;

namespace WallpaperQuiet;

internal static class LiveValidation
{
    public static int Run()
    {
        using var mutex=new Mutex(true,@"Local\WallpaperQuiet.Instance."+Environment.UserName,out bool fresh);
        if(!fresh) { Console.WriteLine("Close the running app before live validation.");return 2; }
        var states=new List<WindowState>();
        using var guard=new RecoveryGuard();
        try {
            states=Native.Targets(true,true).Where(Native.IsWindowVisible).Select(WindowState.Capture).ToList();
            if(states.Count==0) throw new Exception("No desktop targets.");
            guard.Save(states);
            foreach(var s in states) s.Prepare();
            var styles=states.Select(s=>Native.GetWindowLong(s.Window,-20)).ToArray();
            var fade=new Fade();
            var time=Stopwatch.StartNew();
            int frames=0;var levels=new HashSet<byte>();
            fade.To(0,0,600);
            while(time.ElapsedMilliseconds<600) {
                double value=fade.Sample(time.ElapsedMilliseconds);
                foreach(var s in states) s.Apply(value);
                levels.Add((byte)Math.Round(value*255));frames++;
                Thread.Sleep(15);
            }
            foreach(var s in states) s.Apply(0);
            for(int i=0;i<states.Count;i++) {
                if(!Native.IsWindowVisible(states[i].Window)) throw new Exception("Visibility changed at zero.");
                if(Native.GetWindowLong(states[i].Window,-20)!=styles[i]) throw new Exception("Style changed at zero.");
                if(!Native.GetLayeredWindowAttributes(states[i].Window,out _,out byte alpha,out _) || alpha!=0)
                    throw new Exception("Zero alpha not applied.");
                Console.WriteLine("PASS: "+states[i].ClassName+" alpha=0, still visible, style stable");
            }
            fade.To(1,600,250);time.Restart();
            while(time.ElapsedMilliseconds<250) {
                foreach(var s in states) s.Apply(fade.Sample(600+time.ElapsedMilliseconds));
                frames++;Thread.Sleep(15);
            }
            foreach(var s in states) s.Apply(1);
            for(int i=0;i<states.Count;i++)
                if(!Native.IsWindowVisible(states[i].Window) || Native.GetWindowLong(states[i].Window,-20)!=styles[i])
                    throw new Exception("Restore toggled window visibility/style.");
            Console.WriteLine("PASS: live fade frames="+frames+", distinct fade-out alpha levels="+levels.Count);
            if(levels.Count<15) throw new Exception("Insufficient intermediate frames.");
            Console.WriteLine("PASS: restored opacity without hiding/showing/recreating the shell windows");
            return 0;
        } catch(Exception ex) { Console.WriteLine(ex);return 1; }
        finally {
            foreach(var s in states) try { s.Restore(); } catch { }
            mutex.ReleaseMutex();
        }
    }
}

