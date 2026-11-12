namespace RiverGuard.Core;

// Геометрия поперечных сечений
public sealed class SectionGeometry
{
    public RiverSection Source { get; }
    public List<Sample> Profile { get; } = new();
    private readonly double[] area = new double[801], pressure = new double[801], width = new double[801], perimeter = new double[801];
    private readonly double dh;
    public SectionGeometry(RiverSection source, double maximumDepth = 40)
    {
        Source=source; dh=maximumDepth/800;
        var pts = new List<Sample>();
        for(int j=12;j>=1;j--) pts.Add(new(-source.LeftWidth*j/12,source.Bed+source.NormalDepth*Math.Pow(j/12d,2)));
        pts.Add(new(0,source.Bed));
        for(int j=1;j<=12;j++) pts.Add(new(source.RightWidth*j/12,source.Bed+source.NormalDepth*Math.Pow(j/12d,2)));
        pts.AddRange(source.LeftBank.Select(p=>new Sample(-p.Distance,p.Elevation)));
        pts.AddRange(source.RightBank.Select(p=>new Sample(p.Distance,p.Elevation)));
        pts=pts.OrderBy(p=>p.X).ToList();
        double extension=maximumDepth/0.08;
        pts.Insert(0,new(pts[0].X-extension,source.Bed+maximumDepth+5));
        pts.Add(new(pts[^1].X+extension,source.Bed+maximumDepth+5));
        Profile=pts;
        for(int i=1;i<area.Length;i++)
        {
            var values=Integrate(source.Bed+i*dh);
            area[i]=values.A; pressure[i]=values.P; width[i]=values.T; perimeter[i]=values.Chi;
        }
    }
    private (double A,double P,double T,double Chi) Integrate(double level)
    {
        double a=0,p=0,t=0,chi=0;
        for(int i=0;i<Profile.Count-1;i++)
        {
            double h1=level-Profile[i].Y,h2=level-Profile[i+1].Y,dx=Profile[i+1].X-Profile[i].X;
            if(h1<=0&&h2<=0) continue;
            double wet=1;
            if(h1<0) {wet=h2/(h2-h1);h1=0;} else if(h2<0) {wet=h1/(h1-h2);h2=0;}
            double w=dx*wet;
            a+=(h1+h2)*w/2; p+=(h1*h1+h1*h2+h2*h2)*w/6;
            t+=w;chi+=Math.Sqrt(w*w+(h2-h1)*(h2-h1));
        }
        return(a,p,t,chi);
    }
    private double Lookup(double[] values,double depth)
    {
        double idx=Math.Clamp(depth/dh,0,799.999999);int k=(int)idx;
        return values[k]+(values[k+1]-values[k])*(idx-k);
    }
    public double Area(double h)=>h<=0?0:Lookup(area,h);
    public double Pressure(double h)=>h<=0?0:9.81*Lookup(pressure,h);
    public double TopWidth(double h)=>Math.Max(Lookup(width,h),0.001);
    public double Radius(double h)=>Area(h)/Math.Max(Lookup(perimeter,h),0.001);
    public double Depth(double a)
    {
        if(a<=0)return 0;
        int k=Array.BinarySearch(area,a); if(k>=0)return k*dh;
        k=~k; if(k>=area.Length)return 800*dh;
        return (k-1+(a-area[k-1])/Math.Max(area[k]-area[k-1],1e-12))*dh;
    }
    public (double Left,double Right) Extent(double level)
    {
        double left=0,right=0;
        foreach(var p in Profile.Where(p=>p.Y<=level)){left=Math.Max(left,-p.X);right=Math.Max(right,p.X);}
        for(int i=0;i<Profile.Count-1;i++)
        {
            var a=Profile[i];var b=Profile[i+1];
            if((a.Y-level)*(b.Y-level)<0)
            { double x=a.X+(level-a.Y)*(b.X-a.X)/(b.Y-a.Y);left=Math.Max(left,-x);right=Math.Max(right,x); }
        }
        return(left,right);
    }
    public double BedAt(double x)
    {
        for(int i=0;i<Profile.Count-1;i++) if(x>=Profile[i].X&&x<=Profile[i+1].X)
            return Profile[i].Y+(Profile[i+1].Y-Profile[i].Y)*(x-Profile[i].X)/(Profile[i+1].X-Profile[i].X);
        return Profile[^1].Y;
    }
}

// Интерполяция русла по длине
public static class RiverGeometry
{
    public static List<RiverSection> WithOrigin(ProjectData p)
    {
        var sections=p.Sections.OrderBy(s=>s.Distance).ToList();
        sections.Insert(0,new RiverSection {Name="Плотина",Distance=0,Bed=p.DamBed,NormalDepth=p.LowerDepth,LeftWidth=p.LowerWidth/2,RightWidth=p.LowerWidth/2,InitialVelocity=p.LowerVelocity,Manning=sections[0].Manning});
        return sections;
    }
    public static RiverSection Interpolate(List<RiverSection> sections,double x)
    {
        int k=0;while(k<sections.Count-2&&sections[k+1].Distance<x)k++;
        var a=sections[k];var b=sections[k+1];double f=Math.Clamp((x-a.Distance)/(b.Distance-a.Distance),0,1);
        double L(double aa,double bb)=>aa+(bb-aa)*f;
        var s=new RiverSection {Distance=x,Bed=L(a.Bed,b.Bed),NormalDepth=L(a.NormalDepth,b.NormalDepth),LeftWidth=L(a.LeftWidth,b.LeftWidth),RightWidth=L(a.RightWidth,b.RightWidth),InitialVelocity=L(a.InitialVelocity,b.InitialVelocity),Manning=L(a.Manning,b.Manning)};
        void Bank(IList<BankPoint> aa,IList<BankPoint> bb,ObservableBank target,double wa,double wb)
        {
            int n=Math.Max(aa.Count,bb.Count);
            for(int i=0;i<n;i++)
            {
                var pa=i<aa.Count?aa[i]:new BankPoint{Distance=wa+40*(i+1),Elevation=a.Bed+a.NormalDepth+2*(i+1)};
                var pb=i<bb.Count?bb[i]:new BankPoint{Distance=wb+40*(i+1),Elevation=b.Bed+b.NormalDepth+2*(i+1)};
                target.Add(new(){Distance=L(pa.Distance,pb.Distance),Elevation=L(pa.Elevation,pb.Elevation)});
            }
        }
        Bank(a.LeftBank,b.LeftBank,new(s.LeftBank),a.LeftWidth,b.LeftWidth);
        Bank(a.RightBank,b.RightBank,new(s.RightBank),a.RightWidth,b.RightWidth);
        return s;
    }
    private sealed class ObservableBank(System.Collections.ObjectModel.ObservableCollection<BankPoint> bank)
    { public void Add(BankPoint point)=>bank.Add(point); }
}
