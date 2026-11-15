namespace Loupedeck.AudacityPlugin.Tests.Actions
{
    using System;

    using Loupedeck.AudacityPlugin.Actions;
    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    // Characterisation tests: these record current behaviour, including its known gaps.
    // States are passed by name: TransportState is internal, so public test methods can't take it.
    public class TransportStateMachineTests
    {
        private const String S = nameof(TransportState.Stopped);
        private const String Pl = nameof(TransportState.Playing);
        private const String R = nameof(TransportState.Recording);
        private const String Pa = nameof(TransportState.Paused);

        [Theory]
        [InlineData("Record1stChoice", S)]
        [InlineData("Record1stChoice", Pl)]
        [InlineData("Record2ndChoice", S)]
        [InlineData("Record2ndChoice", Pa)]
        public void RecordSendsItsCommandAndStartsRecording(String id, String from) =>
            Assert.Equal(new TransportStep(id, T(R), T(Pl)), TransportStateMachine.Press(id, T(from), T(Pl)));

        [Theory]
        [InlineData(S, Pl)]
        [InlineData(Pl, S)]
        [InlineData(R, S)]
        [InlineData(Pa, S)]
        public void PlayStopStartsWhenStoppedAndStopsOtherwise(String from, String to) =>
            Assert.Equal(new TransportStep("PlayStop", T(to), T(Pl)), TransportStateMachine.Press("PlayStop", T(from), T(Pl)));

        [Theory]
        [InlineData(S)]
        [InlineData(Pl)]
        [InlineData(R)]
        [InlineData(Pa)]
        public void StopAlwaysStops(String from) =>
            Assert.Equal(new TransportStep("Stop", T(S), T(Pl)), TransportStateMachine.Press("Stop", T(from), T(Pl)));

        [Theory]
        [InlineData(Pl)]
        [InlineData(R)]
        public void PauseWhileRunningPausesAndRemembersWhatWasRunning(String from) =>
            Assert.Equal(new TransportStep("Pause", T(Pa), T(from)), TransportStateMachine.Press("Pause", T(from), T(Pl)));

        [Theory]
        [InlineData(Pl)]
        [InlineData(R)]
        public void PauseWhilePausedResumesWhatWasRunning(String before) =>
            Assert.Equal(new TransportStep("Pause", T(before), T(before)), TransportStateMachine.Press("Pause", T(Pa), T(before)));

        // Pause while stopped would arm Audacity's pause so the next Play/Record starts paused,
        // which the optimistic state can't follow. Send nothing instead.
        [Fact]
        public void PauseWhileStoppedSendsNothingAndStaysStopped() =>
            Assert.Equal(new TransportStep(null, T(S), T(Pl)), TransportStateMachine.Press("Pause", T(S), T(Pl)));

        [Theory]
        [InlineData(S, "AddLabel")]
        [InlineData(Pl, "AddLabelPlaying")]
        [InlineData(R, "AddLabelPlaying")]
        [InlineData(Pa, "AddLabelPlaying")]
        public void AddLabelUsesTheCursorWhenStoppedAndThePlayHeadOtherwise(String from, String command) =>
            Assert.Equal(new TransportStep(command, T(from), T(Pl)), TransportStateMachine.Press("AddLabel", T(from), T(Pl)));

        [Theory]
        [InlineData("Undo", Pl)]
        [InlineData("ZoomIn", R)]
        [InlineData("CursProjectStart", Pa)]
        [InlineData("Save", S)]
        public void OtherButtonsSendTheirIdAndLeaveTheStateAlone(String id, String from) =>
            Assert.Equal(new TransportStep(id, T(from), T(R)), TransportStateMachine.Press(id, T(from), T(R)));

        [Theory]
        [InlineData("Record1stChoice", R, true)]
        [InlineData("Record2ndChoice", R, true)]
        [InlineData("Record1stChoice", Pl, false)]
        [InlineData("PlayStop", Pl, true)]
        [InlineData("PlayStop", R, false)]
        [InlineData("Pause", Pa, true)]
        [InlineData("Pause", Pl, false)]
        [InlineData("Stop", S, false)]
        [InlineData("Undo", Pl, false)]
        public void IsActiveLightsTheButtonMatchingTheState(String id, String state, Boolean active) =>
            Assert.Equal(active, TransportStateMachine.IsActive(id, T(state)));

        private static TransportState T(String name) => Enum.Parse<TransportState>(name);
    }
}
