namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Opens the mod-script-pipe stream pair. Implementations are platform-specific.</summary>
    internal interface IPipeTransport : IDisposable
    {
        /// <summary>Opens both pipes. Throws on failure or timeout.</summary>
        Task OpenAsync(TimeSpan timeout, CancellationToken ct);

        Stream ToAudacity { get; }
        Stream FromAudacity { get; }

        /// <summary>Line terminator Audacity expects on this platform.</summary>
        String CommandTerminator { get; }

        public static IPipeTransport Create() =>
            OperatingSystem.IsWindows() ? new WindowsPipeTransport() : new UnixFifoTransport();
    }
}
