using System.Windows;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class StudyTwinEventsWindow : Window
{
    public StudyTwinEventsWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Twin Event Log";
    }
}
