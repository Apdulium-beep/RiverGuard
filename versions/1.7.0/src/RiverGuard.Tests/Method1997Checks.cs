using System.Text.Json;
using RiverGuard.Core;

internal static class Method1997Checks
{
    public static void Run(Action<bool,string> check)
    {
        bool Near(double x,double y)=>Math.Abs(x-y)<=1e-8*Math.Max(1,Math.Abs(y));
        var p=Method1997Examples.Control();var snapshot=JsonSerializer.Serialize(p,ProjectData.Json);var solver=new Solver();var r=solver.Run(p);var d=r.Method1997!;
        check(Validation.Errors(p).Count==0,"method1997 valid synthetic input");
        check(r.ModelVersion==Method1997Solver.Version&&!r.HasMassBalance,"method1997 separate model identity without fabricated mass balance");
        check(JsonSerializer.Serialize(p,ProjectData.Json)==snapshot,"method1997 immutable user input");
        check(Near(d.ReservoirShape,2)&&Near(d.ValleyShape,1),"method1997 equations 10 and 15 independent simple shapes");
        check(Near(d.AccidentVolumeMillion,10)&&Near(d.ReleasedVolumeMillion,9.6),"method1997 equations 11 13 14 storage at NPU and lower pool cutoff");
        check(Near(d.BreachCoefficient,2000d/81),"method1997 area fraction equation 18 not dam length fraction");
        check(Near(d.SourceFlowDepth,5.76)&&Near(d.SourceVelocity,Math.Sqrt(9.81*5.76/2)),"method1997 equations 19 20 source depth and velocity");
        double qc=2000d/81/2*Math.Sqrt(9.81*5.76/2)*5.76*5.76;
        check(Near(d.SourcePeakDischarge,qc),"method1997 equation 21 independent source discharge");
        check(Near(d.EmptyingHours,10*2*1000/(3.6*qc*(-.5))*(1-Math.Pow(.2,-.5))),"method1997 equation 23 nonzero Kc branch");
        check(r.Sections.All(s=>s.ArrivalHours<=s.PeakHours&&s.PeakHours<=s.RecessionHours),"method1997 naturally ordered times without sorting");
        check(r.Sections.All(s=>s.DepthHistory.Count==0&&s.FlowHistory.Count==0&&s.VelocityHistory.Count==0)&&r.BreachFlow.Count==0&&r.Isolines.Count==0,"method1997 no invented hydrographs isolines or drying histories");
        foreach(var s in r.Sections)
        {
            var input=p.Sections.Single(i=>i.Name==s.Name);double k=input.MethodShape,qb=input.NormalDepth*(input.LeftWidth+input.RightWidth)*input.InitialVelocity/((k+1)*1.55);
            check(Near(s.MaxDischarge,2*9.6*1000/(3.6*(s.RecessionHours!.Value-s.ArrivalHours!.Value))+qb),"method1997 equation 34/44 "+s.Name);
            check(Near(s.MaxDepth,input.NormalDepth*Math.Pow(input.MethodConnection*s.MaxDischarge/qb,1/(k+1.667))),"method1997 equation 36/46 "+s.Name);
            check(Near(s.MaxVelocity,Math.Sqrt(9.81*s.MaxDepth/(2*(k+1))))&&Near(s.MethodCrestVelocity!.Value,input.InitialVelocity*Math.Pow(s.MaxDepth/input.NormalDepth,.667)),"method1997 separate maximum and crest velocities "+s.Name);
            check(Near(s.MethodFloodWidth!.Value,(input.LeftWidth+input.RightWidth)/input.NormalDepth*Math.Pow(s.MaxDepth,k)),"method1997 literal equations 4 and 51 "+s.Name);
            check(s.DryingRate==null,"method1997 no invented drying rate "+s.Name);
        }
        var shifted=p.CopyInputs();shifted.DamBed+=100;shifted.ReservoirLevel+=100;shifted.SillElevation+=100;
        foreach(var s in shifted.Sections){s.Bed+=100;foreach(var b in s.LeftBank.Concat(s.RightBank))b.Elevation+=100;}
        var moved=solver.Run(shifted);
        check(r.Sections.Zip(moved.Sections).All(x=>Near(x.First.MaxDepth,x.Second.MaxDepth)&&Near(x.First.MaxLevel+100,x.Second.MaxLevel)&&Near(x.First.PeakHours,x.Second.PeakHours)),"method1997 vertical datum invariance");
        var smaller=p.CopyInputs();smaller.Method1997.AreaFraction=.25;var small=solver.Run(smaller);
        check(Near(small.Method1997!.SourcePeakDischarge,qc/2)&&Near(small.Method1997.EmptyingHours,d.EmptyingHours*2),"method1997 area fraction halves source peak and doubles drainage time");
        var half=p.CopyInputs();half.ReservoirLevel=105;half.SillElevation=101;var partial=solver.Run(half);
        check(Near(partial.Method1997!.AccidentVolumeMillion,2.5)&&Near(partial.Method1997.ReleasedVolumeMillion,2.1),"method1997 power storage law not linear RG law");
        var highSill=p.CopyInputs();highSill.SillElevation=103;var sill=solver.Run(highSill);
        check(Near(sill.Method1997!.ReleasedVolumeMillion,9.1),"method1997 equation14 high sill branch");
        check(Near(sill.Method1997.EmptyingHours,10*2*1000/(3.6*sill.Method1997.SourcePeakDischarge*(-.5))*(1-Math.Pow(.3,-.5))),"method1997 equation23 high sill branch");
        var irrelevant=p.CopyInputs();irrelevant.Cells=1;irrelevant.DurationHours=.0001;irrelevant.ArrivalThreshold=100;irrelevant.DischargeCoefficient=.1;irrelevant.BreachWidth=1000;
        var unchanged=solver.Run(irrelevant);check(r.Sections.Zip(unchanged.Sections).All(x=>x.First.MaxDepth==x.Second.MaxDepth&&x.First.PeakHours==x.Second.PeakHours),"method1997 ignores documented RG-only settings");
        var reversed=p.CopyInputs();reversed.Sections=new(reversed.Sections.Reverse());var sorted=solver.Run(reversed);check(r.Sections.Zip(sorted.Sections).All(x=>x.First.MaxDepth==x.Second.MaxDepth),"method1997 stable section ordering independent of input collection order");
        var iterative=p.CopyInputs();foreach(var s in iterative.Sections)s.MethodConnection=.1;var iterated=solver.Run(iterative);
        check(iterated.Method1997!.FrontCorrections>0&&iterated.Method1997.Iterations>p.Sections.Count,"method1997 first-section equation37 correction actually iterates");
        check(iterated.Method1997.SubsequentCorrections>0&&iterated.Sections.All(s=>s.ArrivalHours<=s.PeakHours&&s.PeakHours<=s.RecessionHours),"method1997 equation48 correction recomputes rather than sorts times");
        var kcZero=p.CopyInputs();kcZero.ReservoirArea=2.5;var zero=solver.Run(kcZero);
        check(Near(zero.Method1997!.EmptyingHours,zero.Method1997.ReleasedVolumeMillion*1000/(3.6*zero.Method1997.SourcePeakDischarge)*4.324/Math.Pow(2.5,.667)),"method1997 literal Kc zero branch in scan");
        var zones=p.CopyInputs();zones.Zones.DangerDepth=2;var zoned=solver.Run(zones);
        check(r.Sections.Zip(zoned.Sections).All(x=>x.First.MaxDepth==x.Second.MaxDepth)&&zoned.DangerArea<=r.DangerArea,"method1997 zone postprocessing cannot change hydraulics");
        var record=CalculationRecord.Create(p,r);check(record.BalanceErrorPercent==null&&record.Input.CalculationMode==CalculationModes.Method1997&&CalculationRecord.Create(p,null,"Проверочная ошибка").ModelVersion==Method1997Solver.Version,"method1997 history records mode and undefined balance including failures");
        p.CalculationHistory.Add(record);var copy=p.Copy();check(copy.SchemaVersion==1&&copy.Method1997.AreaFraction==.5&&copy.Sections[0].MethodCoefficientSource.Length>0&&copy.CalculationHistory[0].Input.CalculationHistory.Count==0,"method1997 schema one coefficients and history round trip");
        var legacy=JsonSerializer.Deserialize<ProjectData>("{\"SchemaVersion\":1}",ProjectData.Json)!;check(legacy.CalculationMode==CalculationModes.Rg1D&&legacy.Method1997!=null,"method1997 legacy projects remain RG1D");
        void Rejected(Action<ProjectData> change,string label){var bad=p.CopyInputs();change(bad);check(Validation.Errors(bad).Count>0,"method1997 rejected "+label);}
        Rejected(x=>x.Sections[0].MethodCoefficientSource="","unattributed coefficients");
        Rejected(x=>x.Method1997.AreaFraction=50,"percentage mistaken for fraction");
        Rejected(x=>x.Method1997.AreaFraction=0,"closed breach undefined analytic wave");
        Rejected(x=>x.Method1997.ReferenceDepth=x.LowerDepth,"singular shape denominator");
        Rejected(x=>x.Method1997.ReservoirWidth=x.LowerWidth,"invalid valley shape");
        Rejected(x=>x.Sections[0].MethodConnection=double.NaN,"nonfinite coefficient");
        Rejected(x=>x.Sections[0].MethodShape=-1,"singular section exponent");
        Rejected(x=>x.FormationMinutes=1,"finite formation time");
        Rejected(x=>x.UseDestructionFraction=true,"RG1D length fraction");
        Rejected(x=>x.Scenario=ScenarioMode.Gate,"gate mechanism");
        Rejected(x=>x.LowerVelocity=0,"zero domestic velocity");
        Rejected(x=>x.SillElevation=x.ReservoirLevel,"no available head");
        Rejected(x=>x.LowerBedElevation=x.DamBed-1,"unsupported independent lower bed");
        Rejected(x=>x.ReservoirReferenceLevel=x.ReservoirLevel-1,"linear volume reference mismatch");
        Rejected(x=>x.Sections[1].Distance=x.Sections[0].Distance,"duplicate section distance");
        Rejected(x=>x.Sections[0].Bed=x.ReservoirLevel,"uphill domestic water level");
        Rejected(x=>x.Zones.UseDepthAndVelocity=true,"unavailable transverse velocity criterion");
        Rejected(x=>x.CalculationMode="Unknown","unknown mode rather than fallback");
        bool cancelled=false;using(var cts=new CancellationTokenSource()){cts.Cancel();try{solver.Run(p,cancellation:cts.Token);}catch(OperationCanceledException){cancelled=true;}}
        check(cancelled,"method1997 cancellation before analytic computation");
        check(SourceCatalog.Entries.Count>=4&&SourceCatalog.ScanSha256.Length==64&&SourceCatalog.ReportText.Contains("не сертификация")&&SourceCatalog.ReportText.Contains("Nр"),"method1997 sources expose provenance and unresolved dependencies");
    }
}
