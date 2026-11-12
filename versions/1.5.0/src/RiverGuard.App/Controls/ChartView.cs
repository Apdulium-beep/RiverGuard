using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using RiverGuard.Core;

namespace RiverGuard.Controls;

// Интерактивные научные графики
public class ChartLine
{
    public string Name { get; set; } = "";
    public Color Color { get; set; } = Colors.Turquoise;
    public List<Sample> Points { get; set; } = new();
    public bool Fill { get; set; }
    public bool Dashed { get; set; }
}
public sealed class ChartView : FrameworkElement
{
    public List<ChartLine> Lines { get; } = new();
    public string XTitle { get; set; } = "";
    public string YTitle { get; set; } = "";
    public string EmptyMessage { get; set; } = "Выполните расчёт, чтобы увидеть данные";
    public double? MarkerX { get; set; }
    private Point? pointer;
    private double minX,maxX,minY,maxY;
    private Rect plot;
    public ChartView()
    {
        MinHeight=180;ClipToBounds=true;
        MouseMove+=(s,e)=>{pointer=e.GetPosition(this);InvalidateVisual();};
        MouseLeave+=(s,e)=>{pointer=null;InvalidateVisual();};
    }
    public void Refresh()=>InvalidateVisual();
    private static Brush Brush(string hex)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private void Text(DrawingContext dc,string text,double x,double y,double size=11,Brush? ink=null)
    { dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,ink??Brush("#91A4BE"),VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y)); }
    private Point Map(Sample p)=>new(plot.Left+(p.X-minX)/(maxX-minX)*plot.Width,plot.Bottom-(p.Y-minY)/(maxY-minY)*plot.Height);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;
        dc.DrawRectangle(Brush("#141E30"),null,new Rect(0,0,w,h));
        if(w<100||h<100)return;
        var points=Lines.SelectMany(l=>l.Points).Where(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)).ToArray();
        if(points.Length<2){Text(dc,EmptyMessage,26,h/2,13);return;}
        minX=points.Min(p=>p.X);maxX=points.Max(p=>p.X);minY=points.Min(p=>p.Y);maxY=points.Max(p=>p.Y);
        if(maxX-minX<1e-10)maxX=minX+1;
        double margin=Math.Max((maxY-minY)*0.12,0.2);minY-=margin;maxY+=margin;
        plot=new Rect(62,39,Math.Max(20,w-84),Math.Max(20,h-87));
        for(int i=0;i<=4;i++)
        {
            double yy=plot.Top+plot.Height*i/4,xx=plot.Left+plot.Width*i/4;
            dc.DrawLine(new Pen(Brush("#26344B"),0.6),new(plot.Left,yy),new(plot.Right,yy));
            dc.DrawLine(new Pen(Brush("#26344B"),0.6),new(xx,plot.Top),new(xx,plot.Bottom));
            Text(dc,(maxY-(maxY-minY)*i/4).ToString("0.##"),4,yy-7,10);
            Text(dc,(minX+(maxX-minX)*i/4).ToString("0.##"),xx-10,plot.Bottom+9,10);
        }
        Text(dc,YTitle,4,5,11);Text(dc,XTitle,Math.Max(62,w-170),h-20,10);
        double lx=plot.Left;
        foreach(var line in Lines)
        {
            if(lx+line.Name.Length*6+25<w){dc.DrawEllipse(new SolidColorBrush(line.Color),null,new(lx+3,18),3,3);Text(dc,line.Name,lx+13,9,10);lx+=line.Name.Length*6+40;}
            var valid=line.Points.Where(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)).ToArray();if(valid.Length<2)continue;
            var geometry=new StreamGeometry();using(var ctx=geometry.Open()){ctx.BeginFigure(Map(valid[0]),false,false);ctx.PolyLineTo(valid.Skip(1).Select(Map).ToArray(),true,false);}geometry.Freeze();
            if(line.Fill)
            {
                var fill=new StreamGeometry();using(var ctx=fill.Open()){ctx.BeginFigure(new(Map(valid[0]).X,plot.Bottom),true,true);ctx.PolyLineTo(valid.Select(Map).ToArray(),true,false);ctx.LineTo(new(Map(valid[^1]).X,plot.Bottom),true,false);}
                dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(62,line.Color.R,line.Color.G,line.Color.B),Color.FromArgb(0,line.Color.R,line.Color.G,line.Color.B),90),null,fill);
            }
            dc.DrawGeometry(null,new Pen(new SolidColorBrush(line.Color),2){DashStyle=line.Dashed?DashStyles.Dash:DashStyles.Solid,LineJoin=PenLineJoin.Round},geometry);
            if(valid.Length<16)foreach(var p in valid)dc.DrawEllipse(new SolidColorBrush(line.Color),new Pen(Brush("#141E30"),2),Map(p),4,4);
        }
        if(MarkerX is double marker&&marker>=minX&&marker<=maxX)
        {double mx=Map(new(marker,minY)).X;dc.DrawLine(new Pen(Brush("#B5A2FF"),1){DashStyle=DashStyles.Dash},new(mx,plot.Top),new(mx,plot.Bottom));}
        if(pointer is Point mouse&&plot.Contains(mouse))
        {
            double x=minX+(mouse.X-plot.Left)/plot.Width*(maxX-minX);
            dc.DrawLine(new Pen(Brush("#7A94B2"),1){DashStyle=DashStyles.Dot},new(mouse.X,plot.Top),new(mouse.X,plot.Bottom));
            var line=Lines.FirstOrDefault(l=>l.Points.Count>0);
            if(line!=null)
            {
                var nearest=line.Points.MinBy(p=>Math.Abs(p.X-x))!;var screen=Map(nearest);
                dc.DrawEllipse(new SolidColorBrush(line.Color),new Pen(Brush("#141E30"),2),screen,5,5);
                double tx=Math.Clamp(mouse.X+12,plot.Left,Math.Max(plot.Left,plot.Right-145)),ty=Math.Clamp(mouse.Y-45,plot.Top,plot.Bottom-35);
                dc.DrawRoundedRectangle(Brush("#243B55"),null,new(tx,ty,145,31),6,6);Text(dc,$"{nearest.X:0.###}  /  {nearest.Y:0.###}",tx+9,ty+7,11,Brush("#F0F8FF"));
            }
        }
    }
    public byte[] Png(int width=1000,int height=400)
    {
        var copy=new ChartView{XTitle=XTitle,YTitle=YTitle,Width=width,Height=height,MarkerX=MarkerX};copy.Lines.AddRange(Lines);
        copy.Measure(new(width,height));copy.Arrange(new Rect(0,0,width,height));copy.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(copy);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var ms=new MemoryStream();encoder.Save(ms);return ms.ToArray();
    }
}
