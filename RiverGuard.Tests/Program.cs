using RiverGuard.Core;
using System.Text.Json;

// Проверки численной модели
int passed=0;var failures=new List<string>();
void Check(bool condition,string name){if(condition){passed++;Console.WriteLine("PASS "+name);}else{failures.Add(name);Console.WriteLine("FAIL "+name);}}
var p=Examples.Valley();p.Cells=40;p.DurationHours=0.08;
var solver=new Solver();var r=solver.Run(p);
Check(r.Sections.Count==p.Sections.Count,"all sections calculated");
Check(r.BalanceErrorPercent<0.001,"mass balance");
Check(r.Sections.All(s=>double.IsFinite(s.MaxDepth)&&s.MaxDepth>=0),"finite nonnegative depths");
Check(r.Isolines.Where(s=>s.Depth==0).All(s=>s.Velocity==0),"zero velocity on dry boundary");
Check(r.ReleasedVolume>0,"breach releases water");
Check(r.ReservoirLevel.Last().Y<p.ReservoirLevel,"reservoir drawdown");
var closed=p.Copy();closed.BreachWidth=0;var zero=solver.Run(closed);
Check(zero.ReleasedVolume==0,"closed breach has no reservoir discharge");
Check(zero.Sections.All(s=>!s.ArrivalHours.HasValue),"background transients not mistaken for breach arrival");
var wider=p.Copy();wider.BreachWidth*=2;var rw=solver.Run(wider);
Check(Math.Abs(rw.ReleasedVolume-r.ReleasedVolume)>10,"breach width affects released volume");
var higher=p.Copy();higher.ReservoirLevel+=1;var rh=solver.Run(higher);
Check(Math.Abs(rh.ReleasedVolume-r.ReleasedVolume)>10,"physical reservoir level affects discharge");
var shifted=p.Copy();shifted.ReservoirLevel+=100;shifted.SillElevation+=100;shifted.DamBed+=100;
foreach(var s in shifted.Sections){s.Bed+=100;foreach(var b in s.LeftBank.Concat(s.RightBank))b.Elevation+=100;}
var rs=solver.Run(shifted);
Check(Math.Abs(rs.ReleasedVolume-r.ReleasedVolume)<0.01,"vertical datum invariance");
var terrain=p.Copy();terrain.Sections[0].LeftBank[0].Distance+=10;var rt=solver.Run(terrain);
Check(Math.Abs(rt.Sections[0].MaxDepth-r.Sections[0].MaxDepth)>1e-7,"bank geometry affects hydraulics");
var invalid=p.Copy();invalid.ReservoirArea=-1;Check(Validation.Errors(invalid).Count>0,"negative area rejected");
invalid=p.Copy();invalid.Sections[1].Distance=invalid.Sections[0].Distance;Check(Validation.Errors(invalid).Count>0,"duplicate sections rejected");
var roundtrip=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(p,ProjectData.Json),ProjectData.Json)!;
Check(roundtrip.Sections[0].LeftBank.Count==p.Sections[0].LeftBank.Count,"project JSON round trip");
double sourceLevel=r.Source.ReservoirLevel;p.ReservoirLevel+=0.001;Check(r.Source.ReservoirLevel==sourceLevel,"result source snapshot independent of edited inputs");p.ReservoirLevel-=0.001;
var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;try{solver.Run(p,null,cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancellation");
var finer=p.Copy();finer.Cells=80;var fine=solver.Run(finer);
var still=p.Copy();still.BreachWidth=0;still.LowerVelocity=0;still.LowerDepth=2;still.DamBed=100;
foreach(var s in still.Sections){double delta=100-s.Bed;s.Bed=100;s.NormalDepth=2;s.InitialVelocity=0;foreach(var b in s.LeftBank.Concat(s.RightBank))b.Elevation+=delta;}
var lake=solver.Run(still);
Check(lake.Sections.All(s=>s.WaveHeight<1e-8&&s.MaxVelocity<1e-7),"lake at rest with changing width");
var slopes=still.Copy();
foreach(var s in slopes.Sections){double drop=s.Distance/10000;s.Bed-=drop;s.NormalDepth+=drop;}
var slopedLake=solver.Run(slopes);
Check(slopedLake.Sections.All(s=>s.MaxVelocity<1e-7),"lake at rest over sloping bed");
var tiny=p.Copy();tiny.ReservoirVolume=0.00001;var empty=solver.Run(tiny);
Check(empty.ReleasedVolume<=tiny.ReservoirVolume*1e6+0.00001,"reservoir cannot release more than available volume");
var fixedCrest=p.Copy();fixedCrest.Scenario=ScenarioMode.Overflow;fixedCrest.FormationMinutes=0;fixedCrest.SillElevation=107;
var fixedNow=solver.Run(fixedCrest);fixedCrest.FormationMinutes=60;var fixedLegacy=solver.Run(fixedCrest);
Check(Math.Abs(fixedNow.ReleasedVolume-fixedLegacy.ReleasedVolume)<1e-8,"overflow ignores inapplicable breach formation time");
fixedCrest.Scenario=ScenarioMode.Breach;var growing=solver.Run(fixedCrest);
Check(Math.Abs(growing.ReleasedVolume-fixedLegacy.ReleasedVolume)>1,"breach and fixed crest use different opening laws");
var unknown=p.Copy();unknown.Scenario="Каскад плотин";Check(Validation.Errors(unknown).Count>0,"unsupported mechanism rejected instead of silently using breach model");
// Проверки затвора и круглого отверстия
var gate=p.Copy();gate.Scenario=ScenarioMode.Gate;gate.SillElevation=101;gate.BreachWidth=3;gate.GateHeight=2;gate.FormationMinutes=0;gate.DischargeCoefficient=0.62;
double unit=gate.DischargeCoefficient*gate.BreachWidth*Math.Sqrt(2*9.81);
Check(Math.Abs(SourceDischarge.Evaluate(gate,108,104,0)-unit*2*Math.Sqrt(4))<1e-10,"submerged gate matches rectangular orifice law");
Check(Math.Abs(SourceDischarge.Evaluate(gate,108,100,0)-unit*2d/3*(Math.Pow(7,1.5)-Math.Pow(5,1.5)))<1e-10,"free gate integrates hydrostatic head over opening height");
Check(Math.Abs(SourceDischarge.Evaluate(gate,108,102,0)-unit*(Math.Sqrt(6)+2d/3*(Math.Pow(6,1.5)-Math.Pow(5,1.5))))<1e-10,"mixed free and submerged gate flow is continuous");
gate.FormationMinutes=2;
Check(SourceDischarge.Evaluate(gate,108,104,0)==0,"gate starts closed with positive opening time");
Check(Math.Abs(SourceDischarge.Evaluate(gate,108,104,60)-unit*Math.Sqrt(4))<1e-10,"gate opening time changes height instead of width");
Check(Math.Abs(SourceDischarge.Evaluate(gate,108,104,120)-unit*2*Math.Sqrt(4))<1e-10,"gate stops opening at maximum height");
Check(SourceDischarge.Evaluate(gate,108,108,120)==0&&SourceDischarge.Evaluate(gate,108,109,120)==0,"gate blocks equal-head and reverse flow");
Check(SourceDischarge.Evaluate(gate,100,99,120)==0,"dry gate produces zero discharge");
var hole=gate.Copy();hole.Scenario=ScenarioMode.Circular;hole.OutletDiameter=2;
double exactHole=hole.DischargeCoefficient*Math.PI*Math.Sqrt(2*9.81*4);
Check(Math.Abs(SourceDischarge.Evaluate(hole,108,104,0)-exactHole)<1e-9,"fully submerged circular opening matches Cd A sqrt(2g deltaH)");
double reference=0;const int strips=100000;double wetHeight=0.8,strip=wetHeight/strips;
for(int i=0;i<strips;i++){double y=(i+0.5)*strip;reference+=2*Math.Sqrt(y*(2-y))*Math.Sqrt(wetHeight-y)*strip;}
reference*=hole.DischargeCoefficient*Math.Sqrt(2*9.81);
Check(Math.Abs(SourceDischarge.Evaluate(hole,101.8,100,0)-reference)/reference<0.0001,"partially wet circular opening agrees with independent strip integration");
Check(SourceDischarge.Evaluate(hole,108,108,0)==0&&SourceDischarge.Evaluate(hole,100,99,0)==0,"circular opening has zero equal-head and dry discharge");
double holeQ=SourceDischarge.Evaluate(hole,108,104,0);hole.FormationMinutes=400;hole.BreachWidth=999;
Check(Math.Abs(SourceDischarge.Evaluate(hole,108,104,0)-holeQ)<1e-12,"circular opening ignores breach width and formation time");
hole.OutletDiameter=4;
Check(Math.Abs(SourceDischarge.Evaluate(hole,110,106,0)-4*holeQ)<1e-8,"fully submerged circular discharge scales with diameter squared");hole.OutletDiameter=2;
foreach(var source in new[]{gate,hole})
{
    string label=source.Scenario;var flow=solver.Run(source);
    Check(flow.ReleasedVolume>0&&flow.BalanceErrorPercent<0.001,"new mechanism releases water with mass conservation: "+label);
    Check(flow.Sections.All(s=>double.IsFinite(s.MaxDepth)&&s.MaxDepth>=0&&double.IsFinite(s.MaxVelocity)),"new mechanism produces finite nonnegative results: "+label);
    Check(flow.Source.Scenario==source.Scenario&&flow.ModelVersion=="RG-1D / 1.2","result snapshot identifies new mechanism and model version: "+label);
    var shut=source.Copy();if(ScenarioMode.IsGate(source.Scenario))shut.BreachWidth=0;else shut.OutletDiameter=0;
    var closedResult=solver.Run(shut);Check(closedResult.ReleasedVolume==0&&closedResult.Sections.All(s=>!s.ArrivalHours.HasValue),"closed new mechanism uses correct no-accident baseline: "+label);
    var small=source.Copy();small.ReservoirVolume=0.000001;var smallResult=solver.Run(small);
    Check(smallResult.ReleasedVolume<=small.ReservoirVolume*1e6+1e-8,"new mechanism cannot exceed available reservoir volume: "+label);
    var moved=source.Copy();moved.ReservoirLevel+=100;moved.DamBed+=100;moved.SillElevation+=100;
    foreach(var section in moved.Sections){section.Bed+=100;foreach(var bank in section.LeftBank.Concat(section.RightBank))bank.Elevation+=100;}
    Check(Math.Abs(solver.Run(moved).ReleasedVolume-flow.ReleasedVolume)<0.001,"new mechanism is invariant under vertical datum shift: "+label);
    var copy=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(source,ProjectData.Json),ProjectData.Json)!;
    Check(copy.GateHeight==source.GateHeight&&copy.OutletDiameter==source.OutletDiameter&&copy.Scenario==source.Scenario,"new mechanism parameters survive JSON round trip: "+label);
}
var badGate=gate.Copy();badGate.GateHeight=0;Check(Validation.Errors(badGate).Count>0,"zero gate height rejected");
var badHole=hole.Copy();badHole.OutletDiameter=-1;Check(Validation.Errors(badHole).Count>0,"negative circular diameter rejected");
badHole.OutletDiameter=double.NaN;Check(Validation.Errors(badHole).Count>0,"nonfinite circular diameter rejected");
var legacyNode=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(p,ProjectData.Json))!;legacyNode.AsObject().Remove("GateHeight");legacyNode.AsObject().Remove("OutletDiameter");
var legacyProject=legacyNode.Deserialize<ProjectData>(ProjectData.Json)!;Check(Validation.Errors(legacyProject).Count==0&&legacyProject.GateHeight==2&&legacyProject.OutletDiameter==2,"legacy version-1 projects receive safe defaults for new fields");
Check(ScenarioCatalog.All.Count==33&&ScenarioCatalog.All.Select(s=>s.Id).Distinct().Count()==33,"catalog has 33 unique presets");
foreach(var preset in ScenarioCatalog.All)
{
    Check(Validation.Errors(preset.Create()).Count==0,"catalog inputs valid: "+preset.Id);
    var copy=preset.Create();copy.Sections[0].LeftBank[0].Elevation+=0.1;
    Check(copy.Sections[0].LeftBank[0].Elevation!=preset.Create().Sections[0].LeftBank[0].Elevation,"catalog factories return independent geometry: "+preset.Id);
}
if(args.Contains("--catalog-tests"))
{
    var summaries=new List<object>();
    foreach(var preset in ScenarioCatalog.All)
    {
        try
        {
            var input=preset.Create();string before=JsonSerializer.Serialize(input,ProjectData.Json);
            var output=solver.Run(input);
            Check(output.BalanceErrorPercent<0.001,"catalog mass balance: "+preset.Id);
            Check(output.Sections.All(s=>double.IsFinite(s.MaxDepth)&&double.IsFinite(s.MaxDischarge)&&double.IsFinite(s.MaxVelocity)),"catalog finite results: "+preset.Id);
            Check(output.ReleasedVolume<=input.ReservoirVolume*1e6+0.01,"catalog available volume: "+preset.Id);
            Check(before==JsonSerializer.Serialize(input,ProjectData.Json),"catalog solver preserves inputs: "+preset.Id);
            if(preset.Id is "base-closed" or "overflow-no-head")Check(output.ReleasedVolume==0,"catalog zero-release control: "+preset.Id);
            summaries.Add(new{preset.Id,preset.Name,input.DurationHours,input.Cells,output.Steps,output.ElapsedSeconds,output.BalanceErrorPercent,output.ReleasedVolume,MaxWave=output.Sections.Max(s=>s.WaveHeight),MaxFlow=output.Sections.Max(s=>s.MaxDischarge),Warnings=output.Warnings.Count});
        }
        catch(Exception ex){Check(false,"catalog simulation: "+preset.Id+" / "+ex.Message);}
    }
    int report=Array.IndexOf(args,"--catalog-report");
    if(report>=0&&report+1<args.Length)File.WriteAllText(args[report+1],JsonSerializer.Serialize(new{Created=DateTime.Now,Scenarios=summaries,Failures=failures},new JsonSerializerOptions{WriteIndented=true}));
}
int catalogOutput=Array.IndexOf(args,"--catalog-output");
if(catalogOutput>=0&&catalogOutput+1<args.Length)
{
    string directory=args[catalogOutput+1];Directory.CreateDirectory(directory);
    foreach(var preset in ScenarioCatalog.All)
    {
        string path=Path.Combine(directory,preset.Id+".riverguard");
        if(!File.Exists(path))File.WriteAllText(path,JsonSerializer.Serialize(preset.Create(),ProjectData.Json));
    }
}
if(args.Length==2&&args[0]=="--examples")
{
    Directory.CreateDirectory(args[1]);
    File.WriteAllText(Path.Combine(args[1],"Демонстрационная долина.riverguard"),JsonSerializer.Serialize(Examples.Valley(),ProjectData.Json));
    File.WriteAllText(Path.Combine(args[1],"Орешко Волга.riverguard"),JsonSerializer.Serialize(Examples.Oreshko(),ProjectData.Json));
}
if(args.Contains("--full"))
{
    foreach(var full in new[]{Examples.Valley(),Examples.Oreshko()})
    {
        var fullResult=solver.Run(full);
        Check(fullResult.BalanceErrorPercent<0.01,"full scenario balance: "+full.Name);
        Console.WriteLine(full.Name+" | seconds="+fullResult.ElapsedSeconds.ToString("F2")+" | wave="+fullResult.Sections.Max(s=>s.WaveHeight).ToString("F3")+" | peak flow="+fullResult.Sections.Max(s=>s.MaxDischarge).ToString("F3"));
    }
}
Console.WriteLine("GRID 40/80 relative peak depth differences: "+string.Join(", ",r.Sections.Zip(fine.Sections).Select(v=>((v.Second.MaxDepth-v.First.MaxDepth)/v.First.MaxDepth*100).ToString("F2")+"%")));
Console.WriteLine($"{passed} passed; {failures.Count} failed; balance {r.BalanceErrorPercent:E3}%");
Environment.ExitCode=failures.Count>0?1:0;
