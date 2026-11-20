using System.Globalization;
using System.Text.Json.Serialization;

namespace RiverGuard.Core;

// Снимки запусков без рекурсивного вложения истории
public sealed class CalculationRecord
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public DateTime Created {get;set;}=DateTime.Now;
    public string Status {get;set;}="Выполнен";
    public string Error {get;set;}="";
    public string ModelVersion {get;set;}=SimulationResult.CurrentModelVersion;
    public string ApplicationVersion {get;set;}="";
    public ProjectData Input {get;set;}=new();
    public List<CalculationSectionSummary> Sections {get;set;}=new();
    public double? ReleasedVolume {get;set;}
    public double? BalanceErrorPercent {get;set;}
    public double? FloodArea {get;set;}
    public double? ElapsedSeconds {get;set;}
    [JsonIgnore] public string Label=>$"{Created:dd.MM.yyyy HH:mm:ss} · {Status} · Z={Input.ReservoirLevel:F3} м · {Sections.Count} ств.";
    public override string ToString()=>Label;
    public static CalculationRecord Create(ProjectData input,SimulationResult? result,string? error=null,bool cancelled=false)=>new()
    {
        Created=result?.Created??DateTime.Now,Status=result!=null?"Выполнен":cancelled?"Отменён":"Ошибка",Error=error??"",Input=input.CopyInputs(),ModelVersion=result?.ModelVersion??SimulationResult.CurrentModelVersion,ApplicationVersion=result?.ApplicationVersion??"",
        ReleasedVolume=result?.ReleasedVolume,BalanceErrorPercent=result?.BalanceErrorPercent,FloodArea=result?.FloodArea,ElapsedSeconds=result?.ElapsedSeconds,
        Sections=result?.Sections.Select(s=>new CalculationSectionSummary{Name=s.Name,Distance=s.Distance,MaxDischarge=s.MaxDischarge,MaxVelocity=s.MaxVelocity,MaxDepth=s.MaxDepth,MaxLevel=s.MaxLevel,WaveHeight=s.WaveHeight,ArrivalHours=s.ArrivalHours,PeakHours=s.WavePeakHours,RecessionHours=s.RecessionHours,FloodLeft=s.FloodLeft,FloodRight=s.FloodRight,Evacuation=s.Evacuation}).ToList()??new()
    };
}
public sealed class CalculationSectionSummary
{
    public string Name {get;set;}="";
    public double Distance {get;set;}
    public double MaxDischarge {get;set;}
    public double MaxVelocity {get;set;}
    public double MaxDepth {get;set;}
    public double MaxLevel {get;set;}
    public double WaveHeight {get;set;}
    public double? ArrivalHours {get;set;}
    public double? PeakHours {get;set;}
    public double? RecessionHours {get;set;}
    public double FloodLeft {get;set;}
    public double FloodRight {get;set;}
    public string Evacuation {get;set;}="";
}
public sealed record HistoryComparisonRow(string Quantity,string First,string Second,string Difference);
public static class HistoryComparison
{
    public static List<HistoryComparisonRow> Compare(CalculationRecord a,CalculationRecord b)
    {
        var rows=new List<HistoryComparisonRow>();string F(double? v)=>v?.ToString("F3",CultureInfo.GetCultureInfo("ru-RU"))??"не определено";
        void Add(string name,double? x,double? y)=>rows.Add(new(name,F(x),F(y),x.HasValue&&y.HasValue?F(y-x):"не определено"));
        rows.Add(new("Статус",a.Status,b.Status,"—"));rows.Add(new("Модель",a.ModelVersion,b.ModelVersion,"—"));
        if(a.Error.Length>0||b.Error.Length>0)rows.Add(new("Сообщение",a.Error,b.Error,"—"));
        rows.Add(new("Версия приложения",a.ApplicationVersion,b.ApplicationVersion,"—"));rows.Add(new("Механизм",a.Input.Scenario,b.Input.Scenario,a.Input.Scenario==b.Input.Scenario?"одинаков":"различается"));
        Add("Уровень водохранилища, м",a.Input.ReservoirLevel,b.Input.ReservoirLevel);Add("Расчётный объём, млн м³",a.Input.EffectiveReservoirVolume,b.Input.EffectiveReservoirVolume);Add("Ширина / диаметр отверстия, м",ScenarioMode.IsCircular(a.Input.Scenario)?a.Input.OutletDiameter:a.Input.EffectiveBreachWidth,ScenarioMode.IsCircular(b.Input.Scenario)?b.Input.OutletDiameter:b.Input.EffectiveBreachWidth);
        Add("Продолжительность, ч",a.Input.DurationHours,b.Input.DurationHours);
        bool sameGeometry=System.Text.Json.JsonSerializer.Serialize(a.Input.Sections,ProjectData.InputJson)==System.Text.Json.JsonSerializer.Serialize(b.Input.Sections,ProjectData.InputJson);
        rows.Add(new("Геометрия створов",a.Input.Sections.Count+" ств.",b.Input.Sections.Count+" ств.",sameGeometry?"одинакова":"различается"));
        rows.Add(new("Критерии зон",a.Input.Zones.Description,b.Input.Zones.Description,"—"));
        Add("Выпуск, м³",a.ReleasedVolume,b.ReleasedVolume);Add("Площадь поймы, м²",a.FloodArea,b.FloodArea);Add("Невязка, %",a.BalanceErrorPercent,b.BalanceErrorPercent);
        var keys=a.Sections.Select(s=>(s.Name,s.Distance)).Union(b.Sections.Select(s=>(s.Name,s.Distance)));
        foreach(var key in keys)
        {
            var x=a.Sections.FirstOrDefault(s=>s.Name==key.Name&&s.Distance==key.Distance);var y=b.Sections.FirstOrDefault(s=>s.Name==key.Name&&s.Distance==key.Distance);string label=$"{key.Name} ({F(key.Distance)} м) · ";
            Add(label+"Qmax, м³/с",x?.MaxDischarge,y?.MaxDischarge);Add(label+"Vmax, м/с",x?.MaxVelocity,y?.MaxVelocity);Add(label+"Hmax, м",x?.MaxDepth,y?.MaxDepth);Add(label+"Zmax, м",x?.MaxLevel,y?.MaxLevel);Add(label+"Волна, м",x?.WaveHeight,y?.WaveHeight);Add(label+"Фронт, ч",x?.ArrivalHours,y?.ArrivalHours);Add(label+"Гребень, ч",x?.PeakHours,y?.PeakHours);Add(label+"Спад, ч",x?.RecessionHours,y?.RecessionHours);
        }
        return rows;
    }
}
