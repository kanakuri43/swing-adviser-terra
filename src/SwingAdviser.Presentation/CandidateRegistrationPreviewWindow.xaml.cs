using MahApps.Metro.Controls;
using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Presentation;

public partial class CandidateRegistrationPreviewWindow : MetroWindow
{
    public CandidateRegistrationPreviewWindow(CandidateRow candidate)
    {
        DataContext = new CandidateRegistrationPreviewViewModel(candidate);
        InitializeComponent();
    }
}

public sealed class CandidateRegistrationPreviewViewModel
{
    public CandidateRegistrationPreviewViewModel(CandidateRow candidate)
    {
        CodeAndName = $"{candidate.Code}  {candidate.Name}";
        Direction = candidate.Direction;
    }

    public string CodeAndName { get; }

    public string Direction { get; }
}
