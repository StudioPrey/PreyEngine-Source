using Avalonia.Controls;
using Avalonia.Interactivity;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnBrowseLocationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var path = await FolderPicker.PickAsync(this, "Choose where new projects are created");
        if (path != null) vm.DefaultProjectLocation = path;
    }
}
