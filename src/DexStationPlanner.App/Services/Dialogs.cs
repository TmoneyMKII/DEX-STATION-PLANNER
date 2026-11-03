using System.Windows;
using DexStationPlanner.Core.Serialization;
using Microsoft.Win32;

namespace DexStationPlanner.App.Services;

/// <summary>Thin wrappers over the standard Windows dialogs.</summary>
public static class Dialogs
{
    private const string ProjectFilter = "DEX Station project (*.dexstation)|*.dexstation";
    private const string ModuleFilter = "DEX module (*.dexmodule)|*.dexmodule";

    public static string? OpenProject() => Open(ProjectFilter);
    public static string? SaveProject(string suggestedName) => Save(ProjectFilter, suggestedName, ProjectSerializer.FileExtension);
    public static string? OpenModule() => Open(ModuleFilter);
    public static string? SaveModule(string suggestedName) => Save(ModuleFilter, suggestedName, ProjectSerializer.ModuleFileExtension);

    private static string? Open(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null;
    }

    private static string? Save(string filter, string suggestedName, string extension)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = SafeFileName(suggestedName), DefaultExt = extension, AddExtension = true };
        return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null;
    }

    public static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Untitled" : cleaned;
    }

    public static bool Confirm(string message, string title = "DEX Station Planner") =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>Yes = save, No = discard, Cancel = abort.</summary>
    public static MessageBoxResult AskSaveChanges(string projectName) =>
        MessageBox.Show(Application.Current.MainWindow, $"Save changes to \"{projectName}\"?", "DEX Station Planner",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    public static void Error(string message, string title = "DEX Station Planner") =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
