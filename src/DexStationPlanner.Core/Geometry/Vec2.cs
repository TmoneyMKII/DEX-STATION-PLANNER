namespace DexStationPlanner.Core.Geometry;

/// <summary>
/// A 2D point or vector in meters. Plan coordinates are X = east/right, Y = north/up.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(DexStationPlanner.Core.Serialization.Vec2JsonConverter))]
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    /// <summary>Angle from +X axis, counterclockwise, in radians.</summary>
    public double Angle => Math.Atan2(Y, X);

    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-12 ? Zero : new Vec2(X / len, Y / len);
    }

    /// <summary>Normal pointing to the left of this direction (counterclockwise 90°).</summary>
    public Vec2 LeftNormal => new(-Y, X);

    /// <summary>Normal pointing to the right of this direction (clockwise 90°).</summary>
    public Vec2 RightNormal => new(Y, -X);

    public Vec2 Rotate(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Z component of the 3D cross product; positive when b is counterclockwise from a.</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static Vec2 FromAngle(double radians, double length = 1) =>
        new(Math.Cos(radians) * length, Math.Sin(radians) * length);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
