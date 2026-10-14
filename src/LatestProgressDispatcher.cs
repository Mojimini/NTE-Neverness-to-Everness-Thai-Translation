using System;
using System.Threading;

// Transfers publish on worker threads. Paint at most the newest report each
// 80ms, and flush the final report before assigning the terminal UI state.
internal sealed class LatestProgressDispatcher : IDisposable {
    private readonly object gate=new object();
    private readonly Action<OperationProgress> apply;
    private readonly System.Windows.Forms.Timer timer;
    private readonly int ownerThread;
    private OperationProgress latest;
    private bool disposed;

    internal LatestProgressDispatcher(Action<OperationProgress> apply) {
        if(apply==null)throw new ArgumentNullException("apply");
        this.apply=apply;ownerThread=Thread.CurrentThread.ManagedThreadId;
        timer=new System.Windows.Forms.Timer{Interval=80};
        timer.Tick+=(s,e)=>Flush();timer.Start();
    }
    internal void Publish(OperationProgress progress) {
        if(progress==null)return;
        lock(gate) {
            if(disposed)return;
            latest=new OperationProgress{Text=progress.Text,CompletedStages=progress.CompletedStages,TotalStages=progress.TotalStages};
        }
    }
    internal void Flush() {
        RequireOwner();
        OperationProgress report;
        lock(gate) {if(disposed)return;report=latest;latest=null;}
        if(report!=null)apply(report);
    }
    public void Dispose() {
        RequireOwner();
        lock(gate){if(disposed)return;disposed=true;latest=null;}
        timer.Stop();timer.Dispose();
    }
    private void RequireOwner(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Progress must be drained on its UI thread.");}
}
