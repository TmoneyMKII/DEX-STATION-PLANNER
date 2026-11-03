using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DexStationPlanner.App.Services;
using DexStationPlanner.Core.Editing;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Serialization;
using DexStationPlanner.Core.Stations;

namespace DexStationPlanner.App.ViewModels;

public enum AppPage { Project, Modules, Stations, Globe, Settings }

/// <summary>
/// Owns the open project, file commands, dirty state and the project-wide undo history.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _checkpointTimer;
    private readonly UndoHistory _history;
    private DeepChangeObserver? _observer;
    private bool _restoring;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _history = new UndoHistory(() => ProjectSerializer.Serialize(Project), RestoreSnapshot);
        _history.Changed += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
        _checkpointTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _checkpointTimer.Tick += (_, _) =>
        {
            // Wait until any drag in progress (mouse captured) has finished.
            if (Mouse.Captured is not null) return;
            _checkpointTimer.Stop();
            _history.Checkpoint();
        };

        Modules = new ModulesViewModel(this);
        Stations = new StationsViewModel(this);
        RecentFiles = new ObservableCollection<string>(settings.RecentFiles.Where(File.Exists));
        AttachProject(resetHistory: true);
    }

    public AppSettings Settings { get; }
    public ModulesViewModel Modules { get; }
    public StationsViewModel Stations { get; }
    public ObservableCollection<string> RecentFiles { get; }

    public StationProject Project { get; private set; } = ExampleProject.CreateBlank();

    public DateTime ProjectCreatedLocal => Project.CreatedUtc.ToLocalTime();

    public string? FilePath
    {
        get;
        private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(WindowTitle)); }
    }

    public bool IsDirty
    {
        get;
        private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(WindowTitle)); }
    }

    public string WindowTitle =>
        $"{(IsDirty ? "• " : "")}{(FilePath is null ? Project.Name : Path.GetFileNameWithoutExtension(FilePath))} — DEX Station Planner";

    public AppPage CurrentPage
    {
        get;
        set => SetProperty(ref field, value);
    } = AppPage.Modules;

    public bool IsImperial
    {
        get => Project.DisplayUnits == UnitSystem.Imperial;
        set => Project.DisplayUnits = value ? UnitSystem.Imperial : UnitSystem.Metric;
    }

    public bool IsDarkTheme
    {
        get => ThemeService.Current == AppTheme.Dark;
        set
        {
            var theme = value ? AppTheme.Dark : AppTheme.Light;
            if (theme == ThemeService.Current) return;
            ThemeService.Apply(theme);
            Settings.Theme = theme;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public string CesiumIonToken
    {
        get => Settings.CesiumIonToken;
        set
        {
            if (Settings.CesiumIonToken == value) return;
            Settings.CesiumIonToken = value.Trim();
            SaveSettings();
            OnPropertyChanged();
            IonTokenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when the user changes the Cesium ion token so the globe can reload its layers.</summary>
    public event EventHandler? IonTokenChanged;

    /// <summary>Raised after the project object is replaced (open, new, undo).</summary>
    public event EventHandler? ProjectReplaced;

    #region Project lifecycle

    private void AttachProject(bool resetHistory)
    {
        _observer?.Dispose();
        _observer = new DeepChangeObserver(Project);
        _observer.Changed += OnProjectChanged;
        Project.PropertyChanged += OnProjectPropertyChanged;
        UnitContext.Instance.System = Project.DisplayUnits;
        if (resetHistory) _history.Reset();
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(ProjectCreatedLocal));
        OnPropertyChanged(nameof(IsImperial));
        OnPropertyChanged(nameof(WindowTitle));
        ProjectReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StationProject.DisplayUnits))
        {
            UnitContext.Instance.System = Project.DisplayUnits;
            OnPropertyChanged(nameof(IsImperial));
        }
        if (e.PropertyName == nameof(StationProject.Name)) OnPropertyChanged(nameof(WindowTitle));
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        if (_restoring) return;
        IsDirty = true;
        _checkpointTimer.Stop();
        _checkpointTimer.Start();
    }

    /// <summary>Called by editors when a gesture finishes so it becomes one undo step right away.</summary>
    public void CheckpointNow()
    {
        _checkpointTimer.Stop();
        _history.Checkpoint();
    }

    private void ReplaceProject(StationProject project, string? path, bool resetHistory)
    {
        Project.PropertyChanged -= OnProjectPropertyChanged;
        Project = project;
        FilePath = path;
        AttachProject(resetHistory);
    }

    private void RestoreSnapshot(string json)
    {
        _restoring = true;
        try
        {
            var path = FilePath;
            ReplaceProject(ProjectSerializer.Deserialize(json), path, resetHistory: false);
            IsDirty = true;
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty) return true;
        switch (Dialogs.AskSaveChanges(Project.Name))
        {
            case MessageBoxResult.Yes: return await SaveAsync(saveAs: false);
            case MessageBoxResult.No: return true;
            default: return false;
        }
    }

    [RelayCommand]
    private async Task NewProject()
    {
        if (!await ConfirmDiscardAsync()) return;
        ReplaceProject(ExampleProject.CreateBlank(), null, resetHistory: true);
        IsDirty = false;
        CurrentPage = AppPage.Project;
    }

    [RelayCommand]
    private async Task OpenExample()
    {
        if (!await ConfirmDiscardAsync()) return;
        ReplaceProject(ExampleProject.CreateExample(), null, resetHistory: true);
        IsDirty = false;
        CurrentPage = AppPage.Stations;
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (Dialogs.OpenProject() is { } path) await LoadAsync(path);
    }

    [RelayCommand]
    private async Task OpenRecent(string path)
    {
        if (!await ConfirmDiscardAsync()) return;
        await LoadAsync(path);
    }

    public async Task LoadAsync(string path)
    {
        try
        {
            var project = await ProjectSerializer.LoadAsync(path);
            ReplaceProject(project, path, resetHistory: true);
            IsDirty = false;
            RememberRecent(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Dialogs.Error($"Couldn't open the project.\n\n{ex.Message}");
            RecentFiles.Remove(path);
        }
    }

    [RelayCommand]
    private Task Save() => SaveAsync(saveAs: false);

    [RelayCommand]
    private Task SaveAs() => SaveAsync(saveAs: true);

    public async Task<bool> SaveAsync(bool saveAs)
    {
        var path = FilePath;
        if (saveAs || path is null)
        {
            path = Dialogs.SaveProject(Project.Name);
            if (path is null) return false;
        }
        try
        {
            CheckpointNow();
            await ProjectSerializer.SaveAsync(Project, path);
            FilePath = path;
            IsDirty = false;
            RememberRecent(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"Couldn't save the project.\n\n{ex.Message}");
            return false;
        }
    }

    private void RememberRecent(string path)
    {
        Settings.AddRecent(path);
        SaveSettings();
        RecentFiles.Clear();
        foreach (var p in Settings.RecentFiles) RecentFiles.Add(p);
    }

    private void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Settings are best effort. */ }
    }

    #endregion

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _history.Undo();

    private bool CanUndo() => _history.CanUndo || IsDirty;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _history.Redo();

    private bool CanRedo() => _history.CanRedo;

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;
}
