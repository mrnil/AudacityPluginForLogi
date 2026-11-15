namespace Loupedeck.AudacityPlugin.Tests.Adjustments
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;

    using Loupedeck.AudacityPlugin.Adjustments;
    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    public class GainControllerTests
    {
        private const String GetTracks = "GetInfo: Type=Tracks Format=JSON";

        private readonly List<String> _sent = new();
        private readonly Queue<PipeResult> _replies = new();
        private DateTime _now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task FirstTurnReadsTheSelectedTrackAndAdjustsFromIt()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 0.501187);
            this.ReplyOk();

            await gain.AdjustAsync(2);

            Assert.Equal([GetTracks, "SetTrack: Volume=-5.0"], this._sent);
            Assert.Equal("-5.0 dB", gain.Display);
        }

        [Fact]
        public async Task TurnsWithinOneGestureDoNotReadAgain()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 0.501187);
            this.ReplyOk();
            await gain.AdjustAsync(2);

            this._now += TimeSpan.FromMilliseconds(100);
            this.ReplyOk();
            await gain.AdjustAsync(1);

            Assert.Equal([GetTracks, "SetTrack: Volume=-5.0", "SetTrack: Volume=-4.5"], this._sent);
        }

        // The bug this fixes: after selecting another track, the dial used to jump it to the old track's gain.
        [Fact]
        public async Task NewGestureAfterAPauseReadsTheCurrentTrackAgain()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 0.501187);
            this.ReplyOk();
            await gain.AdjustAsync(2);

            this._now += TimeSpan.FromSeconds(2);
            this.ReplyTracks(volume: 1.0);
            this.ReplyOk();
            await gain.AdjustAsync(1);

            Assert.Equal([GetTracks, "SetTrack: Volume=-5.0", GetTracks, "SetTrack: Volume=0.5"], this._sent);
            Assert.Equal("+0.5 dB", gain.Display);
        }

        [Fact]
        public async Task NoSelectedWaveTrackSendsNoChangeAndSaysSo()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 1.0, selected: false);

            await gain.AdjustAsync(3);

            Assert.Equal([GetTracks], this._sent);
            Assert.Equal("No track", gain.Display);
        }

        [Theory]
        [InlineData(3.98107, 60, "SetTrack: Volume=36.0")]
        [InlineData(0.0158489, -60, "SetTrack: Volume=-36.0")]
        public async Task VolumeIsClampedToAudacitysRange(Double linear, Int32 ticks, String expected)
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: linear);
            this.ReplyOk();

            await gain.AdjustAsync(ticks);

            Assert.Equal(expected, this._sent[^1]);
        }

        [Fact]
        public async Task FailedReadSendsNoChangeAndShowsUnknown()
        {
            var gain = this.CreateController();
            this._replies.Enqueue(new PipeResult(false, "Not connected to Audacity"));

            await gain.AdjustAsync(1);

            Assert.Equal([GetTracks], this._sent);
            Assert.Equal("--", gain.Display);
        }

        [Fact]
        public async Task FailedChangeMakesTheNextTurnReadAgain()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 1.0);
            this._replies.Enqueue(new PipeResult(false, "Audacity did not respond within 10 s"));
            await gain.AdjustAsync(1);

            this._now += TimeSpan.FromMilliseconds(100);
            this.ReplyTracks(volume: 1.0);
            this.ReplyOk();
            await gain.AdjustAsync(1);

            Assert.Equal([GetTracks, "SetTrack: Volume=0.5", GetTracks, "SetTrack: Volume=0.5"], this._sent);
        }

        [Fact]
        public async Task ResetSetsTheSelectedTracksToZero()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 0.501187);
            this.ReplyOk();

            await gain.ResetAsync();

            Assert.Equal([GetTracks, "SetTrack: Volume=0.0"], this._sent);
            Assert.Equal("0.0 dB", gain.Display);
        }

        [Fact]
        public async Task ResetWithNoSelectedTrackSendsNoChange()
        {
            var gain = this.CreateController();
            this.ReplyTracks(volume: 0.5, selected: false);

            await gain.ResetAsync();

            Assert.Equal([GetTracks], this._sent);
            Assert.Equal("No track", gain.Display);
        }

        [Fact]
        public async Task CommandsUseInvariantNumbersWhateverTheCulture()
        {
            var original = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var gain = this.CreateController();
                this.ReplyTracks(volume: 0.501187);
                this.ReplyOk();

                await gain.AdjustAsync(1);

                Assert.Equal("SetTrack: Volume=-5.5", this._sent[^1]);
                Assert.Equal("-5.5 dB", gain.Display);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        private GainController CreateController() => new(this.SendAsync, () => this._now);

        private Task<PipeResult> SendAsync(String command)
        {
            this._sent.Add(command);
            return Task.FromResult(this._replies.Count > 0 ? this._replies.Dequeue() : new PipeResult(false, "no reply queued"));
        }

        private void ReplyOk() => this._replies.Enqueue(new PipeResult(true, ""));

        private void ReplyTracks(Double volume, Boolean selected = true) =>
            this._replies.Enqueue(new PipeResult(true, FormattableString.Invariant(
                $$"""[ { "name":"T", "selected":{{(selected ? 1 : 0)}}, "kind":"wave", "volume":{{volume}} } ]""")));
    }
}
