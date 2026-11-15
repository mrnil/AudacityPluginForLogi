namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    public class PipeResponseReaderTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task OkResponseWithOutputJoinsLinesWithNewlines()
        {
            using var reader = new StringReader("first\nsecond\nBatchCommand finished: OK\n\n");

            var result = await PipeResponseReader.ReadAsync(reader, Ct);

            Assert.Equal(new PipeResult(true, "first\nsecond"), result);
        }

        [Fact]
        public async Task OkResponseWithoutOutputHasEmptyOutput()
        {
            using var reader = new StringReader("BatchCommand finished: OK\n\n");

            var result = await PipeResponseReader.ReadAsync(reader, Ct);

            Assert.Equal(new PipeResult(true, ""), result);
        }

        [Fact]
        public async Task FailedResponseIsNotOk()
        {
            using var reader = new StringReader("Your batch command of Nope was not recognized.\nBatchCommand finished: Failed!\n\n");

            var result = await PipeResponseReader.ReadAsync(reader, Ct);

            Assert.Equal(new PipeResult(false, "Your batch command of Nope was not recognized."), result);
        }

        [Fact]
        public async Task TrailingNulCharactersAreStrippedFromEachLine()
        {
            using var reader = new StringReader("output\0\0\nBatchCommand finished: OK\0\n\n");

            var result = await PipeResponseReader.ReadAsync(reader, Ct);

            Assert.Equal(new PipeResult(true, "output"), result);
        }

        [Fact]
        public async Task CrLfLineEndingsAreAccepted()
        {
            using var reader = new StringReader("output\r\nBatchCommand finished: OK\r\n\r\n");

            var result = await PipeResponseReader.ReadAsync(reader, Ct);

            Assert.Equal(new PipeResult(true, "output"), result);
        }

        [Fact]
        public async Task TrailingBlankLineIsConsumedSoTheNextResponseReadsCleanly()
        {
            using var reader = new StringReader(
                "one\nBatchCommand finished: OK\n\n" +
                "two\nBatchCommand finished: Failed!\n\n");

            var first = await PipeResponseReader.ReadAsync(reader, Ct);
            var second = await PipeResponseReader.ReadAsync(reader, Ct);

            Assert.Equal(new PipeResult(true, "one"), first);
            Assert.Equal(new PipeResult(false, "two"), second);
        }

        [Fact]
        public async Task PipeClosingBeforeTheFinishedLineThrowsEndOfStream()
        {
            using var reader = new StringReader("partial output\n");

            await Assert.ThrowsAsync<EndOfStreamException>(() => PipeResponseReader.ReadAsync(reader, Ct));
        }
    }
}
