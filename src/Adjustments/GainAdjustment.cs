namespace Loupedeck.AudacityPlugin.Adjustments
{
    using System;
    using System.Threading.Tasks;
    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>
    /// Volume of the selected track(s), 0.5 dB per tick, clamped to Audacity's ±36 dB. Press = 0 dB.
    /// Each gesture starts from the selected track's current volume read from Audacity; the logic
    /// lives in <see cref="GainController"/>.
    /// </summary>
    public class GainAdjustment : PluginDynamicAdjustment
    {
        private static readonly PipeResult NotConnected = new(false, "Not connected to Audacity");

        private readonly TickCoalescer _ticks;
        private readonly GainController _gain;

        public GainAdjustment()
            : base("Track Gain", "Volume of the selected track(s). Press: reset to 0 dB", "Mixer", hasReset: true)
        {
            this._gain = new GainController(this.SendAsync, () => DateTime.UtcNow);
            this._ticks = new TickCoalescer(this.FlushAsync);
        }

        private AudacityPipeClient Client => (this.Plugin as AudacityPlugin)?.Client;

        protected override void ApplyAdjustment(String actionParameter, Int32 diff) => this._ticks.Add(diff);

        protected override void RunCommand(String actionParameter) =>
            _ = this.AfterAsync(this._gain.ResetAsync());

        protected override String GetAdjustmentValue(String actionParameter) => this._gain.Display;

        private Task FlushAsync(Int32 ticks) => this.AfterAsync(this._gain.AdjustAsync(ticks));

        private async Task AfterAsync(Task operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            finally
            {
                this.AdjustmentValueChanged();
            }
        }

        private Task<PipeResult> SendAsync(String command) =>
            this.Client?.SendAsync(command) ?? Task.FromResult(NotConnected);
    }
}
