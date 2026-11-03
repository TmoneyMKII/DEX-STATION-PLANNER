using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DexStationPlanner.App.Controls;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.App.ViewModels;
using DexStationPlanner.Core.Globe;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.Views;

public partial class StationsPage : UserControl
{
    private Point? _dragStart;

    public StationsPage()
    {
        InitializeComponent();
        Site.EditCompleted += (_, _) => Vm?.CheckpointNow();
        Site.PointerWorldMoved += (_, p) =>
            AppStatus.Set($"E {UnitFormatter.Length(p.X, UnitContext.Instance.System, 1)}   N {UnitFormatter.Length(p.Y, UnitContext.Instance.System, 1)}   (site grid)");
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private StationsViewModel? Vm => DataContext as StationsViewModel;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.OriginalSource is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;
        SiteTool? tool = e.Key switch
        {
            Key.V => SiteTool.Select,
            Key.L => SiteTool.Link,
            Key.U => SiteTool.Utility,
            Key.F => SiteTool.Feature,
            _ => null,
        };
        if (tool is { } t)
        {
            vm.Tool = t;
            Site.Focus();
            e.Handled = true;
        }
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        Site.RotateSelection(1);
        Site.Focus();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        Site.DeleteSelection();
        Site.Focus();
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => Site.ZoomToFit();

    /// <summary>Picking a kind means the user wants to place one, so switch to the Infrastructure tool.</summary>
    private void FeatureKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not { } vm || !IsLoaded || e.RemovedItems.Count == 0) return;
        vm.Tool = SiteTool.Feature;
        Site.Focus();
    }

    private void KnownSiteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KnownSiteBox.SelectedItem is not KnownSite site) return;
        Vm?.UseKnownSiteCommand.Execute(site);
        KnownSiteBox.SelectedItem = null;
    }

    private void PickOnGlobe_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main) return;
        main.CurrentPage = AppPage.Globe;
        GlobePage.RequestPlacement();
    }

    private void ModuleList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(ModuleList);

    private void ModuleList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(ModuleList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (ItemUnder(e.OriginalSource) is not { } def || Vm?.SelectedStation is null) return;
        _dragStart = null;
        DragDrop.DoDragDrop(ModuleList, new DataObject(SitePlanCanvas.ModuleDragFormat, def.Id.ToString()), DragDropEffects.Copy);
    }

    private void ModuleList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource) is { } def && Vm?.SelectedStation is { } station) Site.PlaceModule(station, def);
    }

    private static ModuleDefinition? ItemUnder(object source)
    {
        for (var d = source as DependencyObject; d is not null;
             d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ListBoxItem { DataContext: ModuleDefinition def }) return def;
        return null;
    }
}
