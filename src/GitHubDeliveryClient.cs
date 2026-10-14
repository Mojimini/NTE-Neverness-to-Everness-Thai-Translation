using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

// Internal injection seam used only by delivery_* fixture tests. Production callers use
// the public constructor, which sends HTTPS requests directly to GitHub.
internal interface IDeliveryTransport {
    DeliveryHttpResponse Get(Uri uri, CancellationToken token);
}

internal sealed class DeliveryHttpResponse : IDisposable {
    public int StatusCode { get; set; }
    public Uri ResponseUri { get; set; }
    public string Location { get; set; }
    public string ContentType { get; set; }
    public long ContentLength { get; set; }
    public Stream Body { get; set; }
    readonly IDisposable owner;
    public DeliveryHttpResponse() {}
    internal DeliveryHttpResponse(int status,Uri responseUri,string location,string contentType,long contentLength,Stream body,IDisposable owner){
        StatusCode=status;ResponseUri=responseUri;Location=location;ContentType=contentType;ContentLength=contentLength;Body=body;this.owner=owner;
    }
    public void Dispose(){if(Body!=null)Body.Dispose();if(owner!=null)owner.Dispose();}
}

internal sealed class WebRequestDeliveryTransport : IDeliveryTransport {
    public DeliveryHttpResponse Get(Uri uri,CancellationToken token){
        token.ThrowIfCancellationRequested();
        var request=(HttpWebRequest)WebRequest.Create(uri);
        request.Method="GET";request.AllowAutoRedirect=false;request.UserAgent="NTEThai-Distribution/1.0";
        request.Accept="application/vnd.github+json, application/octet-stream";request.Timeout=30000;request.ReadWriteTimeout=30000;request.KeepAlive=false;
        var registration=token.Register(()=>{try{request.Abort();}catch{}});
        try{
            HttpWebResponse response;
            try{response=(HttpWebResponse)request.GetResponse();}
            catch(WebException ex){
                if(token.IsCancellationRequested){registration.Dispose();throw new OperationCanceledException(token);}
                response=ex.Response as HttpWebResponse;
                if(response==null){registration.Dispose();throw new InvalidOperationException("เชื่อมต่อ GitHub ไม่สำเร็จ");}
            }
            return new DeliveryHttpResponse((int)response.StatusCode,response.ResponseUri,response.Headers[HttpResponseHeader.Location],response.ContentType,response.ContentLength,response.GetResponseStream(),new ResponseOwner(response,registration));
        }catch{registration.Dispose();throw;}
    }
    sealed class ResponseOwner : IDisposable {
        readonly HttpWebResponse response;readonly CancellationTokenRegistration registration;
        public ResponseOwner(HttpWebResponse response,CancellationTokenRegistration registration){this.response=response;this.registration=registration;}
        public void Dispose(){response.Dispose();registration.Dispose();}
    }
}

public sealed class GitHubDeliveryClient : IContentDelivery,IReleaseDelivery {
    const long MaxArchiveBytes=256L*1024*1024;
    const long MaxArchiveFileBytes=512L*1024*1024;
    const long MaxGroupFilesBytes=512L*1024*1024;
    const long MaxInstallerBytes=512L*1024*1024;
    const int MaxApiBytes=2*1024*1024;
    const int MaxRedirects=5;
    static readonly Regex RepositoryPattern=new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",RegexOptions.CultureInvariant);
    static readonly Regex ReleaseTagPattern=new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$",RegexOptions.CultureInvariant);
    static readonly Regex VersionPattern=new Regex(@"^v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*))?)$",RegexOptions.CultureInvariant);
    static readonly Regex HexPattern=new Regex(@"^[0-9a-fA-F]{64}$",RegexOptions.CultureInvariant);
    static readonly string[] AllowedAssetRedirectHosts={"release-assets.githubusercontent.com","objects.githubusercontent.com"};
    readonly string appRoot;
    readonly DistributionConfig config;
    readonly ContentCatalog catalog;
    readonly IDeliveryTransport transport;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=MaxApiBytes};
    internal Action<string,string> FileMoveOverride { get; set; }

    sealed class RollbackIncompleteException : InvalidOperationException {
        public RollbackIncompleteException(string stage,Exception cause):base("การย้อนคืนไฟล์เนื้อหาไม่สมบูรณ์ สำเนาสำรองยังเก็บไว้ที่ "+stage,cause){}
    }

    public GitHubDeliveryClient(string appRoot,DistributionConfig config,ContentCatalog catalog)
        :this(appRoot,config,catalog,new WebRequestDeliveryTransport()){}
    internal GitHubDeliveryClient(string appRoot,DistributionConfig config,ContentCatalog catalog,IDeliveryTransport transport){
        if(string.IsNullOrWhiteSpace(appRoot)||!Path.IsPathRooted(appRoot))throw new ArgumentException("ต้องระบุโฟลเดอร์แอปแบบเต็ม","appRoot");
        this.appRoot=Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!Directory.Exists(this.appRoot))throw new DirectoryNotFoundException("ไม่พบโฟลเดอร์แอป");
        RejectReparse(this.appRoot,this.appRoot);
        if(config==null)throw new ArgumentNullException("config");if(catalog==null)throw new ArgumentNullException("catalog");if(transport==null)throw new ArgumentNullException("transport");this.config=config;this.catalog=catalog;this.transport=transport;
        string[] repositoryParts=(config.Repository??"").Split('/');
        if(!RepositoryPattern.IsMatch(config.Repository??"")||repositoryParts.Length!=2||repositoryParts.Any(x=>x=="."||x==".."))throw new ArgumentException("Repository ต้องเป็น owner/repository","config");
        if(string.IsNullOrWhiteSpace(config.CurrentVersion))throw new ArgumentException("ไม่พบเวอร์ชันแอปปัจจุบัน","config");
    }

    public void Ensure(string[] groups,Action<TransferProgress> progress,CancellationToken token){
        token.ThrowIfCancellationRequested();
        if(groups==null)throw new ArgumentNullException("groups");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(string id in groups){
            token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(id)||!ids.Add(id))throw new InvalidOperationException("รายการกลุ่มเนื้อหาไม่ถูกต้อง");
            ContentGroup group=(catalog.Groups??new ContentGroup[0]).SingleOrDefault(x=>x!=null&&x.Id==id);
            if(group==null)throw new InvalidOperationException("ไม่พบกลุ่มเนื้อหาที่ร้องขอ");
            ValidateGroup(group);
            if(IsGroupComplete(group))continue;
            EnsureGroup(group,progress,token);
        }
    }

    void ValidateGroup(ContentGroup group){
        if(!ReleaseTagPattern.IsMatch(catalog.ReleaseTag??"")||string.IsNullOrWhiteSpace(group.AssetName)||
           !SafeAssetName(group.AssetName)||!HexPattern.IsMatch(group.Sha256??"")||group.Bytes<1||group.Bytes>MaxArchiveBytes||
           group.Files==null||group.Files.Length==0||group.Files.Length>64)throw new InvalidOperationException("หมุดกลุ่มเนื้อหาไม่ถูกต้อง");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total=0;
        foreach(ContentFile file in group.Files){
            if(file==null||file.Bytes<0||file.Bytes>MaxArchiveFileBytes||!HexPattern.IsMatch(file.Sha256??"")||!paths.Add(file.Path??""))throw new InvalidOperationException("รายการไฟล์ในกลุ่มเนื้อหาไม่ถูกต้อง");
            if(total>MaxGroupFilesBytes-file.Bytes)throw new InvalidOperationException("ขนาดรวมของเนื้อหาเกินกำหนด");total+=file.Bytes;
            ValidateOwnedRelativePath(file.Path);
        }
    }

    bool IsGroupComplete(ContentGroup group){
        foreach(ContentFile file in group.Files){
            string path=AppPath(file.Path);
            if(Directory.Exists(path))throw new InvalidOperationException("ตำแหน่งไฟล์เนื้อหาถูกแทนที่ด้วยโฟลเดอร์");
            if(!File.Exists(path))return false;
            if(!HashFile(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length!=file.Bytes)return false;
        }
        return true;
    }

    void EnsureGroup(ContentGroup group,Action<TransferProgress> progress,CancellationToken token){
        string stage=CreateStage();
        bool retainRecovery=false;
        try{
            token.ThrowIfCancellationRequested();
            Dictionary<string,object> release=ReadJson(GetApi(new Uri(ApiRoot()+"/releases/tags/"+Uri.EscapeDataString(catalog.ReleaseTag)),token),MaxApiBytes,token);
            ValidateCatalogRelease(release,catalog.ReleaseTag);
            Dictionary<string,object> asset=FindAsset(release,group.AssetName);
            if(asset==null)throw new InvalidOperationException("ไม่พบ archive ที่ตรึงไว้ใน GitHub Release");
            ValidateContentAsset(asset,group);
            string archivePath=Path.Combine(stage,"content.zip.partial");
            Uri url=ExpectedReleaseAssetUrl(catalog.ReleaseTag,group.AssetName);
            DownloadToFile(url,archivePath,group.Bytes,MaxArchiveBytes,"กำลังดาวน์โหลดเนื้อหาที่ร้องขอ",progress,token);
            if(!HashFile(archivePath).Equals(group.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("archive ไม่ตรงกับ hash ที่ตรึงไว้");
            var staged=ValidateAndExtract(archivePath,group,stage,token,progress);
            CommitStaged(staged,group.Files,stage,token);
        }catch(RollbackIncompleteException){retainRecovery=true;throw;}
        finally{if(!retainRecovery)TryDeleteDirectory(stage);}
    }

    Dictionary<string,string> ValidateAndExtract(string archivePath,ContentGroup group,string stage,CancellationToken token,Action<TransferProgress> progress){
        var expected=group.Files.ToDictionary(x=>x.Path,StringComparer.Ordinal);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extracted=new Dictionary<string,string>(StringComparer.Ordinal);
        string extract=Path.Combine(stage,"files");Directory.CreateDirectory(extract);
        using(var fs=new FileStream(archivePath,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var zip=new ZipArchive(fs,ZipArchiveMode.Read,false)){
            foreach(ZipArchiveEntry entry in zip.Entries){
                token.ThrowIfCancellationRequested();
                string name=entry.FullName;
                if(string.IsNullOrEmpty(name)||name.IndexOf('\\')>=0||name.IndexOf(':')>=0||name.StartsWith("/",StringComparison.Ordinal)||name.StartsWith("//",StringComparison.Ordinal))throw new InvalidOperationException("พบชื่อไฟล์ archive ที่ไม่ปลอดภัย");
                bool directory=name.EndsWith("/",StringComparison.Ordinal);
                string normalized=directory?name.Substring(0,name.Length-1):name;
                ValidateZipMemberName(normalized);
                if(!seen.Add(normalized))throw new InvalidOperationException("พบชื่อไฟล์ archive ซ้ำหรือชนกันโดยไม่สนตัวพิมพ์");
                int unixType=(entry.ExternalAttributes>>16)&0xF000;
                if(unixType==0xA000||(entry.ExternalAttributes&((int)FileAttributes.ReparsePoint))!=0)throw new InvalidOperationException("archive มีลิงก์หรือ reparse member");
                if(directory){
                    if(!group.Files.Any(f=>f.Path.StartsWith(normalized+"/",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("archive มีโฟลเดอร์ที่ไม่ได้ระบุใน catalog");
                    continue;
                }
                ContentFile file;
                if(!expected.TryGetValue(normalized,out file))throw new InvalidOperationException("archive มีไฟล์นอก allowlist ของ catalog");
                if(entry.Length!=file.Bytes)throw new InvalidOperationException("ขนาดไฟล์ใน archive ไม่ตรงกับ catalog");
                string temp=Path.Combine(extract,Guid.NewGuid().ToString("N")+".stage");
                using(Stream input=entry.Open())using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    CopyBounded(input,output,file.Bytes,token,progress,"กำลังตรวจไฟล์เนื้อหา");output.Flush(true);
                }
                if(new FileInfo(temp).Length!=file.Bytes||!HashFile(temp).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)){File.Delete(temp);throw new InvalidOperationException("ไฟล์ archive ไม่ตรงกับ hash/size ที่ตรึงไว้");}
                extracted.Add(file.Path,temp);
            }
        }
        if(extracted.Count!=expected.Count||expected.Keys.Any(x=>!extracted.ContainsKey(x)))throw new InvalidOperationException("archive มีไฟล์ไม่ครบตาม catalog");
        return extracted;
    }

    void CommitStaged(Dictionary<string,string> staged,ContentFile[] expectedFiles,string stage,CancellationToken token){
        string backups=Path.Combine(stage,"backups");Directory.CreateDirectory(backups);
        var committed=new List<Tuple<string,string,bool>>();var madeDirs=new List<string>();
        var expected=expectedFiles.ToDictionary(x=>x.Path,StringComparer.Ordinal);
        try{
            foreach(var pair in staged){
                token.ThrowIfCancellationRequested();
                string dest=AppPath(pair.Key);EnsureParentDirectories(dest,madeDirs);RejectReparse(appRoot,dest);
                if(Directory.Exists(dest))throw new InvalidOperationException("ตำแหน่งไฟล์เนื้อหาถูกแทนที่ด้วยโฟลเดอร์");
                ContentFile file=expected[pair.Key];
                if(File.Exists(dest)&&new FileInfo(dest).Length==file.Bytes&&HashFile(dest).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))continue;
                string backup=null;bool existed=File.Exists(dest);
                if(existed){backup=Path.Combine(backups,Guid.NewGuid().ToString("N")+".bak");MoveFile(dest,backup);}
                committed.Add(Tuple.Create(dest,backup,existed));
                MoveFile(pair.Value,dest);
            }
            token.ThrowIfCancellationRequested();
        }catch(Exception failure){
            bool rollbackIncomplete=false;
            for(int i=committed.Count-1;i>=0;i--){
                var item=committed[i];
                try{if(File.Exists(item.Item1))File.Delete(item.Item1);}catch{rollbackIncomplete=true;}
                if(item.Item3&&item.Item2!=null&&File.Exists(item.Item2)){
                    try{if(File.Exists(item.Item1))throw new IOException("ปลายทางยังมีไฟล์หลังพยายามย้อนคืน");MoveFile(item.Item2,item.Item1);}
                    catch{rollbackIncomplete=true;}
                }
            }
            for(int i=madeDirs.Count-1;i>=0;i--)try{if(Directory.Exists(madeDirs[i])&&Directory.GetFileSystemEntries(madeDirs[i]).Length==0)Directory.Delete(madeDirs[i],false);}catch{rollbackIncomplete=true;}
            if(rollbackIncomplete)throw new RollbackIncompleteException(stage,failure);
            throw;
        }
    }

    void MoveFile(string source,string destination){if(FileMoveOverride!=null)FileMoveOverride(source,destination);else File.Move(source,destination);}

    public ReleaseInfo Check(CancellationToken token){
        token.ThrowIfCancellationRequested();
        int[] current=ParseVersion(config.CurrentVersion);
        Dictionary<string,object> release=ReadJson(GetApi(new Uri(ApiRoot()+"/releases/latest"),token),MaxApiBytes,token);
        if(!HasBoolean(release,"draft")||!HasBoolean(release,"prerelease"))throw new InvalidOperationException("GitHub Release ไม่มีสถานะ draft/prerelease ที่ตรวจสอบได้");
        if(Bool(release,"draft")||Bool(release,"prerelease"))return null;
        string tag=GetString(release,"tag_name");Match match=VersionPattern.Match(tag??"");
        if(!match.Success)throw new InvalidOperationException("GitHub Release มี version tag ที่ไม่รองรับ");
        string version=match.Groups["version"].Value;int[] found=ParseVersion(version);
        if(CompareVersion(found,current)<=0)return null;
        string expectedName="Thai-Translator-Setup-"+version+".exe";
        Dictionary<string,object> asset=FindAsset(release,expectedName);
        if(asset==null)throw new InvalidOperationException("ไม่พบ installer ที่ต้องใช้ใน GitHub Release");
        long size=Long(asset,"size");string digest=Digest(GetString(asset,"digest"));
        if(size<1||size>MaxInstallerBytes||digest==null||!String.Equals(GetString(asset,"state"),"uploaded",StringComparison.OrdinalIgnoreCase)&&asset.ContainsKey("state"))throw new InvalidOperationException("ข้อมูล installer ใน GitHub Release ไม่ครบหรือไม่ปลอดภัย");
        Uri expected=ExpectedReleaseAssetUrl(tag,expectedName);ValidateAdvertisedAssetUrl(GetString(asset,"browser_download_url"),expected);
        return new ReleaseInfo{Version=version,Tag=tag,Notes=SafeNotes(GetString(release,"body")),InstallerName=expectedName,InstallerUrl=expected.AbsoluteUri,InstallerSha256=digest,InstallerBytes=size};
    }

    public string Download(ReleaseInfo release,Action<TransferProgress> progress,CancellationToken token){
        ValidateReleaseInfo(release);
        token.ThrowIfCancellationRequested();
        string downloads=Path.Combine(appRoot,"Settings","Downloads");EnsureDirectorySafe(downloads);
        string final=Path.Combine(downloads,release.InstallerName);RejectReparse(appRoot,final);
        if(File.Exists(final)&&new FileInfo(final).Length==release.InstallerBytes&&HashFile(final).Equals(release.InstallerSha256,StringComparison.OrdinalIgnoreCase))return final;
        string stage=CreateStage();
        try{
            string temporary=Path.Combine(stage,"installer.exe.partial");
            DownloadToFile(new Uri(release.InstallerUrl),temporary,release.InstallerBytes,MaxInstallerBytes,"กำลังดาวน์โหลดตัวติดตั้งที่ตรวจสอบแล้ว",progress,token);
            if(!HashFile(temporary).Equals(release.InstallerSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("hash ของ installer ไม่ตรงกับ GitHub Release");
            string backup=null;bool existed=File.Exists(final);
            if(Directory.Exists(final))throw new InvalidOperationException("ตำแหน่ง installer ถูกแทนที่ด้วยโฟลเดอร์");
            if(existed){backup=Path.Combine(stage,"installer.previous");File.Move(final,backup);}
            try{File.Move(temporary,final);}catch{if(existed&&File.Exists(backup))File.Move(backup,final);throw;}
            return final;
        }finally{TryDeleteDirectory(stage);}
    }

    void ValidateReleaseInfo(ReleaseInfo release){
        if(release==null)throw new ArgumentNullException("release");
        int[] version=ParseVersion(release.Version??"");string tag="v"+release.Version;
        string expectedName="Thai-Translator-Setup-"+release.Version+".exe";
        if(release.Tag!=tag||release.InstallerName!=expectedName||release.InstallerBytes<1||release.InstallerBytes>MaxInstallerBytes||!HexPattern.IsMatch(release.InstallerSha256??""))throw new InvalidOperationException("ข้อมูล installer ที่ร้องขอไม่ถูกต้อง");
        Uri expected=ExpectedReleaseAssetUrl(tag,expectedName);ValidateAdvertisedAssetUrl(release.InstallerUrl,expected);
        if(CompareVersion(version,ParseVersion(config.CurrentVersion))<=0)throw new InvalidOperationException("installer ไม่ใช่รุ่นใหม่กว่าที่ติดตั้งอยู่");
    }

    void ValidateContentAsset(Dictionary<string,object> asset,ContentGroup group){
        if(Long(asset,"size")!=group.Bytes)throw new InvalidOperationException("ขนาด archive ใน GitHub Release ไม่ตรงกับ catalog");
        string rawDigest=GetString(asset,"digest");string digest=Digest(rawDigest);
        if(rawDigest!=null&&digest==null)throw new InvalidOperationException("รูปแบบ hash archive ใน GitHub Release ไม่ถูกต้อง");
        if(digest!=null&&!digest.Equals(group.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("hash archive ใน GitHub Release ไม่ตรงกับ catalog");
        Uri expected=ExpectedReleaseAssetUrl(catalog.ReleaseTag,group.AssetName);ValidateAdvertisedAssetUrl(GetString(asset,"browser_download_url"),expected);
        if(asset.ContainsKey("state")&&!String.Equals(GetString(asset,"state"),"uploaded",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("archive ใน GitHub Release ยังอัปโหลดไม่เสร็จ");
    }

    void ValidateCatalogRelease(Dictionary<string,object> release,string expectedTag){
        if(GetString(release,"tag_name")!=expectedTag||!HasBoolean(release,"draft")||!HasBoolean(release,"prerelease")||Bool(release,"draft")||Bool(release,"prerelease"))throw new InvalidOperationException("GitHub Release ไม่ตรงกับ tag ที่ catalog ตรึงไว้");
    }

    Dictionary<string,object> FindAsset(Dictionary<string,object> release,string name){
        object raw;if(!release.TryGetValue("assets",out raw))return null;
        var assets=raw as System.Collections.IEnumerable;if(assets==null||raw is string)return null;
        var matches=assets.Cast<object>().OfType<Dictionary<string,object>>().Where(x=>GetString(x,"name")==name).ToArray();
        if(matches.Length>1)throw new InvalidOperationException("GitHub Release มี asset ชื่อซ้ำ");
        return matches.SingleOrDefault();
    }

    Uri ExpectedReleaseAssetUrl(string tag,string assetName){
        if(!SafeAssetName(assetName)||!ReleaseTagPattern.IsMatch(tag??"")||tag=="."||tag=="..")throw new InvalidOperationException("ชื่อ asset ไม่ถูกต้อง");
        string path="/"+config.Repository+"/releases/download/"+Uri.EscapeDataString(tag)+"/"+Uri.EscapeDataString(assetName);
        return new Uri("https://github.com"+path,UriKind.Absolute);
    }

    void ValidateAdvertisedAssetUrl(string value,Uri expected){
        Uri supplied;
        if(!Uri.TryCreate(value,UriKind.Absolute,out supplied)||supplied.Scheme!=Uri.UriSchemeHttps||supplied.UserInfo.Length!=0||supplied.Port!=443||!String.Equals(supplied.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!String.Equals(supplied.AbsolutePath,expected.AbsolutePath,StringComparison.Ordinal)||!String.IsNullOrEmpty(supplied.Query)||!String.IsNullOrEmpty(supplied.Fragment))throw new InvalidOperationException("URL ของ asset ไม่ตรงกับ GitHub repository ที่กำหนด");
    }

    DeliveryHttpResponse GetApi(Uri uri,CancellationToken token){
        if(uri.Scheme!=Uri.UriSchemeHttps||uri.Host!="api.github.com"||uri.Port!=443||uri.UserInfo.Length!=0||uri.Query.Length!=0)throw new InvalidOperationException("ปลายทาง GitHub API ไม่ปลอดภัย");
        using(DeliveryHttpResponse response=Open(uri,false,token)){
            if(response.StatusCode!=200)throw new InvalidOperationException("GitHub API ไม่ตอบกลับข้อมูลที่ตรวจสอบได้");
            byte[] body=ReadBounded(response.Body,MaxApiBytes,token);
            return new DeliveryHttpResponse(200,response.ResponseUri,null,response.ContentType,body.LongLength,new MemoryStream(body,false),null);
        }
    }

    DeliveryHttpResponse Open(Uri start,bool allowAssetRedirects,CancellationToken token){
        Uri current=start;
        for(int i=0;i<=MaxRedirects;i++){
            token.ThrowIfCancellationRequested();
            ValidateNetworkUri(current,start,allowAssetRedirects,i>0);
            DeliveryHttpResponse response=transport.Get(current,token);
            if(response==null)throw new InvalidOperationException("เครือข่าย GitHub ไม่ส่งการตอบกลับ");
            if(response.ResponseUri!=null&&!SameUri(response.ResponseUri,current)){
                response.Dispose();throw new InvalidOperationException("ปลายทางตอบกลับเปลี่ยนไปโดยไม่ผ่านการตรวจสอบ redirect");
            }
            if(response.StatusCode>=300&&response.StatusCode<400){
                string location=response.Location;response.Dispose();
                Uri next;
                if(string.IsNullOrWhiteSpace(location)||!Uri.TryCreate(current,location,out next)||i==MaxRedirects)throw new InvalidOperationException("GitHub redirect ไม่ถูกต้อง");
                ValidateNetworkUri(next,start,allowAssetRedirects,true);current=next;continue;
            }
            if(response.StatusCode!=200){response.Dispose();throw new InvalidOperationException("GitHub ไม่ตอบกลับไฟล์ที่ร้องขอ");}
            ValidateNetworkUri(response.ResponseUri??current,start,allowAssetRedirects,i>0);
            return response;
        }
        throw new InvalidOperationException("GitHub redirect มีจำนวนเกินกำหนด");
    }

    static void ValidateNetworkUri(Uri uri,Uri start,bool allowAssetRedirects,bool redirected){
        if(uri==null||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length!=0||uri.Port!=443||!String.IsNullOrEmpty(uri.Fragment))throw new InvalidOperationException("GitHub ส่ง URL ที่ไม่ปลอดภัย");
        if(!redirected){if(!SameUri(uri,start))throw new InvalidOperationException("URL เริ่มต้นไม่ตรงกับที่ร้องขอ");return;}
        if(!allowAssetRedirects)throw new InvalidOperationException("GitHub API ไม่อนุญาต redirect");
        bool github=String.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase)&&String.Equals(uri.AbsolutePath,start.AbsolutePath,StringComparison.Ordinal);
        bool cdn=AllowedAssetRedirectHosts.Contains(uri.Host,StringComparer.OrdinalIgnoreCase);
        if(!github&&!cdn)throw new InvalidOperationException("GitHub redirect ออกนอก host ที่อนุญาต");
    }

    void DownloadToFile(Uri url,string path,long expectedBytes,long maximum,string label,Action<TransferProgress> progress,CancellationToken token){
        using(DeliveryHttpResponse response=Open(url,true,token)){
            if(response.ContentLength>=0&&response.ContentLength!=expectedBytes)throw new InvalidOperationException("ขนาดการตอบกลับไม่ตรงกับที่ตรึงไว้");
            if(expectedBytes>maximum||expectedBytes<1)throw new InvalidOperationException("ไฟล์ที่ร้องขอมีขนาดเกินกำหนด");
            using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                long copied=CopyBounded(response.Body,output,expectedBytes,token,progress,label);output.Flush(true);
                if(copied!=expectedBytes)throw new InvalidOperationException("ดาวน์โหลดไฟล์ไม่ครบตามขนาดที่ตรึงไว้");
            }
        }
    }

    static long CopyBounded(Stream input,Stream output,long maximum,CancellationToken token,Action<TransferProgress> progress,string label){
        byte[] buffer=new byte[128*1024];long total=0;int read;
        while((read=input.Read(buffer,0,buffer.Length))>0){
            token.ThrowIfCancellationRequested();
            if(total>maximum-read)throw new InvalidOperationException("ข้อมูลตอบกลับมีขนาดเกินกำหนด");
            output.Write(buffer,0,read);total+=read;
            if(progress!=null)progress(new TransferProgress{Text=label,BytesReceived=total,TotalBytes=maximum});
        }
        token.ThrowIfCancellationRequested();return total;
    }

    static byte[] ReadBounded(Stream stream,int maximum,CancellationToken token){
        using(var output=new MemoryStream()){
            byte[] buffer=new byte[16*1024];int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(output.Length+read>maximum)throw new InvalidOperationException("GitHub API ส่งข้อมูลเกินขนาดกำหนด");output.Write(buffer,0,read);}
            token.ThrowIfCancellationRequested();return output.ToArray();
        }
    }

    Dictionary<string,object> ReadJson(DeliveryHttpResponse response,int maximum,CancellationToken token){
        using(response){byte[] data=ReadBounded(response.Body,maximum,token);try{return json.Deserialize<Dictionary<string,object>>(new UTF8Encoding(false,true).GetString(data));}catch{throw new InvalidOperationException("รูปแบบข้อมูล GitHub ไม่ถูกต้อง");}}
    }

    string ApiRoot(){return "https://api.github.com/repos/"+config.Repository;}
    string CreateStage(){string baseDir=Path.Combine(appRoot,".delivery-staging");EnsureDirectorySafe(baseDir);string stage=Path.Combine(baseDir,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);RejectReparse(appRoot,stage);return stage;}
    string AppPath(string relative){ValidateOwnedRelativePath(relative);string path=Path.GetFullPath(Path.Combine(appRoot,relative.Replace('/',Path.DirectorySeparatorChar)));string rootPrefix=appRoot+Path.DirectorySeparatorChar;if(!path.StartsWith(rootPrefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ตำแหน่ง catalog ออกนอกโฟลเดอร์แอป");RejectReparse(appRoot,path);return path;}

    static void ValidateOwnedRelativePath(string relative){
        if(string.IsNullOrWhiteSpace(relative)||relative.IndexOf('\\')>=0||relative.StartsWith("/",StringComparison.Ordinal)||relative.Contains(":")||relative.IndexOf('\0')>=0)throw new InvalidOperationException("catalog มี path ที่ไม่ปลอดภัย");
        string[] parts=relative.Split('/');if(parts.Any(x=>x.Length==0||x=="."||x==".."))throw new InvalidOperationException("catalog มี path traversal");
        bool ok=false;
        if(relative.StartsWith("แพ็ก/",StringComparison.Ordinal)&&new[]{".pak",".utoc",".ucas"}.Contains(Path.GetExtension(relative),StringComparer.OrdinalIgnoreCase))ok=true;
        if(relative=="เครื่องมือ/ตัวโหลดเสนอ/zzz_NTE_Thai_Shops_999_P.sig"||relative=="เครื่องมือ/ตัวโหลดเสนอ/version.dll"||relative=="เครื่องมือ/ตัวโหลดเสนอ/UniversalSigBypasser.asi")ok=true;
        if(relative=="Tools/FontWorker.exe"||relative=="Tools/repak.exe")ok=true;
        if(relative=="Fonts/Original/MiSans-Demibold.ufont"||relative=="Fonts/Original/MiSans-Heavy.ufont")ok=true;
        if(relative=="native_review_main_v28c.jsonl")ok=true;
        if(!ok)throw new InvalidOperationException("catalog path ไม่อยู่ใน allowlist ของเนื้อหา");
    }

    static void ValidateZipMemberName(string relative){
        if(string.IsNullOrWhiteSpace(relative)||relative.StartsWith("/",StringComparison.Ordinal)||relative.Contains(":")||relative.IndexOf('\\')>=0||relative.IndexOf('\0')>=0||relative.Split('/').Any(x=>x.Length==0||x=="."||x==".."))throw new InvalidOperationException("archive มี path traversal หรือชื่อสมาชิกไม่ปลอดภัย");
    }
    static bool SafeAssetName(string name){return !string.IsNullOrWhiteSpace(name)&&name.Length<=180&&name==Path.GetFileName(name)&&name.IndexOfAny(new[]{'/','\\',':','?','#','\0'})<0&&name!="."&&name!="..";}

    void EnsureParentDirectories(string path,List<string> made){
        string parent=Path.GetDirectoryName(path);var missing=new Stack<string>();
        for(string p=parent;p!=null&&!p.Equals(appRoot,StringComparison.OrdinalIgnoreCase);p=Path.GetDirectoryName(p)){if(Directory.Exists(p))break;if(File.Exists(p))throw new InvalidOperationException("ตำแหน่ง catalog ถูกไฟล์อื่นแทนที่");missing.Push(p);}
        while(missing.Count>0){string dir=missing.Pop();Directory.CreateDirectory(dir);RejectReparse(appRoot,dir);made.Add(dir);}
    }
    void EnsureDirectorySafe(string path){RejectReparse(appRoot,path);Directory.CreateDirectory(path);RejectReparse(appRoot,path);}
    static void RejectReparse(string root,string path){
        string full=Path.GetFullPath(path);string basePath=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!full.Equals(basePath,StringComparison.OrdinalIgnoreCase)&&!full.StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ตำแหน่งไฟล์ออกนอกโฟลเดอร์แอป");
        for(string p=full;p!=null;p=Path.GetDirectoryName(p)){
            if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("พบลิงก์ไฟล์หรือโฟลเดอร์ในเส้นทาง");
            if(p.Equals(Path.GetPathRoot(p),StringComparison.OrdinalIgnoreCase))break;
        }
    }
    static void TryDeleteDirectory(string path){try{if(Directory.Exists(path))Directory.Delete(path,true);}catch{}}
    static string HashFile(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    static string Digest(string raw){if(string.IsNullOrWhiteSpace(raw))return null;const string prefix="sha256:";if(!raw.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return null;string digest=raw.Substring(prefix.Length);return HexPattern.IsMatch(digest)?digest.ToLowerInvariant():null;}
    static bool SameUri(Uri a,Uri b){return a!=null&&b!=null&&String.Equals(a.AbsoluteUri,b.AbsoluteUri,StringComparison.Ordinal);}
    static string GetString(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):null;}
    static long Long(Dictionary<string,object> data,string key){object value;long result;if(data==null||!data.TryGetValue(key,out value)||value==null||!Int64.TryParse(Convert.ToString(value),out result))return -1;return result;}
    static bool Bool(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)&&value!=null&&Convert.ToBoolean(value);}
    static int[] ParseVersion(string value){
        if(string.IsNullOrWhiteSpace(value)||!Regex.IsMatch(value,@"^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*))?$",RegexOptions.CultureInvariant))throw new InvalidOperationException("รูปแบบเวอร์ชันไม่ถูกต้อง");
        try{string[] parts=value.Split('.');return new[]{Int32.Parse(parts[0]),Int32.Parse(parts[1]),Int32.Parse(parts[2]),parts.Length==4?Int32.Parse(parts[3]):0};}
        catch{throw new InvalidOperationException("ตัวเลข version ของ GitHub Release เกินช่วงที่รองรับ");}
    }
    static int CompareVersion(int[] a,int[] b){for(int i=0;i<4;i++){int n=a[i].CompareTo(b[i]);if(n!=0)return n;}return 0;}
    static string SafeNotes(string notes){if(notes==null)return "";var clean=new string(notes.Where(c=>c=='\r'||c=='\n'||c=='\t'||!Char.IsControl(c)).Take(20000).ToArray());return clean;}
    static bool HasBoolean(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)&&value is bool;}
}
