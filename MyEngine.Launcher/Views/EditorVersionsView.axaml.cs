using Avalonia.Controls;
using Avalonia.Interactivity;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class EditorVersionsView : UserControl
{
    public EditorVersionsView()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var path = await FolderPicker.PickAsync(this, "Choose the editor's build output folder");
        if (path != null) vm.EditorVersions.NewFolderPath = path;
    }
}
