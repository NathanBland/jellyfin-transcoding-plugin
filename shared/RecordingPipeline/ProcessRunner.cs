using System.Diagnostics;
using System.Text;

namespace Jellyfin.Plugin.RecordingPipeline;

public sealed class ProcessRunner
{
    private const int MaxCapturedCharacters = 64 * 1024;

    public async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new BoundedTextBuffer(MaxCapturedCharacters);
        var stderr = new BoundedTextBuffer(MaxCapturedCharacters);
        process.OutputDataReceived += (_, eventArgs) => stdout.AppendLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => stderr.AppendLine(eventArgs.Data);

        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {executable}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (!timedOut)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        process.WaitForExit();
        stopwatch.Stop();
        return new ProcessResult(
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            timedOut,
            stopwatch.Elapsed);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class BoundedTextBuffer(int capacity)
    {
        private readonly StringBuilder _builder = new(capacity);
        private readonly object _sync = new();

        public void AppendLine(string? value)
        {
            if (value is null)
            {
                return;
            }

            lock (_sync)
            {
                _builder.AppendLine(value);
                if (_builder.Length > capacity)
                {
                    _builder.Remove(0, _builder.Length - capacity);
                }
            }
        }

        public override string ToString()
        {
            lock (_sync)
            {
                return _builder.ToString();
            }
        }
    }
}
