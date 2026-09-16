using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace WallpaperQuiet;

internal static class UiTheme
{
    public static readonly Color Canvas=Color.FromArgb(246,246,244);
    public static readonly Color Surface=Color.White;
    public static readonly Color Ink=Color.FromArgb(30,31,32);
    public static readonly Color Muted=Color.FromArgb(112,115,118);
    public static readonly Color Line=Color.FromArgb(230,231,228);
    public static readonly Color Green=Color.FromArgb(39,112,79);
    public static bool AllowMotion { get; set; }=true;
    public static bool Motion => AllowMotion && (!Native.SystemParametersInfo(0x1042,0,out bool enabled,0) || enabled);
    public static GraphicsPath Round(RectangleF r,float radius)
    {
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        var path=new GraphicsPath();
        path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        path.CloseFigure();return path;
    }
    public static Color Mix(Color a,Color b,double t)=>Color.FromArgb(
        (int)Math.Round(a.R+(b.R-a.R)*t),(int)Math.Round(a.G+(b.G-a.G)*t),(int)Math.Round(a.B+(b.B-a.B)*t));
    public static double Ease(double t)
    {
        if(t<=0)return 0;if(t>=1)return 1;
        double lo=0,hi=1,u=t;
        for(int i=0;i<18;i++){u=(lo+hi)/2;double x=3*(1-u)*(1-u)*u*.23+3*(1-u)*u*u*.32+u*u*u;if(x<t)lo=u;else hi=u;}
        return 1-Math.Pow(1-u,3);
    }
}

internal sealed class UiTween : IDisposable
{
    readonly System.Windows.Forms.Timer timer=new(){Interval=15};
    readonly Action redraw;
    double start,end;
    long began;
    int duration;
    public double Value {get;private set;}
    public UiTween(Action redraw){this.redraw=redraw;timer.Tick+=(_,_)=>{Sample();redraw();if(Value==end)timer.Stop();};}
    void Sample(){double t=duration==0?1:Math.Clamp((Environment.TickCount64-began)/(double)duration,0,1);Value=start+(end-start)*UiTheme.Ease(t);if(t==1)Value=end;}
    public void To(double target,int milliseconds=160,bool immediate=false)
    {
        Sample();start=Value;end=target;began=Environment.TickCount64;duration=milliseconds;
        if(immediate || !UiTheme.Motion){Snap(target);return;}timer.Start();redraw();
    }
    public void Snap(double value){timer.Stop();start=end=Value=value;duration=0;redraw();}
    public void Dispose()=>timer.Dispose();
}

internal sealed class CardPanel : Panel
{
    [DefaultValue(true)] public bool Border {get;set;}=true;
    public CardPanel(){DoubleBuffered=true;BackColor=UiTheme.Surface;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if(Width<2||Height<2)return;
        using var outline=UiTheme.Round(new RectangleF(0,0,Width,Height),12*DeviceDpi/96f);
        var previous=Region;Region=new Region(outline);previous?.Dispose();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor??UiTheme.Canvas);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=UiTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),12*DeviceDpi/96f);
        using var fill=new SolidBrush(BackColor);e.Graphics.FillPath(fill,path);
        if(Border){using var pen=new Pen(UiTheme.Line);e.Graphics.DrawPath(pen,path);}
    }
}

internal sealed class SoftButton : Button
{
    readonly UiTween press;
    bool hovered;
    [DefaultValue(false)] public bool Primary {get;set;}
    [DefaultValue(false)] public bool Quiet {get;set;}
    public SoftButton()
    {
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
        BackColor=UiTheme.Canvas;Cursor=Cursors.Hand;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        press=new UiTween(Invalidate);
    }
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=false;press.To(0,120);Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)press.To(1,100);base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){press.To(0,120);base.OnMouseUp(e);}
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)press.To(0,120);base.OnMouseCaptureChanged(e);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Parent?.BackColor??UiTheme.Canvas);g.SmoothingMode=SmoothingMode.AntiAlias;
        float scale=1-(float)press.Value*.03f;
        g.TranslateTransform(Width/2f,Height/2f);g.ScaleTransform(scale,scale);g.TranslateTransform(-Width/2f,-Height/2f);
        Color fill=Primary?(hovered?Color.FromArgb(51,52,53):UiTheme.Ink):
            Quiet?(hovered?Color.FromArgb(231,232,229):Parent?.BackColor??UiTheme.Canvas):(hovered?Color.FromArgb(242,243,240):Color.White);
        if(!Enabled)fill=Color.FromArgb(228,229,226);
        using var path=UiTheme.Round(new RectangleF(1,1,Width-2,Height-2),9*DeviceDpi/96f);
        using var brush=new SolidBrush(fill);g.FillPath(brush,path);
        if(!Primary&&!Quiet){using var border=new Pen(UiTheme.Line);g.DrawPath(border,path);}
        using var textBrush=new SolidBrush(!Enabled?UiTheme.Muted:Primary?Color.White:UiTheme.Ink);
        using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        g.DrawString(Text,Font,textBrush,new RectangleF(2,0,Width-4,Height),format);
        if(Focused&&ShowFocusCues){using var focusPen=new Pen(Color.FromArgb(86,136,217),2);using var ring=UiTheme.Round(new RectangleF(4,4,Width-8,Height-8),6);g.DrawPath(focusPen,ring);}
    }
    protected override void Dispose(bool disposing){if(disposing)press.Dispose();base.Dispose(disposing);}
}

internal sealed class ToggleSwitch : CheckBox
{
    readonly UiTween position;
    bool keyboard;
    public ToggleSwitch()
    {
        AutoSize=false;Size=new Size(54,34);Cursor=Cursors.Hand;Text="";
        AccessibleRole=AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        position=new UiTween(Invalidate);
    }
    protected override void OnKeyDown(KeyEventArgs e){keyboard=true;base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);keyboard=false;}
    protected override void OnCheckedChanged(EventArgs e)
    {
        position?.To(Checked?1:0,160,keyboard||!IsHandleCreated);base.OnCheckedChanged(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Parent?.BackColor??UiTheme.Surface);g.SmoothingMode=SmoothingMode.AntiAlias;
        float s=DeviceDpi/96f,w=44*s,h=24*s,x=(Width-w)/2,y=(Height-h)/2;
        double value=position.Value;
        using var path=UiTheme.Round(new RectangleF(x,y,w,h),h/2);
        using var track=new SolidBrush(Enabled?UiTheme.Mix(Color.FromArgb(219,222,219),UiTheme.Ink,value):UiTheme.Line);
        g.FillPath(track,path);
        using var knob=new SolidBrush(Color.White);
        g.FillEllipse(knob,x+3*s+(w-h)*(float)value,y+3*s,18*s,18*s);
        if(Focused&&ShowFocusCues){using var pen=new Pen(Color.FromArgb(86,136,217),1.5f*s);using var focus=UiTheme.Round(new RectangleF(x-3*s,y-3*s,w+6*s,h+6*s),h/2+3*s);g.DrawPath(pen,focus);}
    }
    protected override void Dispose(bool disposing){if(disposing)position.Dispose();base.Dispose(disposing);}
}

internal sealed class NumberStepper : UserControl
{
    readonly TextBox input=new();
    readonly SoftButton minus=new(){Text="−",Quiet=true,TabStop=false};
    readonly SoftButton plus=new(){Text="+",Quiet=true,TabStop=false};
    readonly Label unit=new(){AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,ForeColor=UiTheme.Muted};
    int value;
    [DefaultValue(1)] public int Minimum{get;set;}=1;
    [DefaultValue(3600)] public int Maximum{get;set;}=3600;
    [DefaultValue(1)] public int Step{get;set;}=1;
    [DefaultValue("")] public string Unit {get=>unit.Text;set=>unit.Text=value;}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Value{get=>int.TryParse(input.Text,out int v)?Math.Clamp(v,Minimum,Maximum):value;set{this.value=Math.Clamp(value,Minimum,Maximum);input.Text=this.value.ToString();}}
    public NumberStepper()
    {
        Size=new Size(174,36);BackColor=Color.FromArgb(245,246,243);
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
        input.BorderStyle=BorderStyle.None;input.TextAlign=HorizontalAlignment.Right;input.BackColor=BackColor;input.ForeColor=UiTheme.Ink;
        input.Font=new Font("Segoe UI",11,FontStyle.Bold);
        Controls.AddRange([minus,input,unit,plus]);
        minus.Click+=(_,_)=>Value=Value-Step;plus.Click+=(_,_)=>Value=Value+Step;
        input.KeyPress+=(_,e)=>{if(!char.IsControl(e.KeyChar)&&!char.IsDigit(e.KeyChar))e.Handled=true;};
        input.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Up){Value+=Step;e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Down){Value-=Step;e.SuppressKeyPress=true;}};
        input.Leave+=(_,_)=>Value=Value;
        input.Enter+=(_,_)=>input.SelectAll();
    }
    protected override void OnCreateControl(){base.OnCreateControl();input.AccessibleName=AccessibleName;}
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);float s=DeviceDpi/96f;int b=(int)(32*s);
        minus.SetBounds(0,0,b,Height);plus.SetBounds(Width-b,0,b,Height);
        unit.SetBounds(Width-b-(int)(34*s),0,(int)(34*s),Height);
        input.SetBounds(b,(Height-input.PreferredHeight)/2,Math.Max(20,Width-b*2-unit.Width-5),input.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var pen=new Pen(UiTheme.Line);using var path=UiTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),8*DeviceDpi/96f);e.Graphics.DrawPath(pen,path);
    }
}

internal static class AppBrand
{
    public static Icon LoadIcon()
    {
        using var stream=typeof(AppBrand).Assembly.GetManifestResourceStream("WallpaperQuiet.App.ico")
            ??throw new InvalidOperationException("应用图标资源缺失。");
        using var icon=new Icon(stream);return (Icon)icon.Clone();
    }
}

