using System.Text.Json;
using System.Text.Json.Nodes;
using RiverGuard.Core;

// Регрессии установления режима и параметров аварии
internal static class Stage1Checks
{
    public static void Run(Action<bool,string> check,string? report)
    {
        var solver=new Solver();var input=Examples.ThreeSections();
        string original=JsonSerializer.Serialize(input,ProjectData.Json);
        var low=solver.Run(input);var higher=input.Copy();higher.ReservoirLevel+=1;var high=solver.Run(higher);
        var closed=input.Copy();closed.BreachWidth=0;var baseline=solver.Run(closed);
        double Relative(double value,double initial)=>Math.Abs(value-initial)/Math.Max(1e-9,Math.Abs(initial));
        check(original==JsonSerializer.Serialize(input,ProjectData.Json),"stage1 calculation and warm-up preserve user input");
        check(low.InitializationHours>0&&low.InitializationSteps>0&&low.InitializationResidualPercent<0.1,"stage1 warm-up meets documented residual criterion");
        check(baseline.ReleasedVolume==0&&baseline.BaseInflowVolume>0,"stage1 closed breach keeps domestic inflow without reservoir release");
        check(Relative(baseline.FinalRiverVolume,baseline.InitialRiverVolume)<0.02,"stage1 closed river volume stays within two percent");
        foreach(var s in baseline.Sections)
        {
            check(s.DepthHistory.All(v=>Relative(v.Y,s.InitialDepth)<0.02),"stage1 closed depth stationary within two percent: "+s.Name);
            check(s.FlowHistory.All(v=>Relative(v.Y,s.InitialDischarge)<0.02),"stage1 closed flow stationary within two percent: "+s.Name);
            check(!s.ArrivalHours.HasValue&&!s.RecessionHours.HasValue&&s.WaveHeight<1e-3,"stage1 closed regime is not an accident wave: "+s.Name);
        }
        check(high.BreachFlow.Max(v=>v.Y)>2*low.BreachFlow.Max(v=>v.Y),"stage1 physical plus one metre increases source discharge");
        foreach(var (a,b) in low.Sections.Zip(high.Sections))
        {
            check(b.MaxDischarge>a.MaxDischarge*1.05,"stage1 plus one metre increases total peak discharge by over five percent: "+a.Name);
            check(b.MaxVelocity>a.MaxVelocity*1.01,"stage1 plus one metre increases peak velocity by over one percent: "+a.Name);
            check(b.MaxDepth>a.MaxDepth*1.01,"stage1 plus one metre increases peak depth by over one percent: "+a.Name);
            check(a.InitialDepth==baseline.Sections[low.Sections.IndexOf(a)].InitialDepth,"stage1 accident and baseline share one prepared state: "+a.Name);
        }
        var translated=input.Copy();translated.ReservoirLevel+=1;translated.DamBed+=1;translated.SillElevation+=1;
        foreach(var s in translated.Sections){s.Bed+=1;foreach(var pt in s.LeftBank.Concat(s.RightBank))pt.Elevation+=1;}
        var moved=solver.Run(translated);
        check(low.Sections.Zip(moved.Sections).All(v=>Math.Abs(v.First.MaxDischarge-v.Second.MaxDischarge)<1e-7&&Math.Abs(v.First.MaxVelocity-v.Second.MaxVelocity)<1e-8&&Math.Abs(v.First.MaxDepth-v.Second.MaxDepth)<1e-8&&Math.Abs(v.Second.MaxLevel-v.First.MaxLevel-1)<1e-8),"stage1 datum shift preserves Q V H and translates Z");
        check(low.Sections.Zip(moved.Sections).All(v=>Math.Abs(v.First.PeakHours-v.Second.PeakHours)<1e-8&&SameTime(v.First.ArrivalHours,v.Second.ArrivalHours)&&SameTime(v.First.RecessionHours,v.Second.RecessionHours)),"stage1 datum shift preserves all wave event times");
        var dry=input.Copy();dry.ReservoirLevel=70.1;var blocked=solver.Run(dry);
        check(blocked.ReleasedVolume==0&&blocked.BreachFlow.All(v=>v.Y==0),"stage1 reservoir below sill gives strictly zero source flow");
        check(blocked.Sections.All(s=>!s.ArrivalHours.HasValue),"stage1 below-sill input has no accident arrival");
        var brief=input.Copy();brief.ReservoirLevel+=1;brief.DurationHours=0.1;var truncated=solver.Run(brief);
        check(truncated.Sections.Any(s=>s.ArrivalHours.HasValue&&!s.RecessionHours.HasValue)&&truncated.Warnings.Any(w=>w.Contains("Увеличьте горизонт")),"stage1 unfinished recession remains undefined with horizon warning");
        var longer=input.Copy();longer.DurationHours=12;var extended=solver.Run(longer);
        foreach(var result in new[]{low,high,baseline,moved,blocked,truncated,extended})
        {
            check(result.Sections.All(s=>!s.RecessionHours.HasValue||s.ArrivalHours.HasValue&&s.ArrivalHours.Value<=s.PeakHours&&s.RecessionHours.Value>=s.PeakHours),"stage1 wave events remain ordered: "+result.Source.ReservoirLevel+" / "+result.Source.DurationHours+"h / "+result.Source.BreachWidth);
            double balance=result.FinalRiverVolume-result.InitialRiverVolume-result.ReleasedVolume-result.BaseInflowVolume+result.OutflowVolume;
            check(Math.Abs(balance)<0.001&&result.BalanceErrorPercent<0.001,"stage1 independent mass balance includes domestic inflow: "+result.Source.DurationHours+"h");
        }
        check(low.Sections.Zip(extended.Sections).All(v=>Math.Abs(v.First.MaxDischarge-v.Second.MaxDischarge)<1e-7&&Math.Abs(v.First.PeakHours-v.Second.PeakHours)<1e-6),"stage1 longer horizon preserves early peak and crest");
        var linked=input.Copy();linked.ReservoirReferenceLevel=input.ReservoirLevel;linked.ReservoirLevel+=1;
        check(Math.Abs(linked.EffectiveReservoirVolume-13.92)<1e-10,"stage1 area law converts plus one metre into 13.920 million cubic metres");
        var linkedResult=solver.Run(linked);
        check(linkedResult.ReleasedVolume<=linked.EffectiveReservoirVolume*1e6&&linkedResult.Source.ReservoirVolume==11.6,"stage1 linked volume is used without overwriting reference input");
        var smallLinked=input.Copy();smallLinked.ReservoirVolume=0.000001;smallLinked.ReservoirReferenceLevel=70.3;var linkedCapacity=solver.Run(smallLinked);
        check(linkedCapacity.ReleasedVolume>smallLinked.ReservoirVolume*1e6&&linkedCapacity.ReleasedVolume<=smallLinked.EffectiveReservoirVolume*1e6,"stage1 storage cap uses effective volume rather than reference volume");
        linked.ReservoirReferenceLevel=100;check(Validation.Errors(linked).Count>0,"stage1 nonpositive effective reservoir volume rejected");
        var fractional=input.Copy();fractional.UseDestructionFraction=true;fractional.DamLength=120;fractional.DestructionFraction=0.1;fractional.BreachWidth=999;
        check(fractional.EffectiveBreachWidth==12,"stage1 destruction degree means fraction of dam length");
        var fractionResult=solver.Run(fractional);
        check(Math.Abs(fractionResult.ReleasedVolume-low.ReleasedVolume)<1e-8,"stage1 fraction and explicit width produce identical discharge");
        fractional.DestructionFraction=0;check(solver.Run(fractional).ReleasedVolume==0,"stage1 zero destruction fraction closes breach including baseline");
        fractional.DestructionFraction=1.1;check(Validation.Errors(fractional).Count>0,"stage1 destruction fraction outside zero to one rejected");
        var still=input.Copy();still.LowerVelocity=0;still.BreachWidth=0;var hydrostatic=solver.Run(still);
        check(hydrostatic.BaseDischarge==0&&hydrostatic.Sections.All(s=>s.MaxVelocity<1e-7&&!s.ArrivalHours.HasValue),"stage1 zero domestic inflow uses a stationary hydrostatic reference");
        check(Math.Abs(hydrostatic.FinalRiverVolume-hydrostatic.InitialRiverVolume)<1e-5,"stage1 zero-inflow hydrostatic state preserves river volume");
        var copyInput=input.Copy();copyInput.ReservoirReferenceLevel=71.3;copyInput.UseDestructionFraction=true;copyInput.DamLength=130;copyInput.DestructionFraction=0.2;copyInput.LowerBedElevation=60.2;
        var copy=copyInput.Copy();
        check(copy.SchemaVersion==1&&copy.ReservoirReferenceLevel==71.3&&copy.UseDestructionFraction&&copy.DamLength==130&&copy.DestructionFraction==0.2&&copy.LowerBedElevation==60.2,"stage1 optional hydraulic parameters survive project round trip");
        var separate=input.Copy();separate.LowerBedElevation=60.2;var separateResult=solver.Run(separate);
        check(RiverGeometry.WithOrigin(separate)[0].Bed==60.2&&separate.DamBed==65.3&&separateResult.BalanceErrorPercent<0.001,"stage1 downstream bed is independent of reservoir bed");
        separate.LowerBedElevation=double.NaN;check(Validation.Errors(separate).Count>0,"stage1 nonfinite downstream bed rejected");
        var legacy=JsonNode.Parse(original)!;
        foreach(string key in new[]{"ReservoirReferenceLevel","UseDestructionFraction","DamLength","DestructionFraction","LowerBedElevation"})legacy.AsObject().Remove(key);
        var loaded=legacy.Deserialize<ProjectData>(ProjectData.Json)!;
        check(loaded.SchemaVersion==1&&loaded.ReservoirReferenceLevel==null&&!loaded.UseDestructionFraction&&loaded.LowerBedElevation==null&&loaded.EffectiveBreachWidth==12&&loaded.EffectiveReservoirVolume==11.6&&Validation.Errors(loaded).Count==0,"stage1 legacy schema one projects retain safe parameter defaults");
        var saved=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(higher,ProjectData.Json),ProjectData.Json)!;
        check(saved.SchemaVersion==1&&saved.ReservoirLevel==72.3,"stage1 project schema stays at one");
        var cancel=new CancellationTokenSource();var progress=new CancelProgress(cancel);bool cancelled=false;
        try{solver.Run(input,progress,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled,"stage1 cancellation interrupts warm-up");
        if(report!=null)
        {
            var reference=new[]{new{Q=306.95,V=4.59,H=6.82,Front=0d,Peak=.05,Recession=11.2},new{Q=303.69,V=4.61,H=6.78,Front=.04,Peak=.41,Recession=11.59},new{Q=298.67,V=4.59,H=6.73,Front=.08,Peak=.83,Recession=12.04}};
            var rows=low.Sections.Select((s,i)=>new{s.Name,Low=new{s.MaxDischarge,s.MaxVelocity,s.MaxDepth,s.MaxLevel,s.WaveHeight,s.ArrivalHours,s.PeakHours,s.RecessionHours},High=new{high.Sections[i].MaxDischarge,high.Sections[i].MaxVelocity,high.Sections[i].MaxDepth},Closed=new{baseline.Sections[i].InitialDepth,baseline.Sections[i].InitialDischarge,DepthDriftPercent=100*baseline.Sections[i].DepthHistory.Max(v=>Relative(v.Y,baseline.Sections[i].InitialDepth)),FlowDriftPercent=100*baseline.Sections[i].FlowHistory.Max(v=>Relative(v.Y,baseline.Sections[i].InitialDischarge))},Reference=reference[i],DeltaQPercent=100*(s.MaxDischarge/reference[i].Q-1),DeltaVPercent=100*(s.MaxVelocity/reference[i].V-1),DeltaHPercent=100*(s.MaxDepth/reference[i].H-1)});
            File.WriteAllText(report,JsonSerializer.Serialize(new{CreatedUtc=DateTime.UtcNow,low.ModelVersion,low.InitializationHours,low.InitializationResidualPercent,low.BaseDischarge,low.ReleasedVolume,HighRelease=high.ReleasedVolume,low.InitialRiverVolume,low.FinalRiverVolume,low.BaseInflowVolume,low.OutflowVolume,low.BalanceErrorPercent,low.FloodArea,ReferenceFloodArea=51489.1,Rows=rows,MethodComparison="Diagnostic only: methodology mode is deferred; no five-percent reference-fit acceptance claimed."},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private static bool SameTime(double? a,double? b)=>a.HasValue==b.HasValue&&(!a.HasValue||Math.Abs(a.Value-b!.Value)<1e-8);
    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<double>
    {
        public void Report(double value)=>source.Cancel();
    }
}
