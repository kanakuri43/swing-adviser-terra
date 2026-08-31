namespace SwingAdviser.Presentation;

public partial class MainWindow
{
    private readonly SwingAdviser.Application.Positions.ManualTradeRegistrationService _registrationService;
    private readonly SwingAdviser.Application.Positions.IInstrumentLookup _instrumentLookup;
    private readonly SwingAdviser.Application.Positions.IManualPositionOverviewReader _overviewReader;
    private readonly SwingAdviser.Application.Analysis.IAiCheckQueueController _aiQueue;
    private readonly SwingAdviser.Application.Analysis.IAiCheckOverviewReader _aiOverview;
    private readonly ViewModels.MainWindowViewModel _viewModel;

    public MainWindow(SwingAdviser.Application.Positions.ManualTradeRegistrationService registrationService, SwingAdviser.Application.Positions.IInstrumentLookup instrumentLookup, SwingAdviser.Application.Positions.IManualPositionOverviewReader overviewReader, SwingAdviser.Application.Analysis.IAiCheckQueueController aiQueue, SwingAdviser.Application.Analysis.IAiCheckOverviewReader aiOverview)
    {
        _registrationService = registrationService;
        _instrumentLookup = instrumentLookup;
        _overviewReader = overviewReader;
        _aiQueue = aiQueue;
        _aiOverview = aiOverview;
        _viewModel = new ViewModels.MainWindowViewModel();
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => { await _viewModel.ReloadManualRecordsAsync(_overviewReader); await _viewModel.ReloadAiChecksAsync(_aiOverview); };
    }

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
