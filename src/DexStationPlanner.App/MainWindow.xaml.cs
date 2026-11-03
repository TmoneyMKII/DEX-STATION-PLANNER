using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DexStationPlanner.App.Services;
using DexStationPlanner.App.ViewModels;

namespace DexStationPlanner.App;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        AppStatus.Changed += (_, message) => StatusText.Text = message;
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void NewButton_Click(object sender, RoutedEventArgs e) => OpenMenuBelow((Button)sender);

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenMenu.Items.Clear();
        OpenMenu.Items.Add(new MenuItem { Header = "Browse…", Command = Vm.OpenProjectCommand, InputGestureText = "Ctrl+O" });
        if (Vm.RecentFiles.Count > 0)
        {
            OpenMenu.Items.Add(new Separator());
            foreach (var path in Vm.RecentFiles)
                OpenMenu.Items.Add(new MenuItem
                {
                    Header = Path.GetFileNameWithoutExtension(path),
                    ToolTip = path,
                    Command = Vm.OpenRecentCommand,
                    CommandParameter = path,
                });
        }
        OpenMenuBelow((Button)sender);
    }

    private static void OpenMenuBelow(Button button)
    {
        if (button.ContextMenu is not { } menu) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e) => Vm.IsDarkTheme = !Vm.IsDarkTheme;

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeConfirmed) { base.OnClosing(e); return; }
        if (!Vm.IsDirty) { base.OnClosing(e); return; }

        // Ask asynchronously (saving may show a dialog), then close for real if allowed.
        e.Cancel = true;
        if (await Vm.ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            await Dispatcher.InvokeAsync(Close);
        }
    }
}
