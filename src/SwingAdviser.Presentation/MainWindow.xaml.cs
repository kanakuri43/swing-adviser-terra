namespace SwingAdviser.Presentation;

public partial class MainWindow
{
    private readonly SwingAdviser.Application.Positions.ManualTradeRegistrationService _registrationService;
    private readonly SwingAdviser.Application.Positions.IInstrumentLookup _instrumentLookup;
    private readonly SwingAdviser.Application.Positions.IManualPositionOverviewReader _overviewReader;
    private readonly ViewModels.MainWindowViewModel _viewModel;

    public MainWindow(SwingAdviser.Application.Positions.ManualTradeRegistrationService registrationService, SwingAdviser.Application.Positions.IInstrumentLookup instrumentLookup, SwingAdviser.Application.Positions.IManualPositionOverviewReader overviewReader)
    {
        _registrationService = registrationService;
        _instrumentLookup = instrumentLookup;
        _overviewReader = overviewReader;
        _viewModel = new ViewModels.MainWindowViewModel();
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.ReloadManualRecordsAsync(_overviewReader);
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
}
