using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DexStationPlanner.App.Controls;
using DexStationPlanner.App.Editors;
using DexStationPlanner.App.Services;
using DexStationPlanner.App.ViewModels;
using DexStationPlanner.Core.Model;
using DexStationPlanner.Core.Units;

namespace DexStationPlanner.App.Views;

public partial class ModulesPage : UserControl
{
    private Point? _dragStart;
    private MainViewModel? _main;

    public ModulesPage()
    {
        InitializeComponent();
        Plan.EditCompleted += (_, _) => _main?.CheckpointNow();
        Plan.PointerWorldMoved += (_, p) =>
            AppStatus.Set($"X {UnitFormatter.Length(p.X, UnitContext.Instance.System)}   Y {UnitFormatter.Length(p.Y, UnitContext.Instance.System)}");
        Loaded += (_, _) => _main ??= Window.GetWindow(this)?.DataContext as MainViewModel;
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private ModulesViewModel? Vm => DataContext as ModulesViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ModulesViewModel oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is ModulesViewModel newVm) newVm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Collapse the preview row entirely when the 3D view is off.
        if (e.PropertyName == nameof(ModulesViewModel.ShowPreview) && Vm is { } vm)
            PreviewRow.Height = vm.ShowPreview ? new GridLength(300) : new GridLength(0);
    }

    /// <summary>Single-letter tool shortcuts while the plan (not a text box) has focus.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.OriginalSource is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;
        ModuleTool? tool = e.Key switch
        {
            Key.V => ModuleTool.Select,
            Key.S => ModuleTool.Shell,
            Key.W => ModuleTool.Wall,
            Key.A => ModuleTool.ArcWall,
            Key.D => ModuleTool.Door,
            Key.N => ModuleTool.Window,
            Key.C => ModuleTool.Connector,
            _ => null,
        };
        if (tool is { } t)
        {
            vm.Tool = t;
            Plan.Focus();
            e.Handled = true;
        }
    }

    private void NewModuleButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        TemplateMenu.PlacementTarget = button;
        TemplateMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        TemplateMenu.DataContext = DataContext;
        TemplateMenu.IsOpen = true;
    }

    private void TemplateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem { DataContext: Core.Modules.ModuleTemplate template })
        {
            Vm?.NewModuleCommand.Execute(template);
            Plan.Focus();
        }
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        Plan.RotateSelection(1);
        Plan.Focus();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        Plan.DeleteSelection();
        Plan.Focus();
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        Plan.ZoomToFit();
        Preview.ZoomToFit();
    }

    private void EquipmentList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(EquipmentList);

    private void EquipmentList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(EquipmentList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (ItemUnder(e.OriginalSource) is not { } def) return;
        _dragStart = null;
        var data = new DataObject(FloorPlanCanvas.EquipmentDragFormat, def.Id);
        DragDrop.DoDragDrop(EquipmentList, data, DragDropEffects.Copy);
    }

    private void EquipmentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource) is { } def) Vm?.PlaceEquipmentCommand.Execute(def);
        Plan.Focus();
    }

    private static EquipmentDefinition? ItemUnder(object source)
    {
        // Clicks can land on text Runs, which aren't Visuals, so fall back to the logical tree.
        for (var d = source as DependencyObject; d is not null;
             d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ListBoxItem { DataContext: EquipmentDefinition def }) return def;
        return null;
    }
}
