namespace DexStationPlanner.Core.Geometry;

/// <summary>A 2D rigid transform: rotate by <see cref="RotationDegrees"/> (counterclockwise) then translate.</summary>
public readonly record struct Pose2(Vec2 Position, double RotationDegrees)
{
    public static readonly Pose2 Identity = new(Vec2.Zero, 0);

    public double RotationRadians => RotationDegrees * Math.PI / 180;

    public Vec2 Apply(Vec2 local) => Position + local.Rotate(RotationRadians);
    public Vec2 ApplyDirection(Vec2 local) => local.Rotate(RotationRadians);
    public Vec2 Inverse(Vec2 world) => (world - Position).Rotate(-RotationRadians);

    public static double NormalizeDegrees(double degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }
}
