namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;

    using Loupedeck.AudacityPlugin.Pipe;

    using Xunit;

    public class PipeTransportFactoryTests
    {
        [Fact]
        public void CreateReturnsTransportForCurrentOperatingSystem()
        {
            using var transport = IPipeTransport.Create();

            var expected = OperatingSystem.IsWindows() ? typeof(WindowsPipeTransport) : typeof(UnixFifoTransport);
            Assert.IsType(expected, transport);
        }
    }
}
