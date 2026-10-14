using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class ProgressDispatchTests
{
    private static int assertions;

    [STAThread]
    private static int Main()
    {
        try {
            BurstPublishesAreCoalescedAndFinalValueIsRetained();
            ContinuousProducerStaysWithinBoundedUiRate();
            PublishSnapshotsMutableProgressObjects();
            FlushOrdersPendingProgressBeforeTerminalResult();
            DisposeDropsPendingProgress();
            Console.WriteLine("PROGRESS_DISPATCH_PASS assertions=" + assertions);
            return 0;
        }
        catch (Exception ex) {
            Console.Error.WriteLine("PROGRESS_DISPATCH_FAIL " + ex);
            return 1;
        }
    }

    private static void ContinuousProducerStaysWithinBoundedUiRate()
    {
        int uiThread = Thread.CurrentThread.ManagedThreadId;
        int applied = 0;
        int wrongThread = 0;
        int appliedWhileProducing = 0;
        int producerRunning = 1;
        int published = -1;
        string lastText = null;
        using (var dispatcher = new LatestProgressDispatcher(progress => {
            Interlocked.Increment(ref applied);
            if (Thread.CurrentThread.ManagedThreadId != uiThread) Interlocked.Exchange(ref wrongThread, 1);
            if (Volatile.Read(ref producerRunning) != 0) Interlocked.Increment(ref appliedWhileProducing);
            lastText = progress.Text;
        })) {
            using (var stop = new CancellationTokenSource()) {
                Task producer = Task.Run((Action)(() => {
                    int sequence = 0;
                    while (!stop.IsCancellationRequested) {
                        dispatcher.Publish(new OperationProgress {
                            Text = "live-" + sequence, CompletedStages = sequence, TotalStages = 6000
                        });
                        Interlocked.Exchange(ref published, sequence++);
                    }
                    Volatile.Write(ref producerRunning, 0);
                }));
                PumpFor(400);
                stop.Cancel();
                producer.GetAwaiter().GetResult();
            }
            int beforeFlush = applied;
            dispatcher.Flush();
            Assert(appliedWhileProducing > 0, "UI drains progress while the worker is still publishing");
            Assert(applied <= 7 && beforeFlush <= 7, "continuous progress stays within the 80 ms UI drain cadence");
            Assert(wrongThread == 0, "continuous progress drains only on the UI thread");
            Assert(lastText == "live-" + Volatile.Read(ref published), "final continuous report survives coalescing");
        }
    }

    private static void PublishSnapshotsMutableProgressObjects()
    {
        OperationProgress received = null;
        using (var dispatcher = new LatestProgressDispatcher(progress => received = progress)) {
            var source = new OperationProgress { Text = "captured phase", CompletedStages = 2, TotalStages = 4 };
            dispatcher.Publish(source);
            source.Text = "mutated after publish";
            source.CompletedStages = 4;
            source.TotalStages = 4;
            dispatcher.Flush();
            Assert(received != null && received.Text == "captured phase"
                && received.CompletedStages == 2 && received.TotalStages == 4,
                "publisher mutations cannot change the queued progress snapshot");
        }
    }

    private static void BurstPublishesAreCoalescedAndFinalValueIsRetained()
    {
        int uiThread = Thread.CurrentThread.ManagedThreadId;
        int applied = 0;
        bool wrongThread = false;
        string lastText = null;
        using (var dispatcher = new LatestProgressDispatcher(progress => {
            Interlocked.Increment(ref applied);
            if (Thread.CurrentThread.ManagedThreadId != uiThread) wrongThread = true;
            lastText = progress.Text;
        })) {
            Task producer = Task.Run((Action)(() => {
                for (int i = 0; i < 6000; i++)
                    dispatcher.Publish(new OperationProgress { Text = "progress-" + i, CompletedStages = 0, TotalStages = 0 });
            }));
            producer.GetAwaiter().GetResult();
            PumpFor(260);
            dispatcher.Flush();
            Assert(!wrongThread, "dispatcher applies progress on its creating UI thread");
            Assert(applied <= 5, "6000 rapid reports produce at most five UI applications in 260 ms");
            Assert(lastText == "progress-5999", "coalescing retains the newest progress report for terminal flush");
        }
    }

    private static void FlushOrdersPendingProgressBeforeTerminalResult()
    {
        foreach (string terminal in new[] { "success", "error" }) {
            var order = new List<string>();
            using (var dispatcher = new LatestProgressDispatcher(progress => order.Add(progress.Text))) {
                dispatcher.Publish(new OperationProgress { Text = "final phase: " + terminal, CompletedStages = 0, TotalStages = 0 });
                dispatcher.Flush();
                order.Add("terminal: " + terminal);
                Assert(order.Count == 2 && order[0] == "final phase: " + terminal && order[1] == "terminal: " + terminal,
                    "pending progress flushes before " + terminal + " terminal state");
            }
        }
    }

    private static void DisposeDropsPendingProgress()
    {
        int applied = 0;
        var dispatcher = new LatestProgressDispatcher(progress => Interlocked.Increment(ref applied));
        dispatcher.Publish(new OperationProgress { Text = "must not apply after dispose", CompletedStages = 0, TotalStages = 0 });
        dispatcher.Dispose();
        PumpFor(120);
        Assert(applied == 0, "disposed dispatcher drops buffered progress without late UI callbacks");
    }

    private static void PumpFor(int milliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Application.DoEvents();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT: " + message);
        assertions++;
        Console.WriteLine("PASS " + message);
    }
}
