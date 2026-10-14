using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

// End-to-end fixture: accepted baseline LauncherService/ToggleCore plus the delivery wrapper
// and real GitHubDeliveryClient, with a test transport serving the pinned release ZIP.
// Inputs are repository-relative: runtime/content-catalog.json and assets/translation-v28c.zip.
// The ZIP is fetched manually from the pinned release; FixtureResponse checks its catalog SHA/size.
internal static class IntegrationDeliveryTests {
    sealed class FixtureResponse : IDeliveryTransport {
        readonly string repository,tag,assetName;
        readonly byte[] archive;
        readonly long archiveBytes;
        readonly string archiveSha;
        public readonly List<string> Requests=new List<string>();
        public bool Offline;
        readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024};
        public FixtureResponse(DistributionConfig config,ContentGroup group,string archivePath,string contentTag){
            repository=config.Repository;tag=contentTag;assetName=group.AssetName;archive=File.ReadAllBytes(archivePath);archiveBytes=group.Bytes;archiveSha=group.Sha256;
            if(archive.LongLength!=archiveBytes||!Hash(archive).Equals(archiveSha,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Pinned integration ZIP does not match content catalog");
        }
        public DeliveryHttpResponse Get(Uri uri,CancellationToken token){
            token.ThrowIfCancellationRequested();if(Offline)throw new IOException("offline fixture");Requests.Add(uri.AbsoluteUri);
            string releasePath="/repos/"+repository+"/releases/tags/"+tag;
            string assetPath="/"+repository+"/releases/download/"+tag+"/"+assetName;
            if(uri.Host=="api.github.com"&&uri.AbsolutePath==releasePath){
                string url="https://github.com"+assetPath;
                string body=json.Serialize(new{tag_name=tag,draft=false,prerelease=false,assets=new[]{new{name=assetName,size=archiveBytes,digest="sha256:"+archiveSha,state="uploaded",browser_download_url=url}}});
                return Text(body);
            }
            if(uri.Host=="github.com"&&uri.AbsolutePath==assetPath)return new DeliveryHttpResponse{StatusCode=200,ResponseUri=uri,ContentType="application/zip",ContentLength=archive.LongLength,Body=new MemoryStream(archive,false)};
            throw new InvalidOperationException("Unexpected fixture request: "+uri.AbsoluteUri);
        }
        static DeliveryHttpResponse Text(string value){byte[] bytes=Encoding.UTF8.GetBytes(value);return new DeliveryHttpResponse{StatusCode=200,ContentType="application/json",ContentLength=bytes.LongLength,Body=new MemoryStream(bytes,false)};}
    }

    static int passed;
    static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static string HashFile(string path){using(var sha=SHA256.Create())using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    static string AppFile(string app,string relative){return Path.Combine(app,relative.Replace('/',Path.DirectorySeparatorChar));}
    static void CopyTree(string source,string destination){
        Directory.CreateDirectory(destination);
        foreach(string dir in Directory.GetDirectories(source,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(destination,dir.Substring(source.Length+1)));
        foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){
            string relative=file.Substring(source.Length+1).Replace('\\','/');string dest=Path.Combine(destination,relative.Replace('/',Path.DirectorySeparatorChar));
            if(relative=="แพ็ก/manifest.json"&&File.Exists(dest))continue; // matches Setup.iss onlyifdoesntexist
            Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,true);
        }
    }
    static Dictionary<string,object> ReadObject(JavaScriptSerializer json,string path){return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));}
    static void WriteUtf8(string path,string value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value,new UTF8Encoding(false));}
    static void MakeFakeGame(string app,string game,JavaScriptSerializer json){
        string exe=Path.Combine(game,"Client/WindowsNoEditor/HT/Binaries/Win64/HTGame.exe");
        string basePak=Path.Combine(game,"Client/WindowsNoEditor/HT/Content/Paks/global.utoc");
        Directory.CreateDirectory(Path.GetDirectoryName(exe));Directory.CreateDirectory(Path.GetDirectoryName(basePak));
        File.WriteAllBytes(exe,new byte[]{0x4d,0x5a,0x46,0x49,0x58,0x54,0x55,0x52,0x45});
        File.WriteAllBytes(basePak,new byte[]{0x47,0x41,0x4d,0x45,0x50,0x41,0x4b});
        string defaultManifest=AppFile(app,"Fonts/Default/default_manifest.json");
        var manifest=ReadObject(json,defaultManifest);
        manifest["GameFiles"]=new object[]{
            new Dictionary<string,object>{{"Path","Client/WindowsNoEditor/HT/Binaries/Win64/HTGame.exe"},{"Sha256",HashFile(exe)}},
            new Dictionary<string,object>{{"Path","Client/WindowsNoEditor/HT/Content/Paks/global.utoc"},{"Sha256",HashFile(basePak)}}
        };
        string updated=json.Serialize(manifest);WriteUtf8(defaultManifest,updated);
        WriteUtf8(AppFile(app,"แพ็ก/manifest.json"),updated);
        WriteUtf8(AppFile(app,"ตำแหน่งเกม.txt"),Path.GetFullPath(game).TrimEnd('\\','/'));
    }
    static void AddCustomFontState(string app,string fontFixture){
        byte[] bytes=File.ReadAllBytes(fontFixture);string id=Hash(bytes);string sourceRel="Fonts/Imported/"+id+".ttf";string previewRel="Fonts/Profiles/t_"+id.Substring(0,24)+"/preview.ttf";
        string source=AppFile(app,sourceRel),preview=AppFile(app,previewRel);Directory.CreateDirectory(Path.GetDirectoryName(source));Directory.CreateDirectory(Path.GetDirectoryName(preview));File.WriteAllBytes(source,bytes);File.WriteAllBytes(preview,bytes);
        string name="custom Thai integration fixture";string record=new JavaScriptSerializer().Serialize(new{Id=id,Name=name,Source=sourceRel,Preview=previewRel,SourceSha256=id,PreviewSha256=id,Role="thai"});
        WriteUtf8(AppFile(app,"Fonts/library_thai.json"),new JavaScriptSerializer().Serialize(new{Version=1,Items=new[]{new JavaScriptSerializer().DeserializeObject(record)},HiddenBundled=new string[0]}));
        WriteUtf8(AppFile(app,"Fonts/selection.json"),new JavaScriptSerializer().Serialize(new{Id=id,Name=name,Source=sourceRel,Preview=previewRel}));
        var bundleData=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(AppFile(app,"Fonts/Bundled/bundled_fonts.json"),Encoding.UTF8));
        var bundles=((IEnumerable)bundleData["Fonts"]).Cast<Dictionary<string,object>>();var english=bundles.Single(x=>Convert.ToString(x["Role"])=="english");
        WriteUtf8(AppFile(app,"Fonts/english_selection.json"),new JavaScriptSerializer().Serialize(new{Id=Convert.ToString(english["Id"]),Name=Convert.ToString(english["Name"]),Source=Convert.ToString(english["Source"]),Preview=Convert.ToString(english["Preview"])}));
        WriteUtf8(AppFile(app,"Settings/user-settings.json"),"{\"fixture\":\"preserve me\",\"theme\":\"dark\"}");
    }
    static Dictionary<string,string> HashAndTimes(string[] paths){return paths.ToDictionary(p=>p, p=>HashFile(p)+":"+File.GetLastWriteTimeUtc(p).Ticks,StringComparer.OrdinalIgnoreCase);}

    static int Main(string[] args){
        if(args.Length>1){Console.WriteLine("usage: IntegrationDeliveryTests [distribution-root]");return 2;}
        string distRoot=Path.GetFullPath(args.Length==1?args[0]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
        string runtime=Path.Combine(distRoot,"runtime"),catalogPath=Path.Combine(runtime,"content-catalog.json"),archive=Path.Combine(distRoot,"assets","translation-v28c.zip"),testRoot=Path.Combine(distRoot,"tests","shorttd");
        if(!Directory.Exists(runtime)||!File.Exists(catalogPath)||!File.Exists(archive)){Console.WriteLine("FAIL distribution runtime/catalog/pinned archive is incomplete");return 2;}
        string fixtureRoot=Path.Combine(testRoot,"integration-"+Guid.NewGuid().ToString("N")),app=Path.Combine(fixtureRoot,"app"),game=Path.Combine(fixtureRoot,"fake-game");Directory.CreateDirectory(fixtureRoot);
        try{
            CopyTree(runtime,app);
            var serializer=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
            var config=serializer.Deserialize<DistributionConfig>(File.ReadAllText(Path.Combine(runtime,"release-config.json"),Encoding.UTF8));
            var catalog=serializer.Deserialize<ContentCatalog>(File.ReadAllText(catalogPath,Encoding.UTF8));
            var translation=catalog.Groups.Single(x=>x.Id=="translation");
            var transport=new FixtureResponse(config,translation,archive,catalog.ReleaseTag);
            MakeFakeGame(app,game,serializer);
            string[] six=translation.Files.Select(x=>AppFile(app,x.Path)).ToArray();
            Check(six.Length==6&&six.All(x=>!File.Exists(x)),"fixture starts with all six pinned translation payloads missing");

            var inner=new LauncherService(app,()=>false);
            inner.SetGameRoot(game);
            var client=new GitHubDeliveryClient(app,config,catalog,transport);
            var delivered=new DeliveredLauncherService(app,inner,client);
            delivered.Translate(null);
            Check(transport.Requests.Count==2,"first translation lazily downloads pinned release metadata and ZIP");
            Check(translation.Files.All(f=>File.Exists(AppFile(app,f.Path))&&new FileInfo(AppFile(app,f.Path)).Length==f.Bytes&&HashFile(AppFile(app,f.Path))==f.Sha256),"real client installs and verifies all six catalog payloads");
            var active=inner.Inspect();Check(active.Active&&active.CanRestore,"accepted LauncherService/ToggleCore activates only the private fake game");
            string receipt=AppFile(app,"receipt.json"),selectedManifest=AppFile(app,"แพ็ก/manifest.json");
            string[] gamePayloads=Directory.GetFiles(game,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith("HTGame.exe",StringComparison.OrdinalIgnoreCase)&&!p.EndsWith("global.utoc",StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(gamePayloads.Length==6,"fake game received exactly the six accepted-core target files");
            var originalGame=HashAndTimes(new[]{Path.Combine(game,"Client/WindowsNoEditor/HT/Binaries/Win64/HTGame.exe"),Path.Combine(game,"Client/WindowsNoEditor/HT/Content/Paks/global.utoc")});
            var protectedGame=HashAndTimes(gamePayloads.Concat(originalGame.Keys).ToArray());
            var protectedReceipt=HashAndTimes(new[]{receipt,selectedManifest});

            delivered.Translate(null);
            Check(transport.Requests.Count==2,"repeat active translation skips delivery/network");
            Check(protectedGame.All(x=>HashFile(x.Key)+":"+File.GetLastWriteTimeUtc(x.Key).Ticks==x.Value)&&protectedReceipt.All(x=>HashFile(x.Key)+":"+File.GetLastWriteTimeUtc(x.Key).Ticks==x.Value),"repeat active translation leaves game files, receipt and selected manifest unchanged");

            string customThaiFixture=AppFile(app,"Fonts/Default/current.ttf");AddCustomFontState(app,customThaiFixture);
            string customThaiHash=HashFile(customThaiFixture);
            string[] userState=new[]{"Settings/user-settings.json","Fonts/Imported/"+customThaiHash+".ttf","Fonts/Profiles/t_"+customThaiHash.Substring(0,24)+"/preview.ttf","Fonts/library_thai.json","Fonts/selection.json","Fonts/english_selection.json"}.Select(x=>AppFile(app,x)).Concat(new[]{receipt,selectedManifest,AppFile(app,"ตำแหน่งเกม.txt")}).ToArray();
            var userBefore=HashAndTimes(userState);
            // Simulate the installer [Files] overlay: copy the runtime package without deleting or
            // replacing user-owned files; selected manifest is seeded only on first install.
            CopyTree(runtime,app);
            string overlayManifest=AppFile(app,"แพ็ก/manifest.json");if(!File.Exists(overlayManifest))File.Copy(AppFile(app,"Fonts/Default/default_manifest.json"),overlayManifest);
            Check(userBefore.All(x=>File.Exists(x.Key)&&HashFile(x.Key)+":"+File.GetLastWriteTimeUtc(x.Key).Ticks==x.Value),"runtime overlay preserves active receipt, selected manifest, game path, custom fonts and settings");
            var reopened=new LauncherService(app,()=>false);var afterOverlay=reopened.Inspect();
            Check(afterOverlay.Active&&afterOverlay.CanRestore&&!afterOverlay.FontIsDefault&&!afterOverlay.EnglishFontIsDefault&&afterOverlay.FontName=="custom Thai integration fixture","fresh accepted service reopens active state and both selected custom font roles after overlay");

            transport.Offline=true;string optionalWorker=AppFile(app,"Tools/FontWorker.exe");if(File.Exists(optionalWorker))File.Delete(optionalWorker);
            Check(!File.Exists(optionalWorker),"optional font-tools worker is absent for offline restore");
            var offlineClient=new GitHubDeliveryClient(app,config,catalog,transport);var offlineDelivered=new DeliveredLauncherService(app,reopened,offlineClient);
            offlineDelivered.Restore(null);
            Check(transport.Requests.Count==2,"offline restore makes no network request");
            var restored=reopened.Inspect();Check(!File.Exists(receipt)&&!restored.Active&&!restored.CanRestore,"offline restore clears the active receipt in the accepted service");
            Check(originalGame.All(x=>HashFile(x.Key)+":"+File.GetLastWriteTimeUtc(x.Key).Ticks==x.Value),"offline restore leaves every original fake-game file byte/timestamp unchanged");
            Check(!Directory.Exists(Path.Combine(game,"Client/WindowsNoEditor/HT/Content/Paks/~mods"))||Directory.GetFileSystemEntries(Path.Combine(game,"Client/WindowsNoEditor/HT/Content/Paks/~mods")).Length==0,"offline restore removes only the six owned target files from fake game");
            Console.WriteLine("RESULT "+passed+" passed, 0 failed; isolated fake app/game; actual client, wrapper and accepted core");return 0;
        }catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
        finally{try{if(Directory.Exists(fixtureRoot))Directory.Delete(fixtureRoot,true);}catch{}}
    }
}
