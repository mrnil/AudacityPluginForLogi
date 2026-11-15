namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;
    using System.Collections.Generic;
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
        private static readonly TimeSpan NoTimeout = TimeSpan.FromHours(1);
        private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task SendBeforeConnectingFailsFast()
        {
            var fake = new FakePipeTransport();
            using var client = new AudacityPipeClient(() => fake, () => true, NoPoll, NoTimeout);

            var result = await client.SendAsync("Undo");

            Assert.Equal(new PipeResult(false, "Not connected to Audacity"), result);
        }

        [Fact]
        public async Task StartConnectsAndRaisesStateChanged()
        {
            var fake = new FakePipeTransport();
            using var client = new AudacityPipeClient(() => fake, () => true, NoPoll, NoTimeout);
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
            using var client = new AudacityPipeClient(() => fake, () => true, NoPoll, NoTimeout);

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
            using var client = new AudacityPipeClient(Create, () => true, TimeSpan.FromMilliseconds(50), NoTimeout);

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

        [Fact]
        public async Task DisposeFailsTheCommandInFlightAndThoseStillQueued()
        {
            var fake = new FakePipeTransport();
            var client = await StartConnectedAsync(fake);

            var inFlight = client.SendAsync("Save");
            await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct);
            var queued = client.SendAsync("PlayStop");

            client.Dispose();

            Assert.False((await inFlight.WaitAsync(WaitLimit, Ct)).Ok);
            Assert.False((await queued.WaitAsync(WaitLimit, Ct)).Ok);
        }

        [Fact]
        public async Task SendAfterDisposeFailsFastAsNotConnected()
        {
            var fake = new FakePipeTransport();
            var client = await StartConnectedAsync(fake);

            client.Dispose();
            var result = client.SendAsync("PlayStop");

            Assert.False(client.Connected);
            Assert.True(result.IsCompleted, "Send after Dispose should not wait");
            Assert.Equal(new PipeResult(false, "Not connected to Audacity"), await result);
        }

        [Fact]
        public async Task DisposeCanBeCalledTwice()
        {
            var fake = new FakePipeTransport();
            var client = await StartConnectedAsync(fake);

            client.Dispose();
            client.Dispose();

            Assert.True(fake.IsDisposed);
        }

        [Fact]
        public async Task CommandWithNoResponseTimesOutAndDisconnects()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake, NoPoll, TimeSpan.FromMilliseconds(200));

            var pending = client.SendAsync("Save");
            await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct);
            var result = await pending.WaitAsync(WaitLimit, Ct);

            Assert.False(result.Ok);
            Assert.Contains("did not respond", result.Output, StringComparison.Ordinal);
            await WaitUntilAsync(() => !client.Connected);
            Assert.True(fake.IsDisposed);
        }

        [Fact]
        public async Task CommandsQueuedBehindATimedOutCommandAreDroppedNotSent()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake, NoPoll, TimeSpan.FromMilliseconds(200));

            var first = client.SendAsync("Save");
            var queued = client.SendAsync("PlayStop");
            await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct);
            var next = fake.ReadCommandAsync(Ct);

            Assert.False((await first.WaitAsync(WaitLimit, Ct)).Ok);
            Assert.Equal(new PipeResult(false, "Not connected to Audacity"), await queued.WaitAsync(WaitLimit, Ct));
            // Disconnecting disposes the transport, which ends the fake's command stream:
            // the queued command must not have been written before that.
            Assert.Null(await next.WaitAsync(WaitLimit, Ct));
        }

        [Fact]
        public async Task SendFailsFastWhenTheQueueIsFull()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake);

            _ = client.SendAsync("Save");
            await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct);
            var accepted = new List<Task<PipeResult>>();
            for (var i = 0; i < 32; i++)
            {
                accepted.Add(client.SendAsync("ZoomIn"));
            }
            var rejected = client.SendAsync("ZoomIn");

            Assert.True(rejected.IsCompleted, "Command beyond the queue limit should fail immediately");
            Assert.Equal(new PipeResult(false, "Audacity is busy; command dropped"), await rejected);
            Assert.DoesNotContain(accepted, t => t.IsCompleted);
        }

        // Any command, even a no-op, runs on Audacity's UI thread and closes open menus,
        // so nothing may be sent unless the user acts.
        [Fact]
        public async Task NothingIsSentWhileIdle()
        {
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake, () => true, FastPoll, NoTimeout);

            var read = fake.ReadCommandAsync(Ct);
            await Task.Delay(SettleTime, Ct);

            Assert.False(read.IsCompleted, "A command was sent while idle");
            Assert.True(client.Connected);
        }

        [Fact]
        public async Task AudacityExitingDisconnectsWithoutSendingAnything()
        {
            var running = 1;
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake, () => Volatile.Read(ref running) == 1, FastPoll, NoTimeout);
            var read = fake.ReadCommandAsync(Ct);

            Volatile.Write(ref running, 0);
            await WaitUntilAsync(() => !client.Connected);

            Assert.True(fake.IsDisposed);
            Assert.Equal(TransportState.Stopped, client.Transport);
            // Disposal ends the fake's command stream; nothing may have been written before it.
            Assert.Null(await read.WaitAsync(WaitLimit, Ct));
        }

        [Fact]
        public async Task CommandInFlightWhenAudacityExitsFailsAndDisconnects()
        {
            var running = 1;
            var fake = new FakePipeTransport();
            using var client = await StartConnectedAsync(fake, () => Volatile.Read(ref running) == 1, FastPoll, NoTimeout);

            var pending = client.SendAsync("Save");
            await fake.ReadCommandAsync(Ct).WaitAsync(WaitLimit, Ct);
            Volatile.Write(ref running, 0);

            Assert.False((await pending.WaitAsync(WaitLimit, Ct)).Ok);
            await WaitUntilAsync(() => !client.Connected);
        }

        private static Task<AudacityPipeClient> StartConnectedAsync(FakePipeTransport fake) =>
            StartConnectedAsync(fake, () => true, NoPoll, NoTimeout);

        private static Task<AudacityPipeClient> StartConnectedAsync(FakePipeTransport fake, TimeSpan pollInterval, TimeSpan responseTimeout) =>
            StartConnectedAsync(fake, () => true, pollInterval, responseTimeout);

        private static async Task<AudacityPipeClient> StartConnectedAsync(
            FakePipeTransport fake, Func<Boolean> isAudacityRunning, TimeSpan pollInterval, TimeSpan responseTimeout)
        {
            var client = new AudacityPipeClient(() => fake, isAudacityRunning, pollInterval, responseTimeout);
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
