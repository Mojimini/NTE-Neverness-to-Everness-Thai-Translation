using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Program {
    [STAThread] static void Main() {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        string app=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\','/');
        try {
            Safe(app);var json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024};
            var config=json.Deserialize<DistributionConfig>(File.ReadAllText(Path.Combine(app,"release-config.json"),Encoding.UTF8));
            var catalog=json.Deserialize<ContentCatalog>(File.ReadAllText(Path.Combine(app,"content-catalog.json"),Encoding.UTF8));
            config.CurrentVersion=ReleaseVersion.Current;
            var client=new GitHubDeliveryClient(app,config,catalog);
            var launcher=new DeliveredLauncherService(app,new LauncherService(app,GameRunning),client);
            var window=new WpfLauncherWindow(app,launcher,client,(installer,release)=>{
                UpdateHandoff.Start(app,config.UpdaterSha256,installer,release.InstallerSha256);
            });
            new System.Windows.Application().Run(window);
        } catch(Exception e){MessageBox.Show(e.Message,"เปิดคำแปล NTE",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    static string Quote(string value){if(value==null||value.Contains("\""))throw new InvalidOperationException("ตำแหน่งไฟล์ไม่ถูกต้อง");return "\""+value+"\"";}
    static string Hash(string file){using(var f=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static void Safe(string path){for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("ตำแหน่งโปรแกรมมีโฟลเดอร์เชื่อมไปที่อื่น");}
    internal static bool IsGameProcess(string name){
        if(String.IsNullOrWhiteSpace(name))return false;
        if(name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-4);
        if(name.Equals("NTEGlobalGame",StringComparison.OrdinalIgnoreCase)||name.Equals("NTEGlobalBrowser",StringComparison.OrdinalIgnoreCase)||name.Equals("NTEGlobalWebBooster",StringComparison.OrdinalIgnoreCase))return false;
        return name.StartsWith("HTGame",StringComparison.OrdinalIgnoreCase)||name.StartsWith("HT-Win64-Shipping",StringComparison.OrdinalIgnoreCase)||name.StartsWith("NevernessToEverness",StringComparison.OrdinalIgnoreCase)||name.StartsWith("NTE",StringComparison.OrdinalIgnoreCase);
    }
    internal static bool GameRunning(){bool found=false;foreach(var p in Process.GetProcesses())using(p){try{if(IsGameProcess(p.ProcessName))found=true;}catch(InvalidOperationException){}}return found;}
}
