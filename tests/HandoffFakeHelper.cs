using System;
using System.IO;
using System.Threading;
internal static class HandoffFakeHelper {
    static int Main(string[] args) {
        string app=null,ready=null,pid=null;
        for(int i=0;i<args.Length;i+=2){if(args[i]=="--app-dir")app=args[i+1];if(args[i]=="--ready-file")ready=args[i+1];if(args[i]=="--parent")pid=args[i+1];}
        if(File.Exists(Path.Combine(app,"fail-helper")))return 2;
        Thread.Sleep(File.Exists(Path.Combine(app,"slow-helper"))?2000:300);File.WriteAllText(ready,pid);return 0;
    }
}
