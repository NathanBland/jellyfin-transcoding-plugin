namespace Jellyfin.Plugin.RecordingPipeline;

public static class FileStability
{
    public static async Task<SourceFingerprint?> WaitForStableAsync(
        string path,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        SourceFingerprint before;
        try
        {
            before = SourceFingerprint.FromPath(path);
        }
        catch (IOException)
        {
            return null;
        }

        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

        try
        {
            var after = SourceFingerprint.FromPath(path);
            return before == after ? after : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
