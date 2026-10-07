using Avalonia.Controls;
using Avalonia.Interactivity;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class ProjectsView : UserControl
{
    public ProjectsView()
    {
        InitializeComponent();
    }

    /// <summary>"Open folder": pick an existing project folder and open it right away (the old
    /// "Add + Open" box, minus the typing).</summary>
    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await FolderPicker.PickAsync(this, "Choose an existing project folder");
        if (path == null) return;

        vm.OpenExistingPath = path;
        vm.OpenExistingCommand.Execute(null);
    }
}
