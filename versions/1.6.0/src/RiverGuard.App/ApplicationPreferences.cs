using System.IO;
using System.Text.Json;

namespace RiverGuard;

// Локальные настройки приложения
internal sealed class ApplicationPreferences
{
    public int Version {get;set;}=1;
    public bool AutomaticUpdates {get;set;}
    internal static ApplicationPreferences Load(string folder)
    {
        try{var p=JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(Path.Combine(folder,"preferences.json")));return p?.Version==1?p:new();}catch{return new();}
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
