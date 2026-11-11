using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiverGuard.Core;

// Модели исходных данных
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}

public class BankPoint : Observable
{
    private double distance,elevation;
    public double Distance { get=>distance; set=>Set(ref distance,value); }
    public double Elevation { get=>elevation; set=>Set(ref elevation,value); }
}

public class RiverSection : Observable
{
    private string name="Новый створ";
    private double distance=1000,bed=99,normalDepth=2,leftWidth=35,rightWidth=45,initialVelocity=0.3,manning=0.035,drainageFactor=1;
    public string Name { get=>name;set=>Set(ref name,value); }
    public double Distance { get=>distance;set=>Set(ref distance,value); }
    public double Bed { get=>bed;set=>Set(ref bed,value); }
    public double NormalDepth { get=>normalDepth;set=>Set(ref normalDepth,value); }
    public double LeftWidth { get=>leftWidth;set=>Set(ref leftWidth,value); }
    public double RightWidth { get=>rightWidth;set=>Set(ref rightWidth,value); }
    public double InitialVelocity { get=>initialVelocity;set=>Set(ref initialVelocity,value); }
    public double Manning { get=>manning;set=>Set(ref manning,value); }
    public double DrainageFactor { get=>drainageFactor;set=>Set(ref drainageFactor,value); }
    public ObservableCollection<BankPoint> LeftBank { get; set; } = new();
    public ObservableCollection<BankPoint> RightBank { get; set; } = new();
}

public class ProjectData : Observable
{
    public int SchemaVersion { get; set; } = 1;
    private string name="Новый сценарий",scenario="Прорыв плотины",notes="";
    private double reservoirLevel=108,damBed=100,reservoirVolume=4,reservoirArea=0.5,breachWidth=18,sillElevation=101,formationMinutes=10,dischargeCoefficient=0.385,lowerDepth=2,lowerWidth=80,lowerVelocity=0.3,durationHours=2,arrivalThreshold=0.05,evacuationHours=1.5;
    private int cells=100;
    private double gateHeight=2,outletDiameter=2;
    public string Name { get=>name;set=>Set(ref name,value); }
    public string Scenario { get=>scenario;set=>Set(ref scenario,value); }
    public double ReservoirLevel { get=>reservoirLevel;set=>Set(ref reservoirLevel,value); }
    public double DamBed { get=>damBed;set=>Set(ref damBed,value); }
    [JsonIgnore] public double ReservoirDepth => ReservoirLevel - DamBed;
    public double ReservoirVolume { get=>reservoirVolume;set=>Set(ref reservoirVolume,value); }
    public double ReservoirArea { get=>reservoirArea;set=>Set(ref reservoirArea,value); }
    public double BreachWidth { get=>breachWidth;set=>Set(ref breachWidth,value); }
    public double SillElevation { get=>sillElevation;set=>Set(ref sillElevation,value); }
    public double FormationMinutes { get=>formationMinutes;set=>Set(ref formationMinutes,value); }
    public double DischargeCoefficient { get=>dischargeCoefficient;set=>Set(ref dischargeCoefficient,value); }
    public double GateHeight { get=>gateHeight;set=>Set(ref gateHeight,value); }
    public double OutletDiameter { get=>outletDiameter;set=>Set(ref outletDiameter,value); }
    public double LowerDepth { get=>lowerDepth;set=>Set(ref lowerDepth,value); }
    public double LowerWidth { get=>lowerWidth;set=>Set(ref lowerWidth,value); }
    public double LowerVelocity { get=>lowerVelocity;set=>Set(ref lowerVelocity,value); }
    public double DurationHours { get=>durationHours;set=>Set(ref durationHours,value); }
    public int Cells { get=>cells;set=>Set(ref cells,value); }
    public double ArrivalThreshold { get=>arrivalThreshold;set=>Set(ref arrivalThreshold,value); }
    public double EvacuationHours { get=>evacuationHours;set=>Set(ref evacuationHours,value); }
    public string Notes { get=>notes;set=>Set(ref notes,value); }
    public ObservableCollection<RiverSection> Sections { get; set; } = new();
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.Strict };
    public ProjectData Copy() => JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(this, Json), Json)!;
}

// Модели результатов
public record Sample(double X, double Y);
public class SectionResult
{
    public string Name { get; set; } = "";
    public override string ToString()=>Name;
    public double Distance { get; set; }
    public double MaxLevel { get; set; }
    public double MaxDepth { get; set; }
    public double WaveHeight { get; set; }
    public double MaxDischarge { get; set; }
    public double MaxVelocity { get; set; }
    public double DischargeAtPeak { get; set; }
    public double? ArrivalHours { get; set; }
    public double PeakHours { get; set; }
    public double? RecessionHours { get; set; }
    public double? DrainageHours { get; set; }
    public double FloodLeft { get; set; }
    public double FloodRight { get; set; }
    public double DangerLeft { get; set; }
    public double DangerRight { get; set; }
    public double DestructionLeft { get; set; }
    public double DestructionRight { get; set; }
    public string Evacuation { get; set; } = "Не достигнут критерий";
    public List<Sample> DepthHistory { get; set; } = new();
    public List<Sample> BaselineDepthHistory { get; set; } = new();
    public List<Sample> VelocityHistory { get; set; } = new();
    public List<Sample> FlowHistory { get; set; } = new();
}
public class IsoRow
{
    public string Section { get; set; } = "";
    public string Bank { get; set; } = "";
    public double Depth { get; set; }
    public double Distance { get; set; }
    public double Velocity { get; set; }
    public double? DryingHours { get; set; }
}
public class SimulationResult
{
    public string ModelVersion { get; set; } = "RG-1D / 1.2";
    public DateTime Created { get; set; } = DateTime.Now;
    public ProjectData Source { get; set; } = new();
    public List<SectionResult> Sections { get; set; } = new();
    public List<IsoRow> Isolines { get; set; } = new();
    public List<Sample> BreachFlow { get; set; } = new();
    public List<Sample> ReservoirLevel { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public double FloodArea { get; set; }
    public double DangerArea { get; set; }
    public double ReleasedVolume { get; set; }
    public double BalanceErrorPercent { get; set; }
    public double InitialRiverVolume { get; set; }
    public double FinalRiverVolume { get; set; }
    public double OutflowVolume { get; set; }
    public int Steps { get; set; }
    public double ElapsedSeconds { get; set; }
}

// Встроенные сценарии
public static class Examples
{
    public static ProjectData Valley()
    {
        var p = new ProjectData { Name = "Долина · демонстрационный сценарий", Notes = "Синтетический пример для исследования модели. Не является реальным объектом." };
        foreach (var (x, bed) in new[] { (200d, 99.8), (1000d, 99.0), (2500d, 97.5), (5000d, 95.0) })
        {
            var s = new RiverSection { Name = $"Створ {p.Sections.Count + 1}", Distance = x, Bed = bed, LeftWidth = 35 + x / 500, RightWidth = 45 + x / 400 };
            s.LeftBank.Add(new() { Distance = s.LeftWidth + 45, Elevation = bed + 3.5 });
            s.LeftBank.Add(new() { Distance = s.LeftWidth + 100, Elevation = bed + 6 });
            s.LeftBank.Add(new() { Distance = s.LeftWidth + 160, Elevation = bed + 11 });
            s.RightBank.Add(new() { Distance = s.RightWidth + 30, Elevation = bed + 4 });
            s.RightBank.Add(new() { Distance = s.RightWidth + 80, Elevation = bed + 7 });
            s.RightBank.Add(new() { Distance = s.RightWidth + 130, Elevation = bed + 12 });
            p.Sections.Add(s);
        }
        return p;
    }
    public static ProjectData Oreshko()
    {
        var p = new ProjectData { Name = "Орешко Волга · исходные данные", ReservoirLevel = 71.3, DamBed = 65.3, ReservoirVolume = 11.6, ReservoirArea = 2.32, BreachWidth = 12, SillElevation = 70.2, FormationMinutes = 0, LowerDepth = 5, LowerWidth = 140, LowerVelocity = 0.5, DurationHours = 3, Notes = "Входные данные из отчётов «Волна» 25.0. Алгоритм RG-1D независим; совпадение с закрытой программой не заявляется. Нулевой створ старого отчёта не используется как эталон." };
        var xs = new[] { 100d, 800, 1600 }; var beds = new[] { 60d, 59, 58 };
        double[][] left = { new[] {73d,74,75}, new[] {69d,71,72}, new[] {68d,70,72} };
        double[][] right = { new[] {70.5,71,71.4}, new[] {68d,69,71}, new[] {75d,77,79} };
        for (int i=0;i<3;i++)
        {
            var s = new RiverSection { Name=$"Створ {i+1}",Distance=xs[i],Bed=beds[i],NormalDepth=5,LeftWidth=35,RightWidth=i==0?72:73,InitialVelocity=0.5 };
            for(int j=0;j<3;j++) { s.LeftBank.Add(new(){Distance=new[]{55d,65,69}[j],Elevation=left[i][j]}); s.RightBank.Add(new(){Distance=new[]{74d,95,105}[j],Elevation=right[i][j]}); }
            p.Sections.Add(s);
        }
        return p;
    }
}
