using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiverGuard.Core;

namespace RiverGuard.Controls;

// Плановая схема по интерполированным ширинам створов
public sealed class ZonePlanView : FrameworkElement
{
    public SimulationResult? Result {get;set;}
    public bool Paper {get;set;}
    public void Refresh()=>InvalidateVisual();
    private static SolidColorBrush Ink(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;
        var ink=Ink(Paper?"#25364B":"#C9D8EB");dc.DrawRectangle(Ink(Paper?"#FFFFFF":"#141E30"),null,new Rect(0,0,w,h));
        void Text(string text,double x,double y,double size=11,double width=1000)
        {
            var value=new FormattedText(text,CultureInfo.GetCultureInfo("ru-RU"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),Paper?size*1.9:size,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(20,width)};
            dc.DrawText(value,new Point(x,y));
        }
        Text("Схема зон затопления · не ГИС-карта",24,15,17,Math.Max(40,w-48));
        if(Result==null||Result.Sections.Count==0||w<350||h<300){Text("Выполните расчёт, чтобы построить схему",24,65);return;}
        var r=Result;var rows=r.Sections.OrderBy(s=>s.Distance).ToArray();
        var geometry=rows.Select(s=>r.Source.Sections.First(p=>p.Name==s.Name)).ToArray();
        double maxDistance=Math.Max(1,rows[^1].Distance),cross=Math.Max(1,rows.Select((s,i)=>Math.Max(geometry[i].LeftWidth+s.FloodLeft,geometry[i].RightWidth+s.FloodRight)).Max())*1.15;
        var plot=new Rect(Paper?185:155,Paper?150:105,Math.Max(40,w-(Paper?370:310)),Math.Max(30,h-(Paper?450:300)));
        Point Map(double x,double distance)=>new(plot.Left+plot.Width*(x/cross+1)/2,plot.Top+plot.Height*distance/maxDistance);
        var grid=new Pen(Ink(Paper?"#D9E0E9":"#30415B"),.7){DashStyle=DashStyles.Dot};
        for(int k=-1;k<=1;k++)
        {
            double x=cross*k;var point=Map(x,0);dc.DrawLine(grid,point,Map(x,maxDistance));Text(x.ToString("F3",CultureInfo.GetCultureInfo("ru-RU")),point.X-35,plot.Top-23,10,100);
        }
        void Polygon(Func<SectionResult,int,double> left,Func<SectionResult,int,double> right,string fill)
        {
            if(rows.Length<2)return;
            var points=rows.Select((s,i)=>Map(-left(s,i),s.Distance)).Concat(rows.Select((s,i)=>Map(right(s,i),s.Distance)).Reverse()).ToArray();
            var shape=new StreamGeometry();using(var context=shape.Open()){context.BeginFigure(points[0],true,true);context.PolyLineTo(points.Skip(1).ToArray(),true,true);}shape.Freeze();dc.DrawGeometry(Ink(fill),null,shape);
        }
        Polygon((s,i)=>geometry[i].LeftWidth+s.FloodLeft,(s,i)=>geometry[i].RightWidth+s.FloodRight,Paper?"#BFE7E1":"#225D60");
        if(rows.Any(s=>s.DangerLeft+s.DangerRight>0))Polygon((s,i)=>geometry[i].LeftWidth+s.DangerLeft,(s,i)=>geometry[i].RightWidth+s.DangerRight,Paper?"#FFDCA8":"#94623A");
        if(rows.Any(s=>s.DestructionLeft+s.DestructionRight>0))Polygon((s,i)=>geometry[i].LeftWidth+s.DestructionLeft,(s,i)=>geometry[i].RightWidth+s.DestructionRight,Paper?"#F5B1BF":"#994D68");
        Polygon((s,i)=>geometry[i].LeftWidth,(s,i)=>geometry[i].RightWidth,Paper?"#DCEBFA":"#213D5B");
        dc.DrawLine(new Pen(Ink("#5888AF"),1.5){DashStyle=DashStyles.Dash},Map(0,0),Map(0,maxDistance));
        Text("Нулевой створ / начало оси",plot.Left,plot.Top-46,10,plot.Width);Text("Левый берег ← расстояние от оси, м → Правый берег",plot.Left,plot.Bottom+18,10,plot.Width);
        for(int i=0;i<rows.Length;i++)
        {
            var s=rows[i];double y=Map(0,s.Distance).Y;
            dc.DrawLine(grid,new Point(plot.Left,y),new Point(plot.Right,y));
            Text(s.Name,8,y-(Paper?30:17),10,Paper?165:140);Text(s.Distance.ToString("F3",CultureInfo.GetCultureInfo("ru-RU"))+" м",8,y,10,Paper?165:140);
            dc.DrawEllipse(Ink("#5AA4CC"),null,Map(0,s.Distance),3,3);
            Text(s.ArrivalHours.HasValue?s.Evacuation.Split('·')[0].Trim():"нет прихода",plot.Right+12,y-8,10,135);
        }
        for(int i=0;i<rows.Length-1;i++)
        {
            var a=rows[i];var b=rows[i+1];
            string color=a.ArrivalHours==null?"#78889D":a.Evacuation.StartsWith("I ·")?"#CB6A7E":a.Evacuation.StartsWith("II ·")?"#D3A860":"#66A7B0";
            dc.DrawLine(new Pen(Ink(color),5),new Point(plot.Right+7,Map(0,a.Distance).Y),new Point(plot.Right+7,Map(0,b.Distance).Y));
            if(a.ArrivalHours.HasValue&&b.ArrivalHours.HasValue&&a.ArrivalHours!=b.ArrivalHours)
            {
                foreach(var boundary in new[]{(Time:r.Source.EvacuationHours,Name:"I / II"),(Time:4d,Name:"II / III")})
                {
                    double f=(boundary.Time-a.ArrivalHours.Value)/(b.ArrivalHours.Value-a.ArrivalHours.Value);
                    if(f>0&&f<1){double distance=a.Distance+(b.Distance-a.Distance)*f,y=Map(0,distance).Y;dc.DrawLine(new Pen(Ink("#9B7FB1"),1){DashStyle=DashStyles.Dash},new Point(plot.Left,y),new Point(plot.Right,y));Text(boundary.Name+" · "+distance.ToString("F3",CultureInfo.GetCultureInfo("ru-RU"))+" м",plot.Left+5,y-16,10,plot.Width-10);}
                }
            }
        }
        double legend=plot.Bottom+50;
        foreach(var item in new[]{("#4BABA1","ВЗМ"),("#DBAB67","ВКЗМ"),("#D97691","КР"),("#7898B9","Бытовое русло")})
        {double x=24+Array.IndexOf(new[]{"ВЗМ","ВКЗМ","КР","Бытовое русло"},item.Item2)*Math.Max(95,(w-48)/4);dc.DrawRectangle(Ink(item.Item1),null,new Rect(x,legend,12,12));Text(item.Item2,x+19,legend-2,11,180);}
        Text(r.Source.Zones.Description,24,legend+(Paper?35:25),10,w-48);
        Text($"Эвакуация: I — фронт ≤ {r.Source.EvacuationHours:F3} ч; II — до 4,000 ч; III — позднее 4,000 ч. Границы между категориями интерполируются по времени фронта.",24,legend+(Paper?120:58),10,w-48);
        Text("Линейная интерполяция между створами; участок плотина—первый створ не экстраполируется. Масштабы по осям различаются. Цветные зоны показаны за бытовыми берегами.",24,legend+(Paper?185:95),10,w-48);
    }
    public static byte[] Png(SimulationResult result,int width=1200,int height=900)
    {
        height=Math.Max(height,Math.Min(12000,300+result.Sections.Count*32));
        var view=new ZonePlanView{Result=result,Paper=true,Width=width,Height=height};view.Measure(new(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
}
