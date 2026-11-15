namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Reads one mod-script-pipe response.</summary>
    internal static class PipeResponseReader
    {
        private const String FinishedPrefix = "BatchCommand finished:";

        // Response = zero or more output lines, then "BatchCommand finished: OK|Failed!", then a blank line.
        public static async Task<PipeResult> ReadAsync(TextReader reader, CancellationToken ct)
        {
            var output = new List<String>();
            while (true)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("Audacity closed the pipe");
                line = line.TrimEnd('\0');
                if (line.StartsWith(FinishedPrefix, StringComparison.Ordinal))
                {
                    await reader.ReadLineAsync(ct).ConfigureAwait(false); // trailing blank line
                    var ok = line.Substring(FinishedPrefix.Length).Trim().StartsWith("OK", StringComparison.Ordinal);
                    return new PipeResult(ok, String.Join("\n", output));
                }
                output.Add(line);
            }
        }
    }
}
