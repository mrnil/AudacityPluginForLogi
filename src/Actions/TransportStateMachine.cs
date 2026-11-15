namespace Loupedeck.AudacityPlugin.Actions
{
    using System;

    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>What a button press sends to Audacity, and the optimistic transport state that follows.</summary>
    internal readonly record struct TransportStep(String Command, TransportState State, TransportState BeforePause);

    /// <summary>
    /// Transport logic for the buttons, kept free of SDK and pipe code so it can be tested.
    /// State is optimistic: mod-script-pipe can't report Audacity's real transport state.
    /// </summary>
    internal static class TransportStateMachine
    {
        public static TransportStep Press(String actionId, TransportState state, TransportState beforePause)
        {
            switch (actionId)
            {
                case "Record1stChoice":
                case "Record2ndChoice":
                    return new TransportStep(actionId, TransportState.Recording, beforePause);

                case "PlayStop":
                    return new TransportStep("PlayStop", state == TransportState.Stopped ? TransportState.Playing : TransportState.Stopped, beforePause);

                case "Stop":
                    return new TransportStep("Stop", TransportState.Stopped, beforePause);

                case "Pause":
                    if (state == TransportState.Paused)
                    {
                        return new TransportStep("Pause", beforePause, beforePause);
                    }
                    if (state != TransportState.Stopped)
                    {
                        return new TransportStep("Pause", TransportState.Paused, state);
                    }
                    return new TransportStep("Pause", state, beforePause);

                case "AddLabel":
                    // AddLabel uses the selection/cursor; AddLabelPlaying drops it at the play/record head.
                    return new TransportStep(state == TransportState.Stopped ? "AddLabel" : "AddLabelPlaying", state, beforePause);

                default:
                    return new TransportStep(actionId, state, beforePause);
            }
        }

        /// <summary>Whether the button shows its "-active" image in this state.</summary>
        public static Boolean IsActive(String actionId, TransportState state) => actionId switch
        {
            "Record1stChoice" or "Record2ndChoice" => state == TransportState.Recording,
            "PlayStop" => state == TransportState.Playing,
            "Pause" => state == TransportState.Paused,
            _ => false,
        };
    }
}
