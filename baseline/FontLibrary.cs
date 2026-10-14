using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;

public sealed partial class LauncherService {
    public sealed class LibraryRecord {
        public string Id, Name, Source, Preview, SourceSha256, PreviewSha256, Role;
    }
    public sealed class LibraryState {
        public int Version = 1;
        public List<LibraryRecord> Items = new List<LibraryRecord>();
        public List<string> HiddenBundled = new List<string>();
    }
    public sealed class BundledLibrary { public List<LibraryRecord> Fonts; }
    static string RoleName(FontRole role){ SelectionFile(role);return role==FontRole.Thai?"thai":"english"; }
    static string LibraryFile(FontRole role){return "Fonts/library_"+RoleName(role)+".json";}
    static bool FontId(string id){return id!=null&&id.Length==64&&id.All(c=>"0123456789abcdef".Contains(c));}
    string GameVersion(){try{return Str(Read("Fonts/Default/default_manifest.json"),"SourceVersion")??"1.4.5";}catch{return "1.4.5";}}
    FontChoice SnapshotChoice(FontRole role,ref string error){
        try{return Choice(role);}catch(Exception ex){error+=RoleName(role)+": "+ex.Message+Environment.NewLine;return new FontChoice{Id="unavailable",Name="อ่านข้อมูลฟอนต์ที่เลือกไม่ได้"};}
    }
    FontChoice[] SnapshotLibrary(FontRole role,ref string error){
        try{return ListFonts(role);}catch(Exception ex){error+=RoleName(role)+": "+ex.Message+Environment.NewLine;return new FontChoice[0];}
    }
    LibraryRecord Record(FontChoice c,FontRole role){return new LibraryRecord{Id=c.Id,Name=c.Name,Source=Relative(c.ManagedSourcePath),Preview=Relative(c.PreviewPath),SourceSha256=c.Id,PreviewSha256=FileStore.Hash(c.PreviewPath),Role=RoleName(role)};}
    FontChoice Resolve(LibraryRecord r,FontRole role,bool bundled,bool verify){
        if(r==null||!FontId(r.Id)||r.SourceSha256!=r.Id||r.Role!=RoleName(role)||String.IsNullOrWhiteSpace(r.Name)||!FontId(r.PreviewSha256))throw new InvalidOperationException("ข้อมูลคลังฟอนต์ไม่ถูกต้อง");
        string source=Safe(r.Source),preview=Safe(r.Preview);
        string allowed=bundled?"Fonts/Bundled/":"Fonts/Imported/";
        if(!r.Source.Replace('\\','/').StartsWith(allowed,StringComparison.Ordinal)||(!bundled&&r.Source.Replace('\\','/')!="Fonts/Imported/"+r.Id+".ttf"))throw new InvalidOperationException("ตำแหน่งฟอนต์ในคลังไม่ถูกต้อง");
        string allowedPreview=bundled?"Fonts/Bundled/":"Fonts/Profiles/"+(role==FontRole.Thai?"t_":"e_")+r.Id.Substring(0,24)+"/";
        if(!r.Preview.Replace('\\','/').StartsWith(allowedPreview,StringComparison.Ordinal)||!r.Preview.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ตำแหน่งตัวอย่างฟอนต์ในคลังไม่ถูกต้อง");
        if(!File.Exists(source)||!File.Exists(preview))throw new InvalidOperationException("ไม่พบไฟล์ฟอนต์ที่บันทึกไว้: "+r.Name);
        if(verify&&(FileStore.Hash(source)!=r.SourceSha256||FileStore.Hash(preview)!=r.PreviewSha256))throw new InvalidOperationException("ไฟล์ฟอนต์ในคลังถูกเปลี่ยน กรุณานำเข้าอีกครั้ง");
        return new FontChoice{Id=r.Id,Name=r.Name,ManagedSourcePath=source,PreviewPath=preview,CanDelete=true};
    }
    List<LibraryRecord> Bundled(FontRole role){
        string path=Safe("Fonts/Bundled/bundled_fonts.json");
        if(!File.Exists(path))return new List<LibraryRecord>();
        var data=json.Deserialize<BundledLibrary>(File.ReadAllText(path,Encoding.UTF8));
        if(data==null||data.Fonts==null)throw new InvalidOperationException("ข้อมูลฟอนต์ที่มากับโปรแกรมไม่ครบ");
        var records=data.Fonts.Where(x=>x!=null&&x.Role==RoleName(role)).ToList();
        if(records.Select(x=>x.Id).Distinct().Count()!=records.Count)throw new InvalidOperationException("รายการฟอนต์ที่มากับโปรแกรมซ้ำ");
        return records;
    }
    LibraryState LoadLibrary(FontRole role){
        string path=Safe(LibraryFile(role));
        if(File.Exists(path)){
            var saved=json.Deserialize<LibraryState>(File.ReadAllText(path,Encoding.UTF8));
            if(saved==null||saved.Version!=1||saved.Items==null||saved.HiddenBundled==null||saved.Items.Any(x=>x==null)||saved.Items.Select(x=>x.Id).Distinct().Count()!=saved.Items.Count||saved.HiddenBundled.Any(x=>!FontId(x)))throw new InvalidOperationException("อ่านคลังฟอนต์ไม่ได้");
            return saved;
        }
        var state=new LibraryState();
        string folder=Safe("Fonts/Imported");
        if(Directory.Exists(folder))foreach(string font in Directory.GetFiles(folder,"*.ttf")){
            string id=Path.GetFileNameWithoutExtension(font);
            if(!FontId(id))continue;
            string audit="Fonts/Profiles/"+(role==FontRole.Thai?"t_":"e_")+id.Substring(0,24)+"/validate_result.json";
            if(!File.Exists(Safe(audit)))continue;
            var result=Read(audit);
            object hashesObject;
            if(Str(result,"role")!=RoleName(role)||!result.TryGetValue("sha256",out hashesObject))continue;
            var hashes=hashesObject as Dictionary<string,object>;
            if(hashes==null||Str(hashes,"font")!=id)continue;
            var record=new LibraryRecord{Id=id,Name=Str(result,"font_name"),Source=Relative(font),Preview=Relative(Str(result,"preview_path")),SourceSha256=id,PreviewSha256=Str(hashes,"preview"),Role=RoleName(role)};
            Resolve(record,role,false,false);state.Items.Add(record);
        }
        var selected=Choice(role);
        if(selected.Id!="default"&&!state.Items.Any(x=>x.Id==selected.Id)&&!Bundled(role).Any(x=>x.Id==selected.Id))state.Items.Add(Record(selected,role));
        return state;
    }
    public FontChoice[] ListFonts(FontRole role){
        var state=LoadLibrary(role);
        var result=new List<FontChoice>{Default(role)};
        foreach(var record in Bundled(role))if(!state.HiddenBundled.Contains(record.Id))result.Add(Resolve(record,role,true,false));
        foreach(var record in state.Items)if(!result.Any(x=>x.Id==record.Id))result.Add(Resolve(record,role,false,false));
        return result.ToArray();
    }
    void AddToLibrary(FontChoice c,FontRole role){
        var state=LoadLibrary(role);
        state.Items.RemoveAll(x=>x.Id==c.Id);
        state.Items.Add(Record(c,role));
        state.HiddenBundled.RemoveAll(x=>x==c.Id);
        Save(LibraryFile(role),state);
    }
    public void SelectFont(FontRole role,string id){using(Gate())using(FileLock()){
        Editable();RoleName(role);
        if(id=="default"){Select(Default(role),role);return;}
        var state=LoadLibrary(role);
        var bundle=Bundled(role).FirstOrDefault(x=>x.Id==id&&!state.HiddenBundled.Contains(x.Id));
        var record=bundle??state.Items.FirstOrDefault(x=>x.Id==id);
        if(record==null)throw new InvalidOperationException("ไม่พบฟอนต์ที่เลือกในคลัง");
        var choice=Resolve(record,role,bundle!=null,true);
        Save(LibraryFile(role),state);
        Select(choice,role);
    }}
    public void DeleteFont(FontRole role,string id){using(Gate())using(FileLock()){
        Editable();RoleName(role);
        if(id=="default")throw new InvalidOperationException("ฟอนต์เริ่มต้นของเกมลบไม่ได้");
        var state=LoadLibrary(role);
        var bundle=Bundled(role).FirstOrDefault(x=>x.Id==id&&!state.HiddenBundled.Contains(x.Id));
        var record=state.Items.FirstOrDefault(x=>x.Id==id);
        if(bundle==null&&record==null)throw new InvalidOperationException("ไม่พบฟอนต์ที่ต้องการลบ");
        if(Choice(role).Id==id)Select(Default(role),role);
        state.Items.RemoveAll(x=>x.Id==id);
        if(bundle!=null&&!state.HiddenBundled.Contains(id))state.HiddenBundled.Add(id);
        Save(LibraryFile(role),state);
        // Cached font/build bytes are retained for shared roles and recovery; only this role's catalog entry is removed.
    }}
}
