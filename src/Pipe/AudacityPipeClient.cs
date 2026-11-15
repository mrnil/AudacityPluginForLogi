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
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(10);

        // Bounds how long Dispose (plugin unload) waits for the worker to stop.
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(1);
        private const String ShuttingDown = "Plugin is shutting down";

        private readonly Func<IPipeTransport> _createTransport;
        private readonly Func<Boolean> _isAudacityRunning;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _responseTimeout;

        // Bounded so a blocked Audacity (e.g. a modal dialog) can't build up a backlog that all
        // fires at once when it unblocks.
        private const Int32 MaxQueuedCommands = 32;

        private readonly Channel<(String Command, TaskCompletionSource<PipeResult> Tcs)> _queue =
            Channel.CreateBounded<(String, TaskCompletionSource<PipeResult>)>(
                new BoundedChannelOptions(MaxQueuedCommands) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

        private readonly CancellationTokenSource _cts = new();

        // Guards _transport/_writer/_reader: Disconnect can now run on the poll timer as well as the worker.
        private readonly Lock _gate = new();
        private IPipeTransport _transport;
        private StreamWriter _writer;
        private StreamReader _reader;
        private Task _worker;
        private Timer _poll;
        private Int32 _connecting;
        private Int32 _disposed;
        private Boolean _connected;
        private TransportState _transportState = TransportState.Stopped;

        /// <param name="isAudacityRunning">Process check used to notice Audacity exiting without sending it anything.</param>
        public AudacityPipeClient(Func<Boolean> isAudacityRunning)
            : this(IPipeTransport.Create, isAudacityRunning, DefaultPollInterval, DefaultResponseTimeout)
        {
        }

        // Tests pass a fake transport and their own timings.
        internal AudacityPipeClient(Func<IPipeTransport> createTransport, Func<Boolean> isAudacityRunning, TimeSpan pollInterval, TimeSpan responseTimeout)
        {
            this._createTransport = createTransport;
            this._isAudacityRunning = isAudacityRunning;
            this._pollInterval = pollInterval;
            this._responseTimeout = responseTimeout;
        }

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
            this._poll = new Timer(_ => this.Poll(), null, TimeSpan.Zero, this._pollInterval);
        }

        /// <summary>Queue a command; never blocks the caller. Fails fast when disconnected or the queue is full.</summary>
        public Task<PipeResult> SendAsync(String command)
        {
            var tcs = new TaskCompletionSource<PipeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!this.Connected)
            {
                tcs.SetResult(new PipeResult(false, "Not connected to Audacity"));
            }
            else if (!this._queue.Writer.TryWrite((command, tcs)))
            {
                tcs.SetResult(new PipeResult(false, "Audacity is busy; command dropped"));
            }
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
                // Never probe the pipe: every scripting command, even a no-op, runs on Audacity's
                // UI thread and closes any open menu. Check the process instead; a pipe broken
                // some other way surfaces as an IOException on the next real command.
                if (!this._isAudacityRunning())
                {
                    PluginLog.Info("Audacity has exited");
                    this.Disconnect();
                }
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
            var transport = this._createTransport();
            try
            {
                await transport.OpenAsync(OpenTimeout, this._cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
            {
                transport.Dispose();
                return; // Audacity not running / module disabled; try again next poll
            }

            lock (this._gate)
            {
                // A poll already running when Dispose was called must not leave a live connection behind.
                if (Volatile.Read(ref this._disposed) == 1)
                {
                    transport.Dispose();
                    return;
                }
                this._transport = transport;
                this._writer = new StreamWriter(transport.ToAudacity, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                this._reader = new StreamReader(transport.FromAudacity, new UTF8Encoding(false));
            }
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
                    tcs.TrySetResult(await this.ExchangeAsync(command, ct).WaitAsync(this._responseTimeout, ct).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Dispose: fail this command; Dispose fails anything still queued.
                    tcs.TrySetResult(new PipeResult(false, ShuttingDown));
                    return;
                }
                catch (TimeoutException)
                {
                    // A late response would be read as the reply to the next command, so the
                    // connection can't be reused. Drop it; the poll reconnects.
                    var message = FormattableString.Invariant($"Audacity did not respond within {this._responseTimeout.TotalSeconds:0.#} s");
                    tcs.TrySetResult(new PipeResult(false, message));
                    PluginLog.Warning($"{message} to '{command}'; reconnecting");
                    this.Disconnect();
                }
                // A disconnect from the poll disposes the streams under an in-flight exchange; an
                // aborted pipe read may surface as OperationCanceledException rather than IOException.
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or EndOfStreamException
                    || (ex is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    tcs.TrySetResult(new PipeResult(false, ex.Message));
                    this.Disconnect();
                }
            }
        }

        private async Task<PipeResult> ExchangeAsync(String command, CancellationToken ct)
        {
            StreamWriter writer;
            StreamReader reader;
            String terminator;
            lock (this._gate)
            {
                writer = this._writer;
                reader = this._reader;
                terminator = this._transport?.CommandTerminator;
            }
            if (writer == null)
            {
                return new PipeResult(false, "Not connected to Audacity");
            }
            await writer.WriteAsync(command + terminator).ConfigureAwait(false);
            return await PipeResponseReader.ReadAsync(reader, ct).ConfigureAwait(false);
        }

        // Safe to call from the worker and the poll timer at once; only the first call acts.
        private void Disconnect()
        {
            IPipeTransport transport;
            lock (this._gate)
            {
                transport = this._transport;
                this._transport = null;
                this._writer = null;
                this._reader = null;
            }
            if (transport == null)
            {
                return;
            }
            transport.Dispose();
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

        // Called on plugin unload. Leaves nothing pending: every queued command completes (failed),
        // the worker has stopped (or the bounded wait expired), and no connection survives.
        // _cts is deliberately not disposed: a timer callback already in progress may still read its token.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            {
                return;
            }
            this._poll?.Dispose();
            this._queue.Writer.TryComplete();
            this._cts.Cancel();
            this.Disconnect();

            try
            {
                this._worker?.Wait(ShutdownTimeout);
            }
            catch (AggregateException)
            {
                // The worker ends with OperationCanceledException when cancelled while idle.
            }

            while (this._queue.Reader.TryRead(out var item))
            {
                item.Tcs.TrySetResult(new PipeResult(false, ShuttingDown));
            }
        }
    }
}
