using DexStationPlanner.Core.Model;

namespace DexStationPlanner.App.Editors;

public enum ModuleTool { Select, Shell, Wall, ArcWall, Door, Window, Connector, Equipment }

public enum SiteTool { Select, Link, Utility }

/// <summary>Selection of the shell edge that starts at <see cref="Start"/>.</summary>
public sealed record EdgeSelection(ShellVertex Start);
