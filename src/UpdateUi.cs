using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Label=System.Windows.Forms.Label;

public sealed partial class ToggleWindow {
    private const string AppVersion=ReleaseVersion.Current;
    private IReleaseDelivery releaseDelivery;
    private Action<string,ReleaseInfo> installUpdate;
    private ReleaseInfo availableRelease;
    private AntdUI.Panel updateNotice;
    private Label updateNoticeText,updateState;
    private AntdUI.Button updateButton,checkUpdatesButton,cancelUpdateButton;
    private TableLayoutPanel updateShell;
    private bool checkingUpdates,downloadingUpdate;
    private string updateMessage="";
    private bool updateError;
    private CancellationTokenSource updateLifetime,updateDownload;
    private System.Windows.Forms.Timer updateTimer;

    public ToggleWindow(string app,ILauncherService service,IReleaseDelivery releases,Action<string,ReleaseInfo> install)
        :this(app,service) {
        if(releases==null||install==null)throw new ArgumentNullException();
        releaseDelivery=releases;installUpdate=install;updateLifetime=new CancellationTokenSource();
        BuildUpdateSurface();
        Shown+=(s,e)=>CheckAppUpdate();
        updateTimer=new System.Windows.Forms.Timer{Interval=60*60*1000};
        updateTimer.Tick+=(s,e)=>CheckAppUpdate();updateTimer.Start();
        FormClosed+=(s,e)=>{
            updateTimer.Stop();updateTimer.Dispose();updateLifetime.Cancel();updateLifetime.Dispose();
            if(updateDownload!=null){updateDownload.Cancel();updateDownload.Dispose();}
        };
    }
    private void BuildUpdateSurface() {
        var brand=FindControl<Label>("BrandTitle");if(brand!=null)brand.Text="NTE  ภาษาไทย  •  v"+AppVersion;
        updateShell=FindControl<TableLayoutPanel>("UiShell");
        updateShell.SuspendLayout();
        updateShell.Controls.Remove(tabs);updateShell.RowCount=3;updateShell.RowStyles.Clear();
        updateShell.RowStyles.Add(new RowStyle(SizeType.Absolute,96));
        updateShell.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        updateShell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        updateNotice=new AntdUI.Panel{Name="UpdateNotice",Dock=DockStyle.Fill,Radius=14,BorderWidth=1,
            Shadow=0,Padding=new Padding(12,6,12,6),Margin=new Padding(6,0,SystemInformation.VerticalScrollBarWidth+8,8),Visible=false};
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.Transparent,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,134));
        updateNoticeText=new Label{Name="UpdateNoticeText",Dock=DockStyle.Fill,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,
            BackColor=Color.Transparent,Margin=Padding.Empty,AccessibleName="แจ้งอัปเดตโปรแกรม"};
        updateButton=NewPrimaryButton("อัปเดต","AppUpdateButton",44);updateButton.Dock=DockStyle.Fill;
        updateButton.Click+=(s,e)=>DownloadAppUpdate();
        row.Controls.Add(updateNoticeText,0,0);row.Controls.Add(updateButton,1,0);updateNotice.Controls.Add(row);
        updateShell.Controls.Add(updateNotice,0,1);updateShell.Controls.Add(tabs,0,2);updateShell.ResumeLayout(true);
        var statusStack=FindControl<SmoothStack>("StatusPageStack");
        var card=NewCard(statusStack,"AppUpdateCard",216);
        var rows=CardRows(card,26,28,48,42,28);
        rows.Controls.Add(Heading("อัปเดตโปรแกรม"),0,0);
        rows.Controls.Add(Body("โปรแกรม "+AppVersion+"  •  ตรวจรุ่นใหม่จาก GitHub Releases"),0,1);
        updateState=new Label{Name="AppUpdateState",Dock=DockStyle.Fill,AutoSize=false,BackColor=Color.Transparent,
            Text="กำลังตรวจหาอัปเดต…",TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,AccessibleName="สถานะการอัปเดตโปรแกรม"};
        rows.Controls.Add(updateState,0,2);
        var actions=NewButtonRow("AppUpdateActions");
        checkUpdatesButton=NewSecondaryButton("ตรวจหาอัปเดต","CheckAppUpdates");checkUpdatesButton.Click+=(s,e)=>CheckAppUpdate();
        cancelUpdateButton=NewSecondaryButton("ยกเลิกดาวน์โหลด","CancelAppUpdate");cancelUpdateButton.Enabled=false;
        cancelUpdateButton.Click+=(s,e)=>{if(updateDownload!=null)updateDownload.Cancel();};
        actions.Controls.Add(checkUpdatesButton,0,0);actions.Controls.Add(cancelUpdateButton,1,0);rows.Controls.Add(actions,0,3);
        rows.Controls.Add(Body("ค่าตั้งและฟอนต์ที่นำเข้าไว้จะเก็บไว้หลังอัปเดต"),0,4);
        ApplyThemeTo(updateNotice);ApplyThemeTo(card);UpdatePaletteApplied();UpdateAvailabilityApplied();
    }
    private void CheckAppUpdate() {
        if(releaseDelivery==null||checkingUpdates||busy||IsDisposed)return;
        checkingUpdates=true;updateError=false;updateMessage="กำลังตรวจหาอัปเดต…";UpdatePaletteApplied();UpdateAvailabilityApplied();
        CancellationToken token=updateLifetime.Token;
        Task.Run(()=>{
            ReleaseInfo release=null;Exception error=null;
            try{release=releaseDelivery.Check(token);}catch(Exception e){error=e;}
            PostToUi(()=>{
                checkingUpdates=false;
                if(error==null){availableRelease=release;updateError=false;updateMessage=release==null?"โปรแกรมเป็นรุ่นล่าสุดแล้ว":"มีโปรแกรมรุ่น "+release.Version+" พร้อมอัปเดต";}
                else {updateError=true;updateMessage="ตรวจหาอัปเดตยังไม่สำเร็จ ลองอีกครั้งเมื่อเชื่อมต่ออินเทอร์เน็ต";}
                ShowUpdateNotice();UpdatePaletteApplied();UpdateAvailabilityApplied();
            });
        });
    }
    private void DownloadAppUpdate() {
        if(availableRelease==null||busy||checkingUpdates||releaseDelivery==null)return;
        ReleaseInfo release=availableRelease;
        busy=true;downloadingUpdate=true;Interlocked.Increment(ref refreshGeneration);
        updateDownload=new CancellationTokenSource();CancellationToken token=updateDownload.Token;
        updateError=false;updateMessage="กำลังดาวน์โหลดอัปเดต…";UpdatePaletteApplied();UpdateAvailability();
        Task.Run(()=>{
            string installer=null;Exception error=null;
            try{installer=releaseDelivery.Download(release,p=>PostToUi(()=>{
                updateMessage=p.TotalBytes>0?"ดาวน์โหลดอัปเดต "+Math.Min(100,p.BytesReceived*100/p.TotalBytes)+"%":"กำลังดาวน์โหลดอัปเดต…";
                UpdatePaletteApplied();
            }),token);}catch(Exception e){error=e;}
            PostToUi(()=>{
                downloadingUpdate=false;updateDownload.Dispose();updateDownload=null;
                if(error!=null){busy=false;UpdateAvailability();updateError=!(error is OperationCanceledException);updateMessage=error is OperationCanceledException?"ยกเลิกดาวน์โหลดแล้ว กดอัปเดตเพื่อลองใหม่":"ดาวน์โหลดอัปเดตไม่สำเร็จ กดอัปเดตเพื่อลองใหม่";UpdatePaletteApplied();return;}
                updateMessage="กำลังเตรียมติดตั้งอัปเดต…";UpdatePaletteApplied();UpdateAvailability();
                Task.Run(()=>{
                    Exception handoffError=null;try{installUpdate(installer,release);}catch(Exception e){handoffError=e;}
                    PostToUi(()=>{
                        busy=false;UpdateAvailability();
                        if(handoffError==null)Close();
                        else{updateError=true;updateMessage="เริ่มติดตั้งอัปเดตไม่ได้ กรุณาลองใหม่";UpdatePaletteApplied();}
                    });
                });
            });
        });
    }
    private void ShowUpdateNotice() {
        bool show=availableRelease!=null;
        updateNotice.Visible=show;updateShell.RowStyles[1].Height=show?68:0;
        updateNoticeText.Text=show?"มีอัปเดต "+availableRelease.Version+"  •  ปัจจุบัน "+AppVersion:"";
    }
    partial void UpdatePaletteApplied() {
        if(updateNotice==null)return;
        updateNotice.Back=palette.StatusInfo;updateNotice.BackColor=Color.Transparent;updateNotice.BorderColor=palette.Border;
        updateNoticeText.ForeColor=palette.StatusInfoText;
        updateState.Text=updateMessage;updateState.ForeColor=updateError?palette.StatusErrorText:downloadingUpdate?palette.StatusBusyText:palette.Muted;
    }
    partial void UpdateAvailabilityApplied() {
        if(updateButton==null)return;
        updateButton.Enabled=availableRelease!=null&&!busy&&!checkingUpdates;updateButton.Loading=downloadingUpdate;
        checkUpdatesButton.Enabled=!busy&&!checkingUpdates;checkUpdatesButton.Loading=checkingUpdates;
        cancelUpdateButton.Enabled=downloadingUpdate;
        // Downloading an app update does not animate the game's install action.
        if(downloadingUpdate&&translateButton!=null)translateButton.Loading=false;
    }
}
