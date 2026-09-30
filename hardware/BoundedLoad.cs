using System;
using System.Threading;

namespace GalaxyHardware
{
    // Short, low-priority CPU-only workload. No GPU, disk or network activity.
    sealed class BoundedLoad : IDisposable
    {
        volatile bool stopped;
        readonly Thread[] workers;
        readonly DateTime deadline = DateTime.UtcNow.AddSeconds(45);
        double sink;
        public BoundedLoad()
        {
            workers = new Thread[Math.Min(8, Environment.ProcessorCount)];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(delegate()
                {
                    double value = 1.234567;
                    while (!stopped && DateTime.UtcNow < deadline)
                    {
                        for (int j = 0; j < 10000; j++) value = Math.Sqrt(value + 1234.56789);
                        Interlocked.Exchange(ref sink, value);
                    }
                });
                workers[i].IsBackground = true; workers[i].Priority = ThreadPriority.BelowNormal; workers[i].Start();
            }
        }
        public void Dispose() { stopped = true; foreach (Thread worker in workers) worker.Join(2000); }
    }
}
