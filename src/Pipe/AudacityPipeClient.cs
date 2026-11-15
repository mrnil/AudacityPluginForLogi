namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    internal enum TransportState { Stopped, Playing, Recording, Paused }

    internal sealed record PipeResult(Boolean Ok, String Output);

    /// <summary>
    /// Single long-lived mod-script-pipe connection. All commands go through one queue so
    /// request/response pairs never interleave. Reconnects lazily when Audacity (re)starts.
    /// </summary>
    internal sealed class AudacityPipeClient : IDisposable
    {
        private static readonly TimeSpan OpenTimeout = TimeSpan.FromMilliseconds(750);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly Channel<(String Command, TaskCompletionSource<PipeResult> Tcs)> _queue =
            Channel.CreateUnbounded<(String, TaskCompletionSource<PipeResult>)>(new UnboundedChannelOptions { SingleReader = true });

        private readonly CancellationTokenSource _cts = new();
        private IPipeTransport _transport;
        private StreamWriter _writer;
        private StreamReader _reader;
        private Task _worker;
        private Timer _poll;
        private Int32 _connecting;
        private Boolean _connected;
        private TransportState _transportState = TransportState.Stopped;

        /// <summary>Raised (on a pool thread) when Connected or Transport changes.</summary>
        public event EventHandler StateChanged;

        public Boolean Connected => Volatile.Read(ref this._connected);

        /// <summary>
        /// Optimistic: mod-script-pipe has no transport-state query or push events, so this only
        /// reflects commands sent from the plugin. Pressing Record in Audacity's UI won't update it.
        /// </summary>
        public TransportState Transport
        {
            get => this._transportState;
            set
            {
                if (this._transportState != value)
                {
                    this._transportState = value;
                    this.StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void Start()
        {
            this._worker = Task.Run(() => this.WorkerLoopAsync(this._cts.Token));
            this._poll = new Timer(_ => this.Poll(), null, TimeSpan.Zero, PollInterval);
        }

        /// <summary>Queue a command; never blocks the caller. Fails fast when disconnected.</summary>
        public Task<PipeResult> SendAsync(String command)
        {
            var tcs = new TaskCompletionSource<PipeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!this.Connected)
            {
                tcs.SetResult(new PipeResult(false, "Not connected to Audacity"));
                return tcs.Task;
            }
            this._queue.Writer.TryWrite((command, tcs));
            return tcs.Task;
        }

        /// <summary>Fire-and-forget helper for button presses; logs failures.</summary>
        public void Send(String command) => _ = this.SendAsync(command).ContinueWith(t =>
        {
            if (!t.Result.Ok)
            {
                PluginLog.Warning($"Audacity command '{command}' failed: {t.Result.Output}");
            }
        }, TaskScheduler.Default);

        private void Poll()
        {
            if (this.Connected)
            {
                // Cheap liveness probe; a broken pipe surfaces as an IOException in the worker.
                _ = this.SendAsync("Message: Text=\"\"");
                return;
            }
            if (Interlocked.Exchange(ref this._connecting, 1) == 1)
            {
                return; // previous attempt still in flight (e.g. a blocked FIFO open on macOS)
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.ConnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref this._connecting, 0);
                }
            });
        }

        private async Task ConnectAsync()
        {
            var transport = IPipeTransport.Create();
            try
            {
                await transport.OpenAsync(OpenTimeout, this._cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
            {
                transport.Dispose();
                return; // Audacity not running / module disabled; try again next poll
            }

            this._transport = transport;
            this._writer = new StreamWriter(transport.ToAudacity, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            this._reader = new StreamReader(transport.FromAudacity, new UTF8Encoding(false));
            this.SetConnected(true);
            PluginLog.Info("Connected to Audacity mod-script-pipe");
        }

        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            await foreach (var (command, tcs) in this._queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!this.Connected)
                {
                    tcs.TrySetResult(new PipeResult(false, "Not connected to Audacity"));
                    continue;
                }
                try
                {
                    await this._writer.WriteAsync(command + this._transport.CommandTerminator).ConfigureAwait(false);
                    tcs.TrySetResult(await PipeResponseReader.ReadAsync(this._reader, ct).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or EndOfStreamException)
                {
                    tcs.TrySetResult(new PipeResult(false, ex.Message));
                    this.Disconnect();
                }
            }
        }

        private void Disconnect()
        {
            this._writer = null;
            this._reader = null;
            this._transport?.Dispose();
            this._transport = null;
            this._transportState = TransportState.Stopped;
            this.SetConnected(false);
            PluginLog.Info("Disconnected from Audacity");
        }

        private void SetConnected(Boolean value)
        {
            if (this.Connected != value)
            {
                Volatile.Write(ref this._connected, value);
                this.StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            this._poll?.Dispose();
            this._cts.Cancel();
            this._queue.Writer.TryComplete();
            this._transport?.Dispose();
        }
    }
}
