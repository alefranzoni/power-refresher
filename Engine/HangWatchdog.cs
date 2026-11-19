using System;
using System.Diagnostics;

using System.Threading;

namespace PowerRefresher.Engine
{
    /// <summary>
    /// Watches the Power BI instance a <see cref="PbiRefresher"/> is driving, on a background thread.
    /// Trips when Power BI's window stops responding to Windows for longer than the hang timeout, or when the
    /// whole run exceeds its maximum runtime. On tripping it kills that Power BI process, which also unblocks any
    /// UI Automation call stuck waiting on it. If the automation thread still hasn't returned a minute later,
    /// <c>onStuck</c> is called so the host can exit instead of hanging forever.
    /// </summary>
    public sealed class HangWatchdog : IDisposable
    {
        private const int POLL_MS = 2000;
        private static readonly TimeSpan StuckGrace = TimeSpan.FromSeconds(60);

        private readonly PbiRefresher refresher;
        private readonly TimeSpan hangTimeout;
        private readonly TimeSpan maxRuntime;
        private readonly Stopwatch runClock;
        private readonly Action<LogLevel, string> log;
        private readonly Action onStuck;
        private readonly Thread thread;
        private volatile bool disposed;

        /// <param name="hangTimeout">How long the window may be unresponsive before Power BI is killed. Zero disables hang detection.</param>
        /// <param name="maxRuntime">Limit for the whole run, measured by <paramref name="runClock"/>. Zero disables it.</param>
        /// <param name="runClock">Clock started when the overall run began (shared across retries).</param>
        /// <param name="log">Must be thread-safe: called from the watchdog thread.</param>
        public HangWatchdog(PbiRefresher refresher, TimeSpan hangTimeout, TimeSpan maxRuntime, Stopwatch runClock, Action<LogLevel, string> log, Action onStuck)
        {
            this.refresher = refresher;
            this.hangTimeout = hangTimeout;
            this.maxRuntime = maxRuntime;
            this.runClock = runClock;
            this.log = log;
            this.onStuck = onStuck;
            thread = new Thread(Watch) { IsBackground = true, Name = "PowerRefresher watchdog" };
            thread.Start();
        }

        /// <summary>True once the watchdog has killed Power BI. <see cref="Reason"/> and <see cref="Description"/> are set before this turns true.</summary>
        public bool Tripped => Volatile.Read(ref trippedFlag) == 1;
        private int trippedFlag;

        /// <summary>Why the watchdog tripped. Only meaningful when <see cref="Tripped"/> is true.</summary>
        public HangReason Reason { get; private set; }

        /// <summary>Human readable description of what happened, for the final log line.</summary>
        public string Description { get; private set; }

        private void Watch()
        {
            Stopwatch unresponsive = null;
            TimeSpan nextWarning = TimeSpan.Zero;

            while (!disposed)
            {
                Thread.Sleep(POLL_MS);
                if (disposed) return;

                if (maxRuntime > TimeSpan.Zero && runClock.Elapsed > maxRuntime)
                {
                    Trip(HangReason.MaxRuntime, $"Run exceeded the maximum runtime of {LogFormat.Duration(maxRuntime)} during step '{refresher.CurrentStep}'");
                    return;
                }

                if (hangTimeout <= TimeSpan.Zero) continue;
                IntPtr hwnd = refresher.WindowHandle;
                bool hung = hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd) && !NativeMethods.Responds(hwnd);

                if (hung)
                {
                    if (unresponsive == null)
                    {
                        unresponsive = Stopwatch.StartNew();
                        nextWarning = TimeSpan.FromSeconds(30);
                    }
                    if (unresponsive.Elapsed >= nextWarning)
                    {
                        log(LogLevel.Warn, $"Power BI is not responding ({LogFormat.Duration(unresponsive.Elapsed)} so far, step '{refresher.CurrentStep}'); " +
                            $"will kill it after {LogFormat.Duration(hangTimeout)}");
                        nextWarning += TimeSpan.FromSeconds(60);
                    }
                    if (unresponsive.Elapsed >= hangTimeout)
                    {
                        Trip(HangReason.Hung, $"Power BI stopped responding for {LogFormat.Duration(unresponsive.Elapsed)} during step '{refresher.CurrentStep}'");
                        return;
                    }
                }
                else if (unresponsive != null)
                {
                    log(LogLevel.Info, $"Power BI is responding again after {LogFormat.Duration(unresponsive.Elapsed)}");
                    unresponsive = null;
                }
            }
        }

        private void Trip(HangReason why, string description)
        {
            Description = description;
            Reason = why;
            Volatile.Write(ref trippedFlag, 1);
            log(LogLevel.Error, description);
            refresher.KillPowerBI(why == HangReason.Hung ? "not responding" : "maximum runtime exceeded");

            //Killing Power BI should make the automation thread's pending call fail and return.
            //If it doesn't within the grace period, let the host bail out.
            Stopwatch sinceKill = Stopwatch.StartNew();
            while (!disposed && sinceKill.Elapsed < StuckGrace) Thread.Sleep(500);
            if (!disposed)
            {
                log(LogLevel.Error, $"Automation did not stop {LogFormat.Duration(StuckGrace)} after Power BI was killed; giving up");
                onStuck?.Invoke();
            }
        }

        public void Dispose()
        {
            disposed = true;
        }

    }

    public enum HangReason
    {
        /// <summary>Power BI's window stopped responding for longer than the hang timeout.</summary>
        Hung,
        /// <summary>The whole run took longer than the maximum runtime.</summary>
        MaxRuntime
    }
}
