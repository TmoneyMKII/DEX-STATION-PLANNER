using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;

namespace DexStationPlanner.App.Services;

/// <summary>
/// Watches an object graph of <see cref="INotifyPropertyChanged"/> objects and observable collections,
/// and raises <see cref="Changed"/> whenever anything inside it changes. Re-wires itself as children are
/// added, removed or replaced.
/// </summary>
public sealed class DeepChangeObserver : IDisposable
{
    private readonly HashSet<object> _tracked = new(ReferenceEqualityComparer.Instance);
    private readonly object _root;

    public event EventHandler? Changed;

    /// <summary>When true, changes are not reported (used while restoring snapshots).</summary>
    public bool IsSuspended { get; set; }

    public DeepChangeObserver(object root)
    {
        _root = root;
        Attach(root);
    }

    private void Attach(object? node)
    {
        if (node is null || node is string || node.GetType().IsValueType || !_tracked.Add(node)) return;

        if (node is INotifyPropertyChanged npc) npc.PropertyChanged += OnPropertyChanged;
        if (node is INotifyCollectionChanged ncc) ncc.CollectionChanged += OnCollectionChanged;

        if (node is IEnumerable items and not string)
            foreach (var item in items) Attach(item);

        foreach (var child in ChildObjects(node)) Attach(child);
    }

    private static IEnumerable<object> ChildObjects(object node)
    {
        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var t = p.PropertyType;
            if (t.IsValueType || t == typeof(string)) continue;
            if (!typeof(INotifyPropertyChanged).IsAssignableFrom(t) && !typeof(INotifyCollectionChanged).IsAssignableFrom(t)) continue;
            if (p.GetValue(node) is { } value) yield return value;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Plain values (numbers, points, strings) are the common case while dragging; only rewire
        // when a property that holds child objects may have been swapped.
        var prop = e.PropertyName is null ? null : sender?.GetType().GetProperty(e.PropertyName);
        if (prop is null || !(prop.PropertyType.IsValueType || prop.PropertyType == typeof(string)))
            Rewire();
        Raise();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Rewire();
        Raise();
    }

    private void Rewire()
    {
        DetachAll();
        Attach(_root);
    }

    private void Raise()
    {
        if (!IsSuspended) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DetachAll()
    {
        foreach (var node in _tracked)
        {
            if (node is INotifyPropertyChanged npc) npc.PropertyChanged -= OnPropertyChanged;
            if (node is INotifyCollectionChanged ncc) ncc.CollectionChanged -= OnCollectionChanged;
        }
        _tracked.Clear();
    }

    public void Dispose() => DetachAll();
}
