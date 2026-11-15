namespace Loupedeck.AudacityPlugin.Adjustments
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>
    /// Gain of the selected track(s), 0.5 dB per tick, clamped to Audacity's ±36 dB.
    /// Press = 0 dB. The value is tracked plugin-side (starts at 0 dB): the pipe gives us no
    /// change notifications, so moving the slider in Audacity's UI won't be reflected here.
    /// </summary>
    public class GainAdjustment : PluginDynamicAdjustment
    {
        private const Double StepDb = 0.5;
        private const Double MinDb = -36;
        private const Double MaxDb = 36;

        private readonly TickCoalescer _ticks;
        private Double _gainDb;

        public GainAdjustment()
            : base("Track Gain", "Gain of the selected track(s). Press: reset to 0 dB", "Mixer", hasReset: true)
        {
            this._ticks = new TickCoalescer(this.FlushAsync);
        }

        private AudacityPipeClient Client => (this.Plugin as AudacityPlugin)?.Client;

        protected override void ApplyAdjustment(String actionParameter, Int32 diff) => this._ticks.Add(diff);

        protected override void RunCommand(String actionParameter)
        {
            this._gainDb = 0;
            this.SendGain();
        }

        protected override String GetAdjustmentValue(String actionParameter) =>
            this._gainDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";

        private Task FlushAsync(Int32 ticks)
        {
            this._gainDb = Math.Clamp(this._gainDb + (ticks * StepDb), MinDb, MaxDb);
            return this.SendGain();
        }

        private Task SendGain()
        {
            this.AdjustmentValueChanged();
            var client = this.Client;
            if (client == null || !client.Connected)
            {
                return Task.CompletedTask;
            }
            // No Track= param: applies to the currently selected track(s).
            return client.SendAsync(FormattableString.Invariant($"SetTrack: Gain={this._gainDb:0.0}"));
        }
    }
}
