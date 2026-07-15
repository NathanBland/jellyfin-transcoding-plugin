using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Plugin.CommercialSkipper.Analysis;
using Jellyfin.Plugin.CommercialSkipper.Configuration;
using Jellyfin.Plugin.CommercialSkipper.Models;
using Jellyfin.Plugin.RecordingPipeline;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CommercialSkipper.Services;

public sealed class CommercialJobService(
    ILibraryManager libraryManager,
    IRecordingsManager recordingsManager,
    IMediaSegmentManager mediaSegmentManager,
    LibraryScopeResolver scopeResolver,
    CommercialResultStore resultStore,
    ComskipRunner comskipRunner,
    PipelineLeaseManager leaseManager,
    ILogger<CommercialJobService> logger) : BackgroundService
{
    private readonly Channel<JobRequest> _channel = Channel.CreateUnbounded<JobRequest>(new UnboundedChannelOptions
    {
        SingleWriter = false,
        SingleReader = false
    });
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private readonly ConcurrentDictionary<Guid, byte> _canceled = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();
    private CancellationToken _stoppingToken;
    private string? _lastError;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded += OnItemChanged;
        libraryManager.ItemUpdated += OnItemChanged;
        libraryManager.ItemRemoved += OnItemRemoved;
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        }

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded -= OnItemChanged;
        libraryManager.ItemUpdated -= OnItemChanged;
        libraryManager.ItemRemoved -= OnItemRemoved;
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        foreach (var source in _active.Values)
        {
            source.Cancel();
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public CommercialQueueStatus GetStatus()
    {
        var records = resultStore.ReadAll();
        return new CommercialQueueStatus(
            _queued.Count,
            _active.Keys.Order().ToArray(),
            records.Count(record => record.Status == CommercialAnalysisStatus.Succeeded),
            records.Count(record => record.Status == CommercialAnalysisStatus.Failed),
            _lastError);
    }

    public int EnqueueAll(bool force)
    {
        var configuration = GetConfiguration();
        var items = scopeResolver.GetScopedVideos(configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds);
        var count = 0;
        foreach (var item in items)
        {
            if (Enqueue(item.Id, force))
            {
                count++;
            }
        }

        return count;
    }

    public int EnqueueReconciliation()
        => GetConfiguration().AutomaticAnalysis ? EnqueueAll(false) : 0;

    public bool Enqueue(Guid itemId, bool force)
    {
        if (!_queued.TryAdd(itemId, 0))
        {
            return false;
        }

        if (!_channel.Writer.TryWrite(new JobRequest(itemId, force)))
        {
            _queued.TryRemove(itemId, out _);
            return false;
        }

        return true;
    }

    public bool Cancel(Guid itemId)
    {
        if (_active.TryGetValue(itemId, out var source))
        {
            return Cancel(source);
        }

        if (_queued.TryRemove(itemId, out _))
        {
            _canceled.TryAdd(itemId, 0);
            return true;
        }

        return false;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        foreach (var itemId in resultStore.GetItemIds())
        {
            resultStore.Delete(itemId);
            await RefreshSegmentsAsync(itemId, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var workers = Enumerable.Range(0, Math.Clamp(GetConfiguration().MaxConcurrentJobs, 1, 4))
            .Select(_ => RunWorkerAsync(stoppingToken));
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            _queued.TryRemove(request.ItemId, out _);
            if (_canceled.TryRemove(request.ItemId, out _))
            {
                continue;
            }

            using var jobSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            if (!_active.TryAdd(request.ItemId, jobSource))
            {
                continue;
            }

            try
            {
                await ProcessAsync(request, jobSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (jobSource.IsCancellationRequested)
            {
                logger.LogInformation("Commercial analysis canceled for {ItemId}", request.ItemId);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                logger.LogError(ex, "Commercial analysis failed for {ItemId}", request.ItemId);
            }
            finally
            {
                _active.TryRemove(request.ItemId, out _);
            }
        }
    }

    private async Task ProcessAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var item = libraryManager.GetItemById(request.ItemId);
        var configuration = GetConfiguration();
        if (item is null || !item.IsFileProtocol || string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        if (!scopeResolver.IsInScope(item.Path, configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds))
        {
            await RefreshSegmentsAsync(item.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (recordingsManager.GetActiveRecordingInfo(item.Path) is not null)
        {
            return;
        }

        if (!request.Force)
        {
            var completionDeadline = File.GetLastWriteTimeUtc(item.Path).AddSeconds(
                Math.Clamp(configuration.CompletionDelaySeconds, 0, 86400));
            var remainingDelay = completionDeadline - DateTime.UtcNow;
            if (remainingDelay > TimeSpan.Zero)
            {
                await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        var fingerprint = await FileStability.WaitForStableAsync(
            item.Path,
            TimeSpan.FromSeconds(Math.Clamp(configuration.StabilitySeconds, 1, 3600)),
            cancellationToken).ConfigureAwait(false);
        if (fingerprint is null || recordingsManager.GetActiveRecordingInfo(item.Path) is not null)
        {
            return;
        }

        var executable = comskipRunner.ResolveExecutable(configuration.ComskipPath)
            ?? throw new FileNotFoundException("Comskip was not found. Configure its executable path.");
        var ini = ComskipIniBuilder.Build(configuration);
        var configurationHash = ComskipIniBuilder.ComputeHash(executable, ini, configuration);
        var existing = await resultStore.ReadAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (!request.Force
            && existing is { Status: CommercialAnalysisStatus.Succeeded }
            && existing.SourceFingerprint == fingerprint
            && string.Equals(existing.ConfigurationHash, configurationHash, StringComparison.Ordinal))
        {
            return;
        }

        if (existing is not null && existing.SourceFingerprint != fingerprint)
        {
            await resultStore.WriteAsync(new CommercialAnalysisRecord
            {
                ItemId = item.Id,
                SourcePath = item.Path,
                SourceFingerprint = fingerprint,
                ConfigurationHash = configurationHash,
                Status = CommercialAnalysisStatus.Pending,
                LastAttemptUtc = DateTime.UtcNow,
                Segments = []
            }, cancellationToken).ConfigureAwait(false);
            await RefreshSegmentsAsync(item.Id, cancellationToken).ConfigureAwait(false);
        }

        var lease = await leaseManager.TryAcquireAsync(
            item.Id,
            item.Path,
            fingerprint,
            Plugin.PluginId,
            PipelineStage.CommercialScan,
            PipelineLeaseState.Running,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            logger.LogDebug("Commercial analysis deferred because another pipeline stage owns {ItemId}", item.Id);
            return;
        }

        var workDirectory = Path.Combine(Plugin.Instance!.DataDirectory, "work", $"{item.Id:N}-{Guid.NewGuid():N}");
        using var heartbeatSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RefreshLeaseAsync(lease, heartbeatSource.Token);
        try
        {
            var run = await comskipRunner.RunAsync(item.Path, workDirectory, configuration, cancellationToken).ConfigureAwait(false);
            if (!run.Process.Succeeded)
            {
                throw new InvalidOperationException(
                    run.Process.TimedOut
                        ? "Comskip timed out."
                        : $"Comskip exited with code {run.Process.ExitCode}: {run.Process.StandardError}");
            }

            if (!File.Exists(run.EdlPath))
            {
                throw new InvalidOperationException("Comskip did not create the expected EDL output.");
            }

            var segments = EdlParser.Parse(await File.ReadAllTextAsync(run.EdlPath, cancellationToken).ConfigureAwait(false), item.RunTimeTicks);
            await resultStore.WriteAsync(new CommercialAnalysisRecord
            {
                ItemId = item.Id,
                SourcePath = item.Path,
                SourceFingerprint = fingerprint,
                ConfigurationHash = run.ConfigurationHash,
                Status = CommercialAnalysisStatus.Succeeded,
                LastAttemptUtc = DateTime.UtcNow,
                LastSuccessUtc = DateTime.UtcNow,
                DetectorPath = run.Executable,
                ExitCode = run.Process.ExitCode,
                Segments = segments
            }, cancellationToken).ConfigureAwait(false);
            await RefreshSegmentsAsync(item.Id, cancellationToken).ConfigureAwait(false);
            TryDeleteWorkDirectory(workDirectory);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastError = ex.Message;
            await resultStore.WriteAsync(new CommercialAnalysisRecord
            {
                ItemId = item.Id,
                SourcePath = item.Path,
                SourceFingerprint = fingerprint,
                ConfigurationHash = configurationHash,
                Status = CommercialAnalysisStatus.Failed,
                LastAttemptUtc = DateTime.UtcNow,
                LastSuccessUtc = existing?.LastSuccessUtc,
                DetectorPath = executable,
                Error = ex.Message,
                Segments = existing?.SourceFingerprint == fingerprint ? existing.Segments : []
            }, cancellationToken).ConfigureAwait(false);
            if (!configuration.KeepFailedJobDiagnostics && Directory.Exists(workDirectory))
            {
                TryDeleteWorkDirectory(workDirectory);
            }

            throw;
        }
        finally
        {
            heartbeatSource.Cancel();
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Commercial-analysis lease heartbeat stopped for {ItemId}", item.Id);
            }

            await leaseManager.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RefreshLeaseAsync(PipelineLeaseDocument lease, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            if (await leaseManager.RefreshAsync(
                lease,
                PipelineLeaseState.Running,
                cancellationToken,
                PipelineLeaseState.Running).ConfigureAwait(false) is null)
            {
                return;
            }
        }
    }

    private async Task RefreshSegmentsAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = libraryManager.GetItemById(itemId);
        if (item is not null)
        {
            await mediaSegmentManager.RunSegmentPluginProviders(
                item,
                libraryManager.GetLibraryOptions(item),
                false,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs eventArgs)
    {
        var configuration = GetConfiguration();
        if (!configuration.AutomaticAnalysis || eventArgs.Item is null)
        {
            return;
        }

        _ = EnqueueAfterDelayAsync(eventArgs.Item.Id, configuration.CompletionDelaySeconds, _stoppingToken);
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (eventArgs.Item is not null)
        {
            resultStore.Delete(eventArgs.Item.Id);
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration eventArgs)
    {
        _ = RefreshStoredItemsAsync(_stoppingToken);
    }

    private async Task RefreshStoredItemsAsync(CancellationToken cancellationToken)
    {
        foreach (var itemId in resultStore.GetItemIds())
        {
            await RefreshSegmentsAsync(itemId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnqueueAfterDelayAsync(Guid itemId, int delaySeconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 0, 86400)), cancellationToken).ConfigureAwait(false);
            Enqueue(itemId, false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static PluginConfiguration GetConfiguration() => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private static bool Cancel(CancellationTokenSource source)
    {
        source.Cancel();
        return true;
    }

    private void TryDeleteWorkDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to remove Comskip work directory {Path}", path);
        }
    }

    private sealed record JobRequest(Guid ItemId, bool Force);
}
