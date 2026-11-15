namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    // Characterisation tests for AudacityPipeClient against an in-memory fake of Audacity's side.
    public class AudacityPipeClientTests
    {
        // Long enough that the liveness probe never fires during a test unless a test asks for it.
        private static readonly TimeSpan NoPoll = TimeSpan.FromHours(1);
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task SendBeforeConnectingFailsFast()
        {
            var fake = new FakePipeTransport();
            using var client = new AudacityPipeClient(() => fake, NoPoll);

            var result = await client.SendAsync("Undo");

            Assert.Equal(new PipeResult(false, "Not connected to Audacity"), result);
        }

        [Fact]
        public async Task StartConnectsAndRaisesStateChanged()
        {
            var fake = new FakePipeTransport();
            using var client = new AudacityPipeClient(() => fake, NoPoll);
            var raised = 0;
            client.StateChanged += (_, _) => Interlocked.Increment(ref raised);

            client.Start();
            await WaitUntilAsync(() => client.Connected);

            Assert.Equal(1, Volatile.Read(ref raised));
        }

        [Fact]
        public async Task CommandIsSentAndItsResponseReturned()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake);

            var pending = client.SendAsync("PlayStop");
            Assert.Equal("PlayStop", await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct));
            await fake.RespondAsync(true, "some output");

            Assert.Equal(new PipeResult(true, "some output"), await pending.WaitAsync(WaitLimit, Ct));
        }

        [Fact]
        public async Task CommandsAreSentOneAtATimeInOrder()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake);

            var first = client.SendAsync("ZoomIn");
            var second = client.SendAsync("ZoomOut");

            Assert.Equal("ZoomIn", await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct));
            var next = fake.ReadCommandAsync(Ct);
            await Task.Delay(SettleTime, Ct);
            Assert.False(next.IsCompleted, "Second command was written before the first response");

            await fake.RespondAsync(true);
            Assert.Equal("ZoomOut", await next.WaitAsync(WaitLimit, Ct));
            await fake.RespondAsync(false);

            Assert.True((await first.WaitAsync(WaitLimit, Ct)).Ok);
            Assert.False((await second.WaitAsync(WaitLimit, Ct)).Ok);
        }

        [Fact]
        public async Task AudacityClosingThePipeFailsTheCommandAndDisconnects()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake);
            client.Transport = TransportState.Playing;

            var pending = client.SendAsync("Stop");
            await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct);
            fake.CloseFromAudacity();
            var result = await pending.WaitAsync(WaitLimit, Ct);

            Assert.False(result.Ok);
            // The result is completed just before Disconnect() runs, so wait for it.
            await WaitUntilAsync(() => !client.Connected);
            Assert.Equal(TransportState.Stopped, client.Transport);
            Assert.True(fake.IsDisposed);
        }

        [Fact]
        public async Task FailedOpenStaysDisconnectedAndDisposesTheTransport()
        {
            var fake = new FakePipeTransport { OpenError = new IOException("pipe not found") };
            using var client = new AudacityPipeClient(() => fake, NoPoll);

            client.Start();
            await WaitUntilAsync(() => fake.IsDisposed);

            Assert.False(client.Connected);
        }

        [Fact]
        public async Task LaterPollConnectsOnceAudacityIsAvailable()
        {
            var calls = 0;
            var available = new FakePipeTransport();
            IPipeTransport Create() => Interlocked.Increment(ref calls) == 1
                ? new FakePipeTransport { OpenError = new TimeoutException("not running yet") }
                : available;
            using var client = new AudacityPipeClient(Create, TimeSpan.FromMilliseconds(50));

            client.Start();
            await WaitUntilAsync(() => client.Connected);

            Assert.True(Volatile.Read(ref calls) >= 2);
        }

        [Fact]
        public async Task DisposeDisposesTheOpenTransport()
        {
            var fake = new FakePipeTransport();
            var client = await StartConnectedAsync(fake);

            client.Dispose();

            Assert.True(fake.IsDisposed);
        }

        private static async Task<AudacityPipeClient> StartConnectedAsync(FakePipeTransport fake)
        {
            var client = new AudacityPipeClient(() => fake, NoPoll);
            client.Start();
            await WaitUntilAsync(() => client.Connected);
            return client;
        }

        private static async Task WaitUntilAsync(Func<Boolean> condition)
        {
            var deadline = DateTime.UtcNow + WaitLimit;
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for condition");
                await Task.Delay(10, Ct);
            }
        }
    }
}
