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
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow { CandidateResultId: int candidateId }) return;
        await _aiQueue.EnqueueUserAsync([candidateId]);
        _ = Task.Run(() => _aiQueue.ProcessAvailableAsync());
        await _viewModel.ReloadAiChecksAsync(_aiOverview);
    }

    private async void QueueSelectedCandidateAiChecks(object sender, System.Windows.RoutedEventArgs e)
    {
        var ids = CandidateGrid.SelectedItems.OfType<ViewModels.CandidateRow>().Where(candidate => candidate.CandidateResultId.HasValue).Select(candidate => candidate.CandidateResultId!.Value).ToArray();
        if (ids.Length == 0)
        {
            System.Windows.MessageBox.Show(this, "AIチェックする候補を選択してください。", "AIチェック", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        await _aiQueue.EnqueueUserAsync(ids);
        _ = Task.Run(() => _aiQueue.ProcessAvailableAsync());
        await _viewModel.ReloadAiChecksAsync(_aiOverview);
    }

    private async void CancelOrRetryAiCheck(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow { LatestAiAttemptId: int attemptId } candidate) return;
        if (candidate.AiStatus == "待機中") await _aiQueue.CancelQueuedAsync(attemptId);
        else if (candidate.AiStatus is "失敗" or "timeout" or "キャンセル" or "情報不足") { await _aiQueue.RetryAsync(attemptId); _ = Task.Run(() => _aiQueue.ProcessAvailableAsync()); }
        await _viewModel.ReloadAiChecksAsync(_aiOverview);
    }

    private void ShowAiCheckDetail(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow candidate) return;
        var text = candidate.AiStatus == "情報不足" ? "AIは情報不足です。Neutral（中立）とは異なります。\n\n" + (candidate.AiSummary ?? string.Empty) : $"Verdict: {candidate.AiVerdict ?? "未取得"}\n{candidate.AiVerdictAlignment ?? string.Empty}\n\n{candidate.AiSummary ?? "結果はまだありません。"}";
        System.Windows.MessageBox.Show(this, text + "\n\nAI結果は判断支援情報であり、注文・約定を生成しません。", "AIチェック結果", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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
