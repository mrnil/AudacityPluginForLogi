namespace Loupedeck.AudacityPlugin.Adjustments
{
    using System;
    using System.Threading.Tasks;
    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>Dial zooms in/out. Press = zoom to fit.</summary>
    public class ZoomAdjustment : PluginDynamicAdjustment
    {
        private const Int32 MaxCommandsPerFlush = 4;
        private readonly TickCoalescer _ticks;

        public ZoomAdjustment()
            : base("Zoom", "Zoom the timeline. Press: zoom to fit", "View", hasReset: true)
        {
            this._ticks = new TickCoalescer(this.FlushAsync, intervalMs: 50);
        }

        private AudacityPipeClient Client => (this.Plugin as AudacityPlugin)?.Client;

        protected override void ApplyAdjustment(String actionParameter, Int32 diff) => this._ticks.Add(diff);

        protected override void RunCommand(String actionParameter) => this.Client?.Send("FitInWindow");

        protected override String GetAdjustmentValue(String actionParameter) => null;

        private async Task FlushAsync(Int32 ticks)
        {
            var client = this.Client;
            if (client == null || !client.Connected)
            {
                return;
            }
            var cmd = ticks > 0 ? "ZoomIn" : "ZoomOut";
            var n = Math.Min(Math.Abs(ticks), MaxCommandsPerFlush);
            for (var i = 0; i < n; i++)
            {
                await client.SendAsync(cmd).ConfigureAwait(false);
            }
        }
    }
}
