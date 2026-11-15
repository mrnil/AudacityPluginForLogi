namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    // Characterisation tests: TickCoalescer runs on real timers, so these wait on signals with a
    // generous limit and only use short fixed delays to check that nothing further happens.
    public sealed class TickCoalescerTests : IDisposable
    {
        private const Int32 IntervalMs = 30;
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);

        private readonly List<Int32> _flushes = new();
        private readonly SemaphoreSlim _flushed = new(0);

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task TicksAddedWithinOneIntervalAreSummedIntoOneFlush()
        {
            var coalescer = new TickCoalescer(this.RecordAsync, IntervalMs);

            coalescer.Add(1);
            coalescer.Add(2);
            coalescer.Add(3);
            await this.WaitForFlushAsync();
            await Task.Delay(SettleTime, Ct);

            Assert.Equal([6], this.Flushes());
        }

        [Fact]
        public async Task NegativeTicksKeepTheirSign()
        {
            var coalescer = new TickCoalescer(this.RecordAsync, IntervalMs);

            coalescer.Add(-2);
            await this.WaitForFlushAsync();

            Assert.Equal([-2], this.Flushes());
        }

        [Fact]
        public async Task TicksThatCancelOutAreNotFlushed()
        {
            var coalescer = new TickCoalescer(this.RecordAsync, IntervalMs);

            coalescer.Add(3);
            coalescer.Add(-3);
            await Task.Delay(SettleTime, Ct);

            Assert.Empty(this.Flushes());
        }

        [Fact]
        public async Task TicksArrivingDuringAFlushAreSentInTheNextFlushNotConcurrently()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var inFlight = 0;
            var maxInFlight = 0;

            async Task FlushAsync(Int32 ticks)
            {
                var now = Interlocked.Increment(ref inFlight);
                maxInFlight = Math.Max(maxInFlight, now);
                await this.RecordAsync(ticks);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await gate.Task;
                }
                Interlocked.Decrement(ref inFlight);
            }

            var coalescer = new TickCoalescer(FlushAsync, IntervalMs);

            coalescer.Add(1);
            await this.WaitForFlushAsync();
            coalescer.Add(5);
            await Task.Delay(SettleTime, Ct);
            Assert.Equal([1], this.Flushes());

            gate.SetResult();
            await this.WaitForFlushAsync();

            Assert.Equal([1, 5], this.Flushes());
            Assert.Equal(1, maxInFlight);
        }

        [Fact]
        public async Task AFailedFlushDoesNotStopLaterTicks()
        {
            var calls = 0;

            async Task FlushAsync(Int32 ticks)
            {
                await this.RecordAsync(ticks);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("simulated pipe failure");
                }
            }

            var coalescer = new TickCoalescer(FlushAsync, IntervalMs);

            coalescer.Add(1);
            await this.WaitForFlushAsync();
            coalescer.Add(2);
            await this.WaitForFlushAsync();

            Assert.Equal([1, 2], this.Flushes());
        }

        public void Dispose() => this._flushed.Dispose();

        private Task RecordAsync(Int32 ticks)
        {
            lock (this._flushes)
            {
                this._flushes.Add(ticks);
            }
            this._flushed.Release();
            return Task.CompletedTask;
        }

        private async Task WaitForFlushAsync() =>
            Assert.True(await this._flushed.WaitAsync(WaitLimit, Ct), "Timed out waiting for a flush");

        private Int32[] Flushes()
        {
            lock (this._flushes)
            {
                return this._flushes.ToArray();
            }
        }
    }
}
