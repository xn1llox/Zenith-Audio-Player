using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ZenithAudio.Core.Audio;

public sealed record SacdIsoExtractionResult(IReadOnlyList<string> Tracks, string OutputFolder, string ToolPath, string Log);

public static class SacdIsoExtractor
{
    private const string ToolFileName = "sacd_extract.exe";

    public static bool IsAvailable => FindExecutablePath() is not null;

    public static string ToolHint =>
        $"Selecciona {ToolFileName} desde Ajustes > Herramientas SACD, agregalo en runtimes/win-x64/native o instalalo en el PATH para extraer SACD ISO a DSF sin perdida.";

    public static string LocalToolPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZenithAudio",
        "Tools",
        ToolFileName);

    public static string? CurrentToolPath => FindExecutablePath();

    public static string InstallTool(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("No se encontro el ejecutable seleccionado.", sourcePath);
        }

        if (!Path.GetFileName(sourcePath).Equals(ToolFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"El archivo debe llamarse {ToolFileName}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(LocalToolPath)!);
        File.Copy(sourcePath, LocalToolPath, overwrite: true);
        return LocalToolPath;
    }

    public static Task<SacdIsoExtractionResult> ExtractStereoDsfAsync(string isoPath)
    {
        return Task.Run(() =>
        {
            var toolPath = FindExecutablePath() ?? throw new FileNotFoundException(ToolHint, ToolFileName);
            var outputFolder = GetOutputFolder(isoPath);
            Directory.CreateDirectory(outputFolder);

            var existingTracks = GetExtractedTracks(outputFolder);
            if (existingTracks.Count > 1)
            {
                return new SacdIsoExtractionResult(existingTracks, outputFolder, toolPath, "DSF ya extraidos en cache temporal.");
            }

            if (existingTracks.Count == 1)
            {
                Directory.Delete(outputFolder, recursive: true);
                Directory.CreateDirectory(outputFolder);
            }

            var log = RunExtractor(toolPath, isoPath, outputFolder, stereo: true);
            var tracks = GetExtractedTracks(outputFolder);

            if (tracks.Count == 0)
            {
                log += Environment.NewLine + RunExtractor(toolPath, isoPath, outputFolder, stereo: false);
                tracks = GetExtractedTracks(outputFolder);
            }

            if (tracks.Count == 0)
            {
                throw new InvalidOperationException($"sacd_extract no genero archivos DSF. Log: {log}");
            }

            return new SacdIsoExtractionResult(tracks, outputFolder, toolPath, log);
        });
    }

    public static void CleanupTemporaryFiles()
    {
        var cacheFolder = Path.Combine(Path.GetTempPath(), "ZenithAudio", "SacdIsoExtract");
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

    private static string RunExtractor(string toolPath, string isoPath, string outputFolder, bool stereo)
    {
        var channelMode = stereo ? "-2" : "-m";
        var arguments = $"{channelMode} -s -i\"{isoPath}\" -y\"{outputFolder}\"";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = arguments,
                WorkingDirectory = outputFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var log = $"{output}{Environment.NewLine}{error}".Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sacd_extract termino con codigo {process.ExitCode}. {log}");
        }

        return log;
    }

    private static List<string> GetExtractedTracks(string outputFolder)
    {
        if (!Directory.Exists(outputFolder))
        {
            return new List<string>();
        }

        return Directory
            .EnumerateFiles(outputFolder, "*.dsf", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string GetOutputFolder(string isoPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(isoPath);
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(isoPath))).Substring(0, 12);
        return Path.Combine(Path.GetTempPath(), "ZenithAudio", "SacdIsoExtract", $"{safeName}-{hash}");
    }

    private static string? FindExecutablePath()
    {
        var candidates = new[]
        {
            LocalToolPath,
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", ToolFileName),
            Path.Combine(AppContext.BaseDirectory, ToolFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "ZenithAudio", "runtimes", "win-x64", "native", ToolFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "runtimes", "win-x64", "native", ToolFileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var folder in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var candidate = Path.Combine(folder.Trim(), ToolFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
