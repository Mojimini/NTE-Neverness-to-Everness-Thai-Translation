using System;
using System.IO;
using System.Security.Cryptography;
internal static class ChainParent {
    static int Main(string[] args) {
        string app=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string installer=Path.Combine(app,"Settings","Downloads","next.exe");
        UpdateHandoff.Start(app,Hash(Path.Combine(app,"ThaiUpdate.exe")),installer,Hash(installer));
        File.WriteAllText(Path.Combine(app,"parent-acknowledged"),"ready");
        return 0;
    }
    static string Hash(string path){using(var f=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
}
