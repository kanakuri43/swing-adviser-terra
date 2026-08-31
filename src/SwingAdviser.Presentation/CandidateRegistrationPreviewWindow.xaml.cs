using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using MahApps.Metro.Controls;
using SwingAdviser.Application.Positions;
using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Presentation;

public partial class CandidateRegistrationPreviewWindow : MetroWindow
{
    private readonly CandidateRegistrationViewModel _viewModel;

    public CandidateRegistrationPreviewWindow(CandidateRow candidate, ManualTradeRegistrationService registrationService, IInstrumentLookup instrumentLookup)
    {
        _viewModel = new CandidateRegistrationViewModel(candidate, registrationService, instrumentLookup);
        DataContext = _viewModel;
        InitializeComponent();
    }

    private async void ConfirmAndSave(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildRequest(out var summary)) return;
        if (MessageBox.Show(this, summary + "\n\nこの内容を保存しますか？", "約定内容の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _viewModel.SaveAsync();
            MessageBox.Show(this, "利用者が確認した約定を保存しました。注文・発注は行っていません。", "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            _viewModel.ValidationMessage = exception.Message;
        }
    }
}

public sealed class CandidateRegistrationViewModel : ObservableObject
{
    private readonly ManualTradeRegistrationService _registrationService;
    private readonly IInstrumentLookup _instrumentLookup;
    private string _code;
    private string _side;
    private string _executedAtText = string.Empty;
    private string _priceText = string.Empty;
    private string _quantityText = string.Empty;
    private string _strategyKey;
    private string _strategyVersion = "v1";
    private string _closePositionIdText = string.Empty;
    private bool _userConfirmed;
    private string _validationMessage = string.Empty;

    public CandidateRegistrationViewModel(CandidateRow candidate, ManualTradeRegistrationService registrationService, IInstrumentLookup instrumentLookup)
    {
        _registrationService = registrationService;
        _instrumentLookup = instrumentLookup;
        _code = candidate.Code;
        _side = candidate.Direction;
        _strategyKey = candidate.Strategy;
    }

    public string Code { get => _code; set => Set(ref _code, value); }
    public string Side { get => _side; set => Set(ref _side, value); }
    public string ExecutedAtText { get => _executedAtText; set => Set(ref _executedAtText, value); }
    public string PriceText { get => _priceText; set => Set(ref _priceText, value); }
    public string QuantityText { get => _quantityText; set => Set(ref _quantityText, value); }
    public string StrategyKey { get => _strategyKey; set => Set(ref _strategyKey, value); }
    public string StrategyVersion { get => _strategyVersion; set => Set(ref _strategyVersion, value); }
    public string ClosePositionIdText { get => _closePositionIdText; set => Set(ref _closePositionIdText, value); }
    public bool UserConfirmed { get => _userConfirmed; set => Set(ref _userConfirmed, value); }
    public string ValidationMessage { get => _validationMessage; set => Set(ref _validationMessage, value); }
    public ObservableCollection<AllocationDraft> Allocations { get; } = [];

    public bool TryBuildRequest(out string summary)
    {
        summary = string.Empty;
        ValidationMessage = string.Empty;
        if (!TryParseExecution(out var executedAt, out var price, out var quantity)) return false;
        var isClose = !string.IsNullOrWhiteSpace(ClosePositionIdText);
        if (isClose && (!int.TryParse(ClosePositionIdText, out var positionId) || positionId <= 0 || Allocations.Count == 0))
        {
            ValidationMessage = "決済では対象ポジション ID と、少なくとも1件の lot 割当が必要です。";
            return false;
        }
        summary = $"{(isClose ? "決済" : "新規建")}\n銘柄: {Code}\n方向: {Side}\n約定日時: {executedAt:yyyy-MM-dd HH:mm zzz}\n価格: {price} JPY\n株数: {quantity}";
        return true;
    }

    public async Task SaveAsync()
    {
        if (!TryBuildRequest(out _)) throw new InvalidOperationException(ValidationMessage);
        TryParseExecution(out var executedAt, out var price, out var quantity);
        if (string.IsNullOrWhiteSpace(ClosePositionIdText))
        {
            var instrumentId = await _instrumentLookup.FindCurrentInstrumentIdByCodeAsync(Code) ?? throw new InvalidOperationException("銘柄コードを現在の銘柄マスタで確認できません。更新後に再試行してください。");
            await _registrationService.RegisterOpenAsync(new ManualOpenTradeRequest(instrumentId, Side, executedAt, price, quantity, "JPY", StrategyKey, StrategyVersion, UserConfirmed: UserConfirmed));
            return;
        }
        var allocations = Allocations.Select(draft => new ManualLotAllocationInput(int.Parse(draft.MarginLotIdText, CultureInfo.InvariantCulture), decimal.Parse(draft.QuantityText, CultureInfo.InvariantCulture))).ToArray();
        await _registrationService.RegisterCloseAsync(new ManualCloseTradeRequest(int.Parse(ClosePositionIdText, CultureInfo.InvariantCulture), executedAt, price, quantity, "JPY", allocations, UserConfirmed: UserConfirmed));
    }

    private bool TryParseExecution(out DateTimeOffset executedAt, out decimal price, out int quantity)
    {
        executedAt = default;
        price = default;
        quantity = default;
        if (!DateTimeOffset.TryParseExact(ExecutedAtText, ["yyyy-MM-dd HH:mm zzz", "yyyy-MM-dd H:mm zzz"], CultureInfo.InvariantCulture, DateTimeStyles.None, out executedAt) || executedAt.Offset != TimeSpan.FromHours(9) ||
            !decimal.TryParse(PriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out price) || !int.TryParse(QuantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) || price <= 0 || quantity <= 0)
        {
            ValidationMessage = "約定日時（+09:00 を含む）、価格、株数を利用者が正しく入力してください。";
            return false;
        }
        return true;
    }
}

public sealed class AllocationDraft : ObservableObject
{
    private string _marginLotIdText = string.Empty;
    private string _quantityText = string.Empty;
    public string MarginLotIdText { get => _marginLotIdText; set => Set(ref _marginLotIdText, value); }
    public string QuantityText { get => _quantityText; set => Set(ref _quantityText, value); }
}
