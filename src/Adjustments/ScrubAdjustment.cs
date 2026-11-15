namespace Loupedeck.AudacityPlugin.Adjustments
{
    using System;
    using System.Threading.Tasks;
    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>Dial moves the cursor (stopped) or seeks (playing). Press = go to start.</summary>
    public class ScrubAdjustment : PluginDynamicAdjustment
    {
        private const Int32 MaxCommandsPerFlush = 6;
        private readonly TickCoalescer _ticks;

        public ScrubAdjustment()
            : base("Scrub", "Move the cursor / seek during playback. Press: go to start", "Transport", hasReset: true)
        {
            this._ticks = new TickCoalescer(this.FlushAsync);
        }

        private AudacityPipeClient Client => (this.Plugin as AudacityPlugin)?.Client;

        protected override void ApplyAdjustment(String actionParameter, Int32 diff) => this._ticks.Add(diff);

        protected override void RunCommand(String actionParameter) => this.Client?.Send("CursProjectStart");

        protected override String GetAdjustmentValue(String actionParameter) => null;

        private async Task FlushAsync(Int32 ticks)
        {
            var client = this.Client;
            if (client == null || !client.Connected)
            {
                return;
            }

            // VERIFY: Seek* only act during playback; CursorShortJump* move the edit cursor when stopped.
            var moving = client.Transport is TransportState.Playing or TransportState.Paused;
            var cmd = ticks > 0
                ? (moving ? "SeekRightShort" : "CursorShortJumpRight")
                : (moving ? "SeekLeftShort" : "CursorShortJumpLeft");

            var n = Math.Min(Math.Abs(ticks), MaxCommandsPerFlush);
            for (var i = 0; i < n; i++)
            {
                await client.SendAsync(cmd).ConfigureAwait(false);
            }
        }
    }
}
