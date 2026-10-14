using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// Lazy delivery never touches game files; accepted LauncherService/Core still
// enforce installation/receipt/fingerprint rules after content is verified.
public sealed class DeliveredLauncherService : ILauncherService {
    private readonly string app;
    private readonly ILauncherService inner;
    private readonly IContentDelivery delivery;
    public DeliveredLauncherService(string app, ILauncherService inner, IContentDelivery delivery) {
        if(inner==null||delivery==null)throw new ArgumentNullException();
        this.app=Path.GetFullPath(app).TrimEnd('\\','/');this.inner=inner;this.delivery=delivery;
    }
    public string GuidePath {get{return inner.GuidePath;}}
    public LauncherSnapshot Inspect(){return inner.Inspect();}
    public void SetGameRoot(string path){inner.SetGameRoot(path);}
    public FontChoice[] ListFonts(FontRole role){return inner.ListFonts(role);}
    public void SelectFont(FontRole role,string id){inner.SelectFont(role,id);}
    public void DeleteFont(FontRole role,string id){inner.DeleteFont(role,id);}
    public void SelectDefaultFont(){inner.SelectDefaultFont();}
    public void SelectDefaultFont(FontRole role){inner.SelectDefaultFont(role);}
    public void Restore(Action<OperationProgress> progress){inner.Restore(progress);}
    public FontChoice ImportFont(string path,Action<OperationProgress> progress){return ImportFont(path,FontRole.Thai,progress);}
    public FontChoice ImportFont(string path,FontRole role,Action<OperationProgress> progress) {
        using(Gate()) {
            var s=inner.Inspect();GameClosed(s);
            if(s!=null&&!s.CanSelectFont)throw new InvalidOperationException("กรุณาคืนค่าเดิมหรือตรวจคลังฟอนต์ก่อนเปลี่ยนฟอนต์");
            Prepare(new[]{"translation","font-tools"},progress);
            return inner.ImportFont(path,role,progress);
        }
    }
    public void Translate(Action<OperationProgress> progress) {
        using(Gate()) {
            var state=inner.Inspect();GameClosed(state);
            if(state==null)throw new InvalidOperationException("ยังตรวจสอบความพร้อมไม่ได้");
            if(!state.Active)Prepare(state.FontIsDefault&&state.EnglishFontIsDefault
                ?new[]{"translation"}:new[]{"translation","font-tools"},progress);
            inner.Translate(progress);
        }
    }
    private static void GameClosed(LauncherSnapshot state) {
        if(state!=null&&state.GameRunning)throw new InvalidOperationException("กรุณาปิดเกมก่อนทำรายการ");
    }
    private void Prepare(string[] groups,Action<OperationProgress> progress) {
        CheckPath(Path.Combine(app,"operation.lock"));
        using(var file=new FileStream(Path.Combine(app,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            delivery.Ensure(groups,p=>{
                if(progress!=null)progress(new OperationProgress{Text=p.Text,CompletedStages=0,TotalStages=0});
            },CancellationToken.None);
        }
    }
    private IDisposable Gate() {
        CheckPath(app);
        string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(app.ToLowerInvariant()))).Replace("-","").Substring(0,16);
        var mutex=new Mutex(false,"Local\\NTEThaiUi2_"+key);bool held=false;
        try {try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}
            if(!held)throw new InvalidOperationException("โปรแกรมอีกหน้าต่างกำลังทำงาน กรุณารอให้เสร็จ");
            return new HeldMutex(mutex);
        }catch{mutex.Dispose();throw;}
    }
    private static void CheckPath(string path) {
        for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("ตำแหน่งโปรแกรมเชื่อมไปโฟลเดอร์อื่น");
    }
    private sealed class HeldMutex:IDisposable {
        Mutex value;public HeldMutex(Mutex value){this.value=value;}
        public void Dispose(){if(value!=null){value.ReleaseMutex();value.Dispose();value=null;}}
    }
}
