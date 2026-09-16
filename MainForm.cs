namespace WallpaperQuiet;

internal sealed class MainForm : Form
{
    readonly Settings settings;
    readonly QuietEngine engine;
    readonly StartupRegistration startup;
    readonly NotifyIcon tray;
    readonly Icon appIcon;
    readonly Label stateText=new(),stateHint=new(),metric=new(),dot=new(),footer=new();
    readonly NumberStepper idle=new(){Minimum=1,Maximum=3600,Step=1,Unit="秒",AccessibleName="空闲等待秒数"};
    readonly NumberStepper speed=new(){Minimum=100,Maximum=2000,Step=100,Unit="ms",AccessibleName="渐隐时长毫秒"};
    readonly ToggleSwitch icons=new(){AccessibleName="渐隐桌面图标"};
    readonly ToggleSwitch taskbar=new(){AccessibleName="渐隐任务栏"};
    readonly ToggleSwitch desktop=new(){AccessibleName="仅在桌面时运行"};
    readonly ToggleSwitch motion=new(){AccessibleName="启用渐隐渐现"};
    readonly ToggleSwitch autoRun=new(){AccessibleName="开机自启动"};
    readonly SoftButton start=new(){Primary=true,Text="开始静享"};
    readonly System.Windows.Forms.Timer signals=new(){Interval=200};
    readonly System.Windows.Forms.Timer bootTimer=new(){Interval=2000};
    readonly EventWaitHandle restoreSignal,openSignal,quitSignal;
    readonly UiTween appearance;
    readonly bool preview;
    bool hiddenLaunch,closing,cleaned,hotkey,loading=true;
    int bootAttempts;
    public bool IsQuietEnabled=>engine.Enabled;
    public bool IsHiddenLaunch=>hiddenLaunch;

    public MainForm(bool startupLaunch=false,bool preview=false)
    {
        this.preview=preview;hiddenLaunch=startupLaunch;
        settings=Settings.Load();engine=new(settings);
        startup=new StartupRegistration(Environment.ProcessPath!);
        appIcon=AppBrand.LoadIcon();
        Text="留景";Icon=appIcon;
        Font=new Font("Microsoft YaHei UI",10);
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(640,706);BackColor=UiTheme.Canvas;ForeColor=UiTheme.Ink;
        FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
        StartPosition=FormStartPosition.CenterScreen;
        appearance=new UiTween(()=>{if(!IsDisposed&&IsHandleCreated)Opacity=appearance!.Value;});
        appearance.Snap(1);
        idle.Value=settings.IdleSeconds;speed.Value=settings.FadeMs;
        icons.Checked=settings.Icons;taskbar.Checked=settings.Taskbar;
        desktop.Checked=settings.DesktopOnly;motion.Checked=settings.Motion;
        autoRun.Checked=startup.Enabled;

        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=UiTheme.Canvas};
        Controls.Add(scroll);
        var root=new TableLayoutPanel{Location=new Point(26,22),Size=new Size(588,644),ColumnCount=1,RowCount=11,BackColor=UiTheme.Canvas};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int h in new[]{64,64,18,22,112,18,22,224,20,44,36})root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        scroll.Controls.Add(root);
        scroll.Resize+=(_,_)=>root.Width=Math.Max(400,scroll.ClientSize.Width-(int)(52*DeviceDpi/96f)-(scroll.VerticalScroll.Visible?SystemInformation.VerticalScrollBarWidth:0));

        var header=new Panel{Size=new Size(588,64),Dock=DockStyle.Fill,Margin=Padding.Empty};
        header.Controls.Add(LabelAt("留景",0,-1,220,38,24,UiTheme.Ink,true));
        header.Controls.Add(LabelAt("把桌面留给壁纸。",2,41,360,20,9,UiTheme.Muted));
        var version=LabelAt("设置",500,13,86,24,9,UiTheme.Muted);
        version.TextAlign=ContentAlignment.MiddleRight;version.Anchor=AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(version);
        root.Controls.Add(header,0,0);

        var stateCard=new CardPanel{Size=new Size(588,64),Dock=DockStyle.Fill,Margin=Padding.Empty};
        ConfigureLabel(dot,"●",17,16,18,28,10,UiTheme.Muted,true);
        ConfigureLabel(stateText,"已暂停",38,11,320,25,11,UiTheme.Ink,true);
        ConfigureLabel(stateHint,"轻移鼠标或按键，即可恢复桌面。",38,35,385,20,8.5f,UiTheme.Muted);
        ConfigureLabel(metric,settings.IdleSeconds+" 秒后渐隐",418,17,148,28,10,UiTheme.Muted);
        metric.TextAlign=ContentAlignment.MiddleRight;metric.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        stateCard.Controls.AddRange([dot,stateText,stateHint,metric]);root.Controls.Add(stateCard,0,1);
        root.Controls.Add(Section("时间与过渡"),0,3);

        var timing=new CardPanel{Size=new Size(588,112),Dock=DockStyle.Fill,Margin=Padding.Empty};
        timing.Controls.Add(SettingRow("空闲等待","停止操作后，开始收起桌面。",idle,0,56,588));
        timing.Controls.Add(SettingRow("渐隐时长","渐现为 0.25 秒。",speed,56,56,588));
        AddDivider(timing,56);root.Controls.Add(timing,0,4);
        root.Controls.Add(Section("显示与启动"),0,6);

        var options=new CardPanel{Size=new Size(588,224),Dock=DockStyle.Fill,Margin=Padding.Empty};
        var choices=new Panel{Location=Point.Empty,Size=new Size(588,56),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,BackColor=UiTheme.Surface};
        var left=new Panel{Location=new Point(1,7),Size=new Size(290,42),BackColor=UiTheme.Surface};
        var right=new Panel{Location=new Point(293,7),Size=new Size(290,42),BackColor=UiTheme.Surface,Anchor=AnchorStyles.Top|AnchorStyles.Right};
        left.Controls.Add(LabelAt("桌面图标",17,7,170,28,10,UiTheme.Ink));
        right.Controls.Add(LabelAt("任务栏",17,7,164,28,10,UiTheme.Ink));
        icons.Location=new Point(222,4);taskbar.Location=new Point(222,4);
        left.Controls.Add(icons);right.Controls.Add(taskbar);choices.Controls.AddRange([left,right]);
        choices.Controls.Add(new Panel{BackColor=UiTheme.Line,Location=new Point(293,17),Size=new Size(1,23)});
        options.Controls.Add(choices);
        options.Controls.Add(SettingRow("仅在桌面时运行","使用其他应用时，保持正常显示。",desktop,56,56,588));
        options.Controls.Add(SettingRow("渐隐与渐现","平滑过渡，动态壁纸持续播放。",motion,112,56,588));
        options.Controls.Add(SettingRow("开机自启动",settings.StartupAutoStart?"登录后收起到托盘，自动开启静享。":"登录后收起到托盘，等待手动开启。",autoRun,168,56,588));
        foreach(int y in new[]{56,112,168})AddDivider(options,y);
        root.Controls.Add(options,0,7);
        root.Controls.Add(new Label{Text="任意应用全屏或最大化时，保持桌面显示。",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,8),ForeColor=UiTheme.Muted,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(1,2,0,0)},0,8);

        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,ColumnCount=2};
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,168));
        start.Dock=DockStyle.Fill;start.Margin=new Padding(0,0,10,0);start.Font=new Font(Font,FontStyle.Bold);
        start.Click+=(_,_)=>StartQuiet();
        var pause=new SoftButton{Text="恢复并暂停",Dock=DockStyle.Fill,Margin=Padding.Empty};
        pause.Click+=(_,_)=>engine.Pause();
        actions.Controls.Add(start,0,0);actions.Controls.Add(pause,1,0);root.Controls.Add(actions,0,9);
        var foot=new Panel{Size=new Size(588,36),Dock=DockStyle.Fill,Margin=Padding.Empty};
        ConfigureLabel(footer,"Ctrl + Alt + F12  恢复并暂停",0,8,470,25,8,UiTheme.Muted);
        var exit=new SoftButton{Text="退出应用",Quiet=true,Size=new Size(80,28),Location=new Point(508,6),Anchor=AnchorStyles.Top|AnchorStyles.Right,Font=new Font(Font.FontFamily,8.5f)};
        exit.Click+=(_,_)=>Exit();foot.Controls.AddRange([footer,exit]);root.Controls.Add(foot,0,10);

        var menu=new ContextMenuStrip{Font=Font};
        menu.Items.Add("打开设置",null,(_,_)=>OpenSettings());
        menu.Items.Add("开始 / 继续静享",null,(_,_)=>StartQuiet());
        menu.Items.Add("恢复并暂停",null,(_,_)=>engine.Pause());
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出并恢复桌面",null,(_,_)=>Exit());
        tray=new NotifyIcon{Icon=appIcon,Text="留景",Visible=true,ContextMenuStrip=menu};
        tray.DoubleClick+=(_,_)=>OpenSettings();
        restoreSignal=new EventWaitHandle(false,EventResetMode.AutoReset,Program.SignalName);
        openSignal=new EventWaitHandle(false,EventResetMode.AutoReset,Program.OpenSignalName);
        quitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,Program.QuitSignalName);
        signals.Tick+=(_,_)=>{
            if(quitSignal.WaitOne(0)){Exit();return;}
            if(restoreSignal.WaitOne(0)){engine.Pause();OpenSettings();}
            if(openSignal.WaitOne(0)){OpenSettings();}
        };
        signals.Start();
        engine.Changed+=RefreshStatus;
        engine.Failed+=message=>tray.ShowBalloonTip(4000,"留景已暂停",message,ToolTipIcon.Warning);
        autoRun.CheckedChanged+=(_,_)=>{
            if(loading||preview)return;
            try{startup.SetEnabled(autoRun.Checked);settings.Save();}
            catch(Exception ex){loading=true;autoRun.Checked=startup.Enabled;loading=false;MessageBox.Show(this,ex.Message,"无法保存开机启动设置");}
        };
        bootTimer.Tick+=(_,_)=>BootAttempt();
        loading=false;RefreshStatus();
        _=Handle;
        hotkey=Native.RegisterHotKey(Handle,1,0x4000|0x1|0x2,0x7B);
        if(!hotkey)footer.Text="快捷键已被占用，可从托盘恢复。";
        if(startupLaunch){StartupLog.Write("Tray initialized.");engine.SettingsOpen=false;if(settings.StartupAutoStart)bootTimer.Start();}
        Shown+=(_,_)=>{
            int maxHeight=Screen.FromControl(this).WorkingArea.Height-48;
            if(Height>maxHeight)Height=maxHeight;
            if(!preview&&UiTheme.Motion){appearance.Snap(.01);appearance.To(1,180);}
        };
        FormClosing+=(_,e)=>{
            if(!closing&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();engine.SettingsOpen=false;}
        };
    }
    protected override void SetVisibleCore(bool value)=>base.SetVisibleCore(hiddenLaunch?false:value);
    static Label LabelAt(string text,int x,int y,int width,int height,float size,Color color,bool bold=false)
    {
        var label=new Label();ConfigureLabel(label,text,x,y,width,height,size,color,bold);return label;
    }
    static void ConfigureLabel(Label label,string text,int x,int y,int width,int height,float size,Color color,bool bold=false)
    {
        label.Text=text;label.SetBounds(x,y,width,height);label.Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);
        label.ForeColor=color;label.BackColor=Color.Transparent;label.TextAlign=ContentAlignment.MiddleLeft;label.AutoEllipsis=true;
    }
    static Label Section(string text)=>new(){Text=text,Dock=DockStyle.Fill,Margin=new Padding(1,0,0,0),Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold),ForeColor=UiTheme.Muted};
    static Panel SettingRow(string title,string hint,Control control,int top,int height,int width)
    {
        var row=new Panel{Location=new Point(1,top+1),Size=new Size(width-2,height-2),Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right,BackColor=UiTheme.Surface};
        row.Controls.Add(LabelAt(title,17,5,310,25,10,UiTheme.Ink));
        row.Controls.Add(LabelAt(hint,17,29,365,19,8,UiTheme.Muted));
        control.Location=new Point(width-control.Width-18,(height-control.Height)/2);
        control.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        row.Controls.Add(control);
        return row;
    }
    static void AddDivider(Control parent,int y)=>parent.Controls.Add(new Panel{BackColor=UiTheme.Line,Location=new Point(18,y),Size=new Size(552,1),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right});
    void BootAttempt()
    {
        if(!hiddenLaunch){bootTimer.Stop();return;}
        try {
            engine.SettingsOpen=false;engine.Enable();bootTimer.Stop();StartupLog.Write("Tray ready; quiet mode enabled.");
        } catch(Exception ex) {
            StartupLog.Write("Desktop retry "+(bootAttempts+1)+": "+ex.Message);
            if(++bootAttempts>=60){
                bootTimer.Stop();tray.ShowBalloonTip(5000,"留景尚未开启","桌面尚未准备好，请从托盘点击“开始 / 继续静享”。",ToolTipIcon.Info);
            }
        }
    }
    void StartQuiet()
    {
        if(!icons.Checked&&!taskbar.Checked){MessageBox.Show(this,"请至少选择桌面图标或任务栏。","选择渐隐对象");return;}
        try{
            bootTimer.Stop();engine.Pause();
            settings.IdleSeconds=idle.Value;settings.FadeMs=speed.Value;
            settings.Icons=icons.Checked;settings.Taskbar=taskbar.Checked;settings.DesktopOnly=desktop.Checked;settings.Motion=motion.Checked;
            settings.Save();engine.SettingsOpen=false;engine.Enable();
            // Keep a manually opened settings window on screen. It is a normal
            // window, so the desktop icons and taskbar can still fade around it.
            // Tray and login launches are already hidden and remain that way.
            appearance.Snap(1);
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"未能开启静享");}
    }
    void OpenSettings()
    {
        bootTimer.Stop();hiddenLaunch=false;
        // Opening the control panel must not cancel an active quiet cycle.
        // Unsaved edits are applied only by the primary button.
        engine.SettingsOpen=false;
        appearance.Snap(1);Show();WindowState=FormWindowState.Normal;Activate();
        RefreshStatus();
    }
    void RefreshStatus()
    {
        stateText.Text=engine.Enabled?"静享已开启":"已暂停";
        if(engine.Status.StartsWith("已停止"))stateText.Text="需要处理";
        dot.ForeColor=engine.Enabled?UiTheme.Green:UiTheme.Muted;
        stateHint.Text=engine.Enabled?engine.Status:"轻移鼠标或按键，即可恢复桌面。";
        metric.Text=engine.Enabled?engine.Remaining+" 秒":settings.IdleSeconds+" 秒后渐隐";
        start.Text=engine.Enabled?"保存并继续":"开始静享";
        tray.Text="留景 · "+(engine.Enabled?"静享已开启":"已暂停");
    }
    protected override void WndProc(ref Message m){if(m.Msg==0x0312&&m.WParam==1){bootTimer.Stop();engine.Pause();}base.WndProc(ref m);}
    public void ValidateLayout()
    {
        void Visit(Control control)
        {
            foreach(Control child in control.Controls)
            {
                if(child is ToggleSwitch or NumberStepper or SoftButton)
                    if(child.Left < -1 || child.Top < -1 || child.Right > control.ClientSize.Width+1 || child.Bottom > control.ClientSize.Height+1)
                        throw new InvalidOperationException("控件超出可见区域："+child.GetType().Name+" "+child.AccessibleName+" "+child.Bounds+" parent "+control.ClientSize);
                Visit(child);
            }
        }
        Visit(this);
    }
    public void Exit(){closing=true;Close();}
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!cleaned){
            cleaned=true;signals.Dispose();bootTimer.Dispose();appearance.Dispose();
            restoreSignal.Dispose();openSignal.Dispose();quitSignal.Dispose();if(hotkey)Native.UnregisterHotKey(Handle,1);
            try{engine.Dispose();}finally{tray.Visible=false;tray.Dispose();appIcon.Dispose();}
        }
        base.Dispose(disposing);
    }
}

