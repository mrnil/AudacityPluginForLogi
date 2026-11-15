namespace Loupedeck.AudacityPlugin
{
    using System;
    using Loupedeck.AudacityPlugin.Pipe;

    public class AudacityPlugin : Plugin
    {
        // We talk to Audacity over mod-script-pipe, not via simulated keystrokes.
        public override Boolean UsesApplicationApiOnly => true;

        // Tied to AudacityApplication so the profile auto-switches when Audacity is focused.
        public override Boolean HasNoApplication => false;

        internal AudacityPipeClient Client { get; } = new AudacityPipeClient();

        public AudacityPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            this.Client.StateChanged += this.OnClientStateChanged;
            this.Client.Start();
            this.UpdatePluginStatus();
        }

        public override void Unload()
        {
            this.Client.StateChanged -= this.OnClientStateChanged;
            this.Client.Dispose();
        }

        private void OnClientStateChanged(Object sender, EventArgs e) => this.UpdatePluginStatus();

        // Enum is fully qualified: inside Plugin, `PluginStatus` resolves to the inherited PluginStatusMessage property.
        private void UpdatePluginStatus()
        {
            if (this.Client.Connected)
            {
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, null);
            }
            else
            {
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning,
                    "Can't reach Audacity. Start Audacity and set Preferences > Modules > mod-script-pipe to Enabled, then restart Audacity.");
            }
        }
    }
}
