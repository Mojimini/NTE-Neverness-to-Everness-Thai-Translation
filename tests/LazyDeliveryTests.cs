using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
internal static class LazyDeliveryTests {
    static int count;
    static int Main() {
        string app=Path.Combine(Path.GetTempPath(),"nte-delivery-wrapper-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(app);
        var baseService=new BaseFixture();var delivery=new ContentFixture();var wrapper=new DeliveredLauncherService(app,baseService,delivery);
        wrapper.Translate(null);Check(delivery.Calls[0]=="translation"&&baseService.Translated==1,"first default translation prepares only translation payload before game operation");
        baseService.State.FontIsDefault=false;wrapper.Translate(null);Check(delivery.Calls[1]=="translation,font-tools","custom translation prepares both payload groups");
        baseService.State.Active=true;wrapper.Translate(null);Check(delivery.Calls.Count==2&&baseService.Translated==3,"active recheck needs no new payload or network");
        baseService.State.Active=false;wrapper.ImportFont("font.ttf",FontRole.English,null);Check(delivery.Calls[2]=="translation,font-tools"&&baseService.Imported==1,"font validation prepares its pinned corpus/tools and source pak");
        baseService.State.GameRunning=true;try{wrapper.Translate(null);throw new Exception("guard did not reject");}catch(InvalidOperationException){}Check(delivery.Calls.Count==3,"actual-game guard rejects before downloading or altering content");
        baseService.State.GameRunning=false;delivery.Fail=true;try{wrapper.Translate(null);throw new Exception("failure not propagated");}catch(IOException){}Check(baseService.Translated==3,"download failure prevents game installation");
        wrapper.Restore(null);Check(baseService.Restored==1&&delivery.Calls.Count==3,"offline restore does not need any download");
        Directory.Delete(app,true);Console.WriteLine("LAZY_DELIVERY_PASS "+count);return 0;
    }
    static void Check(bool v,string text){if(!v)throw new Exception(text);Console.WriteLine("PASS "+text);count++;}
    class ContentFixture:IContentDelivery {public List<string> Calls=new List<string>();public bool Fail;public void Ensure(string[] groups,Action<TransferProgress> p,CancellationToken t){if(Fail)throw new IOException("offline");Calls.Add(string.Join(",",groups));}}
    class BaseFixture:ILauncherService {
        public LauncherSnapshot State=new LauncherSnapshot{Ready=true,FontIsDefault=true,EnglishFontIsDefault=true,CanSelectFont=true};public int Translated,Imported,Restored;
        public string GuidePath{get{return null;}}public LauncherSnapshot Inspect(){return State;}public void SetGameRoot(string p){}
        public void Translate(Action<OperationProgress>p){Translated++;}public void Restore(Action<OperationProgress>p){Restored++;}
        public FontChoice ImportFont(string p,Action<OperationProgress>a){return ImportFont(p,FontRole.Thai,a);}public FontChoice ImportFont(string p,FontRole r,Action<OperationProgress>a){Imported++;return null;}
        public void SelectDefaultFont(){}public void SelectDefaultFont(FontRole r){}public void SelectFont(FontRole r,string id){}public void DeleteFont(FontRole r,string id){}public FontChoice[] ListFonts(FontRole r){return new FontChoice[0];}
    }
}
