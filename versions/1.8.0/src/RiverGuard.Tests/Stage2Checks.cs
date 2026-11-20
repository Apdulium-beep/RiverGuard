using System.Text.Json;
using RiverGuard.Core;

// Проверки критериев зон и истории запусков
internal static class Stage2Checks
{
    public static void Run(Action<bool,string> check)
    {
        var c=new ZoneCriteria();
        check(c.FloodDepth==0&&c.DangerDepth==1.5&&c.DestructionDepth==3&&!c.UseDepthAndVelocity&&c.AlternativeDepth==1&&c.AlternativeVelocity==.7,"stage2 legacy zone defaults remain explicit and alternative is disabled");
        check(ZoneEvaluation.EffectiveDangerDepth(c,0,1)==1.5,"stage2 default danger criterion depends only on depth");
        c.UseDepthAndVelocity=true;
        check(double.IsPositiveInfinity(ZoneEvaluation.EffectiveDangerDepth(c,0,1)),"stage2 zero velocity cannot satisfy active velocity criterion");
        check(Math.Abs(ZoneEvaluation.EffectiveDangerDepth(c,.7,1)-1)<1e-12,"stage2 alternative combines depth AND velocity");
        check(ZoneEvaluation.EffectiveDangerDepth(c,.1,1)>1,"stage2 velocity criterion raises required local depth");
        check(ZoneEvaluation.DestructionReached(c,1)&&!ZoneEvaluation.DestructionReached(c,1.001)&&!ZoneEvaluation.DestructionReached(c,null),"stage2 catastrophic destruction requires arrival within first hour");
        var p=Examples.Valley();p.ReservoirLevel=130;p.FormationMinutes=0;p.BreachWidth=100;p.Cells=40;p.DurationHours=.2;
        var solver=new Solver();var a=solver.Run(p);
        check(a.FloodArea>0&&a.Sections.Any(s=>s.DestructionLeft+s.DestructionRight>0),"stage2 high-water fixture actually floods banks and has early destruction zone");
        check(a.Isolines.Any(i=>i.DryingHours.HasValue),"stage2 high-water fixture contains completed drying events");
        check(a.Isolines.Where(i=>i.DryingHours.HasValue).All(i=>
        {
            var s=a.Sections.First(s=>s.Name==i.Section);var input=a.Source.Sections.First(s=>s.Name==i.Section);double terrain=s.MaxLevel-i.Depth;
            return i.DryingHours>0&&terrain>input.Bed+s.InitialDepth&&input.Bed+s.DepthHistory[^1].Y<terrain&&i.DryingRate.HasValue;
        }),"stage2 drying rate is defined only for terrain that was initially and finally dry");
        p.Zones.DestructionArrivalHours=1e-8;var b=solver.Run(p);
        check(b.Sections.All(s=>s.DestructionLeft==0&&s.DestructionRight==0)&&b.DestructionArea==0,"stage2 late front removes catastrophic zone without removing flood");
        check(a.Sections.Zip(b.Sections).All(v=>v.First.MaxDischarge==v.Second.MaxDischarge&&v.First.MaxDepth==v.Second.MaxDepth),"stage2 zone classification never changes hydraulics");
        p.Zones.DangerDepth=2.5;var d=solver.Run(p);
        check(d.DangerArea<=b.DangerArea&&d.Sections.Zip(b.Sections).All(v=>v.First.DangerLeft<=v.Second.DangerLeft+1e-10&&v.First.DangerRight<=v.Second.DangerRight+1e-10),"stage2 higher danger depth cannot enlarge danger zone");
        p.Zones.FloodDepth=-1;check(Validation.Errors(p).Count>0,"stage2 negative zone threshold rejected");p.Zones.FloodDepth=0;
        p.Zones.DangerDepth=4;check(Validation.Errors(p).Count>0,"stage2 inverted zone thresholds rejected");p.Zones.DangerDepth=1.5;
        p.Zones.AlternativeVelocity=double.NaN;check(Validation.Errors(p).Count>0,"stage2 nonfinite optional zone threshold rejected");p.Zones.AlternativeVelocity=.7;
        var record=CalculationRecord.Create(a.Source,a);p.CalculationHistory.Add(record);
        var snapshot=p.CopyInputs();
        check(snapshot.CalculationHistory.Count==0&&record.Input.CalculationHistory.Count==0,"stage3 run snapshots never recursively include history");
        var saved=p.Copy();check(saved.SchemaVersion==1&&saved.CalculationHistory.Count==1&&saved.CalculationHistory[0].Sections.Count==a.Sections.Count,"stage3 history survives schema-one project round trip");
        double oldLevel=record.Input.ReservoirLevel;p.ReservoirLevel+=1;
        check(record.Input.ReservoirLevel==oldLevel,"stage3 editing inputs never changes stored run snapshot");
        var second=CalculationRecord.Create(b.Source,b);second.Input.ReservoirLevel+=1;
        var comparison=HistoryComparison.Compare(record,second);
        check(comparison.First(r=>r.Quantity=="Уровень водохранилища, м").Difference=="1,000","stage3 comparison displays signed arithmetic difference with three decimals");
        second.Sections.RemoveAt(0);comparison=HistoryComparison.Compare(record,second);
        check(comparison.Any(r=>r.Quantity.Contains(a.Sections[0].Name)&&r.Second=="не определено"),"stage3 comparison retains sections missing from one run");
        var cancelled=CalculationRecord.Create(p,null,"Отменено пользователем",true);
        check(cancelled.Status=="Отменён"&&cancelled.Sections.Count==0&&cancelled.ReleasedVolume==null,"stage3 cancelled attempts have status but no invented results");
        var failed=CalculationRecord.Create(p,null,"Проверочная ошибка");check(failed.Status=="Ошибка"&&failed.Error=="Проверочная ошибка","stage3 failed attempts retain error summary");
        string json=JsonSerializer.Serialize(saved,ProjectData.Json);check(json.Length<200000,"stage3 single-run history stays compact without time-series duplication");
    }
}
