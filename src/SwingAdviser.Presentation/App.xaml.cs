using System.Windows;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.DailyUpdates;
using SwingAdviser.Infrastructure.Positions;

namespace SwingAdviser.Presentation;

public partial class App : System.Windows.Application
{
    private RuntimePositionServices? _services;
    private RuntimeAiCheckServices? _aiServices;
    private RuntimeDailyUpdateServices? _dailyUpdateServices;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        try
        {
            _services = RuntimePositionServices.Create();
            _aiServices = RuntimeAiCheckServices.Create();
            _dailyUpdateServices = RuntimeDailyUpdateServices.Create(_aiServices.Queue);
            _ = Task.Run(async () =>
            {
                await _aiServices.Queue.RecoverInterruptedAsync(CancellationToken.None);
                await _aiServices.Queue.ProcessAvailableAsync(CancellationToken.None);
            });
            new MainWindow(_services.Registration, _services.InstrumentLookup, _services.OverviewReader, _aiServices.Queue, _aiServices.Queue, _dailyUpdateServices.Runner, _dailyUpdateServices.OverviewReader).Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"データベースを開始できません。保存先を確認してください。\n{exception.Message}", "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _services?.Dispose();
        _dailyUpdateServices?.Dispose();
        base.OnExit(eventArgs);
    }
}
