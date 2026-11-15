namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;

    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    public class TrackListParserTests
    {
        // Captured from Audacity 3.7.9 (GetInfo: Type=Tracks Format=JSON), with the mono track selected.
        private const String Captured = """
            [
              { "name":"Mono -6 dB", "focused":0, "selected":1, "kind":"wave", "start":0, "end":0, "pan":0, "volume":0.501187, "channels":1, "solo":0, "mute":0, "VZoomMin":-1, "VZoomMax":1 },
              { "name":"Stereo", "focused":0, "selected":0, "kind":"wave", "start":0, "end":0, "pan":0, "volume":1, "channels":2, "solo":0, "mute":0, "VZoomMin":-1, "VZoomMax":1 },
              { "name":"Labels", "focused":1, "selected":0, "kind":"label" } ]
            """;

        [Fact]
        public void ReadsTheSelectedTrackVolumeInDecibels()
        {
            var db = TrackListParser.FirstSelectedWaveVolumeDb(Captured);

            Assert.NotNull(db);
            Assert.Equal(-6.0, db.Value, 2);
        }

        [Fact]
        public void NoSelectedTrackGivesNull() =>
            Assert.Null(TrackListParser.FirstSelectedWaveVolumeDb(Captured.Replace("\"selected\":1", "\"selected\":0", StringComparison.Ordinal)));

        [Fact]
        public void SelectedLabelTrackIsIgnored() =>
            Assert.Null(TrackListParser.FirstSelectedWaveVolumeDb("""[ { "name":"Labels", "selected":1, "kind":"label" } ]"""));

        [Fact]
        public void FirstSelectedWaveTrackWinsAndStereoTracksCount()
        {
            const String json = """
                [ { "name":"A", "selected":0, "kind":"wave", "volume":0.5 },
                  { "name":"B", "selected":1, "kind":"wave", "volume":1, "channels":2 },
                  { "name":"C", "selected":1, "kind":"wave", "volume":0.25 } ]
                """;

            Assert.Equal(0.0, TrackListParser.FirstSelectedWaveVolumeDb(json).Value, 2);
        }

        [Theory]
        [InlineData("0", -36.0)]
        [InlineData("1000", 36.0)]
        [InlineData("-0.5", -36.0)]
        public void VolumeIsClampedToAudacitysRange(String volume, Double expectedDb) =>
            Assert.Equal(expectedDb, TrackListParser.FirstSelectedWaveVolumeDb($$"""[ { "selected":1, "kind":"wave", "volume":{{volume}} } ]""").Value, 2);

        [Theory]
        [InlineData("""[ { "selected":1, "kind":"wave", "volume":"loud" } ]""")]
        [InlineData("""[ { "selected":1, "kind":"wave" } ]""")]
        [InlineData("""[ { "selected":"yes", "kind":"wave", "volume":1 } ]""")]
        [InlineData("""[ 42, "x", null ]""")]
        public void EntriesWithMissingOrWrongFieldsAreSkipped(String json) =>
            Assert.Null(TrackListParser.FirstSelectedWaveVolumeDb(json));

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("""{ "selected":1, "kind":"wave", "volume":1 }""")]
        [InlineData("""[ { "selected":1, "kind":"wave", "volume":1 """)]
        public void MalformedOrNonArrayInputGivesNull(String json) =>
            Assert.Null(TrackListParser.FirstSelectedWaveVolumeDb(json));

        [Fact]
        public void NullInputGivesNull() =>
            Assert.Null(TrackListParser.FirstSelectedWaveVolumeDb(null));

        // Audacity doesn't escape backslashes, e.g. in a track named after a Windows path.
        [Fact]
        public void UnescapedBackslashInATrackNameIsTolerated() =>
            Assert.Equal(0.0, TrackListParser.FirstSelectedWaveVolumeDb("""[ { "name":"C:\Out\take 1", "selected":1, "kind":"wave", "volume":1 } ]""").Value, 2);

        [Theory]
        [InlineData("""C:\\Out""")]
        [InlineData("""say \"hi\" \u0041""")]
        [InlineData("""trailing \\""")]
        public void ValidEscapesAreLeftAlone(String escapedName) =>
            Assert.Equal(0.0, TrackListParser.FirstSelectedWaveVolumeDb($$"""[ { "name":"{{escapedName}}", "selected":1, "kind":"wave", "volume":1 } ]""").Value, 2);

        [Fact]
        public void OversizedInputIsRejected()
        {
            var huge = "[" + String.Concat(System.Linq.Enumerable.Repeat("""{ "selected":0, "kind":"wave", "volume":1 },""", 20_000)) +
                """{ "selected":1, "kind":"wave", "volume":1 } ]""";

            Assert.Null(TrackListParser.FirstSelectedWaveVolumeDb(huge));
        }
    }
}
