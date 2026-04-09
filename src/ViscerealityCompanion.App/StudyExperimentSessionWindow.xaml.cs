using System.Windows;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class StudyExperimentSessionWindow : Window
{
    public StudyExperimentSessionWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Experiment Session";
    }
}
