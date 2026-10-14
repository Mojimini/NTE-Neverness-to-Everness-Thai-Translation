using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

public class PackageFile { public string Source,Target,Sha256; }
public class GameFile { public string Path,Sha256; }
public class PackageManifest { public int Version; public bool Ready; public string Reason; public List<PackageFile> Files; public List<GameFile> GameFiles; }
public class ToggleState { public bool Active,Ready; public string Message; }
public class InstalledFile { public string Target,Sha256; public bool Written; }
public class InstallReceipt { public int Version; public string Root,Phase; public bool CreatedModsDirectory; public List<InstalledFile> Files; }
public class FileStore {
    public static string Hash(string file) {
        // Native Windows SHA256 keeps verification of multi-GB game archives practical.
        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan))
        using(var hash=new SHA256CryptoServiceProvider())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    public virtual void CopyNew(string source,string target){
        // CreateNew never overwrites a collision. The handle excludes concurrent writers.
        bool created=false;
        try {
            using(var input=File.OpenRead(source))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                created=true; input.CopyTo(output); output.Flush(true);
            }
        } catch { if(created)File.Delete(target); throw; }
    }
}
public class ToggleCore {
    public const string PaksRelative="Client/WindowsNoEditor/HT/Content/Paks";
    public const string ExeRelative="Client/WindowsNoEditor/HT/Binaries/Win64/HTGame.exe";
    public const string NativeRelative="Client/WindowsNoEditor/HT/Binaries/Win64";
    public const string ModsRelative=PaksRelative+"/~mods";
    readonly string app;
    readonly Func<bool> isRunning;
    readonly FileStore store;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    string ReceiptPath {get{return Path.Combine(app,"receipt.json");}}
    public ToggleCore(string app,Func<bool> isRunning,FileStore store=null) {
        // AppDomain.BaseDirectory ends in a separator; use the same canonical root as game paths.
        this.app=RootPath(Path.GetFullPath(app));this.isRunning=isRunning;this.store=store??new FileStore();json.MaxJsonLength=1024*1024;
    }
    static void Stop(string message){throw new InvalidOperationException(message);}
    static string RootPath(string value){
        if(string.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value))Stop("ต้องเลือกตำแหน่งเกมแบบเต็ม");
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    }
    static string Relative(string root,string relative){
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(":"))Stop("รายการตำแหน่งไฟล์ไม่ถูกต้อง");
        var parts=relative.Replace('\\','/').Split('/');
        if(parts.Any(x=>x==".."||x=="."||x.Length==0))Stop("รายการไฟล์ออกนอกขอบเขต");
        string full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Stop("รายการไฟล์ออกนอกขอบเขต");
        NoReparse(root,full);return full;
    }
    static void NoReparse(string root,string path){
        string current=path;
        while(current!=null&& (current.Equals(root,StringComparison.OrdinalIgnoreCase)||current.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))){
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)Stop("พบโฟลเดอร์หรือไฟล์เชื่อมไปตำแหน่งอื่น: "+current);
            if(current.Equals(root,StringComparison.OrdinalIgnoreCase))break;current=Path.GetDirectoryName(current);
        }
    }
    static bool GoodHash(string value){return value!=null&&value.Length==64&&value.All(c=>"0123456789abcdefABCDEF".Contains(c));}
    static string[] Targets(int version){
        if(version!=1&&version!=2)Stop("รุ่นรายการไฟล์ไม่ถูกต้อง");
        var result=new List<string>();
        foreach(string extension in new[]{"pak","utoc","ucas"})result.Add(ModsRelative+"/zzz_NTE_Thai_Shops_999_P."+extension);
        if(version==2){result.Add(ModsRelative+"/zzz_NTE_Thai_Shops_999_P.sig");result.Add(NativeRelative+"/version.dll");result.Add(NativeRelative+"/UniversalSigBypasser.asi");}
        return result.ToArray();
    }
    static string CheckTarget(string target,int version){
        string normalized=(target??"").Replace('\\','/');
        if(!Targets(version).Contains(normalized))Stop("รายการไฟล์ไม่ใช่ชื่อชุดทดลองร้านค้าที่อนุญาต");
        return normalized;
    }
    static void ValidateRoot(string root,bool exe){
        NoReparse(root,root);
        if(!Directory.Exists(Relative(root,PaksRelative))||(exe&&!File.Exists(Relative(root,ExeRelative))))Stop("ตำแหน่งนี้ไม่มี HTGame.exe และโฟลเดอร์แพ็กเกมที่ต้องใช้");
    }
    FileStream Lock(){return new FileStream(Path.Combine(app,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    void CheckRunning(){if(isRunning())Stop("เกมยังทำงานอยู่ กรุณาออกเกมให้เรียบร้อยก่อน");}
    PackageManifest Manifest(){
        string path=Path.Combine(app,"แพ็ก/manifest.json");
        if(!File.Exists(path))Stop("ยังไม่พบชุดแพ็กที่เตรียมไว้");
        var m=json.Deserialize<PackageManifest>(File.ReadAllText(path,Encoding.UTF8));
        if(m==null||m.Files==null||m.GameFiles==null||m.GameFiles.Count==0)Stop("ข้อมูลชุดแพ็กไม่ครบ");
        if(m.Files.Count!=Targets(m.Version).Length)Stop("ข้อมูลชุดแพ็กไม่ครบ");
        var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in m.Files){if(f==null)Stop("ข้อมูลไฟล์แพ็กไม่ครบ");f.Target=CheckTarget(f.Target,m.Version);if(!GoodHash(f.Sha256)||!targets.Add(f.Target))Stop("รายการแพ็กซ้ำหรือ hash ไม่ถูกต้อง");Relative(app,f.Source);}
        foreach(var f in m.GameFiles){if(f==null||!GoodHash(f.Sha256))Stop("hash ของเกมไม่ถูกต้อง");Relative(app,f.Path);}
        return m;
    }
    InstallReceipt Receipt(){
        if(!File.Exists(ReceiptPath))return null;
        var r=json.Deserialize<InstallReceipt>(File.ReadAllText(ReceiptPath,Encoding.UTF8));
        if(r==null||r.Files==null||r.Files.Count<1||!new[]{"INSTALLING","ACTIVE","REMOVING"}.Contains(r.Phase))Stop("บันทึกการติดตั้งไม่ถูกต้อง จึงยังคืนค่าไม่ได้");
        int expected=Targets(r.Version).Length;
        if(r.Files.Count>expected||(r.Phase=="ACTIVE"&&r.Files.Count!=expected))Stop("บันทึกการติดตั้งไม่ครบ จึงยังคืนค่าไม่ได้");
        r.Root=RootPath(r.Root);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in r.Files){if(f==null)Stop("รายการในบันทึกการติดตั้งไม่ถูกต้อง");f.Target=CheckTarget(f.Target,r.Version);if(!GoodHash(f.Sha256)||!targets.Add(f.Target))Stop("รายการในบันทึกการติดตั้งไม่ถูกต้อง");Relative(r.Root,f.Target);}
        return r;
    }
    void SaveReceipt(InstallReceipt r){
        string temporary=Path.Combine(app,"receipt.new");byte[] data=Encoding.UTF8.GetBytes(json.Serialize(r));
        using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(data,0,data.Length);file.Flush(true);}
        if(File.Exists(ReceiptPath))File.Replace(temporary,ReceiptPath,null);else File.Move(temporary,ReceiptPath);
    }
    static void VerifyOwned(InstallReceipt r,bool requireAll){
        foreach(var f in r.Files){string target=Relative(r.Root,f.Target);
            if(Directory.Exists(target))Stop("ตำแหน่งไฟล์ชุดทดลองถูกแทนที่ด้วยโฟลเดอร์ จึงยังไม่นำออก: "+Path.GetFileName(target));
            if(!File.Exists(target)){if(requireAll)Stop("ชุดทดลองติดตั้งไม่ครบ กรุณากดคืนค่าเดิมก่อน");continue;}
            if(!f.Written)Stop("พบไฟล์ค้างที่บันทึกยังไม่ยืนยันความเป็นเจ้าของ จึงยังไม่นำออก: "+Path.GetFileName(target));
            if(!FileStore.Hash(target).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))Stop("ไฟล์ชุดทดลองถูกเปลี่ยน จึงยังไม่นำออก: "+Path.GetFileName(target));
        }
    }
    void RemoveFiles(InstallReceipt r,IEnumerable<InstalledFile> files){
        foreach(var f in files){string path=Relative(r.Root,f.Target);if(Directory.Exists(path))Stop("ตำแหน่งไฟล์ชุดทดลองถูกแทนที่ด้วยโฟลเดอร์ จึงยังไม่นำออก: "+Path.GetFileName(path));if(!File.Exists(path))continue;
            if(!f.Written)Stop("ยังไม่ยืนยันว่าโปรแกรมเป็นผู้เพิ่มไฟล์นี้: "+Path.GetFileName(path));
            if(!FileStore.Hash(path).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))Stop("ไฟล์ชุดทดลองถูกเปลี่ยน จึงยังไม่นำออก: "+Path.GetFileName(path));
            CheckRunning();File.Delete(path);
        }
        string mods=Relative(r.Root,ModsRelative);
        if(r.CreatedModsDirectory&&Directory.Exists(mods)&&Directory.GetFileSystemEntries(mods).Length==0)Directory.Delete(mods,false);
    }
    public string InstalledRoot {get {var r=Receipt();return r==null?null:r.Root;}}
    public void Enable(string root){
        using(Lock()){
            CheckRunning();root=RootPath(root);var old=Receipt();
            if(old!=null){
                if(!old.Root.Equals(root,StringComparison.OrdinalIgnoreCase))Stop("ต้องคืนค่าตำแหน่งเกมเดิมก่อนเปลี่ยนโฟลเดอร์");
                ValidateRoot(root,true);VerifyOwned(old,true);
                if(old.Phase!="ACTIVE")Stop("พบการติดตั้งที่ยังไม่จบ กรุณากดคืนค่าเดิมก่อน");
                if(File.Exists(Path.Combine(app,"แพ็ก/manifest.json"))&&Manifest().Version!=old.Version)Stop("ยังเป็นชุดทดลองรุ่นเดิม กรุณากดคืนค่าเดิมก่อนเปิดชุดใหม่");
                return;
            }
            ValidateRoot(root,true);var m=Manifest();
            if(!m.Ready)Stop("ยังไม่พร้อมติดตั้ง: "+m.Reason);
            foreach(var f in m.Files){
                string source=Relative(app,f.Source),target=Relative(root,f.Target);
                if(!File.Exists(source)||!FileStore.Hash(source).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))Stop("ไฟล์แพ็กไม่ตรงกับชุดที่ตรวจไว้: "+Path.GetFileName(source));
                if(File.Exists(target)||Directory.Exists(target))Stop("มีไฟล์ชื่อชนที่ไม่มีบันทึกของโปรแกรม: "+Path.GetFileName(target));
            }
            foreach(var f in m.GameFiles){
                string path=Relative(root,f.Path);
                if(!File.Exists(path)||!FileStore.Hash(path).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))Stop("ไฟล์เกมเปลี่ยนจากรุ่นที่เตรียมแพ็กไว้ กรุณาเตรียมแพ็กใหม่ก่อน");
            }
            CheckRunning();string mods=Relative(root,ModsRelative);
            var receipt=new InstallReceipt{Version=m.Version,Root=root,Phase="INSTALLING",CreatedModsDirectory=!Directory.Exists(mods),Files=m.Files.Select(f=>new InstalledFile{Target=f.Target,Sha256=f.Sha256}).ToList()};
            SaveReceipt(receipt);var written=new List<InstalledFile>();
            try {
                Directory.CreateDirectory(mods);
                foreach(var f in m.Files){CheckRunning();string target=Relative(root,f.Target);store.CopyNew(Relative(app,f.Source),target);written.Add(new InstalledFile{Target=f.Target,Sha256=f.Sha256,Written=true});
                    if(!FileStore.Hash(target).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))Stop("ไฟล์ที่เพิ่มไม่ครบหรือเสียหาย");
                    receipt.Files.Single(x=>x.Target==f.Target).Written=true;SaveReceipt(receipt);
                }
                receipt.Phase="ACTIVE";SaveReceipt(receipt);
            } catch {
                // Roll back only successful writes from this operation, never a collided file.
                RemoveFiles(receipt,written);File.Delete(ReceiptPath);throw;
            }
        }
    }
    public void Disable(){
        using(Lock()){
            CheckRunning();var r=Receipt();if(r==null)return;
            ValidateRoot(r.Root,false);VerifyOwned(r,false);r.Phase="REMOVING";SaveReceipt(r);
            RemoveFiles(r,r.Files.Where(f=>f.Written));
            foreach(var f in r.Files)if(File.Exists(Relative(r.Root,f.Target))||Directory.Exists(Relative(r.Root,f.Target)))Stop("ยังมีไฟล์ชุดทดลองเหลืออยู่ จึงยังคืนค่าไม่ครบ");
            File.Delete(ReceiptPath);
        }
    }
    public ToggleState Inspect(string root){
        try {
            var r=Receipt();
            if(r!=null){VerifyOwned(r,true);return new ToggleState{Active=r.Phase=="ACTIVE",Ready=r.Phase=="ACTIVE",Message=r.Phase=="ACTIVE"?(r.Version==2?"เพิ่มไฟล์ทดลองครบ 6 ไฟล์ — รอตรวจว่าเกมโหลดคำแปลสำเร็จ\r\nออกเกมแล้วกดคืนค่าเดิม":"ยังมีชุดทดลองรุ่นเดิม 3 ไฟล์ — กดคืนค่าเดิมก่อนเปิดชุดใหม่"):"พบการทำงานที่ยังไม่จบ กรุณากดคืนค่าเดิม"};}
            var m=Manifest();root=RootPath(root);ValidateRoot(root,true);
            if(m.Files.Any(f=>File.Exists(Relative(root,f.Target))||Directory.Exists(Relative(root,f.Target))))return new ToggleState{Message="พบไฟล์ชื่อชนที่ไม่มีบันทึก กรุณาตรวจไฟล์ก่อน"};
            return new ToggleState{Ready=m.Ready,Message=m.Ready?(m.Version==2?"พร้อมเพิ่มชุดทดลอง 6 ไฟล์ — รอตรวจการโหลดในเกมจริง":"ปิดคำแปลอยู่ — พร้อมเปิดเพื่อทดลอง"):"ยังไม่พร้อมติดตั้ง: "+m.Reason};
        }catch(Exception ex){return new ToggleState{Message=ex.Message};}
    }
}
