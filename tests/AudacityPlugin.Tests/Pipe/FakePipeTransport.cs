namespace Loupedeck.AudacityPlugin.Tests.Pipe
{
    using System;
    using System.IO;
    using System.IO.Pipelines;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.AudacityPlugin.Pipe;

    /// <summary>Plays Audacity's side of mod-script-pipe in memory.</summary>
    internal sealed class FakePipeTransport : IPipeTransport
    {
        private readonly Pipe _toAudacity = new();
        private readonly Pipe _fromAudacity = new();
        private readonly StreamReader _commands;
        private readonly StreamWriter _responses;

        public FakePipeTransport()
        {
            this._commands = new StreamReader(this._toAudacity.Reader.AsStream(), new UTF8Encoding(false));
            this._responses = new StreamWriter(this._fromAudacity.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        /// <summary>When set, OpenAsync throws this, as when Audacity isn't running.</summary>
        public Exception OpenError { get; init; }

        public Boolean IsDisposed { get; private set; }

        public Stream ToAudacity { get; private set; }

        public Stream FromAudacity { get; private set; }

        public String CommandTerminator => "\n";

        public Task OpenAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (this.OpenError != null)
            {
                return Task.FromException(this.OpenError);
            }
            this.ToAudacity = this._toAudacity.Writer.AsStream();
            this.FromAudacity = this._fromAudacity.Reader.AsStream();
            return Task.CompletedTask;
        }

        /// <summary>Reads the next command the client sent, without its terminator.</summary>
        public async Task<String> ReadCommandAsync(CancellationToken ct) =>
            await this._commands.ReadLineAsync(ct).ConfigureAwait(false);

        /// <summary>Writes a complete response: output lines, the finished line and the trailing blank line.</summary>
        public Task RespondAsync(Boolean ok, params String[] output)
        {
            var response = new StringBuilder();
            foreach (var line in output)
            {
                response.Append(line).Append('\n');
            }
            response.Append("BatchCommand finished: ").Append(ok ? "OK" : "Failed!").Append("\n\n");
            return this._responses.WriteAsync(response.ToString());
        }

        /// <summary>Simulates Audacity quitting: the client sees end of stream.</summary>
        public void CloseFromAudacity() => this._fromAudacity.Writer.Complete();

        public void Dispose() => this.IsDisposed = true;
    }
}
