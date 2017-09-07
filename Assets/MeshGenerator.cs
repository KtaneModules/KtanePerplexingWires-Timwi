using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Rnd = System.Random;

namespace PerplexingWires
{
    public class MeshGenerator
    {
        public const double _wireRadius = .0025;
        public const double _wireRadiusHighlight = .005;

        const double _wireMaxSegmentDeviation = .001;
        const double _wireMinBézierDeviation = .005;
        const double _wireMaxBézierDeviation = .01;

        const double _bottom = -.02;
        const double _firstControlHeight = .01;
        const double _interpolateHeight = .005;
        const double _firstControlHeightHighlight = .005;
        const double _interpolateHeightHighlight = .003;

        sealed class CPC { public Pt ControlBefore, Point, ControlAfter; }

        public enum WirePiece { Uncut, Cut, Copper }
        public enum Mode { Wire, Highlight, Collider }

        public static Mesh GenerateWire(Pt start, Pt startControl, Pt endControl, Pt end, int numSegments, WirePiece piece, Mode mode, int seed, Pt raiseBy)
        {
            const int bézierSteps = 16;
            var tubeRevSteps = mode == Mode.Collider ? 4 : 16;

            var rnd = new Rnd(seed);
            var thickness = mode != Mode.Wire ? _wireRadiusHighlight : _wireRadius;

            var iStart = startControl * .8 + endControl * .2;
            var iEnd = startControl * .2 + endControl * .8;
            var interpolatedPoints = Ut.NewArray<Pt>(numSegments - 1, i => raiseBy + iStart + (iEnd - iStart) * i / (numSegments - 2));
            var controlPointsB = Ut.NewArray<Pt>(numSegments - 1, i =>
            {
                var p1 = interpolatedPoints[i];
                var p2 = i == numSegments - 2 ? endControl : interpolatedPoints[i + 1];
                var v = (p2 - p1).Normalize() * p1.Distance(p2) * .25;
                var dummy = v.X > .5 ? new Pt(0, 1, 0) : new Pt(1, 0, 0);
                var perpendicular = dummy * v;
                v = (p1 + v).Rotate(p1, p1 + perpendicular, 45 * rnd.NextDouble());
                v = v.Rotate(p1, p2, 360 * rnd.NextDouble());
                return v;
            });
            var controlPointsA = Ut.NewArray<Pt>(numSegments - 1, i => 2 * interpolatedPoints[i] - controlPointsB[i]);

            if (piece == WirePiece.Uncut)
            {
                if (mode == Mode.Collider)
                {
                    var points = new[] { start, startControl }.Concat(interpolatedPoints).Concat(new[] { endControl, end }).ToArray();
                    return toMesh(createFaces(false, true, tubeFromCurve(points, thickness, tubeRevSteps)));
                }
                else
                {
                    var points =
                        new[] { new { ControlBefore = default(Pt), Point = start, ControlAfter = startControl } }
                        .Concat(interpolatedPoints.Select((p, i) => new { ControlBefore = controlPointsA[i], Point = p, ControlAfter = controlPointsB[i] }))
                        .Concat(new[] { new { ControlBefore = endControl, Point = end, ControlAfter = default(Pt) } })
                        .SelectConsecutivePairs(false, (one, two) => bézier(one.Point, one.ControlAfter, two.ControlBefore, two.Point, bézierSteps))
                        .SelectMany((x, i) => i == 0 ? x : x.Skip(1))
                        .ToArray();
                    return toMesh(createFaces(false, true, tubeFromCurve(points, thickness, tubeRevSteps)));
                }
            }

            var partialWire = new Func<IEnumerable<CPC>, IEnumerable<VertexInfo[]>>(pts =>
            {
                var points = pts
                    .SelectConsecutivePairs(false, (one, two) => bézier(one.Point, one.ControlAfter, two.ControlBefore, two.Point, bézierSteps))
                    .SelectMany((x, i) => i == 0 ? x : x.Skip(1))
                    .ToArray();

                var reserveForCopper = 6;
                var discardCopper = 2;

                if (piece == WirePiece.Cut)
                {
                    var tube = tubeFromCurve(points, thickness, tubeRevSteps).SkipLast(reserveForCopper).ToArray();
                    var capCenter = points[points.Length - 1 - reserveForCopper];
                    var normal = capCenter - points[points.Length - 2 - reserveForCopper];
                    var cap = tube[tube.Length - 1].SelectConsecutivePairs(true, (v1, v2) => new[] { capCenter, v2.Point, v1.Point }.Select(p => new VertexInfo { Point = p, Normal = normal }).ToArray()).ToArray();
                    return createFaces(false, true, tube).Concat(cap);
                }
                else
                {
                    var copper = tubeFromCurve(points.TakeLast(reserveForCopper + 2).SkipLast(discardCopper).ToArray(), thickness / 2, tubeRevSteps).Skip(1).ToArray();
                    var copperCapCenter = points[points.Length - 1 - discardCopper];
                    var copperNormal = copperCapCenter - points[points.Length - 2];
                    var copperCap = copper[copper.Length - 1].SelectConsecutivePairs(true, (v1, v2) => new[] { copperCapCenter, v2.Point, v1.Point }.Select(p => new VertexInfo { Point = p, Normal = copperNormal }).ToArray()).ToArray();
                    return createFaces(false, true, copper).Concat(copperCap);
                }
            });

            var cutOffEarly = false;// rnd.Next(2) == 0;
            var angleForward = rnd.Next(2) == 0;
            var rotAngle = (rnd.NextDouble() * 10 + 1) * (angleForward ? -1 : 1);
            var rotAxisStart = start;
            var rotAxisEnd = startControl;
            Func<Pt, Pt> rot = p => p.Rotate(rotAxisStart, rotAxisEnd, rotAngle);
            var beforeCut =
                new[] { new CPC { ControlBefore = default(Pt), Point = start, ControlAfter = startControl } }
                .Concat(interpolatedPoints.Take((cutOffEarly ? numSegments : numSegments + 1) / 2).Select((p, i) => new CPC { ControlBefore = rot(controlPointsA[i]), Point = rot(p), ControlAfter = rot(controlPointsB[i]) }));
            var bcTube = partialWire(beforeCut);

            var cutOffPoint = (cutOffEarly ? numSegments - 2 : numSegments - 1) / 2;
            rotAngle = (rnd.NextDouble() * 10 + 1) * (angleForward ? -1 : 1);
            rotAxisStart = end;
            rotAxisEnd = endControl;
            var afterCut =
                new[] { new CPC { ControlBefore = default(Pt), Point = end, ControlAfter = endControl } }
                .Concat(interpolatedPoints.Skip(cutOffPoint).Select((p, i) => new CPC { ControlBefore = rot(controlPointsB[i + cutOffPoint]), Point = rot(p), ControlAfter = rot(controlPointsA[i + cutOffPoint]) }).Reverse());
            var acTube = partialWire(afterCut);

            return toMesh(bcTube.Concat(acTube).ToArray());
        }

        sealed class VertexInfo
        {
            public Pt Point;
            public Pt Normal;
            public Vector3 V { get { return new Vector3((float) Point.X, (float) Point.Y, (float) Point.Z); } }
            public Vector3 N { get { return new Vector3((float) Normal.X, (float) Normal.Y, (float) Normal.Z); } }
        }

        private static Mesh toMesh(VertexInfo[][] triangles)
        {
            return new Mesh
            {
                vertices = triangles.SelectMany(t => t).Select(v => v.V).ToArray(),
                normals = triangles.SelectMany(t => t).Select(v => v.N).ToArray(),
                triangles = triangles.SelectMany(t => t).Select((v, i) => i).ToArray()
            };
        }

        // Converts a 2D array of vertices into triangles by joining each vertex with the next in each dimension
        private static VertexInfo[][] createFaces(bool closedX, bool closedY, VertexInfo[][] meshData)
        {
            var len = meshData[0].Length;
            return Enumerable.Range(0, meshData.Length).SelectManyConsecutivePairs(closedX, (i1, i2) =>
                Enumerable.Range(0, len).SelectManyConsecutivePairs(closedY, (j1, j2) => new[]
                {
                    // triangle 1
                    new[] { meshData[i1][j1], meshData[i2][j1], meshData[i2][j2] },
                    // triangle 2
                    new[] { meshData[i1][j1], meshData[i2][j2], meshData[i1][j2] }
                }))
                    .ToArray();
        }

        private static VertexInfo[][] tubeFromCurve(Pt[] pts, double radius, int revSteps)
        {
            var normals = new Pt[pts.Length];
            normals[0] = ((pts[1] - pts[0]) * pt(0, 1, 0)).Normalize() * radius;
            for (int i = 1; i < pts.Length - 1; i++)
                normals[i] = normals[i - 1].ProjectOntoPlane((pts[i + 1] - pts[i]) + (pts[i] - pts[i - 1])).Normalize() * radius;
            normals[pts.Length - 1] = normals[pts.Length - 2].ProjectOntoPlane(pts[pts.Length - 1] - pts[pts.Length - 2]).Normalize() * radius;

            var axes = pts.Select((p, i) =>
                i == 0 ? new { Start = pts[0], End = pts[1] } :
                i == pts.Length - 1 ? new { Start = pts[pts.Length - 2], End = pts[pts.Length - 1] } :
                new { Start = p, End = p + (pts[i + 1] - p) + (p - pts[i - 1]) }).ToArray();

            return Enumerable.Range(0, pts.Length)
                .Select(ix => new { Axis = axes[ix], Perp = pts[ix] + normals[ix], Point = pts[ix] })
                .Select(inf => Enumerable.Range(0, revSteps)
                    .Select(i => 360 * i / revSteps)
                    .Select(angle => inf.Perp.Rotate(inf.Axis.Start, inf.Axis.End, angle))
                    .Select(p => new VertexInfo { Point = p, Normal = p - inf.Point }).Reverse().ToArray())
                .ToArray();
        }

        private static IEnumerable<Pt> bézier(Pt start, Pt control1, Pt control2, Pt end, int steps)
        {
            return Enumerable.Range(0, steps)
                .Select(i => (double) i / (steps - 1))
                .Select(t => pow(1 - t, 3) * start + 3 * pow(1 - t, 2) * t * control1 + 3 * (1 - t) * t * t * control2 + pow(t, 3) * end);
        }

        static double sin(double x)
        {
            return Math.Sin(x * Math.PI / 180);
        }

        static double cos(double x)
        {
            return Math.Cos(x * Math.PI / 180);
        }

        static double pow(double x, double y)
        {
            return Math.Pow(x, y);
        }

        static Pt pt(double x, double y, double z)
        {
            return new Pt(x, y, z);
        }

        static T[] newArray<T>(int size, Func<int, T> initialiser)
        {
            var result = new T[size];
            for (int i = 0; i < size; i++)
            {
                result[i] = initialiser(i);
            }
            return result;
        }
    }
}
