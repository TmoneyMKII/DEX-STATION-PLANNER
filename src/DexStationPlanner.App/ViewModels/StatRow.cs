namespace DexStationPlanner.App.ViewModels;

/// <summary>One label/value line in a stats panel. <see cref="Tone"/> is "", "good" or "warn".</summary>
public sealed record StatRow(string Label, string Value, string Tone = "");
