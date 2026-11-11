using System.Diagnostics;

namespace RiverGuard.Core;

// Проверка исходных данных
public static class Validation
{
    public static List<string> Errors(ProjectData p)
    {
        var e=new List<string>();
        if(!ScenarioMode.IsKnown(p.Scenario))e.Add("Неизвестный механизм аварии. Поддерживаются проран, перелив, открытие затвора и круглое отверстие.");
        double[] values={p.ReservoirLevel,p.DamBed,p.ReservoirVolume,p.ReservoirArea,p.BreachWidth,p.SillElevation,p.FormationMinutes,p.DischargeCoefficient,p.LowerDepth,p.LowerWidth,p.LowerVelocity,p.DurationHours,p.ArrivalThreshold,p.EvacuationHours};
        if(values.Any(x=>!double.IsFinite(x)))e.Add("Все параметры должны быть конечными числами.");
        if(p.ReservoirLevel<=p.DamBed)e.Add("Уровень водохранилища должен быть выше дна у плотины.");
        if(p.ReservoirVolume<=0||p.ReservoirArea<=0)e.Add("Объём и площадь водохранилища должны быть больше нуля.");
        if(!ScenarioMode.IsCircular(p.Scenario)&&p.BreachWidth<0||ScenarioMode.HasFormation(p.Scenario)&&p.FormationMinutes<0)e.Add("Ширина отверстия и применимое время открытия не могут быть отрицательными.");
        if(ScenarioMode.IsGate(p.Scenario)&&(!double.IsFinite(p.GateHeight)||p.GateHeight<=0))e.Add("Максимальная высота открытия затвора должна быть конечной и положительной.");
        if(ScenarioMode.IsCircular(p.Scenario)&&(!double.IsFinite(p.OutletDiameter)||p.OutletDiameter<0))e.Add("Диаметр отверстия должен быть конечным и неотрицательным (0 — закрыто).");
        if(p.SillElevation<p.DamBed)e.Add("Нижняя отметка отверстия / порога не может быть ниже дна у плотины.");
        if(p.DischargeCoefficient<=0||p.DischargeCoefficient>1)e.Add("Коэффициент расхода должен находиться в диапазоне (0; 1].");
        if(p.LowerDepth<=0||p.LowerWidth<=0)e.Add("Глубина и ширина нижнего бьефа должны быть положительными.");
        if(p.Cells<40||p.Cells>400)e.Add("Количество ячеек: от 40 до 400.");
        if(p.DurationHours<=0||p.DurationHours>48)e.Add("Продолжительность: от 0 до 48 ч, не включая ноль.");
        if(p.ArrivalThreshold<=0||p.EvacuationHours<=0||p.EvacuationHours>4)e.Add("Порог прихода должен быть положительным; время экстренной зоны — от 0 до 4 ч, не включая ноль.");
        if(p.Sections.Count==0)e.Add("Добавьте хотя бы один створ.");
        if(p.Sections.Any(s=>string.IsNullOrWhiteSpace(s.Name))||p.Sections.Select(s=>s.Name).Distinct().Count()!=p.Sections.Count)e.Add("Названия створов должны быть непустыми и различными.");
        if(p.Sections.Any(s=>s.Distance<=0)||p.Sections.Select(s=>s.Distance).Distinct().Count()!=p.Sections.Count)e.Add("Удаления створов должны быть положительными и различными.");
        foreach(var s in p.Sections)
        {
            var v=new[]{s.Distance,s.Bed,s.NormalDepth,s.LeftWidth,s.RightWidth,s.InitialVelocity,s.Manning,s.DrainageFactor};
            if(v.Any(x=>!double.IsFinite(x)))e.Add($"{s.Name}: недопустимое число.");
            if(s.NormalDepth<=0||s.LeftWidth<=0||s.RightWidth<=0)e.Add($"{s.Name}: глубина и ширины должны быть положительными.");
            if(s.Manning<0.005||s.Manning>0.2)e.Add($"{s.Name}: коэффициент Маннинга — от 0,005 до 0,2.");
            if(s.DrainageFactor<1||s.DrainageFactor>1.7)e.Add($"{s.Name}: коэффициент осушения — от 1 до 1,7.");
            foreach(var (bank,w) in new[]{(s.LeftBank,s.LeftWidth),(s.RightBank,s.RightWidth)})
            {
                double last=w,level=s.Bed+s.NormalDepth;
                foreach(var pt in bank)
                {
                    if(!double.IsFinite(pt.Distance)||!double.IsFinite(pt.Elevation)||pt.Distance<=last||pt.Elevation<level)e.Add($"{s.Name}: горизонтали должны идти от русла с возрастающими расстояниями и неубывающими отметками.");
                    last=pt.Distance;level=pt.Elevation;
                }
            }
        }
        if(p.Sections.Count>0&&p.Sections.Max(s=>s.Distance)<20)e.Add("Длина расчётного участка должна быть не менее 20 м.");
        return e.Distinct().ToList();
    }
}

// Численная модель неустановившегося потока
public sealed class Solver
{
    private const double G=9.81;
    public SimulationResult Run(ProjectData original,IProgress<double>? progress=null,CancellationToken cancellation=default)
    {
        var errors=Validation.Errors(original);if(errors.Count>0)throw new ArgumentException(string.Join("\n",errors));
        var watch=Stopwatch.StartNew();
        var background=original.Copy();background.BreachWidth=0;background.OutletDiameter=0;
        var baseline=Calculate(background,new ScaledProgress(progress,0,0.4),cancellation);
        var result=Calculate(original,new ScaledProgress(progress,0.4,0.6),cancellation,baseline);
        result.ElapsedSeconds=watch.Elapsed.TotalSeconds;
        return result;
    }
    private sealed class ScaledProgress(IProgress<double>? parent,double start,double scale) : IProgress<double>
    { public void Report(double value)=>parent?.Report(start+scale*value); }
    private SimulationResult Calculate(ProjectData original,IProgress<double>? progress,CancellationToken cancellation,SimulationResult? baseline=null)
    {
        var p=original.Copy(); var errors=Validation.Errors(p);
        if(errors.Count>0)throw new ArgumentException(string.Join("\n",errors));
        var watch=Stopwatch.StartNew();
        var result=new SimulationResult{Source=p};
        var source=RiverGeometry.WithOrigin(p);
        int n=p.Cells;double length=source[^1].Distance,dx=length/n,end=p.DurationHours*3600;
        var geo=new SectionGeometry[n];var face=new SectionGeometry[n+1];
        double maxDepth=Math.Max(40,p.ReservoirDepth+30+source.Max(s=>p.DamBed-s.Bed));
        for(int i=0;i<n;i++)geo[i]=new(RiverGeometry.Interpolate(source,(i+0.5)*dx),maxDepth);
        for(int i=0;i<=n;i++)face[i]=new(RiverGeometry.Interpolate(source,i*dx),maxDepth);
        double[] a=new double[n],q=new double[n],h=new double[n],u=new double[n],pres=new double[n],fq=new double[n+1],fm=new double[n+1],corrL=new double[n+1],corrR=new double[n+1];
        for(int i=0;i<n;i++){a[i]=geo[i].Area(geo[i].Source.NormalDepth);q[i]=a[i]*geo[i].Source.InitialVelocity;}
        result.InitialRiverVolume=a.Sum()*dx;
        int[] sample=p.Sections.OrderBy(s=>s.Distance).Select(s=>Math.Clamp((int)(s.Distance/dx),0,n-1)).ToArray();
        var originals=p.Sections.OrderBy(s=>s.Distance).ToArray();
        for(int j=0;j<sample.Length;j++)result.Sections.Add(new(){Name=originals[j].Name,Distance=originals[j].Distance,MaxLevel=originals[j].Bed+originals[j].NormalDepth,MaxDepth=originals[j].NormalDepth,MaxDischarge=Math.Abs(q[sample[j]]),MaxVelocity=Math.Abs(geo[sample[j]].Source.InitialVelocity),DischargeAtPeak=q[sample[j]],BaselineDepthHistory=baseline?.Sections[j].DepthHistory??new()});
        var geometry=originals.Select(s=>new SectionGeometry(s,maxDepth)).ToArray();
        double reservoir=p.ReservoirLevel,volume=p.ReservoirVolume*1e6,released=0,outflow=0,time=0,nextSave=0,negativeCorrection=0;
        double[] lastWet=new double[sample.Length];bool[] stillWet=new bool[sample.Length];
        double saveInterval=end/480;
        for(int j=0;j<sample.Length;j++)result.Sections[j].DepthHistory.Add(new(0,geo[sample[j]].Source.NormalDepth));
        while(time<end)
        {
            if(result.Steps%100==0){cancellation.ThrowIfCancellationRequested();progress?.Report(time/end);}
            double maxSpeed=0;
            for(int i=0;i<n;i++)
            {
                h[i]=geo[i].Depth(a[i]);u[i]=a[i]>1e-7?q[i]/a[i]:0;pres[i]=geo[i].Pressure(h[i]);
                maxSpeed=Math.Max(maxSpeed,Math.Abs(u[i])+Math.Sqrt(G*a[i]/geo[i].TopWidth(h[i])));
            }
            double dt=Math.Min(Math.Min(0.35*dx/Math.Max(maxSpeed,0.1),10),end-time);
            double upperHead=Math.Max(0,reservoir-p.SillElevation);
            double breach=SourceDischarge.Evaluate(p,reservoir,geo[0].Source.Bed+h[0],time);
            breach=Math.Min(breach,Math.Min(volume,Math.Max(0,(reservoir-p.SillElevation)*p.ReservoirArea*1e6))/Math.Max(dt,1e-12));
            fq[0]=breach;fm[0]=pres[0]+breach*Math.Sqrt(2*G*upperHead);corrR[0]=0;
            for(int f=1;f<n;f++)
            {
                int l=f-1,r=f;
                double z=Math.Max(geo[l].Source.Bed,geo[r].Source.Bed);
                double hl=Math.Max(0,geo[l].Source.Bed+h[l]-z),hr=Math.Max(0,geo[r].Source.Bed+h[r]-z);
                double al=face[f].Area(hl),ar=face[f].Area(hr),pl=face[f].Pressure(hl),pr=face[f].Pressure(hr);
                double ul=hl>1e-6?u[l]:0,ur=hr>1e-6?u[r]:0;
                double ql=al*ul,qr=ar*ur;
                double cl=Math.Sqrt(G*al/face[f].TopWidth(hl)),cr=Math.Sqrt(G*ar/face[f].TopWidth(hr));
                double sl=Math.Min(0,Math.Min(ul-cl,ur-cr)),sr=Math.Max(0,Math.Max(ul+cl,ur+cr)),den=sr-sl;
                if(den<1e-10){fq[f]=0;fm[f]=0;}else
                {fq[f]=(sr*ql-sl*qr+sl*sr*(ar-al))/den;fm[f]=(sr*(ql*ul+pl)-sl*(qr*ur+pr)+sl*sr*(qr-ql))/den;}
                corrL[f]=pres[l]-pl;corrR[f]=pres[r]-pr;
            }
            fq[n]=q[n-1];fm[n]=q[n-1]*u[n-1]+pres[n-1];corrL[n]=0;
            for(int i=0;i<n;i++)
            {
                double outgoing=Math.Max(0,fq[i+1])+Math.Max(0,-fq[i]);
                if(outgoing>0&&a[i]>1e-9)dt=Math.Min(dt,0.95*a[i]*dx/outgoing);
            }
            if(dt<1e-7||!double.IsFinite(dt))throw new InvalidOperationException("Расчёт потерял устойчивость. Проверьте исходные отметки и уменьшите резкие перепады геометрии.");
            for(int i=0;i<n;i++)
            {
                double an=a[i]-dt/dx*(fq[i+1]-fq[i]);
                double qn=q[i]-dt/dx*((fm[i+1]+corrL[i+1])-(fm[i]+corrR[i]));
                if(an<0){negativeCorrection-=an*dx;an=0;}
                double hn=geo[i].Depth(an),radius=Math.Max(geo[i].Radius(hn),0.001);
                qn/=1+dt*G*geo[i].Source.Manning*geo[i].Source.Manning*Math.Abs(qn)/(Math.Max(an,1e-8)*Math.Pow(radius,4d/3));
                if(an<1e-7)qn=0;
                if(!double.IsFinite(an)||!double.IsFinite(qn))throw new InvalidOperationException("Недопустимое состояние модели: расчёт остановлен.");
                a[i]=an;q[i]=qn;
            }
            released+=breach*dt;volume-=breach*dt;reservoir-=breach*dt/(p.ReservoirArea*1e6);outflow+=fq[n]*dt;time+=dt;result.Steps++;
            if(result.Steps>1_000_000)throw new InvalidOperationException("Превышен лимит шагов. Сократите длительность или измените сетку.");
            bool save=time>=nextSave||time>=end;
            for(int j=0;j<sample.Length;j++)
            {
                int i=sample[j];var r=result.Sections[j];var s=originals[j];
                double depth=geo[i].Depth(a[i]),level=s.Bed+depth,velocity=a[i]>1e-7?Math.Abs(q[i]/a[i]):0;
                r.MaxDischarge=Math.Max(r.MaxDischarge,Math.Abs(q[i]));r.MaxVelocity=Math.Max(r.MaxVelocity,velocity);
                if(depth>r.MaxDepth){r.MaxDepth=depth;r.MaxLevel=level;r.PeakHours=time/3600;r.DischargeAtPeak=q[i];}
                double reference=s.NormalDepth;
                if(baseline!=null)
                {
                    var history=baseline.Sections[j].DepthHistory;double hours=time/3600;
                    int low=0,high=history.Count-1;
                    while(high-low>1){int mid=(low+high)/2;if(history[mid].X<hours)low=mid;else high=mid;}
                    double f=Math.Clamp((hours-history[low].X)/Math.Max(history[high].X-history[low].X,1e-12),0,1);
                    reference=history[low].Y+(history[high].Y-history[low].Y)*f;
                }
                r.WaveHeight=Math.Max(r.WaveHeight,Math.Max(0,depth-reference));
                bool wet=depth>reference+p.ArrivalThreshold;
                if(wet){r.ArrivalHours??=time/3600;lastWet[j]=time;}
                stillWet[j]=wet;
                if(save){r.DepthHistory.Add(new(time/3600,depth));r.VelocityHistory.Add(new(time/3600,velocity));r.FlowHistory.Add(new(time/3600,q[i]));}
            }
            if(save){result.BreachFlow.Add(new(time/3600,breach));result.ReservoirLevel.Add(new(time/3600,reservoir));nextSave+=saveInterval;}
        }
        for(int j=0;j<sample.Length;j++)
        {
            var r=result.Sections[j];var s=originals[j];var g=geometry[j];
            if(r.ArrivalHours.HasValue&&!stillWet[j]){r.RecessionHours=lastWet[j]/3600;r.DrainageHours=Math.Max(0,r.RecessionHours.Value-r.PeakHours)*s.DrainageFactor;}
            var flood=g.Extent(r.MaxLevel);var danger=g.Extent(r.MaxLevel-1.5);var destruct=g.Extent(r.MaxLevel-3);
            r.FloodLeft=Math.Max(0,flood.Left-s.LeftWidth);r.FloodRight=Math.Max(0,flood.Right-s.RightWidth);
            r.DangerLeft=Math.Max(0,danger.Left-s.LeftWidth);r.DangerRight=Math.Max(0,danger.Right-s.RightWidth);
            r.DestructionLeft=Math.Max(0,destruct.Left-s.LeftWidth);r.DestructionRight=Math.Max(0,destruct.Right-s.RightWidth);
            if(r.ArrivalHours is double arrival)r.Evacuation=arrival<=p.EvacuationHours?"I · экстренная":arrival<=4?"II · заблаговременная":"III · при угрозе";
            double conveyance=0;
            for(int k=0;k<200;k++){double x=-flood.Left+(flood.Left+flood.Right)*(k+0.5)/200;conveyance+=Math.Pow(Math.Max(0,r.MaxLevel-g.BedAt(x)),5d/3)*(flood.Left+flood.Right)/200;}
            foreach(var (bank,sign) in new[]{("Левый",-1),("Правый",1)})
            {
                foreach(double d in Enumerable.Range(0,(int)Math.Floor(r.MaxDepth)+1).Select(value=>(double)value).Append(r.MaxDepth).Distinct().OrderByDescending(value=>value))
                {
                    var edge=g.Extent(r.MaxLevel-d);double x=sign<0?edge.Left:edge.Right;
                    double? dry=null;double terrain=r.MaxLevel-d;
                    if(terrain>s.Bed+s.NormalDepth&&r.RecessionHours.HasValue)
                    {
                        var crossings=r.DepthHistory.Where(v=>v.X>=r.PeakHours&&s.Bed+v.Y>=terrain).ToArray();
                        if(crossings.Length>0)dry=(crossings[^1].X-r.PeakHours)*s.DrainageFactor;
                    }
                    result.Isolines.Add(new(){Section=s.Name,Bank=bank,Depth=d,Distance=x,Velocity=d<=0?0:Math.Abs(r.DischargeAtPeak)/Math.Max(conveyance,1e-8)*Math.Pow(d,2d/3),DryingHours=dry});
                }
            }
            if((s.LeftBank.Count>0&&r.MaxLevel>s.LeftBank[^1].Elevation)||(s.RightBank.Count>0&&r.MaxLevel>s.RightBank[^1].Elevation))result.Warnings.Add($"{s.Name}: вода выходит за высотный диапазон горизонталей; граница получена на продолжении профиля.");
            if(stillWet[j])result.Warnings.Add($"{s.Name}: спад не завершён к концу расчёта; продолжительность осушения не определена.");
        }
        var rows=result.Sections;
        for(int j=0;j<rows.Count-1;j++)
        {double l=rows[j+1].Distance-rows[j].Distance;result.FloodArea+=l*(rows[j].FloodLeft+rows[j].FloodRight+rows[j+1].FloodLeft+rows[j+1].FloodRight)/2;result.DangerArea+=l*(rows[j].DangerLeft+rows[j].DangerRight+rows[j+1].DangerLeft+rows[j+1].DangerRight)/2;}
        result.ReleasedVolume=released;result.FinalRiverVolume=a.Sum()*dx;result.OutflowVolume=outflow;
        result.BalanceErrorPercent=100*Math.Abs(result.FinalRiverVolume-result.InitialRiverVolume-released+outflow)/Math.Max(1,result.InitialRiverVolume+released);
        result.Warnings.Insert(0,"Исследовательская одномерная модель. Не является сертифицированным расчётом последствий аварии ГТС.");
        if(ScenarioMode.IsGate(p.Scenario)||ScenarioMode.IsCircular(p.Scenario))result.Warnings.Add("Истечение через заданное отверстие: интегрирование местного напора по площади с постоянным Cd. Обратный поток, потери в длинном водоводе, прочность, эрозия и увеличение дефекта не моделируются. Входной импульс приближён по напору над нижней отметкой отверстия.");
        result.Warnings.Add("Площадь поймы вычислена между заданными створами, без участка плотина–первый створ. Это схема, не GIS-карта.");
        result.Warnings.Add("Приход и высота волны определены относительно отдельного безаварийного расчёта с закрытым прораном. Максимумы и границы затопления относятся к полному потоку и включают начальную перестройку течения; стационарный разгон не выполнялся.");
        if(negativeCorrection>0.01)result.Warnings.Add($"Коррекция отрицательных площадей: {negativeCorrection:F3} м³. Проверьте сходимость сетки.");
        result.ElapsedSeconds=watch.Elapsed.TotalSeconds;progress?.Report(1);return result;
    }
}
