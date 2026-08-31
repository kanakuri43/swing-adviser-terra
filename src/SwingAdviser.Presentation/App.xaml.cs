using System.Windows;
using SwingAdviser.Infrastructure.Positions;

namespace SwingAdviser.Presentation;

public partial class App : System.Windows.Application
{
    private RuntimePositionServices? _services;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        try
        {
            _services = RuntimePositionServices.Create();
            new MainWindow(_services.Registration, _services.InstrumentLookup, _services.OverviewReader).Show();
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
        base.OnExit(eventArgs);
    }
}
