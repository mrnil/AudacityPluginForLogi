namespace Loupedeck.AudacityPlugin.Adjustments
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>
    /// Track Gain dial logic, free of SDK code so it can be tested. At the start of each gesture
    /// it reads the first selected wave track's volume from Audacity, then sets all selected
    /// tracks relative to it (Audacity's SetTrack has no track index, so they share one value).
    /// </summary>
    internal sealed class GainController
    {
        private const String GetTracks = "GetInfo: Type=Tracks Format=JSON";
        private const Double StepDb = 0.5;
        private const Double MinDb = -36;
        private const Double MaxDb = 36;

        // A turn after this much idle time starts a new gesture and re-reads the track, so
        // selecting another track (or moving its slider in Audacity) is picked up.
        private static readonly TimeSpan GestureGap = TimeSpan.FromMilliseconds(500);

        private readonly Func<String, Task<PipeResult>> _send;
        private readonly Func<DateTime> _utcNow;

        // Dial turns and presses arrive on different threads; run them one at a time.
        private readonly Lock _chainLock = new();
        private Task _tail = Task.CompletedTask;

        // Only touched inside RunExclusiveAsync.
        private Double? _gainDb;
        private DateTime _lastTurn = DateTime.MinValue;

        private String _display = "--";

        public GainController(Func<String, Task<PipeResult>> send, Func<DateTime> utcNow)
        {
            this._send = send;
            this._utcNow = utcNow;
        }

        /// <summary>"+1.5 dB", "No track" when no wave track is selected, "--" when unknown.</summary>
        public String Display => Volatile.Read(ref this._display);

        public Task AdjustAsync(Int32 ticks) => this.RunExclusiveAsync(async () =>
        {
            var now = this._utcNow();
            if (this._gainDb == null || now - this._lastTurn > GestureGap)
            {
                this._gainDb = await this.ReadSelectedTrackDbAsync().ConfigureAwait(false);
            }
            this._lastTurn = now;
            if (this._gainDb is Double current)
            {
                await this.SetAsync(Math.Clamp(current + ticks * StepDb, MinDb, MaxDb)).ConfigureAwait(false);
            }
        });

        public Task ResetAsync() => this.RunExclusiveAsync(async () =>
        {
            this._gainDb = await this.ReadSelectedTrackDbAsync().ConfigureAwait(false);
            if (this._gainDb != null)
            {
                await this.SetAsync(0).ConfigureAwait(false);
            }
        });

        private async Task<Double?> ReadSelectedTrackDbAsync()
        {
            var result = await this._send(GetTracks).ConfigureAwait(false);
            if (!result.Ok)
            {
                this.Show("--");
                return null;
            }
            var db = TrackListParser.FirstSelectedWaveVolumeDb(result.Output);
            if (db == null)
            {
                this.Show("No track");
            }
            return db;
        }

        private async Task SetAsync(Double db)
        {
            // Built only from a clamped number, never from response text.
            var result = await this._send(FormattableString.Invariant($"SetTrack: Volume={db:0.0}")).ConfigureAwait(false);
            if (result.Ok)
            {
                this._gainDb = db;
                this.Show(FormattableString.Invariant($"{db:+0.0;-0.0;0.0} dB"));
            }
            else
            {
                this._gainDb = null; // state unknown; read again on the next turn
            }
        }

        private void Show(String text) => Volatile.Write(ref this._display, text);

        private Task RunExclusiveAsync(Func<Task> operation)
        {
            lock (this._chainLock)
            {
                var next = this._tail.ContinueWith(_ => operation(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
                // Keep the chain going even if an operation fails.
                this._tail = next.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return next;
            }
        }
    }
}
