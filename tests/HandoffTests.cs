using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
internal static class HandoffTests {
    static int Main() {
        string root=Path.Combine(Path.GetTempPath(),"nte-handoff-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"Settings","Downloads"));
        string helper=Path.Combine(root,"ThaiUpdate.exe"),installer=Path.Combine(root,"Settings","Downloads","setup.exe");
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"HandoffFakeHelper.exe"),helper);File.WriteAllText(installer,"fixture");
        var clock=Stopwatch.StartNew();UpdateHandoff.Start(root,Hash(helper),installer,Hash(installer));
        Check(clock.ElapsedMilliseconds>=300,"handoff does not permit closing before helper acknowledgement");
        File.WriteAllText(Path.Combine(root,"fail-helper"),"");
        Throws(()=>UpdateHandoff.Start(root,Hash(helper),installer,Hash(installer)),"helper rejection returns error before parent can close");
        Throws(()=>UpdateHandoff.Start(root,new string('0',64),installer,Hash(installer)),"unverified helper cannot run");
        Throws(()=>UpdateHandoff.Start(root,Hash(helper),installer,new string('0',64)),"unverified installer cannot run");
        File.Delete(Path.Combine(root,"fail-helper"));
        foreach(string file in Directory.GetFiles(Path.Combine(root,"Settings","Downloads"),"*.ready"))File.Delete(file);
        File.WriteAllText(Path.Combine(root,"slow-helper"),"");
        try{UpdateHandoff.Start(root,Hash(helper),installer,Hash(installer),150);throw new Exception("must time out");}catch(TimeoutException){}
        System.Threading.Thread.Sleep(2100);
        Check(Directory.GetFiles(Path.Combine(root,"Settings","Downloads"),"*.ready").Length==0,"timeout cancels its owned helper so no late acknowledgement or later install remains");
        Directory.Delete(root,true);Console.WriteLine("HANDOFF_PASS 5");return 0;
    }
    static string Hash(string p){using(var s=File.OpenRead(p))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static void Check(bool b,string m){if(!b)throw new Exception(m);Console.WriteLine("PASS "+m);}
    static void Throws(Action a,string m){try{a();}catch(InvalidOperationException){Console.WriteLine("PASS "+m);return;}throw new Exception(m);}
}
