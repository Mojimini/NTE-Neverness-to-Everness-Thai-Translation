using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

internal static class UpdateHandoff {
    internal static void Start(string app, string helperHash, string installer, string installerHash, int timeoutMs=10000) {
        app=Path.GetFullPath(app).TrimEnd('\\','/');
        string helper=Path.Combine(app,"ThaiUpdate.exe");
        Safe(helper);Safe(installer);
        if(!Hash(helper).Equals(helperHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("เครื่องมืออัปเดตไม่ตรงกับรุ่นโปรแกรม กรุณาติดตั้งโปรแกรมใหม่");
        if(!Hash(installer).Equals(installerHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ตัวติดตั้งอัปเดตไม่ตรงกับไฟล์ที่ตรวจแล้ว");
        string downloads=Path.Combine(app,"Settings","Downloads");Safe(downloads);
        string ready=Path.Combine(downloads,Guid.NewGuid().ToString("N")+".ready");
        int pid=Process.GetCurrentProcess().Id;
        using(var child=Process.Start(new ProcessStartInfo{FileName=helper,WorkingDirectory=app,
            Arguments="--parent "+pid+" --installer "+Quote(installer)+" --sha256 "+Quote(installerHash)+" --app-dir "+Quote(app)+" --ready-file "+Quote(ready),
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
            bool handedOff=false;
            try {
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<timeoutMs) {
                if(File.Exists(ready)) {
                    Safe(ready);
                    if(File.ReadAllText(ready).Trim()!=pid.ToString())throw new InvalidOperationException("เครื่องมืออัปเดตตอบกลับไม่ถูกต้อง");
                    // The helper owns the acknowledgement and removes it after this process exits.
                    handedOff=true;return;
                }
                if(child.HasExited)throw new InvalidOperationException("เครื่องมืออัปเดตเริ่มทำงานไม่ได้ กรุณาลองใหม่");
                Thread.Sleep(25);
            }
            throw new TimeoutException("เครื่องมืออัปเดตยังไม่พร้อม โปรแกรมยังคงเปิดอยู่ กรุณาลองใหม่");
            } finally {
                if(!handedOff) {
                    // This is the verified child we just created; cancel a failed handoff
                    // so closing the app later cannot trigger an unannounced installation.
                    if(!child.HasExited){child.Kill();child.WaitForExit(2000);}
                    foreach(string file in Directory.GetFiles(downloads,Path.GetFileName(ready)+"*",SearchOption.TopDirectoryOnly)) {
                        if((file==ready||file.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Delete(file);
                    }
                }
            }
        }
    }
    private static string Quote(string value){if(value==null||value.Contains("\""))throw new InvalidOperationException("ตำแหน่งไฟล์ไม่ถูกต้อง");return "\""+value+"\"";}
    private static string Hash(string file){using(var f=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    private static void Safe(string path){for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("ตำแหน่งโปรแกรมมีโฟลเดอร์เชื่อมไปที่อื่น");}
}
