using Jellyfin.Plugin.RecordingPipeline;

namespace Jellyfin.Plugin.CommercialSkipper.Tests;

public sealed class ProcessAndStabilityTests
{
    [Fact]
    public async Task ProcessRunner_ReportsFailureExitCode()
    {
        var result = await new ProcessRunner().RunAsync(
            "/bin/sh",
            ["-c", "printf failure >&2; exit 7"],
            TimeSpan.FromSeconds(5),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("failure", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessRunner_KillsTimedOutProcess()
    {
        var result = await new ProcessRunner().RunAsync(
            "/bin/sh",
            ["-c", "sleep 5"],
            TimeSpan.FromMilliseconds(50),
            null,
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FileStability_DetectsStableAndChangingFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "stable");
            Assert.NotNull(await FileStability.WaitForStableAsync(path, TimeSpan.FromMilliseconds(20), CancellationToken.None));

            var check = FileStability.WaitForStableAsync(path, TimeSpan.FromMilliseconds(150), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            await File.AppendAllTextAsync(path, " changed");
            Assert.Null(await check);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
