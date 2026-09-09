namespace SwingAdviser.Presentation;

public partial class MainWindow
{
    private readonly SwingAdviser.Application.Positions.ManualTradeRegistrationService _registrationService;
    private readonly SwingAdviser.Application.Positions.IInstrumentLookup _instrumentLookup;
    private readonly SwingAdviser.Application.Positions.IManualPositionOverviewReader _overviewReader;
    private readonly SwingAdviser.Application.Analysis.IAiCheckQueueController _aiQueue;
    private readonly SwingAdviser.Application.Analysis.IAiCheckOverviewReader _aiOverview;
    private readonly SwingAdviser.Application.DailyUpdates.IDailyUpdateExecutionService _dailyUpdateRunner;
    private readonly SwingAdviser.Application.DailyUpdates.IDailyUpdateOverviewReader _dailyUpdateOverview;
    private readonly ViewModels.MainWindowViewModel _viewModel;
    private CancellationTokenSource? _dailyUpdateCancellation;
    private readonly System.Windows.Threading.DispatcherTimer _aiQueueRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private int _aiQueueRefreshInFlight;

    public MainWindow(SwingAdviser.Application.Positions.ManualTradeRegistrationService registrationService, SwingAdviser.Application.Positions.IInstrumentLookup instrumentLookup, SwingAdviser.Application.Positions.IManualPositionOverviewReader overviewReader, SwingAdviser.Application.Analysis.IAiCheckQueueController aiQueue, SwingAdviser.Application.Analysis.IAiCheckOverviewReader aiOverview, SwingAdviser.Application.DailyUpdates.IDailyUpdateExecutionService dailyUpdateRunner, SwingAdviser.Application.DailyUpdates.IDailyUpdateOverviewReader dailyUpdateOverview)
    {
        _registrationService = registrationService;
        _instrumentLookup = instrumentLookup;
        _overviewReader = overviewReader;
        _aiQueue = aiQueue;
        _aiOverview = aiOverview;
        _dailyUpdateRunner = dailyUpdateRunner;
        _dailyUpdateOverview = dailyUpdateOverview;
        _viewModel = new ViewModels.MainWindowViewModel();
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            await ReloadDisplayedDataAsync("保存済みの分析結果");
            _viewModel.MarkPreviousDailyUpdateAsInterrupted();
            _aiQueueRefreshTimer.Start();
        };
        Closed += (_, _) => _aiQueueRefreshTimer.Stop();
        _aiQueueRefreshTimer.Tick += RefreshAiQueueProgress;
    }

    private async void RunDailyUpdate(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_dailyUpdateCancellation is not null) return;
        _dailyUpdateCancellation = new CancellationTokenSource();
        _viewModel.BeginDailyUpdate();
        var startedAt = DateTimeOffset.Now;
        var heartbeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        heartbeat.Tick += (_, _) => _viewModel.ReportDailyUpdateHeartbeat(DateTimeOffset.Now - startedAt);
        heartbeat.Start();
        try
        {
            var progress = new Progress<SwingAdviser.Application.DailyUpdates.DailyUpdateStepProgress>(_viewModel.ReportDailyUpdate);
            // The data refresh and scan perform many database operations. Keep those continuations off
            // the WPF dispatcher; Progress<T> still posts each stage update back to the UI thread.
            var result = await Task.Run(
                () => _dailyUpdateRunner.RunAsync(progress, _dailyUpdateCancellation.Token),
                _dailyUpdateCancellation.Token);
            _viewModel.EndDailyUpdate($"日次分析更新が{DisplayRunStatus(result.Status)}しました。候補・保有・AI状態を実データから更新しました。AIチェックはバックグラウンドで継続する場合があります。");
        }
        catch (OperationCanceledException)
        {
            _viewModel.EndDailyUpdate("日次分析更新を中止しました。完了済みステップまでの結果と失敗状況を表示します。");
        }
        catch (Exception exception)
        {
            _viewModel.EndDailyUpdate("日次分析更新を開始できませんでした。保存済みの結果を表示しています。");
            System.Windows.MessageBox.Show(this, $"日次分析更新を開始できませんでした。\n{exception.Message}", "更新エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            heartbeat.Stop();
            _dailyUpdateCancellation.Dispose();
            _dailyUpdateCancellation = null;
            await ReloadDisplayedDataAsync("日次分析更新後の結果");
        }
    }

    private void CancelDailyUpdate(object sender, System.Windows.RoutedEventArgs e)
    {
        _dailyUpdateCancellation?.Cancel();
    }

    private void OpenYahooFinanceChart(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.DependencyObject source || FindVisualAncestor<System.Windows.Controls.Button>(source) is not null)
            return;

        var row = FindVisualAncestor<System.Windows.Controls.DataGridRow>(source);
        var code = row?.DataContext switch
        {
            ViewModels.CandidateRow candidate => candidate.Code,
            ViewModels.PositionRow position => position.Code,
            ViewModels.ExecutionRow execution => execution.Code,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(code) || !System.Text.RegularExpressions.Regex.IsMatch(code, "^\\d{4,5}$"))
            return;

        var chartUrl = $"https://finance.yahoo.co.jp/quote/{code}.T/chart";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chartUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(this, $"Yahoo!ファイナンスのチャートを開けませんでした。\n{exception.Message}", "ブラウザ起動エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private static T? FindVisualAncestor<T>(System.Windows.DependencyObject source) where T : System.Windows.DependencyObject
    {
        for (System.Windows.DependencyObject? current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T result) return result;
        return null;
    }

    private async Task ReloadDisplayedDataAsync(string operation)
    {
        _viewModel.BeginDisplayReload(operation);
        var succeeded = false;
        try
        {
            await _viewModel.ReloadManualRecordsAsync(_overviewReader);
            await _viewModel.ReloadAiChecksAsync(_aiOverview);
            await _viewModel.ReloadDailyUpdateAsync(_dailyUpdateOverview);
            succeeded = true;
        }
        catch (Exception)
        {
            // A malformed or interrupted persisted run must never terminate the WPF dispatcher on startup or after an update.
            _viewModel.ReportDisplayReloadFailure();
        }
        finally
        {
            _viewModel.EndDisplayReload(succeeded);
        }
    }

    private async void RefreshAiQueueProgress(object? sender, EventArgs e)
    {
        // User-initiated attempts are intentionally excluded from the daily-update progress card,
        // but their candidate rows still need polling until they reach a terminal status.
        var hasActiveCandidateCheck = _viewModel.Candidates.Any(candidate => candidate.AiStatus is "待機中" or "実行中");
        if (_viewModel.IsUpdateRunning || !hasActiveCandidateCheck || Interlocked.Exchange(ref _aiQueueRefreshInFlight, 1) != 0) return;
        try
        {
            await _viewModel.ReloadAiChecksAsync(_aiOverview);
        }
        catch (Exception)
        {
            // The next timer tick retries. A transient SQLite lock must not make the window unresponsive.
        }
        finally
        {
            Volatile.Write(ref _aiQueueRefreshInFlight, 0);
        }
    }

    private static string DisplayRunStatus(string status) => status switch
    {
        "Succeeded" => "完了",
        "PartiallySucceeded" => "一部完了",
        "Failed" => "失敗",
        _ => status,
    };

    private async void QueueCandidateAiCheck(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow candidate) return;
        await QueueAiChecksAsync([candidate]);
    }

    private async void QueueSelectedCandidateAiChecks(object sender, System.Windows.RoutedEventArgs e)
    {
        await QueueAiChecksAsync(CandidateGrid.SelectedItems.OfType<ViewModels.CandidateRow>());
    }

    private async void CancelOrRetryAiCheck(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow { LatestAiAttemptId: int attemptId } candidate) return;
        if (candidate.CanCancelAiCheck) await _aiQueue.CancelQueuedAsync(attemptId);
        else if (candidate.CanRetryAiCheck) { await _aiQueue.RetryAsync(attemptId); _ = Task.Run(() => _aiQueue.ProcessAvailableAsync()); }
        await _viewModel.ReloadAiChecksAsync(_aiOverview);
    }

    private void ShowAiCheckDetail(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow candidate) return;
        var text = candidate.AiStatus == "情報不足"
            ? "AIは情報不足です。Neutral（中立）とは異なります。\n\n" + (candidate.AiSummary ?? string.Empty)
            : candidate.AiStatus == "旧結果"
                ? "これは以前の分析時点の結果です。現在の候補判断には使用しません。\n\n" + (candidate.AiSummary ?? string.Empty)
                : $"AI状態: {candidate.AiStatus}\nVerdict: {candidate.AiVerdict ?? "未取得"}\n{candidate.AiVerdictAlignment ?? string.Empty}\n\n{candidate.AiSummary ?? candidate.AiStatusDescription}";
        System.Windows.MessageBox.Show(this, text + "\n\nAI結果は判断支援情報であり、注文・約定を生成しません。", "AIチェック結果", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private async Task QueueAiChecksAsync(IEnumerable<ViewModels.CandidateRow> selected)
    {
        var ids = selected.Where(candidate => candidate.CanQueueAiCheck).Select(candidate => candidate.CandidateResultId!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            System.Windows.MessageBox.Show(this, "AIチェックする未実行の実データ候補を選択してください。実行中・成功・旧結果は重複投入しません。", "AIチェック", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = await _aiQueue.EnqueueUserAsync(ids);
        if (result.QueuedCount == 0)
        {
            System.Windows.MessageBox.Show(this, "選択した候補はすでに実行中または待機中のため、AIチェックを追加しませんでした。", "AIチェック", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        else
        {
            _ = Task.Run(() => _aiQueue.ProcessAvailableAsync());
        }

        await _viewModel.ReloadAiChecksAsync(_aiOverview);
    }

    private void ShowCandidateRegistrationPreview(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow candidate)
        {
            return;
        }

        var result = new CandidateRegistrationPreviewWindow(candidate, _registrationService, _instrumentLookup)
        {
            Owner = this,
        }.ShowDialog();
        if (result == true) _ = _viewModel.ReloadManualRecordsAsync(_overviewReader);
    }

    private void ShowPositionRegistrationPreview(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.PositionRow position)
        {
            return;
        }

        try
        {
            var result = new CandidateRegistrationPreviewWindow(position, _registrationService, _instrumentLookup)
            {
                Owner = this,
            }.ShowDialog();
            if (result == true) _ = _viewModel.ReloadManualRecordsAsync(_overviewReader);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"決済約定の入力画面を開けませんでした。\n{exception.Message}", "画面エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
