using DexStationPlanner.Core.Model;

namespace DexStationPlanner.Core.Rendering;

/// <summary>A solid block of wall between two distances along its host and two heights above the floor.</summary>
public readonly record struct WallPiece(double S0, double S1, double Z0, double Z1);

public static class WallPieces
{
    /// <summary>
    /// Splits a wall of the given length and height into solid pieces around its openings:
    /// full-height spans between openings, plus a sill piece below and a header piece above each opening.
    /// Overlapping openings are merged.
    /// </summary>
    public static List<WallPiece> Split(double length, double height, IEnumerable<Opening> openings)
    {
        var holes = openings
            .Select(o => (S0: Math.Max(0, o.Offset - o.Width / 2), S1: Math.Min(length, o.Offset + o.Width / 2),
                          Z0: Math.Clamp(o.SillHeight, 0, height), Z1: Math.Clamp(o.SillHeight + o.Height, 0, height)))
            .Where(h => h.S1 - h.S0 > 1e-6 && h.Z1 - h.Z0 > 1e-6)
            .OrderBy(h => h.S0)
            .ToList();

        var pieces = new List<WallPiece>();
        var cursor = 0.0;
        foreach (var h in holes)
        {
            var s0 = Math.Max(h.S0, cursor);
            if (s0 > cursor + 1e-6) pieces.Add(new WallPiece(cursor, s0, 0, height));
            if (h.S1 <= s0 + 1e-6) continue; // Fully covered by a previous opening.
            if (h.Z0 > 1e-6) pieces.Add(new WallPiece(s0, h.S1, 0, h.Z0));
            if (h.Z1 < height - 1e-6) pieces.Add(new WallPiece(s0, h.S1, h.Z1, height));
            cursor = h.S1;
        }
        if (cursor < length - 1e-6) pieces.Add(new WallPiece(cursor, length, 0, height));
        return pieces;
    }
}
