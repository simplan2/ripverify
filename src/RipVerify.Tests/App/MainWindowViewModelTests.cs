namespace RipVerify.Tests.App;

using FluentAssertions;
using NSubstitute;
using RipVerify.App.Services;
using RipVerify.App.ViewModels;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Cue;

public class MainWindowViewModelTests
{
    [Fact]
    public void AddDroppedPathsAddsFolderItem()
    {
        var directory = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            using var viewModel = CreateViewModel();

            viewModel.AddDroppedPaths(new[] { directory.FullName });

            viewModel.Items.Should().ContainSingle();
            viewModel.Items[0].InputPath.Should().Be(directory.FullName);
            viewModel.Items[0].DisplayName.Should().Be(directory.Name);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void OpenDetailShowsAlbumDetail()
    {
        using var viewModel = CreateViewModel();
        var result = new VerificationResult
        {
            Status = VerificationStatus.VerifiedCdRip,
            StatusIsPreliminary = false
        };
        var item = new BatchItemViewModel
        {
            InputPath = @"C:\album",
            DisplayName = "album",
            Result = result
        };

        viewModel.OpenDetailCommand.Execute(item);

        viewModel.IsDetailVisible.Should().BeTrue();
        viewModel.Detail.Should().NotBeNull();
        viewModel.Detail!.Result.Should().BeSameAs(result);
    }

    [Fact]
    public void ClearQueueRemovesAllItems()
    {
        var first = Directory.CreateTempSubdirectory("ripverify-test-");
        var second = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            using var viewModel = CreateViewModel();
            viewModel.AddDroppedPaths(new[] { first.FullName, second.FullName });

            viewModel.ClearQueueCommand.CanExecute(null).Should().BeTrue();
            viewModel.ClearQueueCommand.Execute(null);

            viewModel.Items.Should().BeEmpty();
            viewModel.HasItems.Should().BeFalse();
            viewModel.CanEditQueue.Should().BeFalse();
        }
        finally
        {
            first.Delete(true);
            second.Delete(true);
        }
    }

    [Fact]
    public void RemoveItemCommandRemovesSingleQueueItem()
    {
        var first = Directory.CreateTempSubdirectory("ripverify-test-");
        var second = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            using var viewModel = CreateViewModel();
            viewModel.AddDroppedPaths(new[] { first.FullName, second.FullName });
            var item = viewModel.Items[0];

            item.RemoveCommand.Should().NotBeNull();
            item.RemoveCommand!.CanExecute(item).Should().BeTrue();
            item.RemoveCommand.Execute(item);

            viewModel.Items.Should().ContainSingle()
                .Which.InputPath.Should().Be(second.FullName);
        }
        finally
        {
            first.Delete(true);
            second.Delete(true);
        }
    }

    [Fact]
    public void RemovePendingItemWhileRunning_RemovesImmediately()
    {
        var directory = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            using var viewModel = CreateViewModel();
            viewModel.AddDroppedPaths(new[] { directory.FullName });
            viewModel.IsBatchRunning = true;
            var item = viewModel.Items[0];

            item.RemoveCommand!.Execute(item);

            viewModel.Items.Should().BeEmpty();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task RemoveActiveItem_CancelsTokenAndRemovesAfterProcessingStops()
    {
        var directory = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            IReadOnlyDictionary<string, CancellationToken>? submittedTokens = null;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var batchProcessor = Substitute.For<IBatchProcessor>();
            batchProcessor
                .RunAsync(
                    Arg.Any<IReadOnlyList<ScanInput>>(),
                    Arg.Any<IProgress<BatchProgress>>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<IReadOnlyDictionary<string, CancellationToken>>())
                .Returns(callInfo =>
                {
                    submittedTokens = callInfo.ArgAt<IReadOnlyDictionary<string, CancellationToken>>(3);
                    started.TrySetResult();
                    return release.Task;
                });

            using var viewModel = CreateViewModel(batchProcessor);
            viewModel.AddDroppedPaths(new[] { directory.FullName });
            var item = viewModel.Items[0];

            var runTask = viewModel.StartBatchAsync();
            await started.Task;
            item.State = BatchItemState.Scanning;

            item.RemoveCommand!.Execute(item);

            item.IsCancelling.Should().BeTrue();
            item.IsRemovable.Should().BeFalse();
            submittedTokens![item.InputPath].IsCancellationRequested.Should().BeTrue();
            viewModel.Items.Should().ContainSingle();

            release.SetResult();
            await runTask;

            viewModel.Items.Should().BeEmpty();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task StartBatchSkipsCompletedItems()
    {
        var completed = Directory.CreateTempSubdirectory("ripverify-test-");
        var pending = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            IReadOnlyList<ScanInput>? submitted = null;
            var batchProcessor = Substitute.For<IBatchProcessor>();
            batchProcessor
                .RunAsync(
                    Arg.Do<IReadOnlyList<ScanInput>>(inputs => submitted = inputs),
                    Arg.Any<IProgress<BatchProgress>>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<IReadOnlyDictionary<string, CancellationToken>>())
                .Returns(Task.CompletedTask);

            using var viewModel = CreateViewModel(batchProcessor);
            viewModel.AddDroppedPaths(new[] { completed.FullName, pending.FullName });
            viewModel.Items[0].State = BatchItemState.Completed;

            await viewModel.StartBatchAsync();

            submitted.Should().NotBeNull();
            submitted!.Should().ContainSingle()
                .Which.Paths.Should().ContainSingle()
                .Which.Should().Be(pending.FullName);
            viewModel.BatchTotal.Should().Be(1);
        }
        finally
        {
            completed.Delete(true);
            pending.Delete(true);
        }
    }

    [Fact]
    public void BatchItemShowsDetailsOnlyAfterSuccessfulCompletion()
    {
        var item = new BatchItemViewModel { DisplayName = "Album" };

        item.CanOpenDetails.Should().BeFalse();

        item.Result = new VerificationResult();
        item.CanOpenDetails.Should().BeFalse();

        item.State = BatchItemState.Completed;
        item.CanOpenDetails.Should().BeTrue();

        item.State = BatchItemState.Failed;
        item.CanOpenDetails.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsyncCancelsRunningBatch()
    {
        var directory = Directory.CreateTempSubdirectory("ripverify-test-");

        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken submittedToken = default;
            var batchProcessor = Substitute.For<IBatchProcessor>();
            batchProcessor
                .RunAsync(
                    Arg.Any<IReadOnlyList<ScanInput>>(),
                    Arg.Any<IProgress<BatchProgress>>(),
                    Arg.Do<CancellationToken>(token =>
                    {
                        submittedToken = token;
                        started.TrySetResult();
                    }),
                    Arg.Any<IReadOnlyDictionary<string, CancellationToken>>())
                .Returns(release.Task);

            using var viewModel = CreateViewModel(batchProcessor);
            viewModel.AddDroppedPaths(new[] { directory.FullName });

            var runTask = viewModel.StartBatchAsync();
            await started.Task;
            viewModel.HasRunningWork.Should().BeTrue();

            var stopTask = viewModel.StopAsync();
            submittedToken.IsCancellationRequested.Should().BeTrue();
            release.TrySetResult();
            await stopTask;
            await runTask;

            viewModel.IsBatchRunning.Should().BeFalse();
            viewModel.HasRunningWork.Should().BeFalse();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static MainWindowViewModel CreateViewModel(IBatchProcessor? batchProcessor = null)
    {
        var localization = new LocalizationService();
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new AppSettings());
        var fileValidator = Substitute.For<IFileValidator>();
        batchProcessor ??= Substitute.For<IBatchProcessor>();
        var albumDetail = new AlbumDetailViewModel(
            Substitute.For<ICueToolsRunner>(),
            new CueToolsOutputParser(),
            Substitute.For<ISpectrumAnalyzer>(),
            Substitute.For<IOriginSignalReader>(),
            Substitute.For<IVerificationClassifier>(),
            settingsService,
            localization,
            Substitute.For<IReportDocumentBuilder>(),
            Substitute.For<IReportWriter>());

        return new MainWindowViewModel(localization, settingsService, fileValidator, batchProcessor, albumDetail);
    }
}
