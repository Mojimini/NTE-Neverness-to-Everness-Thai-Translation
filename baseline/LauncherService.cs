using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

public sealed partial class LauncherService : ILauncherService {
    readonly string app; readonly Func<bool> running; readonly ToggleCore core;
    JavaScriptSerializer json{get{return new JavaScriptSerializer{MaxJsonLength=4*1024*1024};}}
    public LauncherService(string app,Func<bool> running){
        this.app=Path.GetFullPath(app).TrimEnd('\\','/');this.running=running;core=new ToggleCore(this.app,running);
        Safe("Fonts/Default/default_profile.json");
    }
    string Safe(string relative){
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(":")||relative.Replace('\\','/').Split('/').Any(s=>s==".."||s=="."||s.Length==0))throw new InvalidOperationException("ตำแหน่งไฟล์ไม่ถูกต้อง");
        string full=Path.GetFullPath(Path.Combine(app,relative));
        if(!full.StartsWith(app+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ตำแหน่งไฟล์ออกนอกโปรแกรม");
        for(string p=full;p!=null;p=Path.GetDirectoryName(p)){
            if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("พบตำแหน่งไฟล์เชื่อมไปโฟลเดอร์อื่น");
        }
        return full;
    }
    string Relative(string full){string p=Path.GetFullPath(full);if(!p.StartsWith(app+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ไฟล์ฟอนต์ออกนอกโปรแกรม");string r=p.Substring(app.Length+1).Replace('\\','/');Safe(r);return r;}
    Dictionary<string,object> Read(string relative){return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Safe(relative),Encoding.UTF8));}
    static string Str(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)&&v!=null?Convert.ToString(v):null;}
    void Save(string relative,string content){string path=Safe(relative);Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=Safe(Path.Combine(Path.GetDirectoryName(relative)??"",Guid.NewGuid().ToString("N").Substring(0,12)+".tmp"));try{
        using(var f=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] data=new UTF8Encoding(false).GetBytes(content);f.Write(data,0,data.Length);f.Flush(true);}
        if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
    }finally{if(File.Exists(temporary))File.Delete(temporary);}}
    void Save(string relative,object content){Save(relative,json.Serialize(content));}
    IDisposable Gate(){var m=new Mutex(false,"Local\\NTEThaiUi2_"+AppKey());bool owned=false;try{try{owned=m.WaitOne(0);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new InvalidOperationException("โปรแกรมกำลังทำงานอยู่ กรุณารอให้เสร็จ");return new Release(m);}catch{m.Dispose();throw;}}
    string AppKey(){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(app.ToLowerInvariant()))).Replace("-","").Substring(0,16);}
    sealed class Release:IDisposable{Mutex m;public Release(Mutex m){this.m=m;}public void Dispose(){if(m!=null){m.ReleaseMutex();m.Dispose();m=null;}}}
    FileStream FileLock(){return new FileStream(Safe("operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    void Closed(){if(running())throw new InvalidOperationException("กรุณาปิดเกมก่อนทำรายการ");}
    static void CheckGamePath(string path){
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))throw new InvalidOperationException("กรุณาเลือกตำแหน่งเกมแบบเต็ม");
        for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("ตำแหน่งเกมมีโฟลเดอร์เชื่อมไปตำแหน่งอื่น");
    }
    void Editable(){Closed();if(File.Exists(Safe("receipt.json")))throw new InvalidOperationException("กรุณากดคืนค่าเดิมก่อนเปลี่ยนฟอนต์");}
    string Root(){string p=Safe("ตำแหน่งเกม.txt");return File.Exists(p)?File.ReadAllText(p,Encoding.UTF8).Trim():"";}
    static string SelectionFile(FontRole role){if(role!=FontRole.Thai&&role!=FontRole.English)throw new InvalidOperationException("ชนิดฟอนต์ไม่ถูกต้อง");return role==FontRole.Thai?"Fonts/selection.json":"Fonts/english_selection.json";}
    FontChoice Choice(FontRole role){string path=SelectionFile(role);if(!File.Exists(Safe(path)))return Default(role);var d=Read(path);if(Str(d,"Id")=="default")return Default(role);return new FontChoice{Id=Str(d,"Id"),Name=Str(d,"Name"),ManagedSourcePath=Safe(Str(d,"Source")),PreviewPath=Safe(Str(d,"Preview"))};}
    FontChoice Default(FontRole role){SelectionFile(role);var d=Read("Fonts/Default/default_profile.json");return new FontChoice{Id="default",Name=Str(d,role==FontRole.Thai?"default_name":"english_default_name"),PreviewPath=Safe(Str(d,role==FontRole.Thai?"default_preview":"english_default_preview")),IsDefault=true,CanDelete=false};}
    void Select(FontChoice c,FontRole role){Save(SelectionFile(role),new{Id=c.Id,Name=c.Name,Source=c.ManagedSourcePath==null?null:Relative(c.ManagedSourcePath),Preview=Relative(c.PreviewPath)});}
    public string GuidePath{get{return Safe("วิธีใช้.md");}}
    public LauncherSnapshot Inspect(){
        string root=Root();ToggleState state;
        try{if(File.Exists(Safe("receipt.json")))CheckGamePath(core.InstalledRoot);else CheckGamePath(root);state=core.Inspect(root);}catch(Exception e){state=new ToggleState{Message=e.Message};}
        string libraryError="";var c=SnapshotChoice(FontRole.Thai,ref libraryError);var english=SnapshotChoice(FontRole.English,ref libraryError);var thaiLibrary=SnapshotLibrary(FontRole.Thai,ref libraryError);var englishLibrary=SnapshotLibrary(FontRole.English,ref libraryError);bool game=running();
        bool receipt=File.Exists(Safe("receipt.json"));
        return new LauncherSnapshot{Ready=state.Ready,Active=state.Active,GameRunning=game,CanRestore=!game&&receipt,CanSelectFont=!game&&!receipt&&libraryError.Length==0,FontIsDefault=c.Id=="default",EnglishFontIsDefault=english.Id=="default",EnglishFontName=english.Name,EnglishFontPreviewPath=english.PreviewPath,Message=game?"กรุณาปิดเกมก่อนทำรายการ":state.Active?"ติดตั้งคำแปลครบแล้ว ใช้ต่อได้โดยไม่ต้องคืนค่าทุกครั้ง":state.Ready?"พร้อมแปลเกมด้วยฟอนต์ที่เลือก":state.Message,GameRoot=root,FontName=c.Name,FontPreviewPath=c.PreviewPath,GameVersion=GameVersion(),FontId=c.Id,EnglishFontId=english.Id,ThaiFonts=thaiLibrary,EnglishFonts=englishLibrary,FontLibraryError=libraryError.Trim()};
    }
    public void SetGameRoot(string path){using(Gate())using(FileLock()){
        Editable();CheckGamePath(path);var s=core.Inspect(path);if(!s.Ready||s.Active)throw new InvalidOperationException(s.Message);Save("ตำแหน่งเกม.txt",Path.GetFullPath(path).TrimEnd('\\','/'));
    }}
    public FontChoice ImportFont(string path,Action<OperationProgress> progress){return ImportFont(path,FontRole.Thai,progress);}
    public FontChoice ImportFont(string path,FontRole role,Action<OperationProgress> progress){SelectionFile(role);using(Gate())using(FileLock()){
        Editable();if(!string.Equals(Path.GetExtension(path),".ttf",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("กรุณาเลือกฟอนต์ .ttf");
        var info=new FileInfo(path);if(!info.Exists||info.Length<12||info.Length>32*1024*1024)throw new InvalidOperationException("ไฟล์ฟอนต์ไม่ถูกต้องหรือใหญ่เกิน 32 MB");
        string hash=FileStore.Hash(path),source=Safe("Fonts/Imported/"+hash+".ttf");Directory.CreateDirectory(Path.GetDirectoryName(source));
        if(!File.Exists(source)){using(var input=File.OpenRead(path))using(var output=new FileStream(source,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}}
        if(FileStore.Hash(source)!=hash)throw new InvalidOperationException("ไฟล์ฟอนต์ที่คัดลอกไม่ตรงกับต้นฉบับ");
        string id=hash;string outputId=(role==FontRole.Thai?"t_":"e_")+hash.Substring(0,24);var result=Worker("validate",source,outputId,role,null,null,progress);Editable();
        string preview=Str(result,"preview_path");Relative(preview);
        var hashes=(Dictionary<string,object>)result["sha256"];if(FileStore.Hash(preview)!=Str(hashes,"preview")||FileStore.Hash(source)!=Str(hashes,"font"))throw new InvalidOperationException("ตัวอย่างฟอนต์ไม่ตรงกับผลตรวจ");
        var choice=new FontChoice{Id=id,Name=Str(result,"font_name"),ManagedSourcePath=source,PreviewPath=preview,CanDelete=true};AddToLibrary(choice,role);Select(choice,role);Report(progress,"บันทึกฟอนต์แล้ว เลือกใช้จากคลังได้",1,1);return choice;
    }}
    public void SelectDefaultFont(){SelectDefaultFont(FontRole.Thai);}
    public void SelectDefaultFont(FontRole role){SelectionFile(role);using(Gate())using(FileLock()){Editable();Select(Default(role),role);}}
    static void Report(Action<OperationProgress> callback,string text,int done,int total){if(callback!=null)callback(new OperationProgress{Text=text,CompletedStages=done,TotalStages=total});}
    public void Translate(Action<OperationProgress> progress){using(Gate()){
        Closed();string root=Root();CheckGamePath(root);if(File.Exists(Safe("receipt.json"))){VerifyActive(null);Report(progress,"ติดตั้งคำแปลครบแล้ว",1,1);return;}
        string previous=null;bool published=false;PackageManifest selected=null;
        try{
            using(FileLock()){
                Editable();Report(progress,"กำลังเตรียมคำแปลและฟอนต์",0,3);var choice=Choice(FontRole.Thai);var english=Choice(FontRole.English);
                var baseline=Read("Fonts/Default/default_manifest.json");var profile=Read("Fonts/Default/default_profile.json");
                string basePak=Safe(Str(profile,"default_pak"));if(FileStore.Hash(basePak)!=Str(profile,"default_pak_sha256"))throw new InvalidOperationException("แพ็กฟอนต์เดิมไม่ตรงกับรุ่นหลัก");
                if(choice.Id!="default"||english.Id!="default"){
                    foreach(var selectedFont in new[]{choice,english})if(selectedFont.Id!="default"&&FileStore.Hash(selectedFont.ManagedSourcePath)!=selectedFont.Id)throw new InvalidOperationException("ไฟล์ฟอนต์ที่เลือกถูกเปลี่ยน กรุณานำเข้าอีกครั้ง");
                    string profileId;using(var hash=System.Security.Cryptography.SHA256.Create())profileId=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(choice.Id+"|"+english.Id+"|"+FileStore.Hash(Safe("Tools/FontWorker.exe"))+"|"+Str(profile,"default_pak_sha256")))).Replace("-","").ToLowerInvariant().Substring(0,32);
                    var result=Worker("build",choice.ManagedSourcePath??Safe(Str(profile,"default_preview")),profileId,FontRole.Thai,choice,english,progress);Editable();
                    string pak=Str(result,"generated_pak");string relative=Relative(pak);var hashes=(Dictionary<string,object>)result["sha256"];
                    if(FileStore.Hash(pak)!=Str(hashes,"generated_pak")||Str(hashes,"source_pak")!=Str(profile,"default_pak_sha256"))throw new InvalidOperationException("แพ็กฟอนต์ไม่ตรงกับผลตรวจ");
                    var files=(System.Collections.IEnumerable)baseline["Files"];int count=0;foreach(Dictionary<string,object> f in files){if(Str(f,"Target").EndsWith(".pak",StringComparison.OrdinalIgnoreCase)){f["Source"]=relative;f["Sha256"]=Str(hashes,"generated_pak");count++;}}
                    if(count!=1)throw new InvalidOperationException("รายการแพ็กไม่ครบ");baseline["FontProfile"]=new{Id=profileId,ThaiId=choice.Id,ThaiName=choice.Name,EnglishId=english.Id,EnglishName=english.Name};
                }
                string content=json.Serialize(baseline);selected=json.Deserialize<PackageManifest>(content);previous=File.ReadAllText(Safe("แพ็ก/manifest.json"),Encoding.UTF8);Editable();Save("แพ็ก/manifest.json",content);published=true;
            }
            CheckGamePath(root);Report(progress,"กำลังตรวจไฟล์เกมและติดตั้งคำแปล",1,3);core.Enable(root);
            Report(progress,"กำลังยืนยันไฟล์ที่ติดตั้ง",2,3);VerifyActive(selected);Report(progress,"ติดตั้งคำแปลและฟอนต์สำเร็จ พร้อมเปิดเกม",3,3);
        }catch{
            if(published&&!File.Exists(Safe("receipt.json")))using(FileLock()){if(!File.Exists(Safe("receipt.json")))Save("แพ็ก/manifest.json",previous);}
            throw;
        }
    }}
    void VerifyActive(PackageManifest expected){using(FileLock()){
        var r=json.Deserialize<InstallReceipt>(File.ReadAllText(Safe("receipt.json"),Encoding.UTF8));CheckGamePath(r.Root);
        if(expected==null)expected=json.Deserialize<PackageManifest>(File.ReadAllText(Safe("แพ็ก/manifest.json"),Encoding.UTF8));
        if(r==null||r.Phase!="ACTIVE"||r.Files.Count!=6||expected.Files.Count!=6)throw new InvalidOperationException("ยังยืนยันการติดตั้งครบไม่ได้ กรุณาคืนค่าเดิมก่อน");
        foreach(var f in expected.Files){var owned=r.Files.SingleOrDefault(x=>x.Target==f.Target);if(owned==null||!owned.Written||owned.Sha256!=f.Sha256)throw new InvalidOperationException("ชุดติดตั้งไม่ตรงกับฟอนต์ที่เลือก");}
        var state=core.Inspect(r.Root);if(!state.Active)throw new InvalidOperationException(state.Message);
    }}
    public void Restore(Action<OperationProgress> progress){using(Gate()){
        Closed();if(File.Exists(Safe("receipt.json")))CheckGamePath(core.InstalledRoot);Report(progress,"กำลังคืนค่าเดิม",0,2);core.Disable();if(File.Exists(Safe("receipt.json")))throw new InvalidOperationException("ยังคืนค่าไม่ครบ");Report(progress,"คืนค่าเดิมสำเร็จ",2,2);
    }}
    Dictionary<string,object> Worker(string action,string font,string id,FontRole role,FontChoice thai,FontChoice english,Action<OperationProgress> progress){
        string output=Safe("Fonts/Profiles/"+id);Directory.CreateDirectory(output);var defaults=Read("Fonts/Default/default_profile.json");
        string request=Safe("Fonts/Requests/"+Guid.NewGuid().ToString("N")+".json");Directory.CreateDirectory(Path.GetDirectoryName(request));
        var input=new Dictionary<string,object>{{"action",action},{"role",role==FontRole.Thai?"thai":"english"},{"app_root",app},{"font_path",font},{"source_pak",Safe(Str(defaults,"default_pak"))},{"output_dir",output},{"native_path",Safe(Str(defaults,"native_candidate"))}};
        if(action=="build"){input["thai_font_path"]=thai.Id=="default"?null:thai.ManagedSourcePath;input["english_font_path"]=english.Id=="default"?null:english.ManagedSourcePath;}
        Save(Relative(request),input);
        Dictionary<string,object> result=null;Exception outputError=null;var stderr=new StringBuilder();
        try{using(var job=new WorkerJob())using(var p=new Process()){
            p.StartInfo=new ProcessStartInfo{FileName=Safe("Tools/FontWorker.exe"),Arguments="--request \""+request+"\"",WorkingDirectory=app,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            p.OutputDataReceived+=(s,e)=>{if(string.IsNullOrWhiteSpace(e.Data))return;try{var item=json.Deserialize<Dictionary<string,object>>(e.Data);if(Str(item,"type")=="result")result=item;else if(Str(item,"type")=="progress")Report(progress,Str(item,"text"),0,0);}catch(Exception ex){outputError=ex;}};
            p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(stderr){if(stderr.Length<8192)stderr.AppendLine(e.Data);}};
            p.Start();try{job.Attach(p);}catch{if(!p.HasExited)p.Kill();p.WaitForExit();throw;}p.BeginOutputReadLine();p.BeginErrorReadLine();if(!p.WaitForExit(600000)){job.Terminate();p.WaitForExit();throw new InvalidOperationException("เตรียมฟอนต์นานเกินกำหนด กรุณาลองใหม่");}p.WaitForExit();
            if(outputError!=null)throw new InvalidOperationException("อ่านผลตรวจฟอนต์ไม่ได้",outputError);
            if(result==null)throw new InvalidOperationException("เครื่องมือฟอนต์ไม่ส่งผลตรวจ: "+stderr.ToString());
            if(p.ExitCode!=0||!result.ContainsKey("success")||!Convert.ToBoolean(result["success"])){
                var error=result.ContainsKey("error")?result["error"] as Dictionary<string,object>:null;throw new InvalidOperationException(error==null?"เตรียมฟอนต์ไม่สำเร็จ":Str(error,"message"));
            }
            if(Str(result,"action")!=action)throw new InvalidOperationException("ผลตรวจฟอนต์ไม่ตรงกับงานที่สั่ง");
            if(action=="validate"&&Str(result,"role")!=(role==FontRole.Thai?"thai":"english"))throw new InvalidOperationException("เครื่องมือฟอนต์ไม่รองรับชนิดฟอนต์ที่เลือก");
            if(action=="build"){
                object rolesObject;if(!result.TryGetValue("font_roles",out rolesObject))throw new InvalidOperationException("ผลตรวจฟอนต์ไทยและอังกฤษไม่ครบ");var roles=rolesObject as Dictionary<string,object>;
                if(roles==null||!roles.ContainsKey("thai")||!roles.ContainsKey("english"))throw new InvalidOperationException("ผลตรวจฟอนต์ไทยและอังกฤษไม่ครบ");
                foreach(var pair in new[]{new KeyValuePair<string,FontChoice>("thai",thai),new KeyValuePair<string,FontChoice>("english",english)})if(pair.Value.Id!="default"){
                    var details=roles[pair.Key] as Dictionary<string,object>;if(details==null||Str(details,"input_font_sha256")!=pair.Value.Id)throw new InvalidOperationException("แพ็กไม่ตรงกับฟอนต์ที่เลือก");
                }
            }
            Save("Fonts/Profiles/"+id+"/"+action+"_result.json",result);
            return result;
        }}finally{if(File.Exists(request))File.Delete(request);}
    }
}
