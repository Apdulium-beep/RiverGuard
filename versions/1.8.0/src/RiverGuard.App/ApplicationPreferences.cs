using System.IO;
using System.Text.Json;

namespace RiverGuard;

// Локальные настройки приложения
internal sealed class ApplicationPreferences
{
    public int Version {get;set;}=1;
    public bool AutomaticUpdates {get;set;}
    public string Theme {get;set;}="Ocean";
    public string InterfaceMode {get;set;}="Standard";
    public double TextSize {get;set;}=13;
    public bool ShowHints {get;set;}=true;
    public bool ReduceMotion {get;set;}
    internal ApplicationPreferences Copy()=>new(){AutomaticUpdates=AutomaticUpdates,Theme=Theme,InterfaceMode=InterfaceMode,TextSize=TextSize,ShowHints=ShowHints,ReduceMotion=ReduceMotion};
    internal ApplicationPreferences Normalize()
    {
        if(!new[]{"Ocean","Light","Graphite","Forest"}.Contains(Theme))Theme="Ocean";
        if(!new[]{"Simple","Standard","Expert"}.Contains(InterfaceMode))InterfaceMode="Standard";
        TextSize=double.IsFinite(TextSize)?Math.Clamp(TextSize,12,17):13;return this;
    }
    internal static ApplicationPreferences Load(string folder)
    {
        try{var p=JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(Path.Combine(folder,"preferences.json")));return p?.Version==1?p.Normalize():new();}catch{return new();}
    }
    internal void Save(string folder)
    {
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"preferences.json");
        if(File.Exists(path))
        {
            bool valid;try{valid=JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(path))?.Version==1;}catch{valid=false;}
            if(!valid)File.Copy(path,path+".backup-"+Guid.NewGuid().ToString("N"));
        }
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true);
    }
}
