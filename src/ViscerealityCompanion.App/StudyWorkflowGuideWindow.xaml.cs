using System.Windows;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class StudyWorkflowGuideWindow : Window
{
    public StudyWorkflowGuideWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Sequential Guide";
    }
}
