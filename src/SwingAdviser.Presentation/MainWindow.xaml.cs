namespace SwingAdviser.Presentation;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ShowCandidateRegistrationPreview(object sender, System.Windows.RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not ViewModels.CandidateRow candidate)
        {
            return;
        }

        new CandidateRegistrationPreviewWindow(candidate)
        {
            Owner = this,
        }.ShowDialog();
    }
}
