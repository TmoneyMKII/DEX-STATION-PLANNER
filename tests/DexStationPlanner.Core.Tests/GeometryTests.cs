using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Tests;

public class GeometryTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Straight_segment_has_chord_length_and_midpoint()
    {
        var s = new Segment(new Vec2(0, 0), new Vec2(3, 4));
        Assert.False(s.IsArc);
        Assert.Equal(5, s.Length, Tol);
        Assert.Equal(new Vec2(1.5, 2), s.PointAt(2.5));
    }

    [Fact]
    public void Positive_bulge_semicircle_bows_right_of_chord_and_sweeps_ccw()
    {
        // From (1,0) to (-1,0) with bulge 1: counterclockwise half circle through (0,1).
        var s = new Segment(new Vec2(1, 0), new Vec2(-1, 0), 1);
        Assert.True(s.IsArc);
        Assert.Equal(Math.PI, s.Sweep, Tol);
        Assert.Equal(1, s.Radius, Tol);
        Assert.Equal(0, s.Center.X, Tol);
        Assert.Equal(0, s.Center.Y, Tol);
        Assert.Equal(Math.PI, s.Length, Tol);
        var mid = s.PointAt(s.Length / 2);
        Assert.Equal(0, mid.X, Tol);
        Assert.Equal(1, mid.Y, Tol);
    }

    [Fact]
    public void Negative_bulge_mirrors_the_arc()
    {
        var s = new Segment(new Vec2(1, 0), new Vec2(-1, 0), -1);
        var mid = s.PointAt(s.Length / 2);
        Assert.Equal(-1, mid.Y, Tol);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(-0.7)]
    public void Bulge_from_sagitta_round_trips(double bulge)
    {
        var a = new Vec2(2, 1);
        var b = new Vec2(7, 3);
        var s = new Segment(a, b, bulge);
        var through = s.PointAt(s.Length / 2);
        Assert.Equal(bulge, Segment.BulgeFromSagitta(a, b, through), 1e-9);
    }

    [Fact]
    public void Tangent_on_ccw_arc_is_perpendicular_to_radius()
    {
        var s = new Segment(new Vec2(1, 0), new Vec2(0, 1), Math.Tan(Math.PI / 8));
        var p = s.PointAt(0.3);
        var t = s.TangentAt(0.3);
        Assert.Equal(0, Vec2.Dot(t, p - s.Center), 1e-9);
        Assert.True(Vec2.Cross(p - s.Center, t) > 0, "Tangent should run counterclockwise");
    }

    [Fact]
    public void Project_distance_finds_closest_point_on_arc()
    {
        var s = new Segment(new Vec2(1, 0), new Vec2(-1, 0), 1);
        var d = s.ProjectDistance(new Vec2(0, 5));
        Assert.Equal(Math.PI / 2, d, 1e-9);
    }

    [Fact]
    public void Exact_area_of_full_circle_from_four_quarter_arcs()
    {
        var b = Math.Tan(Math.PI / 8);
        var loop = new List<Segment>
        {
            new(new Vec2(2, 0), new Vec2(0, 2), b),
            new(new Vec2(0, 2), new Vec2(-2, 0), b),
            new(new Vec2(-2, 0), new Vec2(0, -2), b),
            new(new Vec2(0, -2), new Vec2(2, 0), b),
        };
        Assert.Equal(Math.PI * 4, Polygon.SignedArea(loop), 1e-9);
        Assert.Equal(Math.PI * 4, Polygon.Perimeter(loop), 1e-9);
        // Tessellated area converges from below.
        var ring = Polygon.Tessellate(loop, Math.PI / 180);
        Assert.InRange(Polygon.SignedArea(ring), Math.PI * 4 - 0.01, Math.PI * 4);
    }

    [Fact]
    public void Clockwise_rectangle_has_negative_area()
    {
        var ring = new List<Vec2> { new(0, 0), new(0, 2), new(3, 2), new(3, 0) };
        Assert.Equal(-6, Polygon.SignedArea(ring), Tol);
    }

    [Fact]
    public void Triangulates_concave_l_shape_into_correct_area()
    {
        var ring = new List<Vec2> { new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 3), new(0, 3) };
        var tris = Polygon.Triangulate(ring);
        Assert.Equal((ring.Count - 2) * 3, tris.Count);
        double area = 0;
        for (var i = 0; i < tris.Count; i += 3)
        {
            var tri = new List<Vec2> { ring[tris[i]], ring[tris[i + 1]], ring[tris[i + 2]] };
            var a = Polygon.SignedArea(tri);
            Assert.True(a > 0, "Triangles should be counterclockwise");
            area += a;
        }
        Assert.Equal(Polygon.SignedArea(ring), area, 1e-9);
    }

    [Fact]
    public void Triangulates_clockwise_input_as_counterclockwise_triangles()
    {
        var ring = new List<Vec2> { new(0, 0), new(0, 2), new(3, 2), new(3, 0) };
        var tris = Polygon.Triangulate(ring);
        Assert.Equal(6, tris.Count);
        for (var i = 0; i < tris.Count; i += 3)
            Assert.True(Polygon.SignedArea(new List<Vec2> { ring[tris[i]], ring[tris[i + 1]], ring[tris[i + 2]] }) > 0);
    }

    [Fact]
    public void Pose_applies_rotation_then_translation_and_inverts()
    {
        var pose = new Pose2(new Vec2(10, 5), 90);
        var w = pose.Apply(new Vec2(1, 0));
        Assert.Equal(10, w.X, 1e-9);
        Assert.Equal(6, w.Y, 1e-9);
        var back = pose.Inverse(w);
        Assert.Equal(1, back.X, 1e-9);
        Assert.Equal(0, back.Y, 1e-9);
    }
}
