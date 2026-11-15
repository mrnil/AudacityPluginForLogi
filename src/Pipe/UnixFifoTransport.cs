namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>/tmp/audacity_script_pipe.to.&lt;uid&gt; and .from.&lt;uid&gt; (FIFOs created by mod-script-pipe).</summary>
    internal sealed class UnixFifoTransport : IPipeTransport
    {
        [DllImport("libc", SetLastError = true)]
        private static extern UInt32 getuid();

        private FileStream _to;
        private FileStream _from;

        public Stream ToAudacity => this._to;
        public Stream FromAudacity => this._from;
        public String CommandTerminator => "\n";

        public async Task OpenAsync(TimeSpan timeout, CancellationToken ct)
        {
            var uid = getuid();
            var toPath = $"/tmp/audacity_script_pipe.to.{uid}";
            var fromPath = $"/tmp/audacity_script_pipe.from.{uid}";

            if (!File.Exists(toPath) || !File.Exists(fromPath))
            {
                throw new IOException("Audacity script pipes not found (Audacity not running, or mod-script-pipe disabled).");
            }

            // open(2) on a FIFO blocks until the other end is opened. If the FIFO files are stale
            // (Audacity quit without removing them) this would hang forever, so run it on the pool
            // with a timeout. A timed-out open leaks one blocked pool thread until Audacity next
            // starts; acceptable given the 2 s reconnect cadence is gated on the previous attempt.
            var open = Task.Run(() =>
            {
                this._to = new FileStream(toPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1, FileOptions.None);
                this._from = new FileStream(fromPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None);
            }, ct);

            var winner = await Task.WhenAny(open, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (winner != open)
            {
                throw new TimeoutException("Timed out opening Audacity script pipes.");
            }
            await open.ConfigureAwait(false);
        }

        public void Dispose()
        {
            this._to?.Dispose();
            this._from?.Dispose();
        }
    }
}
