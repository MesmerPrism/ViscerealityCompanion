using System.Windows;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class StudyClockAlignmentWindow : Window
{
    public StudyClockAlignmentWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Clock Alignment";
    }
}
