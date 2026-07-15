using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Configuration;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using Jellyfin.Plugin.RecordingTranscoder.Transcoding;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RecordingTranscoder.Services;

public sealed class TranscodeJobService(
    ILibraryManager libraryManager,
    ILibraryMonitor libraryMonitor,
    IRecordingsManager recordingsManager,
    LibraryScopeResolver scopeResolver,
    PipelineLeaseManager leaseManager,
    TranscodeStore store,
    MediaProbeService probeService,
    RecordingTranscoderRunner runner,
    ILogger<TranscodeJobService> logger) : BackgroundService
{
    private readonly Channel<JobRequest> _channel = Channel.CreateUnbounded<JobRequest>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private readonly ConcurrentDictionary<Guid, PipelineLeaseDocument> _pendingLeases = new();
    private readonly ConcurrentDictionary<Guid, byte> _canceled = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();
    private CancellationToken _stoppingToken;
    private string? _lastError;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RecoverTransactionsAsync(cancellationToken).ConfigureAwait(false);
        libraryManager.ItemAdded += OnItemChanged;
        libraryManager.ItemUpdated += OnItemChanged;
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded -= OnItemChanged;
        libraryManager.ItemUpdated -= OnItemChanged;
        foreach (var source in _active.Values)
        {
            source.Cancel();
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public TranscodeQueueStatus GetStatus()
    {
        var records = store.ReadAll();
        return new TranscodeQueueStatus(
            _queued.Count,
            _active.Keys.Order().ToArray(),
            records.Count(record => record.Status == TranscodeStatus.Succeeded),
            records.Count(record => record.Status == TranscodeStatus.Failed),
            records.Count(record => record.Status == TranscodeStatus.Skipped),
            _lastError);
    }

    public async Task<int> EnqueueAllAsync(bool force, CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        var count = 0;
        foreach (var item in scopeResolver.GetScopedVideos(configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds))
        {
            if (await EnqueueAsync(item.Id, force, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    public Task<int> EnqueueReconciliationAsync(CancellationToken cancellationToken)
        => GetConfiguration().AutomaticTranscoding
            ? EnqueueAllAsync(false, cancellationToken)
            : Task.FromResult(0);

    public async Task<bool> EnqueueAsync(Guid itemId, bool force, CancellationToken cancellationToken)
    {
        if (_queued.ContainsKey(itemId) || _active.ContainsKey(itemId))
        {
            return false;
        }

        var item = libraryManager.GetItemById(itemId);
        var configuration = GetConfiguration();
        if (item is null
            || !item.IsFileProtocol
            || string.IsNullOrWhiteSpace(item.Path)
            || !File.Exists(item.Path)
            || !scopeResolver.IsInScope(item.Path, configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds)
            || recordingsManager.GetActiveRecordingInfo(item.Path) is not null)
        {
            return false;
        }

        var fingerprint = SourceFingerprint.FromPath(item.Path);
        var lease = await leaseManager.TryAcquireAsync(
            item.Id,
            item.Path,
            fingerprint,
            Plugin.PluginId,
            PipelineStage.Transcode,
            PipelineLeaseState.Pending,
            cancellationToken).ConfigureAwait(false);
        if (lease is null || !_queued.TryAdd(item.Id, 0))
        {
            if (lease is not null)
            {
                await leaseManager.ReleaseAsync(lease, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        _pendingLeases[item.Id] = lease;

        if (!_channel.Writer.TryWrite(new JobRequest(item.Id, force, lease)))
        {
            _queued.TryRemove(item.Id, out _);
            _pendingLeases.TryRemove(item.Id, out _);
            await leaseManager.ReleaseAsync(lease, cancellationToken).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    public async Task<bool> CancelAsync(Guid itemId, CancellationToken cancellationToken)
    {
        if (_active.TryGetValue(itemId, out var source))
        {
            source.Cancel();
            return true;
        }

        if (_queued.TryRemove(itemId, out _))
        {
            _canceled.TryAdd(itemId, 0);
            if (_pendingLeases.TryRemove(itemId, out var lease))
            {
                await leaseManager.ReleaseAsync(lease, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var workers = Enumerable.Range(0, Math.Clamp(GetConfiguration().MaxConcurrentJobs, 1, 2))
            .Select(_ => RunWorkerAsync(stoppingToken))
            .Append(RefreshPendingLeasesAsync(stoppingToken));
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            _queued.TryRemove(request.ItemId, out _);
            if (_canceled.TryRemove(request.ItemId, out _))
            {
                _pendingLeases.TryRemove(request.ItemId, out _);
                await leaseManager.ReleaseAsync(request.Lease, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            using var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            if (!_active.TryAdd(request.ItemId, source))
            {
                _pendingLeases.TryRemove(request.ItemId, out _);
                await leaseManager.ReleaseAsync(request.Lease, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            try
            {
                await ProcessAsync(request, source.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                logger.LogInformation("Recording transcode canceled for {ItemId}", request.ItemId);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                logger.LogError(ex, "Recording transcode failed for {ItemId}", request.ItemId);
            }
            finally
            {
                _active.TryRemove(request.ItemId, out _);
                _pendingLeases.TryRemove(request.ItemId, out _);
                await leaseManager.ReleaseAsync(request.Lease, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var configuration = GetConfiguration();
        var callerCancellationToken = cancellationToken;
        using var jobTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        jobTimeoutSource.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(configuration.TimeoutMinutes, 1, 2880)));
        cancellationToken = jobTimeoutSource.Token;
        var item = libraryManager.GetItemById(request.ItemId);
        if (item is null
            || string.IsNullOrWhiteSpace(item.Path)
            || !File.Exists(item.Path)
            || !string.Equals(item.Path, request.Lease.SourcePath, StringComparison.Ordinal)
            || !scopeResolver.IsInScope(item.Path, configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds))
        {
            return;
        }

        var path = item.Path;
        if (recordingsManager.GetActiveRecordingInfo(path) is not null)
        {
            return;
        }

        SourceFingerprint? fingerprint = null;
        while (fingerprint is null)
        {
            if (!File.Exists(path) || recordingsManager.GetActiveRecordingInfo(path) is not null)
            {
                return;
            }

            fingerprint = await FileStability.WaitForStableAsync(
                path,
                TimeSpan.FromSeconds(Math.Clamp(configuration.StabilitySeconds, 1, 3600)),
                cancellationToken).ConfigureAwait(false);
        }

        _pendingLeases.TryRemove(request.ItemId, out _);
        var lease = await leaseManager.RefreshAsync(
            request.Lease with { SourceFingerprint = fingerprint },
            PipelineLeaseState.Running,
            cancellationToken,
            PipelineLeaseState.Pending).ConfigureAwait(false);
        if (lease is null)
        {
            return;
        }

        var probe = await probeService.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
        var video = probe.Video ?? throw new InvalidOperationException("Recording has no video stream.");
        if (!request.Force && EncoderPlanner.IsEfficientInputCodec(video.CodecName))
        {
            await WriteRecordAsync(item.Id, path, fingerprint, TranscodeStatus.Skipped, video.CodecName, null, "Already efficiently encoded.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (probe.Streams.Count(stream => stream.CodecType == "video") != 1)
        {
            throw new InvalidOperationException("Recording must contain exactly one video stream.");
        }

        var capabilities = await runner.GetCapabilitiesAsync(configuration.Encoder, cancellationToken).ConfigureAwait(false);
        if (capabilities.SelectedEncoder is null)
        {
            throw new InvalidOperationException($"No usable encoder is available: {capabilities.EncoderTestError}");
        }

        var targetBitRate = EncoderPlanner.CalculateTargetBitRate(video, configuration.BitrateMultiplier);
        EnsureFreeSpace(path, probe, targetBitRate, configuration.FreeSpaceHeadroomBytes);

        var jobId = Guid.NewGuid();
        var directory = Path.GetDirectoryName(path)!;
        var fileName = Path.GetFileName(path);
        var temporaryPath = Path.Combine(directory, $".{fileName}.recording-transcoder-{jobId:N}.tmp");
        var backupPath = Path.Combine(directory, $".{fileName}.recording-transcoder-{jobId:N}.raw-backup");
        var journal = new TransactionJournal
        {
            JobId = jobId,
            ItemId = item.Id,
            SourcePath = path,
            TemporaryPath = temporaryPath,
            BackupPath = backupPath,
            OriginalFingerprint = fingerprint,
            Stage = TransactionStage.Encoding,
            UpdatedUtc = DateTime.UtcNow
        };

        using var heartbeatSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RefreshLeaseAsync(lease, heartbeatSource.Token);
        try
        {
            await WriteRecordAsync(item.Id, path, fingerprint, TranscodeStatus.Running, capabilities.SelectedEncoder, null, null, cancellationToken).ConfigureAwait(false);
            await store.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            var process = await runner.EncodeAsync(path, temporaryPath, probe, capabilities, configuration, cancellationToken).ConfigureAwait(false);
            if (!process.Succeeded)
            {
                throw new InvalidOperationException(process.TimedOut
                    ? "FFmpeg encoding timed out."
                    : $"FFmpeg exited with code {process.ExitCode}: {process.StandardError}");
            }

            var validation = await runner.ValidateAsync(path, temporaryPath, probe, configuration, capabilities, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Message);
            }

            if (recordingsManager.GetActiveRecordingInfo(path) is not null
                || SourceFingerprint.FromPath(path) != fingerprint)
            {
                throw new InvalidOperationException("Recording changed during encoding; the raw source was retained.");
            }

            journal = journal with { Stage = TransactionStage.Validated, UpdatedUtc = DateTime.UtcNow };
            await store.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            var publishedJournal = await PublishAsync(journal, cancellationToken).ConfigureAwait(false);
            var outputFingerprint = SourceFingerprint.FromPath(path);
            await store.WriteAsync(new TranscodeRecord
            {
                ItemId = item.Id,
                SourcePath = path,
                OriginalFingerprint = fingerprint,
                OutputFingerprint = outputFingerprint,
                Status = TranscodeStatus.Succeeded,
                UpdatedUtc = DateTime.UtcNow,
                Encoder = capabilities.SelectedEncoder,
                OriginalBytes = fingerprint.Length,
                OutputBytes = outputFingerprint.Length
            }, cancellationToken).ConfigureAwait(false);
            File.Delete(publishedJournal.BackupPath);
            store.DeleteJournal(publishedJournal.JobId);
        }
        catch (Exception ex)
        {
            await RollBackAsync(journal, CancellationToken.None).ConfigureAwait(false);
            var error = ex is OperationCanceledException && !callerCancellationToken.IsCancellationRequested
                ? "Transcode job timed out; the raw recording was retained."
                : ex is OperationCanceledException
                    ? "Transcode canceled; the raw recording was retained."
                : ex.Message;
            await WriteRecordAsync(item.Id, path, fingerprint, TranscodeStatus.Failed, capabilities.SelectedEncoder, null, error, CancellationToken.None).ConfigureAwait(false);

            throw;
        }
        finally
        {
            heartbeatSource.Cancel();
            await ObserveHeartbeatAsync(heartbeat, item.Id).ConfigureAwait(false);
            if (File.Exists(temporaryPath) && (File.Exists(path) || File.Exists(backupPath)))
            {
                File.Delete(temporaryPath);
            }
            else if (File.Exists(temporaryPath))
            {
                logger.LogCritical(
                    "Preserving temporary output {TemporaryPath} because it is the only remaining copy for {ItemId}",
                    temporaryPath,
                    item.Id);
            }
        }
    }

    private async Task<TransactionJournal> PublishAsync(TransactionJournal journal, CancellationToken cancellationToken)
    {
        var monitoringPaused = false;
        try
        {
            libraryMonitor.ReportFileSystemChangeBeginning(journal.SourcePath);
            monitoringPaused = true;
            File.Move(journal.SourcePath, journal.BackupPath);
            journal = journal with { Stage = TransactionStage.BackupCreated, UpdatedUtc = DateTime.UtcNow };
            await store.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);

            File.Move(journal.TemporaryPath, journal.SourcePath);
            journal = journal with { Stage = TransactionStage.Published, UpdatedUtc = DateTime.UtcNow };
            await store.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);

            _ = await probeService.ProbeAsync(journal.SourcePath, cancellationToken).ConfigureAwait(false);
            libraryMonitor.ReportFileSystemChangeComplete(journal.SourcePath, true);
            monitoringPaused = false;
            libraryMonitor.ReportFileSystemChanged(journal.SourcePath);

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (libraryManager.FindByPath(journal.SourcePath, false) is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            if (libraryManager.FindByPath(journal.SourcePath, false) is null)
            {
                throw new InvalidOperationException("Jellyfin did not resolve the published recording path.");
            }

            journal = journal with { Stage = TransactionStage.Refreshed, UpdatedUtc = DateTime.UtcNow };
            await store.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            return journal;
        }
        finally
        {
            if (monitoringPaused)
            {
                libraryMonitor.ReportFileSystemChangeComplete(journal.SourcePath, true);
            }
        }
    }

    private async Task RollBackAsync(TransactionJournal journal, CancellationToken cancellationToken)
    {
        if (File.Exists(journal.BackupPath))
        {
            if (File.Exists(journal.SourcePath))
            {
                File.Delete(journal.SourcePath);
            }

            File.Move(journal.BackupPath, journal.SourcePath);
            libraryMonitor.ReportFileSystemChanged(journal.SourcePath);
        }
        else if (File.Exists(journal.SourcePath))
        {
            DeleteIfPresent(journal.TemporaryPath);
        }
        else
        {
            logger.LogCritical(
                "Rollback for transaction {JobId} found no raw source or backup. Preserving temporary file {TemporaryPath} and transaction journal.",
                journal.JobId,
                File.Exists(journal.TemporaryPath) ? journal.TemporaryPath : "(missing)");
            return;
        }

        store.DeleteJournal(journal.JobId);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task RecoverTransactionsAsync(CancellationToken cancellationToken)
    {
        foreach (var journal in store.ReadJournals())
        {
            try
            {
                var sourceExists = File.Exists(journal.SourcePath);
                var backupExists = File.Exists(journal.BackupPath);
                var temporaryExists = File.Exists(journal.TemporaryPath);
                var record = await store.ReadAsync(journal.ItemId, cancellationToken).ConfigureAwait(false);
                SourceFingerprint? currentFingerprint = null;
                if (sourceExists)
                {
                    currentFingerprint = SourceFingerprint.FromPath(journal.SourcePath);
                }

                switch (DetermineRecoveryAction(journal, sourceExists, backupExists, temporaryExists, record, currentFingerprint))
                {
                    case TransactionRecoveryAction.FinishPublished:
                        File.Delete(journal.BackupPath);
                        DeleteIfPresent(journal.TemporaryPath);
                        store.DeleteJournal(journal.JobId);
                        libraryMonitor.ReportFileSystemChanged(journal.SourcePath);
                        logger.LogInformation("Finished committed recording transaction {JobId}", journal.JobId);
                        break;
                    case TransactionRecoveryAction.RestoreBackup:
                        DeleteIfPresent(journal.SourcePath);
                        File.Move(journal.BackupPath, journal.SourcePath);
                        DeleteIfPresent(journal.TemporaryPath);
                        store.DeleteJournal(journal.JobId);
                        libraryMonitor.ReportFileSystemChanged(journal.SourcePath);
                        logger.LogWarning("Restored raw recording for interrupted transaction {JobId}", journal.JobId);
                        break;
                    case TransactionRecoveryAction.CleanupIncomplete:
                        DeleteIfPresent(journal.TemporaryPath);
                        store.DeleteJournal(journal.JobId);
                        libraryMonitor.ReportFileSystemChanged(journal.SourcePath);
                        logger.LogWarning("Cleaned up incomplete recording transaction {JobId}", journal.JobId);
                        break;
                    case TransactionRecoveryAction.PreserveOnlyCopy:
                        logger.LogCritical(
                            "Transaction {JobId} has no source or backup. Preserving temporary file {TemporaryPath} and journal for manual recovery.",
                            journal.JobId,
                            temporaryExists ? journal.TemporaryPath : "(missing)");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                logger.LogError(ex, "Unable to recover recording transaction {JobId}", journal.JobId);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static TransactionRecoveryAction DetermineRecoveryAction(
        TransactionJournal journal,
        bool sourceExists,
        bool backupExists,
        bool temporaryExists,
        TranscodeRecord? record,
        SourceFingerprint? currentFingerprint)
    {
        if (backupExists)
        {
            var committedOutput = journal.Stage == TransactionStage.Refreshed
                && sourceExists
                && record is { Status: TranscodeStatus.Succeeded, OutputFingerprint: not null }
                && record.OutputFingerprint == currentFingerprint;
            return committedOutput
                ? TransactionRecoveryAction.FinishPublished
                : TransactionRecoveryAction.RestoreBackup;
        }

        if (sourceExists)
        {
            return TransactionRecoveryAction.CleanupIncomplete;
        }

        _ = temporaryExists;
        return TransactionRecoveryAction.PreserveOnlyCopy;
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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

    private async Task RefreshPendingLeasesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            foreach (var pair in _pendingLeases.ToArray())
            {
                if (!_pendingLeases.TryGetValue(pair.Key, out var current) || current.LeaseId != pair.Value.LeaseId)
                {
                    continue;
                }

                var refreshed = await leaseManager.RefreshAsync(
                    current,
                    PipelineLeaseState.Pending,
                    cancellationToken,
                    PipelineLeaseState.Pending).ConfigureAwait(false);
                if (refreshed is not null)
                {
                    _pendingLeases.TryUpdate(pair.Key, refreshed, current);
                }
            }
        }
    }

    private Task WriteRecordAsync(
        Guid itemId,
        string path,
        SourceFingerprint fingerprint,
        TranscodeStatus status,
        string? encoder,
        SourceFingerprint? output,
        string? error,
        CancellationToken cancellationToken)
        => store.WriteAsync(new TranscodeRecord
        {
            ItemId = itemId,
            SourcePath = path,
            OriginalFingerprint = fingerprint,
            OutputFingerprint = output,
            Status = status,
            UpdatedUtc = DateTime.UtcNow,
            Encoder = encoder,
            OriginalBytes = fingerprint.Length,
            OutputBytes = output?.Length,
            Error = error
        }, cancellationToken);

    private static void EnsureFreeSpace(string path, MediaProbe probe, long targetBitRate, long headroomBytes)
    {
        var estimatedBytes = probe.DurationSeconds > 0
            ? (long)(probe.DurationSeconds * (targetBitRate + 1_000_000) / 8)
            : new FileInfo(path).Length;
        var required = estimatedBytes + Math.Max(0, headroomBytes);
        var fullPath = Path.GetFullPath(path);
        var drive = DriveInfo.GetDrives()
            .Where(candidate => candidate.IsReady && LibraryScopeResolver.ContainsPath(candidate.RootDirectory.FullName, fullPath))
            .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
            .FirstOrDefault()
            ?? new DriveInfo(Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("Unable to determine recording volume."));
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException($"Insufficient free space. At least {required} bytes are required.");
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (!GetConfiguration().AutomaticTranscoding || eventArgs.Item is null)
        {
            return;
        }

        _ = EnqueueAsync(eventArgs.Item.Id, false, _stoppingToken);
    }

    private async Task ObserveHeartbeatAsync(Task task, Guid itemId)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Recording-transcode lease heartbeat stopped for {ItemId}", itemId);
        }
    }

    private static PluginConfiguration GetConfiguration() => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private sealed record JobRequest(Guid ItemId, bool Force, PipelineLeaseDocument Lease);
}
