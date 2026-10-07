using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MyEngine.Launcher.ViewModels;

namespace MyEngine.Launcher.Views;

public partial class NewProjectView : UserControl
{
    public NewProjectView()
    {
        InitializeComponent();
    }

    /// <summary>Enter moves to the next step, or creates the project on the last one.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter && !e.Handled && DataContext is NewProjectViewModel vm)
        {
            vm.Advance();
            e.Handled = true;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewProjectViewModel vm) return;

        var path = await FolderPicker.PickAsync(this, "Choose a location for the new project");
        if (path != null) vm.Location = path;
    }
}
