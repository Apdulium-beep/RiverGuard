using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiverGuard.Updates;

namespace RiverGuard;

public partial class MainWindow
{
    // Изолированные проверки обновления без сети и запуска нового процесса
    private async Task VerifyUpdaterAsync(string directory)
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        async Task Reject(Func<Task> action,string name)
        {bool rejected=false;try{await action();}catch(Exception ex) when(ex is InvalidDataException or HttpRequestException or OperationCanceledException){rejected=true;}Check(rejected,name);}
        void RejectParse(string json,string name)
        {bool rejected=false;try{UpdateService.ParseRelease(json,new Version(1,5,0));}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
        const string next="1.6.0";string prefix="RiverGuard-"+next+"/";
        byte[] executable=Encoding.UTF8.GetBytes("MZisolated-updater-fixture");
        byte[] Zip(params (string Name,byte[] Bytes)[] files)
        {
            using var memory=new MemoryStream();
            using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))foreach(var file in files){var e=zip.CreateEntry(file.Name);using var stream=e.Open();stream.Write(file.Bytes);}
            return memory.ToArray();
        }
        byte[] package=Zip((prefix+"RiverGuard.exe",executable),(prefix+"THIRD_PARTY_NOTICES.txt",Encoding.UTF8.GetBytes("Fixture notices")));
        string digest=Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        string url=$"https://github.com/{UpdateService.Repository}/releases/download/v{next}/";
        string packageName=$"RiverGuard-{next}-win-x64.zip",shaName=$"RiverGuard-{next}-SHA256.txt";
        string metadata=JsonSerializer.Serialize(new{draft=false,prerelease=false,tag_name="v"+next,body="Тестовое описание новой версии. Проверка без обращения к GitHub.",assets=new[]{new{name=packageName,browser_download_url=url+packageName,size=(long)package.Length,digest=(string?)("sha256:"+digest)},new{name=shaName,browser_download_url=url+shaName,size=96L,digest=(string?)null}}});
        string Modify(Action<JsonNode> edit){var node=JsonNode.Parse(metadata)!;edit(node);return node.ToJsonString();}
        var release=UpdateService.ParseRelease(metadata,new Version(1,5,0))!;
        Check(release.Version==next,"Newer stable version is detected");
        Check(UpdateService.ParseRelease(metadata,new Version(1,6,0,0))==null,"Equal versions with different component counts do not update");
        Check(UpdateService.ParseRelease(metadata,new Version(1,7,0))==null,"Older release never downgrades installed version");
        Check(UpdateService.ParseRelease(Modify(n=>n["draft"]=true),new Version(1,5,0))==null,"Draft release is ignored");
        Check(UpdateService.ParseRelease(Modify(n=>n["prerelease"]=true),new Version(1,5,0))==null,"Prerelease is ignored");
        Check(UpdateService.ParseRelease(Modify(n=>n["tag_name"]="v1.6.0-beta"),new Version(1,5,0))==null,"Nonstable tag is ignored");
        Check(UpdateService.ParseVersion("1.10.0")>UpdateService.ParseVersion("1.9.0"),"Version comparison is numeric, not lexicographic");
        RejectParse(Modify(n=>n["assets"]![0]!["browser_download_url"]="https://example.com/update.zip"),"Foreign asset origin is rejected");
        RejectParse(Modify(n=>n["assets"]![0]!["size"]=1024L*1024*1024),"Oversized release asset is rejected");
        RejectParse(Modify(n=>n["assets"]!.AsArray().RemoveAt(1)),"Release without checksum is rejected");
        RejectParse(Modify(n=>n["assets"]![0]!["digest"]="sha1:abc"),"Unsupported GitHub digest is rejected");
        Check(UpdateService.ExpectedChecksum("\uFEFF"+digest+"  "+packageName+"\r\n",packageName)==digest,"UTF8 BOM and CRLF checksum files are supported");
        bool ambiguous=false;try{UpdateService.ExpectedChecksum(digest+"  "+packageName+"\n"+digest+"  "+packageName,packageName);}catch(InvalidDataException){ambiguous=true;}Check(ambiguous,"Ambiguous checksum entries are rejected");
        string root=Path.Combine(directory,"Updater",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var requests=new List<string>();
        UpdateService Fixture(string name,string? checksum=null,byte[]? bytes=null)
        {
            return new(new FixtureUpdateHandler((request,token)=>
            {
                token.ThrowIfCancellationRequested();requests.Add(request.RequestUri!.AbsoluteUri);
                if(request.Content!=null||!request.Headers.UserAgent.Any())throw new InvalidDataException("Unexpected outgoing request");
                byte[] content=request.RequestUri.AbsoluteUri==UpdateService.LatestUrl?Encoding.UTF8.GetBytes(metadata):request.RequestUri.AbsoluteUri==url+shaName?Encoding.UTF8.GetBytes((checksum??digest)+"  "+packageName+"\n"):bytes??package;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(content)});
            }),Path.Combine(root,name),(path,version)=>{if(version!=next||!File.ReadAllBytes(path).SequenceEqual(executable))throw new InvalidDataException("Fixture executable validation failed");});
        }
        using(var service=Fixture("valid"))
        {
            Check((await service.CheckAsync())?.Version==next,"Background API check detects a newer stable release");
            var prepared=await service.PrepareAsync(release);
            Check(File.Exists(prepared.ExecutablePath),"Verified update is installed into a separate version folder");
            Check(File.ReadAllBytes(prepared.ExecutablePath).SequenceEqual(executable),"Downloaded payload is preserved exactly");
            Check(!Directory.EnumerateDirectories(Path.Combine(root,"valid","Updates")).Any(),"Temporary package is cleaned after successful installation");
        }
        using(var bad=Fixture("bad-sha",new string('0',64)))
        {await Reject(()=>bad.PrepareAsync(release),"Incorrect checksum blocks installation");Check(!Directory.Exists(Path.Combine(root,"bad-sha","Versions")),"Checksum failure leaves installed versions unchanged");}
        using(var bad=Fixture("bad-digest"))await Reject(()=>bad.PrepareAsync(release with{Digest="sha256:"+new string('0',64)}),"GitHub digest mismatch blocks installation even with valid SHA256 file");
        using(var bad=Fixture("short",bytes:package[..^1]))await Reject(()=>bad.PrepareAsync(release),"Incomplete download blocks installation");
        using(var canceled=Fixture("canceled"))
        {using var cts=new CancellationTokenSource();cts.Cancel();await Reject(()=>canceled.PrepareAsync(release,cancellation:cts.Token),"Canceled update never installs");}
        string existing=Path.Combine(root,"existing","Versions",next);Directory.CreateDirectory(existing);File.WriteAllText(Path.Combine(existing,"keep.txt"),"preserve");
        using(var service=Fixture("existing"))
        {var prepared=await service.PrepareAsync(release);Check(File.ReadAllText(Path.Combine(existing,"keep.txt"))=="preserve"&&Path.GetDirectoryName(prepared.ExecutablePath)!=existing,"Existing version folder is never overwritten");}
        using(var offline=new UpdateService(new FixtureUpdateHandler((r,c)=>throw new HttpRequestException("Offline fixture")),root))
            await Reject(()=>offline.CheckAsync(),"Offline check returns a handled failure rather than a false up-to-date result");
        using(var redirect=new UpdateService(new FixtureUpdateHandler((r,c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new Uri("https://example.com/payload")}})),root))
            await Reject(()=>redirect.CheckAsync(),"Redirect to a foreign host is blocked");
        async Task BadZip(string name,params (string Name,byte[] Bytes)[] files)
        {
            string path=Path.Combine(root,name+".zip"),destination=Path.Combine(root,name);File.WriteAllBytes(path,Zip(files));Directory.CreateDirectory(destination);
            await Reject(()=>UpdateService.ExtractAsync(path,destination,next,CancellationToken.None),"Unsafe archive rejected: "+name);
            Check(!Directory.EnumerateFiles(destination).Any(),"Archive rejected before writing files: "+name);
        }
        await BadZip("traversal",(prefix+"../escape.exe",executable));
        await BadZip("wrong-version",("RiverGuard-1.4.1/RiverGuard.exe",executable));
        await BadZip("duplicate",(prefix+"RiverGuard.exe",executable),(prefix+"RIVERGUARD.EXE",executable));
        await BadZip("script",(prefix+"install.ps1",executable));
        await BadZip("missing-notices",(prefix+"RiverGuard.exe",executable));
        await BadZip("reserved-device",(prefix+"CON.txt",executable));
        await BadZip("user-project",(prefix+"user.riverguard",executable));
        string currentExe=Path.Combine(AppContext.BaseDirectory,"RiverGuard.exe");
        UpdateService.ValidateExecutable(currentExe,UpdateService.CurrentVersionText);Check(true,"Actual application EXE version validates against the installed version");
        bool wrongVersion=false;try{UpdateService.ValidateExecutable(currentExe,"99.0.0");}catch(InvalidDataException){wrongVersion=true;}Check(wrongVersion,"Executable version mismatch is rejected");
        Check(requests.All(s=>s==UpdateService.LatestUrl||s==url+shaName||s==url+packageName),"Only release metadata and assets are requested; no project data is transmitted");
        using(var service=Fixture("dialog-later"))
        {
            var dialog=new UpdateWindow(service,release){Owner=this};dialog.Show();await Task.Delay(150);dialog.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(directory,"update-prompt.png")))png.Save(stream);
            dialog.Defer();Check(dialog.Prepared==null&&!dialog.IsVisible,"Later closes the offer without downloading, installing or restarting");
            Check(!Directory.Exists(Path.Combine(root,"dialog-later")),"Declining update creates no download or installation folders");
        }
        using(var service=Fixture("dialog-install"))
        {var dialog=new UpdateWindow(service,release){Owner=this};dialog.Show();await dialog.DownloadAsync();Check(dialog.Prepared!=null&&!dialog.IsVisible,"Update dialog returns verified installation without launching a process");}
        var downloading=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var service=new UpdateService(new FixtureUpdateHandler(async (request,token)=>
        {
            if(request.RequestUri!.AbsoluteUri==url+shaName)return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(digest+"  "+packageName)};
            downloading.TrySetResult(true);await Task.Delay(Timeout.Infinite,token);throw new InvalidOperationException("Unreachable canceled download");
        }),Path.Combine(root,"dialog-cancel")))
        {
            var dialog=new UpdateWindow(service,release){Owner=this};dialog.Show();var task=dialog.DownloadAsync();await downloading.Task;dialog.Defer();await task;
            Check(dialog.Prepared==null&&!dialog.IsVisible&&!Directory.Exists(Path.Combine(root,"dialog-cancel","Versions")),"Cancel button interrupts active download without installation or restart");
        }
        Check(CheckUpdatesButton.IsEnabled&&preparedUpdate==null&&!restartForUpdate,"Smoke tests leave real updater state and shortcuts untouched");
        File.WriteAllLines(Path.Combine(directory,"update-checks.txt"),checks);
    }
    private sealed class FixtureUpdateHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);}
}
