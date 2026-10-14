using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
internal static class UpdateUiTests {
    [STAThread] static int Main() {
        Application.EnableVisualStyles();string root=Path.Combine(Path.GetTempPath(),"nte-update-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var releases=new Releases();bool launched=false;
        using(var form=new ToggleWindow(root,new GameFixture(),releases,(path,r)=>launched=true)) {
            form.Show();Pump(()=>releases.Checks>0&&Find<Control>(form,"UpdateNotice").Visible);
            Check(Find<Label>(form,"UpdateNoticeText").Text.Contains("1.1.0"),"startup check displays available version in a themed notice");
            Check(Find<AntdUI.Button>(form,"AppUpdateButton").Enabled,"available release offers the Update button");
            string oldStatus=Find<Label>(form,"ReadOnlyStatus").Text;
            releases.Fail=true;Find<AntdUI.Button>(form,"CheckAppUpdates").PerformClick();Pump(()=>releases.Checks==2&&!Find<AntdUI.Button>(form,"CheckAppUpdates").Loading);
            Check(Find<Label>(form,"ReadOnlyStatus").Text==oldStatus,"network check failure preserves game translation status");
            Check(Find<Control>(form,"UpdateNotice").Visible,"temporary network failure keeps a previously discovered update");
            releases.Fail=false;Find<AntdUI.Button>(form,"AppUpdateButton").PerformClick();Pump(()=>launched);
            Check(releases.Downloads==1&&launched,"update action downloads verified installer before invoking installation callback");
        }
        var current=new Releases{Current=true};
        using(var form=new ToggleWindow(root,new GameFixture(),current,(p,r)=>{})) {
            form.Show();Pump(()=>current.Checks>0&&!Find<AntdUI.Button>(form,"CheckAppUpdates").Loading);
            Check(!Find<Control>(form,"UpdateNotice").Visible,"current version hides the update notice");
        }
        var failed=new Releases{FailDownload=true};
        using(var form=new ToggleWindow(root,new GameFixture(),failed,(p,r)=>{throw new Exception("must not install");})) {
            form.Show();Pump(()=>Find<Control>(form,"UpdateNotice").Visible);
            Find<AntdUI.Button>(form,"AppUpdateButton").PerformClick();Pump(()=>Find<Label>(form,"AppUpdateState").Text.Contains("ดาวน์โหลดอัปเดตไม่สำเร็จ"));
            Check(!form.IsDisposed&&Find<AntdUI.Button>(form,"AppUpdateButton").Enabled,"failed download preserves window and permits retry");
        }
        var blocked=new Releases{BlockDownload=true};
        using(var form=new ToggleWindow(root,new GameFixture(),blocked,(p,r)=>{throw new Exception("must not install");})) {
            form.Show();Pump(()=>Find<Control>(form,"UpdateNotice").Visible);
            Find<AntdUI.Button>(form,"AppUpdateButton").PerformClick();Pump(()=>blocked.Downloads>0);
            Check(!Find<AntdUI.Button>(form,"EnableTranslation").Enabled&&Find<AntdUI.Button>(form,"CancelAppUpdate").Enabled,"update transfer prevents simultaneous game mutation and allows cancellation");
            Find<AntdUI.Button>(form,"CancelAppUpdate").PerformClick();Pump(()=>Find<Label>(form,"AppUpdateState").Text.Contains("ยกเลิกดาวน์โหลดแล้ว"));
            Check(Find<AntdUI.Button>(form,"AppUpdateButton").Enabled,"cancellation permits retry without installation");
        }
        var rejected=new Releases();
        using(var form=new ToggleWindow(root,new GameFixture(),rejected,(p,r)=>{throw new IOException("helper rejected");})) {
            form.Show();Pump(()=>Find<Control>(form,"UpdateNotice").Visible);
            Find<AntdUI.Button>(form,"AppUpdateButton").PerformClick();Pump(()=>Find<Label>(form,"AppUpdateState").Text.Contains("เริ่มติดตั้งอัปเดตไม่ได้"));
            Check(!form.IsDisposed&&Find<AntdUI.Button>(form,"AppUpdateButton").Enabled,"helper handoff failure preserves window and update retry");
        }
        Directory.Delete(root,true);Console.WriteLine("UPDATE_UI_PASS 10");return 0;
    }
    static T Find<T>(Control c,string name)where T:Control{return c.Controls.Find(name,true)[0]as T;}
    static void Pump(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(5);while(!done()&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();if(!done())throw new Exception("timeout");}
    static void Check(bool v,string s){if(!v)throw new Exception(s);Console.WriteLine("PASS "+s);}
    class Releases:IReleaseDelivery {
        public int Checks,Downloads;public bool Fail,Current,FailDownload,BlockDownload;
        public ReleaseInfo Check(CancellationToken t){Interlocked.Increment(ref Checks);if(Fail)throw new IOException("offline");return Current?null:new ReleaseInfo{Version="1.1.0",Notes="ปรับปรุงโปรแกรม"};}
        public string Download(ReleaseInfo r,Action<TransferProgress> p,CancellationToken t){Interlocked.Increment(ref Downloads);if(FailDownload)throw new IOException("fixture");if(BlockDownload){while(!t.IsCancellationRequested)Thread.Sleep(10);t.ThrowIfCancellationRequested();}return "verified-test.exe";}
    }
    class GameFixture:ILauncherService {
        public string GuidePath{get{return null;}}public LauncherSnapshot Inspect(){return new LauncherSnapshot{Ready=true,FontIsDefault=true,EnglishFontIsDefault=true,GameVersion="1.4.5",Message="ready",ThaiFonts=new FontChoice[0],EnglishFonts=new FontChoice[0]};}
        public void SetGameRoot(string p){}public void Translate(Action<OperationProgress>p){}public void Restore(Action<OperationProgress>p){}
        public FontChoice ImportFont(string p,Action<OperationProgress>a){return null;}public FontChoice ImportFont(string p,FontRole r,Action<OperationProgress>a){return null;}
        public void SelectDefaultFont(){}public void SelectDefaultFont(FontRole r){}public FontChoice[] ListFonts(FontRole r){return new FontChoice[0];}public void SelectFont(FontRole r,string id){}public void DeleteFont(FontRole r,string id){}
    }
}
