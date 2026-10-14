using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

// Retained-mode view; all game/font/update work remains behind the accepted service boundary.
public sealed partial class WpfLauncherWindow : Window {
    private readonly string app;
    private readonly ILauncherService service;
    private readonly IReleaseDelivery releases;
    private readonly Action<string,ReleaseInfo> installUpdate;
    private readonly ThemeSettings themeSettings;
    private LauncherTheme theme;
    private ThemePalette palette;
    private LauncherSnapshot snapshot;
    private bool busy,closed,refreshingFonts,inspecting,checkingUpdates,updatingApp;
    private int generation;
    private string statusKind="idle",statusMessage="";
    private string lastThaiPath,lastEnglishPath;
    private string thaiPreviewError,englishPreviewError;
    private bool previewInitialized;
    private ReleaseInfo availableRelease;
    private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    private CancellationTokenSource download;
    private DispatcherTimer inspectTimer,updateTimer;
    private LatestProgressDispatcher progressDispatch;

    public WpfLauncherWindow(string app,ILauncherService service,IReleaseDelivery releases,Action<string,ReleaseInfo> install) {
        if(String.IsNullOrWhiteSpace(app)||service==null)throw new ArgumentNullException();
        this.app=Path.GetFullPath(app);this.service=service;this.releases=releases;installUpdate=install;
        themeSettings=ThemeSettings.ForApplication(this.app);theme=themeSettings.Load();palette=ThemePalette.For(theme);
        Title="NTE ภาษาไทย";Width=Math.Min(980,SystemParameters.WorkArea.Width-40);Height=Math.Min(830,SystemParameters.WorkArea.Height-40);
        MinWidth=820;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Leelawadee UI");FontSize=14;UseLayoutRounding=true;SnapsToDevicePixels=true;
        BuildView();
        Width=Math.Max(MinWidth,Math.Min(980,SystemParameters.WorkArea.Width-40));
        Height=Math.Max(MinHeight,Math.Min(800,SystemParameters.WorkArea.Height-40));
        SetTheme(theme,false);SetStatus("loading","กำลังตรวจสอบความพร้อม…");
        translateButton.Click+=(s,e)=>RunWork(snapshot!=null&&snapshot.Active?"verify":"translate",p=>service.Translate(p));
        restoreButton.Click+=(s,e)=>RunWork("restore",p=>service.Restore(p));
        browseButton.Click+=(s,e)=>BrowseGame();guideButton.Click+=(s,e)=>OpenGuide();
        themeButton.Click+=(s,e)=>SetTheme(theme==LauncherTheme.Dark?LauncherTheme.Light:LauncherTheme.Dark,true);
        thaiLibrary.SelectionChanged+=(s,e)=>SelectFont(FontRole.Thai,thaiLibrary);
        englishLibrary.SelectionChanged+=(s,e)=>SelectFont(FontRole.English,englishLibrary);
        importThaiButton.Click+=(s,e)=>ImportFont(FontRole.Thai);importEnglishButton.Click+=(s,e)=>ImportFont(FontRole.English);
        deleteThaiButton.Click+=(s,e)=>DeleteFont(FontRole.Thai,thaiLibrary);deleteEnglishButton.Click+=(s,e)=>DeleteFont(FontRole.English,englishLibrary);
        checkUpdatesButton.Click+=(s,e)=>CheckAppUpdate();updateButton.Click+=(s,e)=>DownloadAppUpdate();
        cancelUpdateButton.Click+=(s,e)=>{if(download!=null)download.Cancel();};
        Loaded+=(s,e)=>{RefreshSnapshot(true);CheckAppUpdate();};
        SourceInitialized+=(s,e)=>ApplyChrome();
        inspectTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};inspectTimer.Tick+=(s,e)=>RefreshSnapshot(false);inspectTimer.Start();
        updateTimer=new DispatcherTimer{Interval=TimeSpan.FromHours(1)};updateTimer.Tick+=(s,e)=>CheckAppUpdate();updateTimer.Start();
        Closing+=(s,e)=>{if(busy){e.Cancel=true;if(!updatingApp)SetStatus("busy","กำลังทำงาน กรุณารอให้เสร็จก่อนปิดหน้าต่าง");else updateState.Text="กำลังอัปเดต กรุณารอให้เสร็จก่อนปิดหน้าต่าง";}};
        Closed+=(s,e)=>{
            closed=true;inspectTimer.Stop();updateTimer.Stop();lifetime.Cancel();
            if(download!=null)download.Cancel();if(progressDispatch!=null)progressDispatch.Dispose();
        };
        UpdateAvailability();
    }
    private void Post(Action action){if(closed||Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(()=>{if(!closed)action();}),DispatcherPriority.Background);}
    private void SetTheme(LauncherTheme value,bool persist) {
        theme=value;palette=ThemePalette.For(value);ApplyViewTheme(palette);operationProgress.ApplyTheme(palette);
        themeButton.Content=value==LauncherTheme.Dark?"สว่าง":"มืด";SetStatusVisual(statusKind,palette);ApplyChrome();
        if(persist)try{themeSettings.Save(value);}catch(Exception e){SetStatus("error","บันทึกธีมไม่ได้\n"+e.Message);}
    }
    private void ApplyChrome(){
        try{
            IntPtr hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
            int dark=theme==LauncherTheme.Dark?1:0;
            if(DwmSetWindowAttribute(hwnd,20,ref dark,4)!=0)DwmSetWindowAttribute(hwnd,19,ref dark,4);
            int caption=ColorRef(palette.Window),border=ColorRef(palette.Border),text=ColorRef(palette.Text);
            DwmSetWindowAttribute(hwnd,35,ref caption,4);DwmSetWindowAttribute(hwnd,34,ref border,4);DwmSetWindowAttribute(hwnd,36,ref text,4);
        }catch(DllNotFoundException){}catch(EntryPointNotFoundException){}catch(ExternalException){}
    }
    private static int ColorRef(System.Drawing.Color color){return color.R|(color.G<<8)|(color.B<<16);}
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
    private void SetStatus(string state,string message) {
        statusKind=state;statusMessage=message??"";
        if(statusText.Text!=statusMessage)statusText.Text=statusMessage;
        SetStatusVisual(state,palette);
        string detail=(state=="error"?"พบปัญหา":state=="success"?"ติดตั้งคำแปลแล้ว":state=="busy"?"กำลังทำงาน":"พร้อมใช้งาน")+"\n"+statusMessage;
        if(statusDetail.Text!=detail)statusDetail.Text=detail;
    }
    private void RefreshSnapshot(bool initial) {
        if(busy||inspecting||closed)return;inspecting=true;int ticket=generation;
        Task.Run(()=>{
            LauncherSnapshot next=null;Exception failure=null;try{next=service.Inspect();}catch(Exception e){failure=e;}
            Post(()=>{inspecting=false;if(ticket!=generation||busy)return;if(failure!=null){SetStatus("error",UserError(failure));return;}ApplySnapshot(next,initial,true);});
        });
    }
    private void ApplySnapshot(LauncherSnapshot next,bool initial,bool external) {
        if(next==null)return;
        var previous=snapshot;snapshot=next;gamePath.Text=next.GameRoot??"";
        releaseBadge.Text="NTE "+(String.IsNullOrWhiteSpace(next.GameVersion)?"1.4.5":next.GameVersion);
        gameHint.Text=(next.GameRunning?"เกมเปิดอยู่":"เกมปิดอยู่")+"  •  ภาษาเกม: English";
        fontHint.Text="TH: "+next.FontName+"    EN: "+next.EnglishFontName;
        RefreshLibrary(thaiLibrary,next.ThaiFonts,next.FontId,next.FontIsDefault,next.FontName);
        RefreshLibrary(englishLibrary,next.EnglishFonts,next.EnglishFontId,next.EnglishFontIsDefault,next.EnglishFontName);
        SetPreview(next);
        string[] fontWarnings={next.FontLibraryError,thaiPreviewError,englishPreviewError};
        fontError.Text=String.Join("\n",fontWarnings.Where(x=>!String.IsNullOrWhiteSpace(x)));
        fontErrorBox.Visibility=String.IsNullOrWhiteSpace(fontError.Text)?Visibility.Collapsed:Visibility.Visible;
        if(initial) {
            operationProgress.SetExternalActive(next.Active);
            SetStatus(next.GameRunning?"waiting":next.Active?"success":"idle",next.GameRunning?"กรุณาปิดเกมก่อนทำรายการ":next.Active?"เปิดคำแปลอยู่":next.Message);
        } else if(external&&statusKind!="error"&&statusKind!="busy") {
            if(previous!=null&&previous.Active!=next.Active)operationProgress.SetExternalActive(next.Active);
            if(next.GameRunning&& (previous==null||!previous.GameRunning))SetStatus("waiting","กรุณาปิดเกมก่อนทำรายการ");
            else if(!next.GameRunning&&statusKind=="waiting")SetStatus(next.Active?"success":"idle",next.Active?"เปิดคำแปลอยู่":next.Message);
        }
        UpdateAvailability();
    }
    private void RefreshLibrary(ComboBox combo,FontChoice[] choices,string id,bool isDefault,string name) {
        choices=(choices??new FontChoice[0]).Where(x=>x!=null).ToArray();
        bool same=combo.Items.Count==choices.Length;
        if(same)for(int i=0;i<choices.Length;i++){var old=((ComboBoxItem)combo.Items[i]).Tag as FontChoice;var c=choices[i];if(old==null||old.Id!=c.Id||old.Name!=c.Name||old.IsDefault!=c.IsDefault||old.CanDelete!=c.CanDelete){same=false;break;}}
        var selected=Selected(combo);bool selectedSame=selected!=null&&(!String.IsNullOrEmpty(id)?selected.Id==id:selected.IsDefault==isDefault&&selected.Name==name);
        if(same&&selectedSame)return;
        refreshingFonts=true;
        try {
            if(!same){combo.Items.Clear();foreach(var c in choices)combo.Items.Add(new ComboBoxItem{Content=c.Name+(c.IsDefault?"  •  ค่าเดิม":""),Tag=c});}
            ComboBoxItem match=null;foreach(ComboBoxItem item in combo.Items){var c=(FontChoice)item.Tag;if(!String.IsNullOrEmpty(id)?c.Id==id:c.IsDefault==isDefault&&c.Name==name){match=item;break;}}
            combo.SelectedItem=match;
        } finally{refreshingFonts=false;}
    }
    private static FontChoice Selected(ComboBox combo){var item=combo.SelectedItem as ComboBoxItem;return item==null?null:item.Tag as FontChoice;}
    private void SetPreview(LauncherSnapshot next) {
        if(!previewInitialized||lastThaiPath!=next.FontPreviewPath){lastThaiPath=next.FontPreviewPath;FontFamily f=PreviewFamily(lastThaiPath,"ภาษาไทย",out thaiPreviewError);previewThai1.FontFamily=f;previewThai2.FontFamily=f;}
        if(!previewInitialized||lastEnglishPath!=next.EnglishFontPreviewPath){lastEnglishPath=next.EnglishFontPreviewPath;FontFamily f=PreviewFamily(lastEnglishPath,"English",out englishPreviewError);previewEnglish1.FontFamily=f;previewEnglish2.FontFamily=f;}
        previewInitialized=true;
        previewCaption.Text="TH: "+next.FontName+(thaiPreviewError==null?"":" (ตัวอย่างใช้ฟอนต์หน้าต่าง)")+"    EN: "+next.EnglishFontName+(englishPreviewError==null?"":" (ตัวอย่างใช้ฟอนต์หน้าต่าง)");
    }
    private static FontFamily PreviewFamily(string path,string role,out string error) {
        error=null;
        try{
            if(!String.IsNullOrEmpty(path)&&File.Exists(path)){
                string full=Path.GetFullPath(path);var fileUri=new Uri(new Uri(full,UriKind.Absolute).AbsoluteUri,UriKind.Absolute);var glyph=new GlyphTypeface(fileUri);string name=glyph.FamilyNames.Values.First();
                // A directory-only family can select another TTF with the same family
                // name (e.g. raw Monocraft instead of its normalized preview).
                var family=new FontFamily(fileUri.AbsoluteUri+"#"+name);
                GlyphTypeface resolved;
                if(!new Typeface(family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out resolved)
                    ||!resolved.FontUri.IsFile||!String.Equals(Path.GetFullPath(resolved.FontUri.LocalPath),full,StringComparison.OrdinalIgnoreCase))throw new IOException("ตัวอย่างไม่ตรงกับไฟล์ฟอนต์ที่เลือก");
                return family;
            }
        }catch{}
        error="แสดงตัวอย่างฟอนต์ "+role+" ที่เลือกไม่ได้ — ตัวอย่างแสดงด้วยฟอนต์หน้าต่างแทน";
        return new FontFamily("Leelawadee UI");
    }
    private void UpdateAvailability() {
        bool has=snapshot!=null,stopped=has&&!snapshot.GameRunning,select=!busy&&stopped&&snapshot.CanSelectFont&&String.IsNullOrWhiteSpace(snapshot.FontLibraryError);
        translateButton.IsEnabled=!busy&&stopped&&snapshot.Ready;
        translateButton.Content=busy&&!updatingApp?"กำลังทำงาน…":has&&snapshot.Active?"ตรวจอีกครั้ง":"แปลเกม";
        restoreButton.IsEnabled=!busy&&stopped&&snapshot.CanRestore;
        browseButton.IsEnabled=!busy&&stopped&&!snapshot.Active&&!snapshot.CanRestore;
        thaiLibrary.IsEnabled=englishLibrary.IsEnabled=importThaiButton.IsEnabled=importEnglishButton.IsEnabled=select;
        deleteThaiButton.IsEnabled=select&&CanDelete(Selected(thaiLibrary));deleteEnglishButton.IsEnabled=select&&CanDelete(Selected(englishLibrary));
        checkUpdatesButton.IsEnabled=releases!=null&&!busy&&!checkingUpdates;
        updateButton.IsEnabled=availableRelease!=null&&!busy&&!checkingUpdates;cancelUpdateButton.IsEnabled=download!=null;
    }
    private static bool CanDelete(FontChoice c){return c!=null&&c.Id!="default"&&!c.IsDefault&&c.CanDelete;}
    private void RunWork(string kind,Action<Action<OperationProgress>> work) {
        if(busy||snapshot==null||snapshot.GameRunning)return;
        if((kind=="translate"||kind=="verify")&&!snapshot.Ready)return;
        if(kind=="restore"&&!snapshot.CanRestore)return;
        if(kind.StartsWith("font")&&!snapshot.CanSelectFont)return;
        if(kind=="root"&&(snapshot.Active||snapshot.CanRestore))return;
        busy=true;generation++;float restoreStart=0;
        if(kind=="translate")operationProgress.BeginTranslation();else if(kind=="restore")restoreStart=operationProgress.BeginRestore();
        phaseText.Text=kind=="verify"?"กำลังตรวจคำแปล…":kind=="restore"?"กำลังคืนค่าเดิม…":"กำลังเตรียมคำแปลและฟอนต์…";
        SetStatus("busy",phaseText.Text);UpdateAvailability();
        var dispatch=new LatestProgressDispatcher(p=>ApplyProgress(p,kind,restoreStart));progressDispatch=dispatch;
        Task.Run(()=>{
            Exception failure=null;LauncherSnapshot next=null;try{work(dispatch.Publish);}catch(Exception e){failure=e;}
            try{next=service.Inspect();}catch(Exception e){if(failure==null)failure=e;}
            Post(()=>{
                dispatch.Flush();dispatch.Dispose();if(ReferenceEquals(progressDispatch,dispatch))progressDispatch=null;busy=false;
                if(next!=null)ApplySnapshot(next,false,false);UpdateAvailability();
                if(failure!=null){if(kind=="translate"||kind=="restore"||kind=="verify")operationProgress.StopAtCurrentValue();SetStatus("error",UserError(failure));return;}
                if((kind=="translate"||kind=="verify")&&next!=null&&next.Active){operationProgress.MarkTranslationVerified();SetStatus("success","แปลเสร็จแล้ว");phaseText.Text="ตรวจสอบและติดตั้งคำแปลเรียบร้อยแล้ว";}
                else if(kind=="restore"&&next!=null&&!next.Active&&!next.CanRestore){operationProgress.MarkRestoreVerified();SetStatus("idle","คืนค่าเดิมแล้ว");phaseText.Text="คืนค่าเดิมสำเร็จ";}
                else if(kind=="restore"){operationProgress.StopAtCurrentValue();SetStatus("error","ยังคืนค่าไฟล์เดิมไม่ครบ");}
                else if(kind.StartsWith("font"))SetStatus("font","เลือกฟอนต์แล้ว • กดแปลเกมเมื่อต้องการเริ่ม");
                else if(kind=="root")SetStatus("idle","บันทึกตำแหน่งเกมแล้ว");
                else if(next!=null&&next.Active)SetStatus("success","เปิดคำแปลอยู่");
                else SetStatus("info","ทำรายการเสร็จแล้ว แต่ยังตรวจสอบสถานะคำแปลไม่ได้");
            });
        });
    }
    private void ApplyProgress(OperationProgress p,string kind,float restoreStart) {
        if(p==null||closed)return;string text=String.IsNullOrWhiteSpace(p.Text)?"กำลังทำงาน…":p.Text;
        if(phaseText.Text!=text){phaseText.Text=text;SetStatus("busy",text);}
        if(kind=="verify")return;
        float fraction=p.TotalStages>0?Math.Max(0,Math.Min(1,(float)p.CompletedStages/p.TotalStages)):0;
        if(kind=="translate")operationProgress.ShowTranslationProgress(fraction,p.TotalStages>0&&p.CompletedStages>=0);
        else if(kind=="restore")operationProgress.ShowRestoreProgress(fraction,restoreStart);
    }
    private void SelectFont(FontRole role,ComboBox combo){if(refreshingFonts||busy)return;var c=Selected(combo);if(c==null)return;RunWork("font-select",p=>service.SelectFont(role,c.Id));}
    private void DeleteFont(FontRole role,ComboBox combo){var c=Selected(combo);if(!CanDelete(c))return;RunWork("font-delete",p=>service.DeleteFont(role,c.Id));}
    private void ImportFont(FontRole role) {
        if(busy||snapshot==null||!snapshot.CanSelectFont)return;
        var dialog=new Microsoft.Win32.OpenFileDialog{Title=role==FontRole.Thai?"เพิ่มฟอนต์ภาษาไทย":"Add English font",Filter="TrueType font (*.ttf)|*.ttf",Multiselect=false,CheckFileExists=true};
        if(dialog.ShowDialog(this)==true){string path=dialog.FileName;RunWork("font-import",p=>service.ImportFont(path,role,p));}
    }
    private void BrowseGame() {
        if(busy||snapshot==null||snapshot.Active||snapshot.CanRestore||snapshot.GameRunning)return;
        using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description="เลือกโฟลเดอร์ที่ติดตั้งเกม NTE",ShowNewFolderButton=false,SelectedPath=gamePath.Text}){
            if(dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle))==System.Windows.Forms.DialogResult.OK){string path=dialog.SelectedPath;RunWork("root",p=>service.SetGameRoot(path));}
        }
    }
    private sealed class DialogOwner:System.Windows.Forms.IWin32Window{private readonly IntPtr hwnd;public DialogOwner(IntPtr value){hwnd=value;}public IntPtr Handle{get{return hwnd;}}}
    private void OpenGuide(){try{string path=service.GuidePath;if(!String.IsNullOrWhiteSpace(path)&&File.Exists(path))Process.Start(new ProcessStartInfo{FileName=path,UseShellExecute=true});else SetStatus("info","หลังเล่นปิดโปรแกรมได้โดยคงคำแปลไว้ คืนค่าเดิมเมื่อเปลี่ยนฟอนต์ อัปเดตเกม หรือถอนโปรแกรม");}catch(Exception e){SetStatus("error",UserError(e));}}
    private static string UserError(Exception e){return "ทำรายการไม่สำเร็จ\n"+(e.Message??"กรุณาลองใหม่");}
    private void CheckAppUpdate() {
        if(releases==null||checkingUpdates||busy||closed)return;checkingUpdates=true;updateState.Text="กำลังตรวจหาอัปเดต…";UpdateAvailability();
        Task.Run(()=>{
            ReleaseInfo found=null;Exception failure=null;try{found=releases.Check(lifetime.Token);}catch(Exception e){failure=e;}
            Post(()=>{checkingUpdates=false;if(failure==null){availableRelease=found;updateState.Text=found==null?"โปรแกรมเป็นรุ่นล่าสุดแล้ว":"มีโปรแกรมรุ่น "+found.Version+" พร้อมอัปเดต";}else updateState.Text="ตรวจหาอัปเดตยังไม่สำเร็จ ลองอีกครั้งเมื่อเชื่อมต่ออินเทอร์เน็ต";
                updateNotice.Visibility=availableRelease==null?Visibility.Collapsed:Visibility.Visible;
                updateNoticeText.Text=availableRelease==null?"":"มีอัปเดต "+availableRelease.Version+"  •  ปัจจุบัน "+ReleaseVersion.Current;UpdateAvailability();});
        });
    }
    private void DownloadAppUpdate() {
        if(availableRelease==null||busy||checkingUpdates||installUpdate==null)return;
        ReleaseInfo release=availableRelease;busy=true;updatingApp=true;generation++;download=new CancellationTokenSource();var token=download.Token;
        updateState.Text="กำลังดาวน์โหลดอัปเดต…";UpdateAvailability();
        var dispatcher=new LatestProgressDispatcher(p=>{updateState.Text=p.Text;});progressDispatch=dispatcher;
        Task.Run(()=>{
            string installer=null;Exception failure=null;
            try{installer=releases.Download(release,p=>dispatcher.Publish(new OperationProgress{Text=p.TotalBytes>0?"ดาวน์โหลดอัปเดต "+Math.Min(100,p.BytesReceived*100/p.TotalBytes)+"%":"กำลังดาวน์โหลดอัปเดต…"}),token);}catch(Exception e){failure=e;}
            Post(()=>{
                dispatcher.Flush();dispatcher.Dispose();if(ReferenceEquals(progressDispatch,dispatcher))progressDispatch=null;
                download.Dispose();download=null;
                if(failure!=null){busy=false;updatingApp=false;updateState.Text=failure is OperationCanceledException?"ยกเลิกดาวน์โหลดแล้ว กดอัปเดตเพื่อลองใหม่":"ดาวน์โหลดอัปเดตไม่สำเร็จ กดอัปเดตเพื่อลองใหม่";UpdateAvailability();return;}
                updateState.Text="กำลังเตรียมติดตั้งอัปเดต…";UpdateAvailability();
                Task.Run(()=>{Exception handoffFailure=null;try{installUpdate(installer,release);}catch(Exception e){handoffFailure=e;}
                    Post(()=>{busy=false;updatingApp=false;UpdateAvailability();if(handoffFailure==null)Close();else updateState.Text="เริ่มติดตั้งอัปเดตไม่ได้ กรุณาลองใหม่";});});
            });
        });
    }
}
