using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

internal static class GitHubDeliveryClientTests {
    sealed class Pair { public string Path; public byte[] Bytes; public Pair(string path, byte[] bytes){Path=path;Bytes=bytes;} }
    sealed class FakeTransport : IDeliveryTransport {
        public readonly List<string> Requests = new List<string>();
        public readonly Dictionary<string,Func<DeliveryHttpResponse>> Responses = new Dictionary<string,Func<DeliveryHttpResponse>>(StringComparer.OrdinalIgnoreCase);
        public DeliveryHttpResponse Get(Uri uri,CancellationToken token){
            Requests.Add(uri.AbsoluteUri);
            Func<DeliveryHttpResponse> f;
            if(Responses.TryGetValue(uri.AbsolutePath,out f)) return f();
            throw new InvalidOperationException("Unexpected local fixture URL: "+uri.AbsolutePath);
        }
        public void Add(string absolutePath,byte[] bytes,string contentType="application/octet-stream"){
            Responses[absolutePath]=()=>new DeliveryHttpResponse{StatusCode=200,ContentLength=bytes.LongLength,Body=new MemoryStream(bytes,false),ContentType=contentType};
        }
        public void AddText(string absolutePath,string text){Add(absolutePath,Encoding.UTF8.GetBytes(text),"application/json");}
    }
    sealed class Fixture : IDisposable {
        public readonly string Root;
        public readonly FakeTransport Http = new FakeTransport();
        public readonly DistributionConfig Config = new DistributionConfig{Repository="acme/nte-thai",CurrentVersion="1.0.0"};
        public readonly ContentCatalog Catalog;
        public readonly Pair[] Files;
        public readonly byte[] ZipBytes;
        public readonly ContentGroup Group;
        public GitHubDeliveryClient Client { get { return new GitHubDeliveryClient(Root,Config,Catalog,Http); } }
        public Fixture(Pair[] files=null){
            Root=Path.Combine(Path.GetTempPath(),"nte-delivery-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
            Files=files??new[]{new Pair("แพ็ก/zzz_NTE_Thai_Shops_999_P.pak",Encoding.UTF8.GetBytes("pinned pak bytes"))};
            ZipBytes=Zip(Files);Group=new ContentGroup{Id="translation",AssetName="translation-v28c.zip",Sha256=Hash(ZipBytes),Bytes=ZipBytes.LongLength,Files=Files.Select(x=>new ContentFile{Path=x.Path,Sha256=Hash(x.Bytes),Bytes=x.Bytes.LongLength}).ToArray()};
            Catalog=new ContentCatalog{ReleaseTag="v28c",Groups=new[]{Group}};
        }
        public string ReleasePath { get { return "/repos/acme/nte-thai/releases/tags/v28c"; } }
        public string LatestPath { get { return "/repos/acme/nte-thai/releases/latest"; } }
        public string AssetPath { get { return "/acme/nte-thai/releases/download/v28c/"+Group.AssetName; } }
        public string ReleaseUrl { get { return "https://github.com/acme/nte-thai/releases/download/v28c/"+Group.AssetName; } }
        public void AddContentRelease(){
            var json=new JavaScriptSerializer().Serialize(new{tag_name="v28c",draft=false,prerelease=false,assets=new[]{new{name=Group.AssetName,size=Group.Bytes,digest="sha256:"+Group.Sha256,browser_download_url=ReleaseUrl}}});
            Http.AddText(ReleasePath,json);Http.Add(AssetPath,ZipBytes);
        }
        public void Dispose(){Directory.Delete(Root,true);}
    }
    static byte[] Zip(Pair[] files){
        using(var memory=new MemoryStream()){
            using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))foreach(var file in files){var entry=zip.CreateEntry(file.Path,CompressionLevel.Optimal);using(var stream=entry.Open())stream.Write(file.Bytes,0,file.Bytes.Length);}
            return memory.ToArray();
        }
    }
    static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    static string HashFile(string path){return Hash(File.ReadAllBytes(path));}
    static int passed,failed;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Test(string name,Action action){try{action();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex.GetType().Name+": "+ex.Message);failed++;}}
    static void Throws(Action action,string messageContains=null){try{action();}catch(Exception ex){if(messageContains!=null)Check(ex.Message.Contains(messageContains),"Unexpected failure: "+ex.Message);return;}throw new Exception("Expected delivery failure");}
    static int Main(){
        Test("valid pinned content skips every network request",()=>{using(var f=new Fixture()){
            var file=f.Files[0];string target=Path.Combine(f.Root,file.Path.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,file.Bytes);
            f.Client.Ensure(new[]{"translation"},null,CancellationToken.None);Check(f.Http.Requests.Count==0,"Valid files still triggered network requests");
        }});
        Test("download verifies archive and files then replaces only corrupt catalog-owned bytes",()=>{using(var f=new Fixture()){
            string target=Path.Combine(f.Root,f.Files[0].Path.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,"corrupt");
            string settings=Path.Combine(f.Root,"Settings.json"),selection=Path.Combine(f.Root,"Fonts/selection.json"),receipt=Path.Combine(f.Root,"receipt.json"),manifest=Path.Combine(f.Root,"แพ็ก/manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(selection));Directory.CreateDirectory(Path.GetDirectoryName(manifest));File.WriteAllText(settings,"settings");File.WriteAllText(selection,"selected");File.WriteAllText(receipt,"receipt");File.WriteAllText(manifest,"selected manifest");
            f.AddContentRelease();f.Client.Ensure(new[]{"translation"},null,CancellationToken.None);
            Check(HashFile(target)==Hash(f.Files[0].Bytes),"Pinned target did not replace corrupt content");
            Check(File.ReadAllText(settings)=="settings"&&File.ReadAllText(selection)=="selected"&&File.ReadAllText(receipt)=="receipt"&&File.ReadAllText(manifest)=="selected manifest","User or active-install state changed");
        }});
        Test("installing one missing group member preserves verified peer bytes and timestamp",()=>{using(var f=new Fixture(new[]{new Pair("Tools/repak.exe",Encoding.UTF8.GetBytes("verified tool")),new Pair("Tools/FontWorker.exe",Encoding.UTF8.GetBytes("new worker"))})){
            string valid=Path.Combine(f.Root,"Tools/repak.exe"),missing=Path.Combine(f.Root,"Tools/FontWorker.exe");Directory.CreateDirectory(Path.GetDirectoryName(valid));File.WriteAllBytes(valid,f.Files[0].Bytes);
            DateTime marker=new DateTime(2001,2,3,4,5,6,DateTimeKind.Utc);File.SetLastWriteTimeUtc(valid,marker);
            f.AddContentRelease();f.Client.Ensure(new[]{"translation"},null,CancellationToken.None);
            Check(HashFile(valid)==Hash(f.Files[0].Bytes),"Verified peer bytes changed");Check(File.GetLastWriteTimeUtc(valid)==marker,"Verified peer timestamp changed during partial group repair");Check(HashFile(missing)==Hash(f.Files[1].Bytes),"Missing catalog member was not installed");
        }});
        Test("unsafe and user-state paths in catalog are rejected before network",()=>{using(var f=new Fixture(new[]{new Pair("../receipt.json",Encoding.UTF8.GetBytes("x"))})){
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(f.Http.Requests.Count==0,"Unsafe catalog caused network traffic");Check(!File.Exists(Path.Combine(f.Root,"receipt.json")),"Catalog wrote the receipt");
        }});
        Test("archive traversal is rejected without changing an existing destination",()=>{using(var f=new Fixture()){
            byte[] unsafeZip=Zip(new[]{new Pair("../receipt.json",Encoding.UTF8.GetBytes("owned"))});f.Group.Sha256=Hash(unsafeZip);f.Group.Bytes=unsafeZip.LongLength;f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));f.Http.Add(f.AssetPath,unsafeZip);
            string receipt=Path.Combine(f.Root,"receipt.json");File.WriteAllText(receipt,"keep");Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(File.ReadAllText(receipt)=="keep","Traversal modified receipt");
        }});
        Test("archive case collisions are rejected before commit",()=>{using(var f=new Fixture(new[]{new Pair("Tools/FontWorker.exe",new byte[]{1})})){
            byte[] collision=Zip(new[]{new Pair("Tools/FontWorker.exe",new byte[]{1}),new Pair("tools/fontworker.exe",new byte[]{2})});f.Group.Sha256=Hash(collision);f.Group.Bytes=collision.LongLength;f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));f.Http.Add(f.AssetPath,collision);
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(!Directory.Exists(Path.Combine(f.Root,"Tools")),"Case-colliding members were extracted");
        }});
        Test("unlisted archive members are rejected before extraction",()=>{using(var f=new Fixture()){
            byte[] extra=Zip(new[]{f.Files[0],new Pair("Settings.json",Encoding.UTF8.GetBytes("not owned"))});f.Group.Sha256=Hash(extra);f.Group.Bytes=extra.LongLength;f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));f.Http.Add(f.AssetPath,extra);
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(!File.Exists(Path.Combine(f.Root,"Settings.json")),"Unlisted file was extracted");
        }});
        Test("archive symbolic-link members are rejected even at an allowlisted file path",()=>{using(var f=new Fixture()){
            byte[] link=ZipLink(f.Files[0].Path,Encoding.UTF8.GetBytes("target"));f.Group.Sha256=Hash(link);f.Group.Bytes=link.LongLength;f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));f.Http.Add(f.AssetPath,link);
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(!File.Exists(Path.Combine(f.Root,f.Files[0].Path.Replace('/',Path.DirectorySeparatorChar))),"Symbolic link was extracted as content");
        }});
        Test("failed multi-file commit restores overwritten bytes and leaves no staged files",()=>{using(var f=new Fixture(new[]{new Pair("Tools/repak.exe",Encoding.UTF8.GetBytes("new tool")),new Pair("Fonts/Original/MiSans-Heavy.ufont",Encoding.UTF8.GetBytes("new font"))})){
            f.AddContentRelease();string first=Path.Combine(f.Root,"Tools/repak.exe");Directory.CreateDirectory(Path.GetDirectoryName(first));File.WriteAllText(first,"previous tool");Directory.CreateDirectory(Path.Combine(f.Root,"Fonts"));File.WriteAllText(Path.Combine(f.Root,"Fonts/Original"),"blocking noncatalog file");
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(File.ReadAllText(first)=="previous tool","Failed transaction did not restore prior bytes");Check(File.ReadAllText(Path.Combine(f.Root,"Fonts/Original"))=="blocking noncatalog file","Transaction changed noncatalog path");Check(Directory.GetFiles(Path.Combine(f.Root,".delivery-staging"),"*",SearchOption.AllDirectories).Length==0,"Failed transaction left staged files");
        }});
        Test("failed rollback retains recovery backup and reports its location",()=>{using(var f=new Fixture(new[]{new Pair("Tools/repak.exe",Encoding.UTF8.GetBytes("new tool")),new Pair("Fonts/Original/MiSans-Heavy.ufont",Encoding.UTF8.GetBytes("new font"))})){
            f.AddContentRelease();string first=Path.Combine(f.Root,"Tools/repak.exe");Directory.CreateDirectory(Path.GetDirectoryName(first));File.WriteAllText(first,"previous tool");Directory.CreateDirectory(Path.Combine(f.Root,"Fonts"));File.WriteAllText(Path.Combine(f.Root,"Fonts/Original"),"blocking noncatalog file");
            var client=f.Client;client.FileMoveOverride=(source,destination)=>{if(source.EndsWith(".bak",StringComparison.OrdinalIgnoreCase))throw new IOException("fixture blocks restore");File.Move(source,destination);};
            try{client.Ensure(new[]{"translation"},null,CancellationToken.None);throw new Exception("Expected incomplete rollback");}catch(InvalidOperationException ex){Check(ex.Message.Contains("สำเนาสำรองยังเก็บไว้ที่"),"Incomplete rollback error did not explain recovery");Check(ex.Message.Contains(".delivery-staging"),"Recovery location was omitted");}
            string[] backups=Directory.GetFiles(Path.Combine(f.Root,".delivery-staging"),"*.bak",SearchOption.AllDirectories);Check(backups.Length==1&&File.ReadAllText(backups[0])=="previous tool","Prior bytes were not retained as a recovery backup");
        }});
        Test("constructor rejects app roots below a reparse-point ancestor",()=>{
            string temp=Path.Combine(Path.GetTempPath(),"nte-delivery-junction-"+Guid.NewGuid().ToString("N"));string target=Path.Combine(temp,"target"),junction=Path.Combine(temp,"linked"),app=Path.Combine(junction,"app");Directory.CreateDirectory(Path.Combine(target,"app"));Directory.CreateDirectory(temp);
            try{var start=new System.Diagnostics.ProcessStartInfo("cmd.exe","/c mklink /J \""+junction+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true};using(var process=System.Diagnostics.Process.Start(start)){process.WaitForExit();Check(process.ExitCode==0,"Could not create junction fixture");}
                Throws(()=>new GitHubDeliveryClient(app,new DistributionConfig{Repository="acme/nte-thai",CurrentVersion="1.0.0"},new ContentCatalog{ReleaseTag="v28c",Groups=new ContentGroup[0]},new FakeTransport()),"ลิงก์");
            }finally{try{if(Directory.Exists(junction))Directory.Delete(junction,false);}catch{}try{if(Directory.Exists(temp))Directory.Delete(temp,true);}catch{}}
        });
        Test("release asset redirects only to the official GitHub CDN hosts",()=>{using(var f=new Fixture()){
            f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));
            string cdnUrl="https://release-assets.githubusercontent.com/download/signed?token=fixture-signature";
            f.Http.Responses[f.AssetPath]=()=>new DeliveryHttpResponse{StatusCode=302,Location=cdnUrl};
            f.Http.Add("/download/signed",f.ZipBytes);
            f.Client.Ensure(new[]{"translation"},null,CancellationToken.None);
            Check(HashFile(Path.Combine(f.Root,f.Files[0].Path.Replace('/',Path.DirectorySeparatorChar)))==Hash(f.Files[0].Bytes),"Official CDN redirect did not deliver verified content");
            Check(f.Http.Requests.Any(x=>x.StartsWith("https://release-assets.githubusercontent.com/",StringComparison.Ordinal)),"CDN request was not followed");
        }});
        Test("redirect to an unapproved host is rejected without exposing signed URL",()=>{using(var f=new Fixture()){
            f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));f.Http.Responses[f.AssetPath]=()=>new DeliveryHttpResponse{StatusCode=302,Location="https://attacker.invalid/signed?token=secret-fixture"};
            try{f.Client.Ensure(new[]{"translation"},null,CancellationToken.None);throw new Exception("Expected redirect rejection");}catch(InvalidOperationException ex){Check(!ex.Message.Contains("attacker.invalid")&&!ex.Message.Contains("secret-fixture"),"Failure leaked redirect URL or signed token");}
            Check(!File.Exists(Path.Combine(f.Root,f.Files[0].Path.Replace('/',Path.DirectorySeparatorChar))),"Untrusted redirect wrote catalog content");
        }});
        Test("cancellation during archive streaming preserves prior content and removes partial staging",()=>{using(var f=new Fixture()){
            string target=Path.Combine(f.Root,f.Files[0].Path.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,"old verified path");f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));
            using(var source=new CancellationTokenSource()){
                f.Http.Responses[f.AssetPath]=()=>new DeliveryHttpResponse{StatusCode=200,ContentLength=f.ZipBytes.LongLength,Body=new CancelAfterStream(f.ZipBytes,source)};
                Throws(()=>f.Client.Ensure(new[]{"translation"},null,source.Token));
            }
            Check(File.ReadAllText(target)=="old verified path","Cancelled transfer replaced existing content");Check(Directory.GetFiles(Path.Combine(f.Root,".delivery-staging"),"*",SearchOption.AllDirectories).Length==0,"Cancellation left partial files");
        }});
        Test("latest stable release returns newer version only with exact installer asset and digest",()=>{using(var f=new Fixture()){
            byte[] exe=Encoding.UTF8.GetBytes("verified installer fixture");string digest=Hash(exe);string url="https://github.com/acme/nte-thai/releases/download/v1.0.1/Thai-Translator-Setup-1.0.1.exe";
            f.Http.AddText(f.LatestPath,new JavaScriptSerializer().Serialize(new{tag_name="v1.0.1",draft=false,prerelease=false,body="Release notes",assets=new[]{new{name="Thai-Translator-Setup-1.0.1.exe",size=exe.Length,digest="sha256:"+digest,browser_download_url=url}}}));
            ReleaseInfo info=f.Client.Check(CancellationToken.None);Check(info!=null&&info.Version=="1.0.1"&&info.InstallerSha256==digest,"Stable release metadata was not parsed");
        }});
        Test("current and older releases do not show an update",()=>{using(var f=new Fixture()){
            f.Http.AddText(f.LatestPath,LatestJson("v1.0.0",false,false,"Thai-Translator-Setup-1.0.0.exe",new byte[]{1}));Check(f.Client.Check(CancellationToken.None)==null,"Current version produced an update");
        }});
        Test("draft and prerelease payloads are ignored and invalid tags fail closed",()=>{using(var f=new Fixture()){
            f.Http.AddText(f.LatestPath,LatestJson("v1.0.2",true,false,"Thai-Translator-Setup-1.0.2.exe",new byte[]{1}));Check(f.Client.Check(CancellationToken.None)==null,"Draft release was offered");
        } using(var g=new Fixture()){
            g.Http.AddText(g.LatestPath,LatestJson("v1.0.2-beta",false,false,"Thai-Translator-Setup-1.0.2.exe",new byte[]{1}));Throws(()=>g.Client.Check(CancellationToken.None));
        } using(var h=new Fixture()){
            h.Http.AddText(h.LatestPath,LatestJson("v1.0.2",false,true,"Thai-Translator-Setup-1.0.2.exe",new byte[]{1}));Check(h.Client.Check(CancellationToken.None)==null,"Prerelease was offered");
        } using(var i=new Fixture()){
            i.Http.AddText(i.LatestPath,LatestJson("v١.٠.٢",false,false,"Thai-Translator-Setup-1.0.2.exe",new byte[]{1}));Throws(()=>i.Client.Check(CancellationToken.None));
        }});
        Test("version ordering is numeric across patch and revision components",()=>{using(var f=new Fixture()){
            f.Config.CurrentVersion="1.0.9";f.Http.AddText(f.LatestPath,LatestJson("v1.0.10",false,false,"Thai-Translator-Setup-1.0.10.exe",new byte[]{1}));Check(f.Client.Check(CancellationToken.None).Version=="1.0.10","Patch versions were compared lexically");
        } using(var g=new Fixture()){
            g.Config.CurrentVersion="1.2.3";g.Http.AddText(g.LatestPath,LatestJson("v1.2.3.1",false,false,"Thai-Translator-Setup-1.2.3.1.exe",new byte[]{1}));Check(g.Client.Check(CancellationToken.None).Version=="1.2.3.1","Revision version was not recognized as newer");
        }});
        Test("latest installer digest and repository URL must both match trusted release metadata",()=>{using(var f=new Fixture()){
            string url="https://github.com/acme/nte-thai/releases/download/v1.0.2/Thai-Translator-Setup-1.0.2.exe";
            f.Http.AddText(f.LatestPath,new JavaScriptSerializer().Serialize(new{tag_name="v1.0.2",draft=false,prerelease=false,assets=new[]{new{name="Thai-Translator-Setup-1.0.2.exe",size=10,digest="sha256:bad",browser_download_url=url}}}));
            Throws(()=>f.Client.Check(CancellationToken.None));
        } using(var g=new Fixture()){
            g.Http.AddText(g.LatestPath,new JavaScriptSerializer().Serialize(new{tag_name="v1.0.2",draft=false,prerelease=false,assets=new[]{new{name="Thai-Translator-Setup-1.0.2.exe",size=10,digest="sha256:"+new string('a',64),browser_download_url="https://evil.example/x"}}}));Throws(()=>g.Client.Check(CancellationToken.None));
        }});
        Test("malformed nonempty content digest fails closed",()=>{using(var f=new Fixture()){
            f.Http.AddText(f.ReleasePath,new JavaScriptSerializer().Serialize(new{tag_name="v28c",draft=false,prerelease=false,assets=new[]{new{name=f.Group.AssetName,size=f.Group.Bytes,digest="sha256:not-a-hash",browser_download_url=f.ReleaseUrl}}}));
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(f.Http.Requests.Count==1,"Malformed digest reached archive download");
        }});
        Test("download verifies installer size and digest before returning a local path",()=>{using(var f=new Fixture()){
            byte[] exe=Encoding.UTF8.GetBytes("approved installer bytes");string digest=Hash(exe);string url="https://github.com/acme/nte-thai/releases/download/v1.0.1/Thai-Translator-Setup-1.0.1.exe";string path="/acme/nte-thai/releases/download/v1.0.1/Thai-Translator-Setup-1.0.1.exe";f.Http.Add(path,exe);
            var info=new ReleaseInfo{Version="1.0.1",Tag="v1.0.1",InstallerName="Thai-Translator-Setup-1.0.1.exe",InstallerUrl=url,InstallerSha256=digest,InstallerBytes=exe.LongLength};
            string installed=f.Client.Download(info,null,CancellationToken.None);string expectedPath=Path.Combine(f.Root,"Settings","Downloads","Thai-Translator-Setup-1.0.1.exe");Check(String.Equals(installed,expectedPath,StringComparison.OrdinalIgnoreCase),"Installer path does not match updater-helper contract");Check(HashFile(installed)==digest,"Returned installer hash does not match pinned digest");
            int requestCount=f.Http.Requests.Count;string cached=f.Client.Download(info,null,CancellationToken.None);Check(cached==installed&&f.Http.Requests.Count==requestCount,"Verified cached installer triggered another network request");
        }});
        Test("installer digest mismatch does not leave a runnable partial",()=>{using(var f=new Fixture()){
            byte[] exe=Encoding.UTF8.GetBytes("wrong installer");string url="https://github.com/acme/nte-thai/releases/download/v1.0.1/Thai-Translator-Setup-1.0.1.exe";f.Http.Add("/acme/nte-thai/releases/download/v1.0.1/Thai-Translator-Setup-1.0.1.exe",exe);
            var info=new ReleaseInfo{Version="1.0.1",Tag="v1.0.1",InstallerName="Thai-Translator-Setup-1.0.1.exe",InstallerUrl=url,InstallerSha256=new string('0',64),InstallerBytes=exe.LongLength};
            Throws(()=>f.Client.Download(info,null,CancellationToken.None));string downloads=Path.Combine(f.Root,"Settings","Downloads");Check(!Directory.Exists(downloads)||Directory.GetFiles(downloads).Length==0,"Unverified installer was left behind");
        }});
        Test("partial network failure preserves prior file and cleans temporary data",()=>{using(var f=new Fixture()){
            string target=Path.Combine(f.Root,f.Files[0].Path.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,"old catalog bytes");f.Http.AddText(f.ReleasePath,ContentReleaseJson(f));
            f.Http.Responses[f.AssetPath]=()=>new DeliveryHttpResponse{StatusCode=200,ContentLength=f.ZipBytes.LongLength,Body=new ThrowAfterStream(f.ZipBytes,128)};
            Throws(()=>f.Client.Ensure(new[]{"translation"},null,CancellationToken.None));Check(File.ReadAllText(target)=="old catalog bytes","Failed transfer replaced prior content");Check(Directory.GetFiles(f.Root,"*.partial",SearchOption.AllDirectories).Length==0,"Partial download remained after failure");
        }});
        Test("test transport still receives repository-bound HTTPS URLs",()=>{using(var f=new Fixture()){
            f.AddContentRelease();f.Client.Ensure(new[]{"translation"},null,CancellationToken.None);Check(f.Http.Requests.Count==2,"Expected tag metadata and one asset request");Check(f.Http.Requests.All(x=>x.StartsWith("https://api.github.com/repos/acme/nte-thai/",StringComparison.Ordinal)||x.StartsWith("https://github.com/acme/nte-thai/",StringComparison.Ordinal)),"Request escaped the configured HTTPS repository");
        }});
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static string ContentReleaseJson(Fixture f){return new JavaScriptSerializer().Serialize(new{tag_name="v28c",draft=false,prerelease=false,assets=new[]{new{name=f.Group.AssetName,size=f.Group.Bytes,digest="sha256:"+f.Group.Sha256,browser_download_url=f.ReleaseUrl}}});}
    static string LatestJson(string tag,bool draft,bool prerelease,string name,byte[] bytes){string url="https://github.com/acme/nte-thai/releases/download/"+tag+"/"+name;return new JavaScriptSerializer().Serialize(new{tag_name=tag,draft=draft,prerelease=prerelease,body="test notes",assets=new[]{new{name=name,size=bytes.LongLength,digest="sha256:"+Hash(bytes),browser_download_url=url}}});}
    static byte[] ZipLink(string name,byte[] target){using(var memory=new MemoryStream()){using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){var entry=zip.CreateEntry(name,CompressionLevel.Optimal);entry.ExternalAttributes=(0xA000<<16)|0x400;using(var stream=entry.Open())stream.Write(target,0,target.Length);}return memory.ToArray();}}
    sealed class ThrowAfterStream : Stream {
        readonly MemoryStream inner; readonly int limit; int read;
        public ThrowAfterStream(byte[] bytes,int limit){inner=new MemoryStream(bytes,false);this.limit=limit;}
        public override int Read(byte[] buffer,int offset,int count){if(read>=limit)throw new IOException("fixture interrupted");int n=inner.Read(buffer,offset,Math.Min(count,limit-read));read+=n;return n;}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{return inner.Length;}}public override long Position{get{return read;}set{throw new NotSupportedException();}}public override void Flush(){throw new NotSupportedException();}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long v){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
    }
    sealed class CancelAfterStream : Stream {
        readonly MemoryStream inner;readonly CancellationTokenSource source;bool cancelled;
        public CancelAfterStream(byte[] bytes,CancellationTokenSource source){inner=new MemoryStream(bytes,false);this.source=source;}
        public override int Read(byte[] buffer,int offset,int count){int n=inner.Read(buffer,offset,count);if(!cancelled&&n>0){cancelled=true;source.Cancel();}return n;}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{return inner.Length;}}public override long Position{get{return inner.Position;}set{throw new NotSupportedException();}}public override void Flush(){throw new NotSupportedException();}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long v){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
    }
}
