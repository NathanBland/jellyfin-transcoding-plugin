using Jellyfin.Plugin.CommercialSkipper.Configuration;
using Jellyfin.Plugin.RecordingPipeline;

namespace Jellyfin.Plugin.CommercialSkipper.Analysis;

public sealed record ComskipRunResult(
    ProcessResult Process,
    string Executable,
    string ConfigurationHash,
    string EdlPath,
    string WorkDirectory);

public sealed class ComskipRunner(ProcessRunner processRunner)
{
    public string? ResolveExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(directory => Path.Combine(directory, "comskip")));
        candidates.Add("/opt/homebrew/bin/comskip");
        candidates.Add("/usr/local/bin/comskip");
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<ProcessResult> TestAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var executable = ResolveExecutable(configuration.ComskipPath)
            ?? throw new FileNotFoundException("Comskip was not found. Configure an executable path first.");
        _ = ComskipIniBuilder.Build(configuration);
        return await processRunner.RunAsync(
            executable,
            ["--help"],
            TimeSpan.FromSeconds(10),
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ComskipRunResult> RunAsync(
        string sourcePath,
        string workDirectory,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var executable = ResolveExecutable(configuration.ComskipPath)
            ?? throw new FileNotFoundException("Comskip was not found. Configure an executable path first.");
        Directory.CreateDirectory(workDirectory);
        var ini = ComskipIniBuilder.Build(configuration);
        var iniPath = Path.Combine(workDirectory, "effective-comskip.ini");
        await File.WriteAllTextAsync(iniPath, ini, cancellationToken).ConfigureAwait(false);
        var outputBase = "commercials";
        var arguments = new List<string>
        {
            $"--ini={iniPath}",
            $"--output={workDirectory}",
            $"--output-filename={outputBase}",
            $"--threads={Math.Clamp(configuration.ComskipThreads, 1, 16)}"
        };
        if (configuration.PlayNice)
        {
            arguments.Add("--playnice");
        }

        arguments.Add(sourcePath);
        var result = await processRunner.RunAsync(
            executable,
            arguments,
            TimeSpan.FromMinutes(Math.Clamp(configuration.TimeoutMinutes, 1, 1440)),
            workDirectory,
            cancellationToken).ConfigureAwait(false);
        return new ComskipRunResult(
            result,
            executable,
            ComskipIniBuilder.ComputeHash(executable, ini, configuration),
            Path.Combine(workDirectory, $"{outputBase}.edl"),
            workDirectory);
    }
}
