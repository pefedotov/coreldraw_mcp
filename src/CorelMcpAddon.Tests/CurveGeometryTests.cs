using System;
using System.Collections.Generic;
using Xunit;
using CorelMcp.Tools;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// Geometry of the drawing tools. The VGCore append calls themselves can only be exercised
    /// against a running CorelDRAW, but the maths that decides where the handles and vertices land
    /// is pure — and that is where the smoothed-curve and star defects lived.
    /// </summary>
    public class CurveGeometryTests
    {
        private static List<ToolPoint> Points(params double[] xAndY)
        {
            var points = new List<ToolPoint>();
            for (var i = 0; i < xAndY.Length; i += 2) points.Add(new ToolPoint(xAndY[i], xAndY[i + 1]));
            return points;
        }

        private static void AssertClose(double expected, double actual, double tolerance = 1e-9)
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                "Expected " + expected + " but was " + actual + ".");
        }

        private static void AssertClose(ToolPoint expected, ToolPoint actual, double tolerance = 1e-9)
        {
            AssertClose(expected.X, actual.X, tolerance);
            AssertClose(expected.Y, actual.Y, tolerance);
        }

        // ---- smoothing -------------------------------------------------------------------

        /// <summary>
        /// The two handles of a node are mirror images through it, and each is parallel to the
        /// chord between the node's neighbours — the Catmull-Rom tangent.
        /// </summary>
        [Fact]
        public void OutgoingAndIncoming_AreMirroredTangents()
        {
            var points = Points(0, 0, 4, 6, 9, 2);
            var outgoing = CurveGeometry.Outgoing(points, 1, false);
            var incoming = CurveGeometry.Incoming(points, 1, false);

            AssertClose(2 * points[1].X, incoming.X + outgoing.X);
            AssertClose(2 * points[1].Y, incoming.Y + outgoing.Y);

            // Parallel to the chord (next - previous), i.e. the cross product vanishes.
            var chordX = points[2].X - points[0].X;
            var chordY = points[2].Y - points[0].Y;
            AssertClose(0, (outgoing.X - points[1].X) * chordY - (outgoing.Y - points[1].Y) * chordX);
        }

        /// <summary>
        /// A collinear run must stay a straight run: the handles lie on the line itself.
        /// </summary>
        [Fact]
        public void CollinearPoints_KeepHandlesOnTheLine()
        {
            var points = Points(0, 0, 5, 0, 10, 0);
            AssertClose(new ToolPoint(5 + 10.0 / 6, 0), CurveGeometry.Outgoing(points, 1, false));
            AssertClose(new ToolPoint(5 - 10.0 / 6, 0), CurveGeometry.Incoming(points, 1, false));
        }

        /// <summary>
        /// An open path's end node has no outer neighbour, so the missing one is mirrored through
        /// it. That yields a tangent of one third of the first span rather than the degenerate
        /// midpoint the old fallback produced.
        /// </summary>
        [Fact]
        public void OpenPath_EndNodeGetsTheMirroredTangent()
        {
            var points = Points(0, 0, 1, 1, 2, 0);
            AssertClose(new ToolPoint(1.0 / 3, 1.0 / 3), CurveGeometry.Outgoing(points, 0, false));

            var last = CurveGeometry.Incoming(points, 2, false);
            // Mirrored neighbour of the last node is (3, -1), so the tangent is ((3,-1)-(1,1))/6.
            AssertClose(new ToolPoint(2 - 2.0 / 6, 2.0 / 6), last);
        }

        /// <summary>A closed path has no ends: the neighbours of the first node wrap around.</summary>
        [Fact]
        public void ClosedPath_WrapsAroundTheSeam()
        {
            var points = Points(0, 0, 10, 0, 10, 10, 0, 10);

            AssertClose(new ToolPoint(10.0 / 6, -10.0 / 6), CurveGeometry.Outgoing(points, 0, true));
            AssertClose(new ToolPoint(-10.0 / 6, 10.0 / 6), CurveGeometry.Incoming(points, 0, true));
            AssertClose(new ToolPoint(-10.0 / 6, 10 - 10.0 / 6), CurveGeometry.Outgoing(points, 3, true));
        }

        // ---- star ------------------------------------------------------------------------

        [Fact]
        public void Star_BuildsTwoVerticesPerRayAlternatingTheRadius()
        {
            var vertices = CurveGeometry.Star(0, 0, 10, 5, 0.4);

            Assert.Equal(10, vertices.Count);
            for (var i = 0; i < vertices.Count; i++)
            {
                var radius = Math.Sqrt(vertices[i].X * vertices[i].X + vertices[i].Y * vertices[i].Y);
                AssertClose(i % 2 == 0 ? 10 : 4, radius, 1e-9);
            }
        }

        /// <summary>Four-pointed stars used to fail inside CorelDRAW; the vertex list must not.</summary>
        [Fact]
        public void Star_SupportsFourPoints()
        {
            var vertices = CurveGeometry.Star(5, 5, 3, 4, 0.5);

            Assert.Equal(8, vertices.Count);
            var radius = Math.Sqrt(Math.Pow(vertices[0].X - 5, 2) + Math.Pow(vertices[0].Y - 5, 2));
            AssertClose(3, radius, 1e-9);
        }

        [Fact]
        public void Star_FirstRayPointsUp()
        {
            var vertices = CurveGeometry.Star(0, 0, 10, 6, 0.4);

            AssertClose(0, vertices[0].X);
            AssertClose(10, vertices[0].Y);
        }

        [Theory]
        [InlineData(2, 0.4)]
        [InlineData(3, 0.0)]
        [InlineData(3, 1.0)]
        [InlineData(3, -0.5)]
        public void Star_RejectsOutOfRangeArguments(int points, double innerRatio)
        {
            Assert.Throws<ArgumentException>(() => CurveGeometry.Star(0, 0, 10, points, innerRatio));
        }

        [Fact]
        public void Star_RejectsNonPositiveRadius()
        {
            Assert.Throws<ArgumentException>(() => CurveGeometry.Star(0, 0, 0, 5, 0.4));
        }
    }
}
