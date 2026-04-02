using System.Windows.Controls;
using System.Windows.Input;

namespace ViscerealityCompanion.App;

public partial class StudyShellView : UserControl
{
    public StudyShellView()
    {
        InitializeComponent();
    }

    private void OnVisualProfilesTablePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (RootScrollViewer is null)
        {
            return;
        }

        e.Handled = true;
        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };
        RootScrollViewer.RaiseEvent(forwardedEvent);
    }
}
