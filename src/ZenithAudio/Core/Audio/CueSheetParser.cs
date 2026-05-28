using System.Globalization;
using System.Text.RegularExpressions;

namespace ZenithAudio.Core.Audio;

public sealed record CueAudioEntry(
    string Title,
    string Performer,
    string CuePath,
    string AudioPath,
    TimeSpan Start,
    TimeSpan? End);

public static partial class CueSheetParser
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ape",
        ".wv",
        ".flac",
        ".wav",
        ".aiff",
        ".aif",
        ".alac",
        ".mqa",
        ".mp3",
        ".aac",
        ".ogg",
        ".opus"
    };

    public static List<CueAudioEntry> ParseFile(string cuePath)
    {
        var folder = Path.GetDirectoryName(cuePath) ?? string.Empty;
        var lines = File.ReadAllLines(cuePath);
        var tracks = new List<MutableCueTrack>();
        var currentFile = string.Empty;
        var albumPerformer = string.Empty;
        MutableCueTrack? currentTrack = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var fileMatch = FileLineRegex().Match(line);
            if (fileMatch.Success)
            {
                currentFile = ResolveCueAudioPath(folder, fileMatch.Groups["path"].Value);
                currentTrack = null;
                continue;
            }

            var performerMatch = PerformerLineRegex().Match(line);
            if (performerMatch.Success)
            {
                if (currentTrack is null)
                {
                    albumPerformer = performerMatch.Groups["value"].Value.Trim();
                }
                else
                {
                    currentTrack.Performer = performerMatch.Groups["value"].Value.Trim();
                }

                continue;
            }

            var titleMatch = TitleLineRegex().Match(line);
            if (titleMatch.Success)
            {
                if (currentTrack is not null)
                {
                    currentTrack.Title = titleMatch.Groups["value"].Value.Trim();
                }

                continue;
            }

            var trackMatch = TrackLineRegex().Match(line);
            if (trackMatch.Success && trackMatch.Groups["type"].Value.Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
            {
                currentTrack = new MutableCueTrack
                {
                    Number = int.Parse(trackMatch.Groups["number"].Value, CultureInfo.InvariantCulture),
                    AudioPath = currentFile,
                    Performer = albumPerformer
                };
                tracks.Add(currentTrack);
                continue;
            }

            var indexMatch = IndexLineRegex().Match(line);
            if (indexMatch.Success &&
                currentTrack is not null &&
                indexMatch.Groups["number"].Value == "01")
            {
                currentTrack.Start = ParseCueTime(indexMatch.Groups["time"].Value);
            }
        }

        var entries = new List<CueAudioEntry>();
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (string.IsNullOrWhiteSpace(track.AudioPath) ||
                !File.Exists(track.AudioPath) ||
                !AudioExtensions.Contains(Path.GetExtension(track.AudioPath)))
            {
                continue;
            }

            var next = tracks
                .Skip(i + 1)
                .FirstOrDefault(candidate => candidate.AudioPath.Equals(track.AudioPath, StringComparison.OrdinalIgnoreCase));
            entries.Add(new CueAudioEntry(
                string.IsNullOrWhiteSpace(track.Title) ? $"Pista {track.Number:00}" : track.Title,
                track.Performer,
                cuePath,
                track.AudioPath,
                track.Start,
                next?.Start));
        }

        return entries;
    }

    private static string ResolveCueAudioPath(string folder, string cueFilePath)
    {
        var path = cueFilePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        var direct = Path.Combine(folder, path);
        if (File.Exists(direct))
        {
            return direct;
        }

        var sameName = Path.Combine(folder, Path.GetFileName(path));
        return sameName;
    }

    private static TimeSpan ParseCueTime(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3)
        {
            return TimeSpan.Zero;
        }

        var minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var seconds = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var frames = int.Parse(parts[2], CultureInfo.InvariantCulture);
        return TimeSpan.FromSeconds((minutes * 60) + seconds + (frames / 75.0));
    }

    [GeneratedRegex("^FILE\\s+\"(?<path>[^\"]+)\"\\s+.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FileLineRegex();

    [GeneratedRegex("^TRACK\\s+(?<number>\\d+)\\s+(?<type>\\S+).*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackLineRegex();

    [GeneratedRegex("^TITLE\\s+\"(?<value>[^\"]*)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLineRegex();

    [GeneratedRegex("^PERFORMER\\s+\"(?<value>[^\"]*)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PerformerLineRegex();

    [GeneratedRegex("^INDEX\\s+(?<number>\\d+)\\s+(?<time>\\d{2,}:\\d{2}:\\d{2})\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IndexLineRegex();

    private sealed class MutableCueTrack
    {
        public int Number { get; init; }

        public string Title { get; set; } = string.Empty;

        public string Performer { get; set; } = string.Empty;

        public string AudioPath { get; init; } = string.Empty;

        public TimeSpan Start { get; set; }
    }
}
