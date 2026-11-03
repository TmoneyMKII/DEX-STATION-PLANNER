using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DexStationPlanner.App.Services;
using DexStationPlanner.App.ViewModels;

namespace DexStationPlanner.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Enter commits a TextBox edit (bindings otherwise update only when focus leaves).
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.KeyDownEvent, new KeyEventHandler(OnTextBoxKeyDown));

        var settings = AppSettings.Load();
        ThemeService.Apply(settings.Theme);

        var vm = new MainViewModel(settings);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        HandleArguments(vm, e.Args);
    }

    /// <summary>
    /// <c>DexStationPlanner [project.dexstation] [--example] [--page Project|Modules|Stations|Globe|Settings]</c>
    /// </summary>
    private static void HandleArguments(MainViewModel vm, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--example") vm.OpenExampleCommand.Execute(null);
            else if (args[i] == "--page" && i + 1 < args.Length && Enum.TryParse<AppPage>(args[++i], true, out var page)) vm.CurrentPage = page;
            else if (File.Exists(args[i])) _ = vm.LoadAsync(args[i]); // e.g. double-clicking a .dexstation file
        }
    }

    private static void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { AcceptsReturn: false } box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.SelectAll();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "error.log"), $"[{DateTime.Now:u}] {e.Exception}\n\n");
        }
        catch (IOException)
        {
        }
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to error.log in {AppSettings.Folder}. " +
                        "Your project is still open; save it under a new name if you're worried about its state.",
            "DEX Station Planner", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
