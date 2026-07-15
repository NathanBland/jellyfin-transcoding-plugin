using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RecordingPipeline;

public static class AtomicJsonStore
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A file path with a directory is required.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Preserve the original serialization or replacement exception.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original serialization or replacement exception.
                }
            }
        }
    }
}
