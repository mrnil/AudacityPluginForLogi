namespace Loupedeck.AudacityPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>All Audacity buttons as parameters of one dynamic command.</summary>
    public class AudacityCommand : PluginDynamicCommand
    {
        private sealed record Def(String DisplayName, String Group, String Icon);

        // Key = our action parameter (mostly the Audacity scripting command id).
        // VERIFY all ids with tools/verify_commands.py against your Audacity build.
        private static readonly Dictionary<String, Def> Defs = new()
        {
            ["Record1stChoice"] = new("Record", "Transport", "record"),
            ["Record2ndChoice"] = new("Record New Track", "Transport", "record-new"),
            ["PlayStop"] = new("Play / Stop", "Transport", "play"),
            ["Stop"] = new("Stop", "Transport", "stop"),
            ["Pause"] = new("Pause", "Transport", "pause"),
            ["CursProjectStart"] = new("Go to Start", "Transport", "skip-start"),
            ["CursProjectEnd"] = new("Go to End", "Transport", "skip-end"),
            ["AddLabel"] = new("Add Label", "Edit", "label"),
            ["Undo"] = new("Undo", "Edit", "undo"),
            ["Save"] = new("Save Project", "Edit", "save"),
            ["ZoomIn"] = new("Zoom In", "View", "zoom-in"),
            ["ZoomOut"] = new("Zoom Out", "View", "zoom-out"),
            ["FitInWindow"] = new("Zoom to Fit", "View", "zoom-fit"),
        };

        private TransportState _beforePause = TransportState.Playing;

        public AudacityCommand()
            : base()
        {
            foreach (var (id, def) in Defs)
            {
                this.AddParameter(id, def.DisplayName, def.Group);
            }
        }

        private AudacityPipeClient Client => (this.Plugin as AudacityPlugin)?.Client;

        protected override Boolean OnLoad()
        {
            this.Client?.StateChanged += this.OnStateChanged;
            return true;
        }

        protected override Boolean OnUnload()
        {
            this.Client?.StateChanged -= this.OnStateChanged;
            return true;
        }

        private void OnStateChanged(Object sender, EventArgs e) => this.ActionImageChanged();

        protected override void RunCommand(String actionParameter)
        {
            var client = this.Client;
            if (client == null || !client.Connected)
            {
                return;
            }

            var state = client.Transport;
            switch (actionParameter)
            {
                case "Record1stChoice":
                case "Record2ndChoice":
                    client.Send(actionParameter);
                    client.Transport = TransportState.Recording;
                    break;

                case "PlayStop":
                    client.Send("PlayStop");
                    client.Transport = state == TransportState.Stopped ? TransportState.Playing : TransportState.Stopped;
                    break;

                case "Stop":
                    client.Send("Stop");
                    client.Transport = TransportState.Stopped;
                    break;

                case "Pause":
                    client.Send("Pause");
                    if (state == TransportState.Paused)
                    {
                        client.Transport = this._beforePause;
                    }
                    else if (state != TransportState.Stopped)
                    {
                        this._beforePause = state;
                        client.Transport = TransportState.Paused;
                    }
                    break;

                case "AddLabel":
                    // AddLabel uses the selection/cursor; AddLabelPlaying drops it at the play/record head.
                    client.Send(state == TransportState.Stopped ? "AddLabel" : "AddLabelPlaying");
                    break;

                default:
                    client.Send(actionParameter);
                    break;
            }
        }

        // Images: 80x80 PNG embedded resources, Images/<icon>[-active|-off].png
        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == null || !Defs.TryGetValue(actionParameter, out var def))
            {
                return null;
            }

            var client = this.Client;
            var suffix = "";
            if (client == null || !client.Connected)
            {
                suffix = "-off";
            }
            else if (IsActive(actionParameter, client.Transport))
            {
                suffix = "-active";
            }
            return PluginResources.ReadImage($"{def.Icon}{suffix}.png");
        }

        private static Boolean IsActive(String id, TransportState state) => id switch
        {
            "Record1stChoice" or "Record2ndChoice" => state == TransportState.Recording,
            "PlayStop" => state == TransportState.Playing,
            "Pause" => state == TransportState.Paused,
            _ => false,
        };
    }
}
