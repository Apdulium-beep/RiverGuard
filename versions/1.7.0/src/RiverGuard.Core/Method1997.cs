using System.Diagnostics;

namespace RiverGuard.Core;

public static class CalculationModes
{
    public const string Rg1D="RG-1D";
    public const string Method1997="Методика 1997 · экспериментальный";
}

// Отдельные параметры: не переиспользовать долю длины плотины из RG-1D.
public sealed class Method1997Settings : Observable
{
    private double referenceDepth=45,reservoirWidth=345,areaFraction=.5;
    public double ReferenceDepth {get=>referenceDepth;set=>Set(ref referenceDepth,value);}
    public double ReservoirWidth {get=>reservoirWidth;set=>Set(ref reservoirWidth,value);}
    public double AreaFraction {get=>areaFraction;set=>Set(ref areaFraction,value);}
}

public sealed class Method1997Diagnostics
{
    public double ReservoirShape {get;set;}
    public double ValleyShape {get;set;}
    public double BreachCoefficient {get;set;}
    public double AccidentVolumeMillion {get;set;}
    public double ReleasedVolumeMillion {get;set;}
    public double SourcePeakDischarge {get;set;}
    public double EmptyingHours {get;set;}
    public double ZeroDepth {get;set;}
    public double SourceFlowDepth {get;set;}
    public double SourceVelocity {get;set;}
    public int Iterations {get;set;}
    public int FrontCorrections {get;set;}
    public int SubsequentCorrections {get;set;}
}

// Проверяемая транскрипция скана, печатные с. 14–20, формулы (10)–(54).
// Nр задаётся явно: таблица «2» в переданном скане не идентифицирована.
public sealed class Method1997Solver
{
    public const string Version="ВНИИ ГОЧС 1997 / experimental 1";
    public static List<string> Errors(ProjectData p)
    {
        var e=new List<string>();
        if(p.Method1997==null||p.Zones==null||p.Sections==null){e.Add("Отсутствуют параметры методики, зон или створов.");return e;}
        var m=p.Method1997;
        if(p.Scenario!=ScenarioMode.Breach)e.Add("Методика 1997 поддерживает только мгновенный прорыв плотины; выберите RG-1D для других механизмов.");
        if(p.FormationMinutes!=0)e.Add("Методика 1997 предполагает мгновенное разрушение: время формирования должно быть 0.");
        if(p.UseDestructionFraction)e.Add("Отключите долю длины плотины RG-1D: в методике используется отдельная доля площади Ер.");
        if(new[]{m.ReferenceDepth,m.ReservoirWidth,m.AreaFraction,p.ReservoirLevel,p.DamBed,p.ReservoirVolume,p.ReservoirArea,p.SillElevation,p.LowerDepth,p.LowerWidth,p.LowerVelocity,p.EvacuationHours}.Any(x=>!double.IsFinite(x)))e.Add("Методика: все параметры должны быть конечными.");
        if(m.ReferenceDepth<=0||m.ReservoirWidth<=p.LowerWidth||p.LowerWidth<=0||p.LowerDepth<=0||m.ReferenceDepth<=p.LowerDepth||p.LowerVelocity<=0||p.ReservoirVolume<=0||p.ReservoirArea<=0)e.Add("Методика: Hв > Hб0 > 0, Вв > Вб0 > 0; объём, площадь и бытовая скорость должны быть положительными.");
        if(m.AreaFraction<=0||m.AreaFraction>1)e.Add("Методика: Ер — доля площади в диапазоне (0; 1], не процент и не доля длины.");
        double h=p.ReservoirDepth,threshold=p.SillElevation-p.DamBed;
        if(h<=Math.Max(threshold,p.LowerDepth)||h>m.ReferenceDepth||threshold<0)e.Add("Методика: глубина при аварии должна быть выше порога и НБ и не выше глубины при НПУ; порог не ниже дна.");
        if(p.LowerBedElevation.HasValue&&(!double.IsFinite(p.LowerBedElevation.Value)||Math.Abs(p.LowerBedElevation.Value-p.DamBed)>1e-8))e.Add("Методика (1): дно нулевого створа равно дну у плотины. Для отдельного перепада дна используйте RG-1D.");
        if(p.ReservoirReferenceLevel.HasValue&&Math.Abs(p.ReservoirReferenceLevel.Value-(p.DamBed+m.ReferenceDepth))>1e-8)e.Add("Методика: опорный уровень должен совпадать с НПУ = дно + Hв; линейная связь объёма RG-1D не применяется.");
        if(p.EvacuationHours<=0||p.EvacuationHours>4)e.Add("Порог эвакуации должен быть в (0; 4] ч.");
        if(p.Sections.Count is <1 or >100)e.Add("Методика: необходимо от 1 до 100 створов.");
        if(p.Sections.Select(s=>s.Name).Distinct().Count()!=p.Sections.Count||p.Sections.Any(s=>string.IsNullOrWhiteSpace(s.Name)))e.Add("Названия створов должны быть непустыми и различными.");
        double last=0,previousLevel=p.DamBed+p.LowerDepth;
        foreach(var s in p.Sections.OrderBy(s=>s.Distance))
        {
            if(new[]{s.Distance,s.Bed,s.NormalDepth,s.InitialVelocity,s.LeftWidth,s.RightWidth,s.MethodShape,s.MethodConnection}.Any(v=>!double.IsFinite(v))||s.Distance<=last||s.NormalDepth<=0||s.InitialVelocity<=0||s.LeftWidth<=0||s.RightWidth<=0||s.MethodShape<0||s.MethodShape>10||s.MethodConnection<=0||s.MethodConnection>100)e.Add(s.Name+": проверьте геометрию, скорость, K (0–10) и Nр (0–100, не включая 0).");
            if(s.Bed+s.NormalDepth>=previousLevel)e.Add(s.Name+": бытовые отметки воды должны уменьшаться вниз по реке (инструкция «Волны», с. 6).");
            if(string.IsNullOrWhiteSpace(s.MethodCoefficientSource))e.Add(s.Name+": укажите источник/обоснование K и Nр; значения по умолчанию не считаются подтверждёнными.");
            foreach(var (bank,width) in new[]{(s.LeftBank,s.LeftWidth),(s.RightBank,s.RightWidth)})
            {
                double x=width,z=s.Bed+s.NormalDepth;
                foreach(var point in bank){if(!double.IsFinite(point.Distance)||!double.IsFinite(point.Elevation)||point.Distance<=x||point.Elevation<z)e.Add(s.Name+": недопустимая береговая геометрия.");x=point.Distance;z=point.Elevation;}
            }
            previousLevel=s.Bed+s.NormalDepth;last=s.Distance;
        }
        var c=p.Zones;
        if(new[]{c.FloodDepth,c.DangerDepth,c.DestructionDepth,c.AlternativeDepth,c.AlternativeVelocity,c.DestructionArrivalHours}.Any(v=>!double.IsFinite(v)||v<0)||c.FloodDepth>c.DangerDepth||c.DangerDepth>c.DestructionDepth||c.AlternativeDepth<c.FloodDepth||c.DestructionArrivalHours<=0)e.Add("Недопустимые критерии сценарных зон.");
        if(c.UseDepthAndVelocity)e.Add("Методика: альтернативная ВКЗМ по локальной скорости недоступна — поперечное распределение скорости этим режимом не восстанавливается.");
        return e.Distinct().ToList();
    }
    public SimulationResult Run(ProjectData original,IProgress<double>? progress=null,CancellationToken cancellation=default)
    {
        var errors=Errors(original);if(errors.Count>0)throw new ArgumentException(string.Join("\n",errors));
        cancellation.ThrowIfCancellationRequested();var watch=Stopwatch.StartNew();var p=original.CopyInputs();var m=p.Method1997;
        double hv=m.ReferenceDepth,hr=p.ReservoirDepth,threshold=p.SillElevation-p.DamBed;
        double nv=hv*p.ReservoirArea/p.ReservoirVolume; // (10), км² = млн м²
        double k0=Math.Log(m.ReservoirWidth/p.LowerWidth)/Math.Log(hv/p.LowerDepth); // (15)
        double a0=m.ReservoirWidth/Math.Pow(hv,k0),kp=k0; // ограничение: форма прорана = форма долины
        double ap=m.AreaFraction*a0*Math.Pow(hr,k0+1)/Math.Pow(hr-threshold,kp+1); // (18), Ер/100
        double accident=p.ReservoirVolume*Math.Pow(hr/hv,nv); // (11),(13)
        double lower=Math.Max(threshold,p.LowerDepth),wc=accident*(1-Math.Pow(lower/hr,nv)); // (14)
        double hc=(hr-threshold)*Math.Pow((2*kp+2)/(2*kp+3),2); // (19)
        double vc=Math.Sqrt(9.81*hc/(kp+1)); // (20)
        double qc=ap/(kp+1)*vc*Math.Pow(hc,kp+1); // (21)
        double kc=nv-k0-1.5; // (22)
        double tc=Math.Abs(kc)<1e-10?wc*1000/(3.6*qc)*4.324/Math.Pow(nv,.667):accident*nv*1000/(3.6*qc*kc)*(1-Math.Pow(lower/hr,kc)); // (23), ветвь Kc=0 буквально по скану
        double qb0=p.LowerDepth*p.LowerWidth*p.LowerVelocity/((k0+1)*1.55); // (9)
        double h0=p.LowerDepth*Math.Pow(qc/qb0,1/(k0+1.667)); // (24), нулевой створ использует K0
        if(new[]{nv,k0,ap,accident,wc,hc,vc,qc,tc,qb0,h0}.Any(v=>!double.IsFinite(v)||v<=0))throw new InvalidOperationException("Методика: недопустимый промежуточный результат формул (10)–(29). Проверьте область применимости.");
        var result=new SimulationResult{Source=p,ModelVersion=Version,HasMassBalance=false,ReleasedVolume=wc*1e6,BaseDischarge=qb0,
            Method1997=new(){ReservoirShape=nv,ValleyShape=k0,BreachCoefficient=ap,AccidentVolumeMillion=accident,ReleasedVolumeMillion=wc,SourcePeakDischarge=qc,EmptyingHours=tc,ZeroDepth=h0,SourceFlowDepth=hc,SourceVelocity=vc}};
        double front0=0,tail0=tc,crest0=0,distance0=0;
        var sections=p.Sections.OrderBy(s=>s.Distance).ToArray();
        for(int index=0;index<sections.Length;index++)
        {
            cancellation.ThrowIfCancellationRequested();var s=sections[index];double length=s.Distance-distance0,k=s.MethodShape,nr=s.MethodConnection,np=1.51;
            double qb=s.NormalDepth*(s.LeftWidth+s.RightWidth)*s.InitialVelocity/((k+1)*1.55); // (9)
            double c0=Math.Sqrt(9.81*h0/(k+1)),cf=Math.Sqrt(9.81*s.NormalDepth/(k+1)); // (30),(31),(41)
            double tail=tail0+length/(3600*s.InitialVelocity),front=0,peak=0,q=0,h=0,cg=0;
            bool converged=false;
            for(int iteration=0;iteration<256;iteration++)
            {
                cancellation.ThrowIfCancellationRequested();result.Method1997.Iterations++;
                front=index==0?front0+2*length/(3600*(c0+cf)):front0+length/(3600*cf);
                if(tail<=front)throw new InvalidOperationException(s.Name+": хвост не позже фронта; формулы (34)/(44) вне области применимости.");
                q=2*wc*1000/(3.6*(tail-front))+qb; // (34),(44)
                h=s.NormalDepth*Math.Pow(nr*q/qb,1/(k+1.667)); // (36),(46)
                cg=s.InitialVelocity/(np*nr)*Math.Pow(h/s.NormalDepth,.667); // (37),(47), м/с; в скане единица м/ч противоречит делителю 3600
                peak=crest0+length/(3600*cg); // (38),(48)
                if(!double.IsFinite(h)||!double.IsFinite(cg)||cg<=0)throw new InvalidOperationException(s.Name+": нечисловой результат методики.");
                if(index==0&&cg>cf*(1+1e-10)){result.Method1997.FrontCorrections++;cf=cg;continue;} // повтор (31)–(37)
                if(index>0&&peak<front-1e-10)
                {
                    result.Method1997.SubsequentCorrections++;
                    // (48): принять Tфi=Tг,i−1 и повторить (43)–(48).
                    front=crest0;
                    q=2*wc*1000/(3.6*(tail-front))+qb;h=s.NormalDepth*Math.Pow(nr*q/qb,1/(k+1.667));
                    cg=s.InitialVelocity/(np*nr)*Math.Pow(h/s.NormalDepth,.667);peak=crest0+length/(3600*cg);
                }
                converged=true;break;
            }
            if(!converged||new[]{front,tail,peak,q,h,cg}.Any(v=>!double.IsFinite(v)||v<0)||front>peak+1e-8||peak>tail+1e-8||h<s.NormalDepth)
                throw new InvalidOperationException(s.Name+": методика не дала согласованных результатов за 256 итераций. Времена не переставляются; используйте RG-1D или уточните коэффициенты.");
            var row=new SectionResult{Name=s.Name,Distance=s.Distance,InitialDepth=s.NormalDepth,InitialDischarge=qb,MaxDepth=h,MaxLevel=s.Bed+h,WaveHeight=h-s.NormalDepth,MaxDischarge=q,
                MaxVelocity=Math.Sqrt(9.81*h/(2*(k+1))), // (53), не Vг по (52)
                MethodCrestVelocity=s.InitialVelocity*Math.Pow(h/s.NormalDepth,.667), // (52)
                MethodFloodWidth=(s.LeftWidth+s.RightWidth)/s.NormalDepth*Math.Pow(h,k), // (4),(51), буквальная A=Bм/Hм
                MethodFrontSpeed=cf,DischargeAtPeak=q,ArrivalHours=front,PeakHours=peak,LevelPeakHours=peak,RecessionHours=tail};
            if(new[]{row.MaxLevel,row.MaxVelocity,row.MethodCrestVelocity!.Value,row.MethodFloodWidth!.Value}.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException(s.Name+": переполнение производных параметров методики.");
            // Дополнение RiverGuard: пересечение уровня с введённым береговым профилем, не формулы методики.
            var geometry=new SectionGeometry(s,Math.Max(40,h+10));
            var flood=geometry.Extent(row.MaxLevel-p.Zones.FloodDepth);var danger=geometry.Extent(row.MaxLevel-p.Zones.DangerDepth);
            var destruction=ZoneEvaluation.DestructionReached(p.Zones,front)?geometry.Extent(row.MaxLevel-p.Zones.DestructionDepth):(Left:0d,Right:0d);
            row.FloodLeft=Math.Max(0,flood.Left-s.LeftWidth);row.FloodRight=Math.Max(0,flood.Right-s.RightWidth);
            row.DangerLeft=Math.Max(0,danger.Left-s.LeftWidth);row.DangerRight=Math.Max(0,danger.Right-s.RightWidth);
            row.DestructionLeft=Math.Max(0,destruction.Left-s.LeftWidth);row.DestructionRight=Math.Max(0,destruction.Right-s.RightWidth);
            if(new[]{row.FloodLeft,row.FloodRight,row.DangerLeft,row.DangerRight,row.DestructionLeft,row.DestructionRight}.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException(s.Name+": недопустимые геометрические границы.");
            if(s.LeftBank.Count==0||s.RightBank.Count==0||s.LeftBank.Count>0&&row.MaxLevel>s.LeftBank[^1].Elevation||s.RightBank.Count>0&&row.MaxLevel>s.RightBank[^1].Elevation)
                result.Warnings.Add(s.Name+": уровень выходит за заданные береговые горизонтали (или они отсутствуют); границы схемы используют экстраполяцию профиля RiverGuard, не исходную топосъёмку.");
            row.Evacuation=front<=p.EvacuationHours?"I · экстренная":front<=4?"II · до 4 часов":"III · поздняя";
            result.Sections.Add(row);distance0=s.Distance;front0=front;tail0=tail;crest0=peak;
            progress?.Report((index+1d)/sections.Length);
        }
        for(int i=1;i<result.Sections.Count;i++)
        {
            var a=result.Sections[i-1];var b=result.Sections[i];double dx=(b.Distance-a.Distance)/2;
            result.FloodArea+=dx*(a.FloodLeft+a.FloodRight+b.FloodLeft+b.FloodRight);
            result.DangerArea+=dx*(a.DangerLeft+a.DangerRight+b.DangerLeft+b.DangerRight);
            result.DestructionArea+=dx*(a.DestructionLeft+a.DestructionRight+b.DestructionLeft+b.DestructionRight);
        }
        result.Warnings.AddRange(new[]{SourceCatalog.MethodLimitations,
            "Методика: Nр и K задаются пользователем с обоснованием. Таблица Nр не восстановлена; это не подтверждённое воспроизведение программы «Волна».",
            "Нет временных рядов, гидрографа, баланса массы или изодат осушения: методика вычисляет сводные максимумы и времена. Нулевая невязка не заявляется.",
            "ВЗМ/ВКЗМ/КР и площади — дополнительная геометрическая постобработка RiverGuard по введённым профилям; ширина B по формуле (51) хранится отдельно.",p.Zones.Description});
        result.ElapsedSeconds=watch.Elapsed.TotalSeconds;return result;
    }
}
