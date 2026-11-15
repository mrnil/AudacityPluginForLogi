namespace Loupedeck.AudacityPlugin.Pipe
{
    using System;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Reads Audacity's track list (GetInfo: Type=Tracks Format=JSON). The response comes from
    /// another process, so it's treated as untrusted: size-capped, every field type-checked,
    /// values clamped, and nothing from it is ever copied into a command.
    /// </summary>
    internal static partial class TrackListParser
    {
        // Far above any real project (one track is ~200 bytes); stops pathological input.
        private const Int32 MaxLength = 512 * 1024;

        // Audacity's track volume range.
        private const Double MinDb = -36;
        private const Double MaxDb = 36;

        private const String JsonEscapes = "\"\\/bfnrtu";

        /// <summary>
        /// Volume in dB of the first selected wave track, or null if there is none or the input
        /// can't be read. Audacity reports volume as linear gain.
        /// </summary>
        public static Double? FirstSelectedWaveVolumeDb(String json)
        {
            if (String.IsNullOrEmpty(json) || json.Length > MaxLength)
            {
                return null;
            }
            try
            {
                using var document = JsonDocument.Parse(EscapeStrayBackslashes(json));
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }
                foreach (var track in document.RootElement.EnumerateArray())
                {
                    if (IsSelectedWaveTrack(track) && TryGetLinearVolume(track, out var linear))
                    {
                        return ToDb(linear);
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static Boolean IsSelectedWaveTrack(JsonElement track) =>
            track.ValueKind == JsonValueKind.Object
            && track.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.ValueEquals("wave")
            && track.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.Number
            && selected.TryGetInt32(out var flag) && flag == 1;

        private static Boolean TryGetLinearVolume(JsonElement track, out Double linear)
        {
            linear = 0;
            return track.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number
                && volume.TryGetDouble(out linear) && Double.IsFinite(linear);
        }

        private static Double ToDb(Double linear) =>
            linear <= 0 ? MinDb : Math.Clamp(20 * Math.Log10(linear), MinDb, MaxDb);

        // Audacity writes strings without escaping backslashes (e.g. "C:\Out"), which is invalid JSON.
        // Keep valid escapes and double any other backslash.
        private static String EscapeStrayBackslashes(String json) =>
            BackslashAndNext().Replace(json, m =>
                m.Groups[1].Length == 1 && JsonEscapes.Contains(m.Groups[1].Value[0]) ? m.Value : @"\\" + m.Groups[1].Value);

        [GeneratedRegex(@"\\(.?)", RegexOptions.Singleline)]
        private static partial Regex BackslashAndNext();
    }
}
