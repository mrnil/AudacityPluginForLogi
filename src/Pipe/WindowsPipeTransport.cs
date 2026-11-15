namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>\\.\pipe\ToSrvPipe (we write) and \\.\pipe\FromSrvPipe (we read).</summary>
    internal sealed class WindowsPipeTransport : IPipeTransport
    {
        private NamedPipeClientStream _to;
        private NamedPipeClientStream _from;

        public Stream ToAudacity => this._to;
        public Stream FromAudacity => this._from;

        // Matches Audacity's own pipe_test.py on win32.
        public String CommandTerminator => "\r\n\0";

        public async Task OpenAsync(TimeSpan timeout, CancellationToken ct)
        {
            this._to = new NamedPipeClientStream(".", "ToSrvPipe", PipeDirection.Out, PipeOptions.Asynchronous);
            this._from = new NamedPipeClientStream(".", "FromSrvPipe", PipeDirection.In, PipeOptions.Asynchronous);

            // Same order as pipe_test.py: "to" first, then "from".
            await this._to.ConnectAsync((Int32)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
            await this._from.ConnectAsync((Int32)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            this._to?.Dispose();
            this._from?.Dispose();
        }
    }
}
