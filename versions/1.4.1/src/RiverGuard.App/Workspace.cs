using System.IO;
using System.Text.Json;
using RiverGuard.Core;

namespace RiverGuard;

// Состояние рабочего пространства
public class Workspace : Observable
{
    private ProjectData project=Examples.Valley();
    private RiverSection? selectedSection;
    private List<SectionResult> results=new();
    private List<IsoRow> filtered=new();
    private List<string> warnings=new(){"Расчёт ещё не выполнялся. Настройте исходные данные и нажмите «Рассчитать сценарий»."};
    private string flow="—",height="—",area="—",balance="—",summary="Результаты появятся после завершения расчёта.";
    public ProjectData Project {get=>project;set=>Set(ref project,value);}
    public RiverSection? SelectedSection {get=>selectedSection;set=>Set(ref selectedSection,value);}
    public List<SectionResult> Results {get=>results;set=>Set(ref results,value);}
    public List<IsoRow> FilteredIsolines {get=>filtered;set=>Set(ref filtered,value);}
    public List<string> Warnings {get=>warnings;set=>Set(ref warnings,value);}
    public string FlowMetric {get=>flow;set=>Set(ref flow,value);}
    public string HeightMetric {get=>height;set=>Set(ref height,value);}
    public string AreaMetric {get=>area;set=>Set(ref area,value);}
    public string BalanceMetric {get=>balance;set=>Set(ref balance,value);}
    public string Summary {get=>summary;set=>Set(ref summary,value);}
}

// Сохранение и восстановление проектов
public static class ProjectStorage
{
    internal static string? TestFolderOverride {get;set;}
    public static string Folder => TestFolderOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RiverGuard");
    public static string ProjectsFolder=>TestFolderOverride!=null?Path.Combine(Folder,"Projects"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is {Length:>0} documents?documents:Folder,"RiverGuard","Проекты");
    public static void Save(ProjectData project,string path)
    {
        string json=JsonSerializer.Serialize(project,ProjectData.Json);string temp=path+".tmp";
        File.WriteAllText(temp,json);File.Move(temp,path,true);
    }
    public static ProjectData Load(string path)
    {
        string text=File.ReadAllText(path);using var document=JsonDocument.Parse(text);
        if(!document.RootElement.TryGetProperty("SchemaVersion",out _)||!document.RootElement.TryGetProperty("Sections",out var sections)||sections.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Это не файл исходных данных RiverGuard. Используйте проект .riverguard, а не JSON результатов.");
        var p=JsonSerializer.Deserialize<ProjectData>(text,ProjectData.Json)??throw new InvalidDataException("Файл не содержит проект.");
        if(p.SchemaVersion!=1)throw new InvalidDataException("Неизвестная версия формата проекта.");
        if(!ScenarioMode.IsKnown(p.Scenario))throw new InvalidDataException("Этот механизм аварии не поддерживается. Файл не был изменён.");
        if(p.Sections==null||p.Sections.Any(s=>s.LeftBank==null||s.RightBank==null))throw new InvalidDataException("В файле отсутствуют данные створов или берегов.");
        return p;
    }
    public static List<string> Recent()
    {
        try{return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(Folder,"recent.json")))?.Where(File.Exists).ToList()??new();}catch{return new();}
    }
    public static void Remember(string path)
    {
        Directory.CreateDirectory(Folder);path=Path.GetFullPath(path);var list=Recent();list.RemoveAll(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase));list.Insert(0,path);
        WriteIndex("recent.json",list.Take(8));
        var projects=ReadIndex("projects.json");projects.RemoveAll(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase));projects.Insert(0,path);WriteIndex("projects.json",projects);
    }
    // Библиотека проектов
    public static void Delete(ProjectEntry entry)
    {
        string path=Path.GetFullPath(entry.Path);
        if(File.Exists(path))Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        Directory.CreateDirectory(Folder);
        foreach(string index in new[]{"recent.json","projects.json"})WriteIndex(index,ReadIndex(index).Where(p=>!string.Equals(Path.GetFullPath(p),path,StringComparison.OrdinalIgnoreCase)));
    }
    private static List<string> ReadIndex(string name)
    {try{return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(Folder,name)))?.Where(p=>!string.IsNullOrWhiteSpace(p)).ToList()??new();}catch{return new();}}
    private static void WriteIndex(string name,IEnumerable<string> paths)
    {
        string path=Path.Combine(Folder,name);
        if(File.Exists(path))
        {try{JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));}catch{File.Copy(path,path+".backup-"+Guid.NewGuid().ToString("N"));}}
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(paths));File.Move(temp,path,true);
    }
    public static string Create(ProjectData project)
    {
        if(string.IsNullOrWhiteSpace(project.Name))throw new InvalidDataException("Укажите название проекта.");
        Directory.CreateDirectory(ProjectsFolder);
        string name=string.Join("_",project.Name.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        name=name.Length>70?name[..70]:name;
        string path=Path.Combine(ProjectsFolder,$"{name}_{Guid.NewGuid():N}.riverguard");
        Save(project,path);Remember(path);return path;
    }
    public static List<ProjectEntry> Library()
    {
        var paths=ReadIndex("projects.json").Concat(Recent()).ToList();
        if(Directory.Exists(ProjectsFolder))paths.AddRange(Directory.EnumerateFiles(ProjectsFolder,"*.riverguard",SearchOption.TopDirectoryOnly));
        var entries=paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>ProjectEntry.Read(p)).ToList();
        string recovery=Path.Combine(Folder,"last-session.riverguard");
        if(File.Exists(recovery)&&!paths.Contains(recovery,StringComparer.OrdinalIgnoreCase))entries.Add(ProjectEntry.Read(recovery,true));
        return entries.OrderBy(e=>e.IsRecovery).ThenByDescending(e=>e.Modified).ToList();
    }
}

// Карточки библиотеки
public sealed class ProjectEntry
{
    public string Path {get;init;}="";
    public string Name {get;init;}="";
    public string Scenario {get;init;}="";
    public DateTime Modified {get;init;}
    public bool IsAvailable {get;init;}
    public bool IsRecovery {get;init;}
    public string Detail=>IsAvailable?$"{Scenario} · {Modified:dd.MM.yyyy HH:mm}" :"Файл недоступен или повреждён";
    public string Label=>IsRecovery?"АВТОВОССТАНОВЛЕНИЕ":"ПРОЕКТ";
    public static ProjectEntry Read(string path,bool recovery=false)
    {
        try
        {var p=ProjectStorage.Load(path);return new(){Path=path,Name=p.Name,Scenario=p.Scenario,Modified=File.GetLastWriteTime(path),IsAvailable=true,IsRecovery=recovery};}
        catch{return new(){Path=path,Name=System.IO.Path.GetFileNameWithoutExtension(path),IsAvailable=false,IsRecovery=recovery};}
    }
}
