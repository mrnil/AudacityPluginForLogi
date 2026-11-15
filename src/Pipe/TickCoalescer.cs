namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Accumulates dial ticks and flushes them at most once per interval, waiting for the
    /// previous flush to finish, so a fast spin can't flood the pipe and lag behind the hand.
    /// </summary>
    internal sealed class TickCoalescer
    {
        private readonly Func<Int32, Task> _flush;
        private readonly TimeSpan _interval;
        private Int32 _pending;
        private Int32 _running;

        public TickCoalescer(Func<Int32, Task> flush, Int32 intervalMs = 30)
        {
            this._flush = flush;
            this._interval = TimeSpan.FromMilliseconds(intervalMs);
        }

        public void Add(Int32 ticks)
        {
            Interlocked.Add(ref this._pending, ticks);
            if (Interlocked.Exchange(ref this._running, 1) == 0)
            {
                _ = Task.Run(this.DrainAsync);
            }
        }

        private async Task DrainAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(this._interval).ConfigureAwait(false);
                    var ticks = Interlocked.Exchange(ref this._pending, 0);
                    if (ticks == 0)
                    {
                        break;
                    }
                    try
                    {
                        await this._flush(ticks).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning($"Dial flush failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                Volatile.Write(ref this._running, 0);
                // Ticks that arrived between the last read and clearing _running.
                if (Volatile.Read(ref this._pending) != 0 && Interlocked.Exchange(ref this._running, 1) == 0)
                {
                    _ = Task.Run(this.DrainAsync);
                }
            }
        }
    }
}
