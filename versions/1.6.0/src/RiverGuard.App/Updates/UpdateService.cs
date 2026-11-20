using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RiverGuard.Updates;

internal sealed record ReleaseUpdate(string Version,string Tag,string Page,string Notes,string PackageUrl,string ChecksumUrl,long PackageSize,string? Digest);
internal sealed record UpdateProgress(double Fraction,string Message);
internal sealed record PreparedUpdate(string Version,string ExecutablePath);

// Проверка и безопасная подготовка обновления
internal sealed class UpdateService : IDisposable
{
    internal const string Repository="Apdulium-beep/RiverGuard";
    internal const string LatestUrl="https://api.github.com/repos/"+Repository+"/releases/latest";
    private const long MaxPackage=512L*1024*1024,MaxExpanded=1024L*1024*1024;
    private readonly HttpClient client;
    private readonly string dataFolder;
    private readonly Action<string,string> validateExecutable;
    internal static Version CurrentVersion=>Normalize(Assembly.GetExecutingAssembly().GetName().Version!);
    internal static string CurrentVersionText=>CurrentVersion.Revision==0?CurrentVersion.ToString(3):CurrentVersion.ToString(4);
    internal UpdateService(HttpMessageHandler? handler=null,string? dataFolder=null,Action<string,string>? validateExecutable=null)
    {
        client=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RiverGuard/"+CurrentVersionText);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        this.dataFolder=dataFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RiverGuard");
        this.validateExecutable=validateExecutable??ValidateExecutable;
    }
    private static Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
    internal static Version ParseVersion(string text)
    {
        if(text.Length>40||!Regex.IsMatch(text,@"^\d+\.\d+\.\d+(?:\.\d+)?$")||!Version.TryParse(text,out var v))throw new InvalidDataException("Неподдерживаемый номер версии.");
        return Normalize(v);
    }
    internal static ReleaseUpdate? ParseRelease(string json,Version current)
    {
        using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
        if(r.GetProperty("draft").GetBoolean()||r.GetProperty("prerelease").GetBoolean())return null;
        string tag=r.GetProperty("tag_name").GetString()??"";
        string version=tag.StartsWith('v')?tag[1..]:tag;
        Version parsed;try{parsed=ParseVersion(version);}catch(InvalidDataException){return null;}
        if(parsed<=Normalize(current))return null;
        string package=$"RiverGuard-{version}-win-x64.zip",checksum=$"RiverGuard-{version}-SHA256.txt";
        var assets=r.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement Asset(string name)
        {
            var matches=assets.Where(a=>a.GetProperty("name").GetString()==name).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("В релизе нет однозначного комплекта Windows x64 и SHA256.");
            return matches[0];
        }
        var zip=Asset(package);var sha=Asset(checksum);
        string Download(JsonElement asset,string name)
        {
            string expected=$"https://github.com/{Repository}/releases/download/{tag}/{name}";
            if(asset.GetProperty("browser_download_url").GetString()!=expected)throw new InvalidDataException("Недоверенный адрес файла обновления.");
            return expected;
        }
        long size=zip.GetProperty("size").GetInt64();
        if(size<=0||size>MaxPackage||sha.GetProperty("size").GetInt64() is <=0 or >8192)throw new InvalidDataException("Некорректный размер обновления.");
        string? digest=zip.TryGetProperty("digest",out var d)&&d.ValueKind==JsonValueKind.String?d.GetString():null;
        if(digest!=null&&!Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("Неподдерживаемая контрольная сумма GitHub.");
        string notes=r.TryGetProperty("body",out var b)?b.GetString()??"":"";
        if(notes.Length>4000)notes=notes[..4000]+"…";
        return new(version,tag,$"https://github.com/{Repository}/releases/tag/{tag}",notes,Download(zip,package),Download(sha,checksum),size,digest);
    }
    internal async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellation=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response=await GetAsync(new Uri(LatestUrl),timeout.Token);
        if(response.StatusCode==HttpStatusCode.NotFound)throw new HttpRequestException("Публичный релиз GitHub недоступен.");
        response.EnsureSuccessStatusCode();
        string json=await ReadTextAsync(response,1024*1024,timeout.Token);
        return ParseRelease(json,CurrentVersion);
    }

    // HTTPS и ограниченные перенаправления GitHub
    private static bool Trusted(Uri uri)=>uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&new[]{"api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com"}.Contains(uri.Host,StringComparer.OrdinalIgnoreCase);
    private async Task<HttpResponseMessage> GetAsync(Uri uri,CancellationToken cancellation)
    {
        for(int redirects=0;redirects<=5;redirects++)
        {
            if(!Trusted(uri))throw new InvalidDataException("Небезопасное перенаправление обновления.");
            var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellation);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location=response.Headers.Location;response.Dispose();
                if(location==null)throw new InvalidDataException("Пустое перенаправление GitHub.");
                uri=location.IsAbsoluteUri?location:new Uri(uri,location);continue;
            }
            return response;
        }
        throw new InvalidDataException("Слишком много перенаправлений обновления.");
    }
    private static async Task<string> ReadTextAsync(HttpResponseMessage response,long limit,CancellationToken cancellation)
    {
        using var stream=await response.Content.ReadAsStreamAsync(cancellation);using var memory=new MemoryStream();
        await CopyLimitedAsync(stream,memory,limit,null,cancellation);
        return new UTF8Encoding(false,true).GetString(memory.ToArray()).TrimStart('\uFEFF');
    }
    private static async Task<long> CopyLimitedAsync(Stream source,Stream target,long limit,Action<long>? report,CancellationToken cancellation)
    {
        byte[] buffer=new byte[81920];long total=0;int read;
        while((read=await source.ReadAsync(buffer,cancellation))>0)
        {total+=read;if(total>limit)throw new InvalidDataException("Превышен допустимый размер файла.");await target.WriteAsync(buffer.AsMemory(0,read),cancellation);report?.Invoke(total);}
        return total;
    }
    internal static string ExpectedChecksum(string text,string fileName)
    {
        string pattern=@"^([a-fA-F0-9]{64})[ \t]+\*?"+Regex.Escape(fileName)+"$";
        var matches=text.TrimStart('\uFEFF').Split('\n').Select(s=>Regex.Match(s.TrimEnd('\r'),pattern)).Where(m=>m.Success).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("В SHA256 нет однозначной суммы выбранного архива.");
        return matches[0].Groups[1].Value;
    }
    internal async Task<PreparedUpdate> PrepareAsync(ReleaseUpdate release,IProgress<UpdateProgress>? progress=null,CancellationToken cancellation=default)
    {
        ParseVersion(release.Version);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(10));var token=timeout.Token;
        string work=Path.Combine(dataFolder,"Updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            progress?.Report(new(0,"Получение контрольной суммы…"));
            string checksum;
            using(var response=await GetAsync(new Uri(release.ChecksumUrl),token))
            {response.EnsureSuccessStatusCode();checksum=ExpectedChecksum(await ReadTextAsync(response,8192,token),$"RiverGuard-{release.Version}-win-x64.zip");}
            string zipPath=Path.Combine(work,"package.zip");
            using(var response=await GetAsync(new Uri(release.PackageUrl),token))
            {
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is long length&&length!=release.PackageSize)throw new InvalidDataException("Размер архива не совпал с описанием релиза.");
                using var source=await response.Content.ReadAsStreamAsync(token);using var destination=File.Create(zipPath);
                long downloaded=await CopyLimitedAsync(source,destination,release.PackageSize,n=>progress?.Report(new(0.05+0.65*n/release.PackageSize,$"Скачивание: {n/1048576d:F1} / {release.PackageSize/1048576d:F1} МБ")),token);
                if(downloaded!=release.PackageSize)throw new InvalidDataException("Архив скачан не полностью.");
            }
            progress?.Report(new(0.75,"Проверка SHA256…"));
            string actual;using(var stream=File.OpenRead(zipPath))actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
            if(!actual.Equals(checksum,StringComparison.OrdinalIgnoreCase)||release.Digest!=null&&!actual.Equals(release.Digest[7..],StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма обновления не совпала. Установка отменена.");
            string payload=Path.Combine(work,"payload");Directory.CreateDirectory(payload);
            progress?.Report(new(0.85,"Проверка содержимого архива…"));
            await ExtractAsync(zipPath,payload,release.Version,token);
            validateExecutable(Path.Combine(payload,"RiverGuard.exe"),release.Version);
            token.ThrowIfCancellationRequested();
            string versions=Path.Combine(dataFolder,"Versions");Directory.CreateDirectory(versions);
            string target=Path.Combine(versions,release.Version);
            if(Directory.Exists(target))target+="-"+Guid.NewGuid().ToString("N")[..8];
            Directory.Move(payload,target);
            progress?.Report(new(1,"Обновление проверено. Готово к перезапуску."));
            return new(release.Version,Path.Combine(target,"RiverGuard.exe"));
        }
        finally{try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    // Проверка и извлечение без перезаписи установленной программы
    internal static async Task ExtractAsync(string zipPath,string destination,string version,CancellationToken cancellation)
    {
        using var zip=ZipFile.OpenRead(zipPath);string prefix=$"RiverGuard-{version}/";long total=0;
        if(zip.Entries.Count is <1 or >256)throw new InvalidDataException("Некорректный состав архива обновления.");
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();string path=entry.FullName;
            if(path==prefix&&entry.Length==0)continue;
            if(!path.StartsWith(prefix,StringComparison.Ordinal)||path.Contains('\\'))throw new InvalidDataException("Архив содержит постороннюю папку или небезопасный путь.");
            string name=path[prefix.Length..];
            if(name.Length==0||name.Contains('/')||name.Contains(':')||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.EndsWith('.')||name.EndsWith(' ')||Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase)||!files.Add(name))throw new InvalidDataException("Некорректное или повторяющееся имя файла обновления.");
            string extension=Path.GetExtension(name).ToLowerInvariant();
            if(!new[]{".exe",".dll",".json",".txt",".config",".md"}.Contains(extension)||extension==".exe"&&name!="RiverGuard.exe"||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Архив содержит неожиданный исполняемый файл или ссылку.");
            total+=entry.Length;
            if(entry.Length<0||entry.Length>MaxPackage||total>MaxExpanded)throw new InvalidDataException("Превышен допустимый объём распаковки.");
        }
        if(!files.Contains("RiverGuard.exe")||!files.Contains("THIRD_PARTY_NOTICES.txt"))throw new InvalidDataException("Архив не содержит программу и обязательные уведомления.");
        foreach(var entry in zip.Entries.Where(e=>e.FullName!=prefix))
        {
            using var input=entry.Open();using var output=new FileStream(Path.Combine(destination,entry.FullName[prefix.Length..]),FileMode.CreateNew);
            long copied=await CopyLimitedAsync(input,output,entry.Length,null,cancellation);
            if(copied!=entry.Length)throw new InvalidDataException("Повреждённый файл внутри архива.");
        }
    }
    internal static void ValidateExecutable(string path,string version)
    {
        using(var stream=File.OpenRead(path))if(stream.ReadByte()!='M'||stream.ReadByte()!='Z')throw new InvalidDataException("Обновление не является Windows-программой.");
        string fileVersion=FileVersionInfo.GetVersionInfo(path).FileVersion??"";
        if(ParseVersion(fileVersion)!=ParseVersion(version))throw new InvalidDataException("Версия EXE не совпала с номером релиза.");
    }
    public void Dispose()=>client.Dispose();
}
