using DiscUtils.Iso9660;

namespace ZenithAudio.Core.Audio;

public sealed record IsoAudioEntry(string Title, string Extension, string IsoPath, string InternalPath, long Size);

public static class IsoImageBrowser
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf",
        ".dff",
        ".flac",
        ".wav",
        ".aiff",
        ".aif",
        ".alac",
        ".mp3",
        ".aac",
        ".ogg"
    };

    public static Task<List<IsoAudioEntry>> ScanAsync(string isoPath)
    {
        return Task.Run(() =>
        {
            using var stream = File.OpenRead(isoPath);
            using var cd = new CDReader(stream, joliet: true);
            var entries = new List<IsoAudioEntry>();
            ScanDirectory(cd, @"\", isoPath, entries);
            return entries;
        });
    }

    public static Task<string> ExtractToTemporaryFileAsync(IsoAudioEntry entry)
    {
        return Task.Run(() =>
        {
            var cacheFolder = Path.Combine(Path.GetTempPath(), "ZenithAudio", "IsoExtract");
            Directory.CreateDirectory(cacheFolder);

            var safeName = string.Join("_", entry.InternalPath.Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(cacheFolder, $"{Path.GetFileNameWithoutExtension(entry.IsoPath)}-{safeName}");

            if (File.Exists(outputPath) && new FileInfo(outputPath).Length == entry.Size)
            {
                return outputPath;
            }

            using var isoStream = File.OpenRead(entry.IsoPath);
            using var cd = new CDReader(isoStream, joliet: true);
            using var input = cd.OpenFile(entry.InternalPath, FileMode.Open);
            using var output = File.Create(outputPath);
            input.CopyTo(output);
            return outputPath;
        });
    }

    public static void CleanupTemporaryFiles()
    {
        var cacheFolder = Path.Combine(Path.GetTempPath(), "ZenithAudio", "IsoExtract");
        try
        {
            if (Directory.Exists(cacheFolder))
            {
                Directory.Delete(cacheFolder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ScanDirectory(CDReader cd, string path, string isoPath, List<IsoAudioEntry> entries)
    {
        foreach (var file in cd.GetFiles(path))
        {
            var extension = Path.GetExtension(file);
            if (!AudioExtensions.Contains(extension))
            {
                continue;
            }

            var info = cd.GetFileInfo(file);
            entries.Add(new IsoAudioEntry(
                Path.GetFileNameWithoutExtension(file),
                extension,
                isoPath,
                file,
                info.Length));
        }

        foreach (var directory in cd.GetDirectories(path))
        {
            ScanDirectory(cd, directory, isoPath, entries);
        }
    }
}
