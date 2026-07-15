using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using Jellyfin.Plugin.RecordingTranscoder.Services;

namespace Jellyfin.Plugin.RecordingTranscoder.Tests;

public sealed class TransactionRecoveryTests
{
    public static TheoryData<TransactionStage> InterruptedStages => new()
    {
        TransactionStage.Encoding,
        TransactionStage.Validated,
        TransactionStage.BackupCreated,
        TransactionStage.Published
    };

    [Theory]
    [MemberData(nameof(InterruptedStages))]
    public void DetermineRecoveryAction_RawBackupWinsForEveryUncommittedStage(TransactionStage stage)
    {
        var journal = Journal(stage);
        var action = TranscodeJobService.DetermineRecoveryAction(journal, true, true, true, null, Fingerprint(2));
        Assert.Equal(TransactionRecoveryAction.RestoreBackup, action);
    }

    [Fact]
    public void DetermineRecoveryAction_FinishesDurablyCommittedPublishedOutput()
    {
        var fingerprint = Fingerprint(2);
        var journal = Journal(TransactionStage.Refreshed);
        var record = Record(TranscodeStatus.Succeeded, fingerprint);

        var action = TranscodeJobService.DetermineRecoveryAction(journal, true, true, false, record, fingerprint);

        Assert.Equal(TransactionRecoveryAction.FinishPublished, action);
    }

    [Fact]
    public void DetermineRecoveryAction_RestoresBackupWhenCommittedFingerprintDoesNotMatch()
    {
        var journal = Journal(TransactionStage.Refreshed);
        var action = TranscodeJobService.DetermineRecoveryAction(
            journal,
            true,
            true,
            false,
            Record(TranscodeStatus.Succeeded, Fingerprint(2)),
            Fingerprint(3));

        Assert.Equal(TransactionRecoveryAction.RestoreBackup, action);
    }

    [Theory]
    [InlineData(TransactionStage.Encoding)]
    [InlineData(TransactionStage.Validated)]
    [InlineData(TransactionStage.Refreshed)]
    public void DetermineRecoveryAction_CleansTemporaryWorkWhenSourceIsPresentWithoutBackup(TransactionStage stage)
    {
        var action = TranscodeJobService.DetermineRecoveryAction(Journal(stage), true, false, true, null, Fingerprint(1));
        Assert.Equal(TransactionRecoveryAction.CleanupIncomplete, action);
    }

    [Fact]
    public void DetermineRecoveryAction_PreservesOnlyRemainingTemporaryCopy()
    {
        var action = TranscodeJobService.DetermineRecoveryAction(Journal(TransactionStage.Encoding), false, false, true, null, null);
        Assert.Equal(TransactionRecoveryAction.PreserveOnlyCopy, action);
    }

    private static TransactionJournal Journal(TransactionStage stage) => new()
    {
        JobId = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        SourcePath = "/recordings/show.ts",
        TemporaryPath = "/recordings/.show.tmp",
        BackupPath = "/recordings/.show.raw-backup",
        OriginalFingerprint = Fingerprint(1),
        Stage = stage,
        UpdatedUtc = DateTime.UtcNow
    };

    private static TranscodeRecord Record(TranscodeStatus status, SourceFingerprint output) => new()
    {
        ItemId = Guid.NewGuid(),
        SourcePath = "/recordings/show.ts",
        OriginalFingerprint = Fingerprint(1),
        OutputFingerprint = output,
        Status = status,
        UpdatedUtc = DateTime.UtcNow
    };

    private static SourceFingerprint Fingerprint(long length) => new(length, DateTime.UnixEpoch);
}
