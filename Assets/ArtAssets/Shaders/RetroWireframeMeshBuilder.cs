using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LudumDare60.Rendering
{
    public enum WireframeEdgeMode { RetroQuads, AllTriangles, FeatureEdges, SparseStructure }

    /// <summary>Reconstructs likely quad diagonals for display only. Never modifies the source mesh.</summary>
    public static class RetroWireframeMeshBuilder
    {
        private sealed class Triangle
        {
            public int A, B, C, Submesh;
            public Vector3 Normal;
            public bool Paired;
            public bool Keep = true;
            public int Opposite(Edge edge) => A != edge.A && A != edge.B ? A : B != edge.A && B != edge.B ? B : C;
        }
        private sealed class Edge
        {
            public int A, B, First, Second = -1, Count;
            public long Key;
            public bool Hidden;
        }
        private struct Candidate
        {
            public Edge Edge;
            public float Score;
        }

        public readonly struct Statistics
        {
            public readonly int InputEdges, VisibleEdges, PairedQuads, RemovedParts;
            public Statistics(int input, int visible, int quads, int removedParts = 0)
            { InputEdges = input; VisibleEdges = visible; PairedQuads = quads; RemovedParts = removedParts; }
        }

        public static Mesh Build(Mesh source, WireframeEdgeMode mode, float quadAngle, float featureAngle, out Statistics statistics,
            int curveSegments = 4, float minimumPartSize = 0, float seamTolerance = 0, float lineSpacing = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.isReadable) throw new InvalidOperationException(source.name + " is not readable. Enable Read/Write or use a readable low-poly proxy.");
            var original = source.vertices;
            var positions = new List<Vector3>();
            var weld = new Dictionary<Vector3, int>();
            var cells = new Dictionary<Vector3Int, List<int>>();
            var vertexMap = new int[original.Length];
            // Exact-position welding joins UV/normal seams without joining nearby, separate details.
            for (var i = 0; i < original.Length; i++)
            {
                if (mode == WireframeEdgeMode.SparseStructure && seamTolerance > 0)
                {
                    vertexMap[i] = WeldPosition(original[i], seamTolerance, positions, cells);
                    continue;
                }
                if (!weld.TryGetValue(original[i], out var id))
                {
                    id = positions.Count;
                    weld.Add(original[i], id);
                    positions.Add(original[i]);
                }
                vertexMap[i] = id;
            }
            var triangles = new List<Triangle>();
            var edges = new Dictionary<long, Edge>();
            var authoredQuadEdges = new List<long>();
            for (var submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var topology = source.GetTopology(submesh);
                if (topology != MeshTopology.Triangles && topology != MeshTopology.Quads) continue;
                var indices = source.GetIndices(submesh);
                var stride = topology == MeshTopology.Quads ? 4 : 3;
                for (var i = 0; i + stride - 1 < indices.Length; i += stride)
                {
                    var a = vertexMap[indices[i]];
                    var b = vertexMap[indices[i + 1]];
                    var c = vertexMap[indices[i + 2]];
                    AddTriangle(a, b, c, submesh, positions, triangles, edges);
                    if (stride != 4) continue;
                    AddTriangle(a, c, vertexMap[indices[i + 3]], submesh, positions, triangles, edges);
                    authoredQuadEdges.Add(Key(a, c));
                }
            }
            var pairedQuads = 0;
            var removedParts = 0;
            if (mode == WireframeEdgeMode.RetroQuads)
            {
                // True authored quads take priority. Triangulated assets can only be inferred heuristically.
                foreach (var key in authoredQuadEdges)
                    if (edges.TryGetValue(key, out var edge) && CanPair(edge, triangles))
                    { Pair(edge, triangles); pairedQuads++; }
                var candidates = new List<Candidate>();
                var normalLimit = Mathf.Cos(Mathf.Clamp(quadAngle, 0, 60) * Mathf.Deg2Rad);
                foreach (var edge in edges.Values)
                {
                    if (!CanPair(edge, triangles)) continue;
                    var first = triangles[edge.First];
                    var second = triangles[edge.Second];
                    if (first.Submesh != second.Submesh || Vector3.Dot(first.Normal, second.Normal) < normalLimit) continue;
                    if (TryQuadScore(edge, first, second, positions, out var score))
                        candidates.Add(new Candidate { Edge = edge, Score = score });
                }
                candidates.Sort((x, y) => { var compare = x.Score.CompareTo(y.Score); return compare != 0 ? compare : x.Edge.Key.CompareTo(y.Edge.Key); });
                foreach (var candidate in candidates)
                {
                    if (!CanPair(candidate.Edge, triangles)) continue;
                    Pair(candidate.Edge, triangles);
                    pairedQuads++;
                }
            }
            else if (mode == WireframeEdgeMode.FeatureEdges)
            {
                var normalLimit = Mathf.Cos(Mathf.Clamp(featureAngle, 0, 180) * Mathf.Deg2Rad);
                foreach (var edge in edges.Values)
                    if (edge.Count == 2 && triangles[edge.First].Submesh == triangles[edge.Second].Submesh)
                        edge.Hidden = Vector3.Dot(triangles[edge.First].Normal, triangles[edge.Second].Normal) >= normalLimit;
            }

            else if (mode == WireframeEdgeMode.SparseStructure)
            {
                removedParts = FilterSmallParts(triangles, positions, minimumPartSize);
                var patchIds = new int[triangles.Count];
                for (var i = 0; i < triangles.Count; i++) patchIds[i] = NormalPatch(triangles[i].Normal, curveSegments);
                foreach (var edge in edges.Values)
                {
                    if (!triangles[edge.First].Keep) { edge.Hidden = true; continue; }
                    // All original front/back edges remain eligible; no camera or depth culling is involved.
                    // Curved subdivisions and narrow bevel strips in one direction patch disappear together.
                    if (edge.Count == 2) edge.Hidden = patchIds[edge.First] == patchIds[edge.Second];
                }
                if (lineSpacing > 0) MergeCloseLines(edges, positions, lineSpacing);
            }

            var vertices = new List<Vector3>();
            var masks = new List<Vector3>();
            var outputIndices = new List<int>();
            for (var i = 0; i < triangles.Count; i++)
            {
                var triangle = triangles[i];
                // Barycentric X measures distance to BC, Y to CA, Z to AB.
                var mask = new Vector3(edges[Key(triangle.B, triangle.C)].Hidden ? 0 : 1,
                    edges[Key(triangle.C, triangle.A)].Hidden ? 0 : 1,
                    edges[Key(triangle.A, triangle.B)].Hidden ? 0 : 1);
                if (mask == Vector3.zero) continue;
                var firstVertex = vertices.Count;
                vertices.Add(positions[triangle.A]);
                vertices.Add(positions[triangle.B]);
                vertices.Add(positions[triangle.C]);
                for (var corner = 0; corner < 3; corner++)
                {
                    outputIndices.Add(firstVertex + corner);
                    masks.Add(mask);
                }
            }
            var mesh = new Mesh { name = source.name + " [Wireframe Edges]", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(3, masks);
            mesh.SetTriangles(outputIndices, 0);
            mesh.bounds = source.bounds;
            var visible = 0;
            foreach (var edge in edges.Values) if (!edge.Hidden) visible++;
            statistics = new Statistics(edges.Count, visible, pairedQuads, removedParts);
            return mesh;
        }

        private readonly struct LineCell : IEquatable<LineCell>
        {
            public readonly Vector3Int Direction, Offset;
            public LineCell(Vector3Int direction, Vector3Int offset) { Direction = direction; Offset = offset; }
            public bool Equals(LineCell other) => Direction == other.Direction && Offset == other.Offset;
            public override bool Equals(object obj) => obj is LineCell other && Equals(other);
            public override int GetHashCode() => unchecked(Direction.GetHashCode() * 397 ^ Offset.GetHashCode());
        }
        private struct KeptLine
        {
            public Vector3 Offset;
            public Vector3 Point, Direction;
            public float Start, End;
        }
        private static void MergeCloseLines(Dictionary<long, Edge> edges, List<Vector3> positions, float spacing)
        {
            var ordered = new List<Edge>();
            foreach (var edge in edges.Values) if (!edge.Hidden) ordered.Add(edge);
            ordered.Sort((a, b) =>
            {
                var compare = (positions[b.B] - positions[b.A]).sqrMagnitude.CompareTo((positions[a.B] - positions[a.A]).sqrMagnitude);
                return compare != 0 ? compare : a.Key.CompareTo(b.Key);
            });
            var buckets = new Dictionary<LineCell, List<KeptLine>>();
            foreach (var edge in ordered)
            {
                var a = positions[edge.A]; var b = positions[edge.B];
                var direction = (b - a).normalized;
                var absolute = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
                var dominant = absolute.x >= absolute.y && absolute.x >= absolute.z ? direction.x : absolute.y >= absolute.z ? direction.y : direction.z;
                if (dominant < 0) direction = -direction;
                var quantized = new Vector3Int(Mathf.RoundToInt(direction.x * 32), Mathf.RoundToInt(direction.y * 32), Mathf.RoundToInt(direction.z * 32));
                var axis = ((Vector3)quantized).normalized;
                var start = Mathf.Min(Vector3.Dot(a, axis), Vector3.Dot(b, axis));
                var end = Mathf.Max(Vector3.Dot(a, axis), Vector3.Dot(b, axis));
                var midpoint = (a + b) * 0.5f;
                var offset = midpoint - axis * Vector3.Dot(midpoint, axis);
                var cell = new Vector3Int(Mathf.FloorToInt(offset.x / spacing), Mathf.FloorToInt(offset.y / spacing), Mathf.FloorToInt(offset.z / spacing));
                var covered = false;
                for (var z = -1; z <= 1 && !covered; z++) for (var y = -1; y <= 1 && !covered; y++) for (var x = -1; x <= 1 && !covered; x++)
                {
                    if (!buckets.TryGetValue(new LineCell(quantized, cell + new Vector3Int(x, y, z)), out var nearby)) continue;
                    foreach (var existing in nearby)
                    {
                        var overlap = Mathf.Min(end, existing.End) - Mathf.Max(start, existing.Start);
                        var da = a - existing.Point; da -= existing.Direction * Vector3.Dot(da, existing.Direction);
                        var db = b - existing.Point; db -= existing.Direction * Vector3.Dot(db, existing.Direction);
                        if (overlap >= (end - start) * 0.98f && (offset - existing.Offset).sqrMagnitude <= spacing * spacing
                            && da.sqrMagnitude <= spacing * spacing && db.sqrMagnitude <= spacing * spacing)
                        { covered = true; break; }
                    }
                }
                if (covered) { edge.Hidden = true; continue; }
                var key = new LineCell(quantized, cell);
                if (!buckets.TryGetValue(key, out var retained)) { retained = new List<KeptLine>(); buckets.Add(key, retained); }
                retained.Add(new KeptLine { Offset = offset, Point = a, Direction = direction, Start = start, End = end });
            }
        }

        private static int NormalPatch(Vector3 normal, int segments)
        {
            segments = Mathf.Clamp(segments, 4, 16);
            var latitudeSteps = Mathf.Max(1, segments / 4);
            // Snap floating-point noise before binning so planar faces on a bin boundary stay together.
            var azimuth = Mathf.Round(Mathf.Atan2(normal.z, normal.x) * Mathf.Rad2Deg * 100f) / 100f;
            var elevation = Mathf.Round(Mathf.Asin(Mathf.Clamp(normal.y, -1, 1)) * Mathf.Rad2Deg * 100f) / 100f;
            var latitude = Mathf.RoundToInt(elevation / 90f * latitudeSteps);
            var longitude = ((Mathf.RoundToInt(azimuth / 360f * segments) % segments) + segments) % segments;
            if (Mathf.Abs(latitude) == latitudeSteps) longitude = 0;
            return (latitude + latitudeSteps) * segments + longitude;
        }

        private static int WeldPosition(Vector3 position, float tolerance, List<Vector3> positions,
            Dictionary<Vector3Int, List<int>> cells)
        {
            var cell = new Vector3Int(Mathf.FloorToInt(position.x / tolerance), Mathf.FloorToInt(position.y / tolerance), Mathf.FloorToInt(position.z / tolerance));
            var squared = tolerance * tolerance;
            for (var z = -1; z <= 1; z++) for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++)
            {
                if (!cells.TryGetValue(cell + new Vector3Int(x, y, z), out var nearby)) continue;
                foreach (var index in nearby) if ((positions[index] - position).sqrMagnitude <= squared) return index;
            }
            var id = positions.Count; positions.Add(position);
            if (!cells.TryGetValue(cell, out var contents)) { contents = new List<int>(); cells.Add(cell, contents); }
            contents.Add(id);
            return id;
        }

        private static int FilterSmallParts(List<Triangle> triangles, List<Vector3> positions, float minimumSize)
        {
            if (minimumSize <= 0 || positions.Count == 0) return 0;
            var parents = new int[positions.Count];
            for (var i = 0; i < parents.Length; i++) parents[i] = i;
            foreach (var triangle in triangles)
            {
                Join(parents, triangle.A, triangle.B); Join(parents, triangle.A, triangle.C);
            }
            var bounds = new Dictionary<int, Bounds>();
            foreach (var triangle in triangles)
            {
                var root = Find(parents, triangle.A);
                if (!bounds.TryGetValue(root, out var box)) box = new Bounds(positions[triangle.A], Vector3.zero);
                box.Encapsulate(positions[triangle.A]); box.Encapsulate(positions[triangle.B]); box.Encapsulate(positions[triangle.C]);
                bounds[root] = box;
            }
            var removed = new HashSet<int>();
            foreach (var entry in bounds)
            {
                var size = entry.Value.size;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < minimumSize) removed.Add(entry.Key);
            }
            foreach (var triangle in triangles) triangle.Keep = !removed.Contains(Find(parents, triangle.A));
            return removed.Count;
        }
        private static int Find(int[] parents, int index)
        {
            while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
            return index;
        }
        private static void Join(int[] parents, int a, int b)
        {
            a = Find(parents, a); b = Find(parents, b);
            if (a != b) parents[Mathf.Max(a, b)] = Mathf.Min(a, b);
        }

        private static long Key(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
        private static void AddTriangle(int a, int b, int c, int submesh, List<Vector3> positions,
            List<Triangle> triangles, Dictionary<long, Edge> edges)
        {
            if (a == b || b == c || c == a) return;
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (normal.sqrMagnitude < 1e-20f) return;
            var triangleIndex = triangles.Count;
            triangles.Add(new Triangle { A = a, B = b, C = c, Submesh = submesh, Normal = normal.normalized });
            AddEdge(a, b, triangleIndex, edges); AddEdge(b, c, triangleIndex, edges); AddEdge(c, a, triangleIndex, edges);
        }
        private static void AddEdge(int a, int b, int triangle, Dictionary<long, Edge> edges)
        {
            var key = Key(a, b);
            if (!edges.TryGetValue(key, out var edge))
            { edge = new Edge { A = Math.Min(a, b), B = Math.Max(a, b), Key = key, First = triangle }; edges.Add(key, edge); }
            else if (edge.Count == 1) edge.Second = triangle;
            edge.Count++;
        }
        private static bool CanPair(Edge edge, List<Triangle> triangles) => edge.Count == 2 && !edge.Hidden
            && !triangles[edge.First].Paired && !triangles[edge.Second].Paired;
        private static void Pair(Edge edge, List<Triangle> triangles)
        {
            edge.Hidden = true;
            triangles[edge.First].Paired = triangles[edge.Second].Paired = true;
        }
        private static bool TryQuadScore(Edge edge, Triangle first, Triangle second, List<Vector3> positions, out float score)
        {
            // The prospective quad boundary runs opposite0 -> A -> opposite1 -> B.
            var p0 = positions[first.Opposite(edge)];
            var p1 = positions[edge.A];
            var p2 = positions[second.Opposite(edge)];
            var p3 = positions[edge.B];
            var normal = (first.Normal + second.Normal).normalized;
            var sign = 0f;
            score = 0;
            for (var i = 0; i < 4; i++)
            {
                var current = Corner(i, p0, p1, p2, p3);
                var next = Corner((i + 1) % 4, p0, p1, p2, p3) - current;
                var previous = Corner((i + 3) % 4, p0, p1, p2, p3) - current;
                var turn = Vector3.Dot(Vector3.Cross(next, previous), normal);
                if (Mathf.Abs(turn) < 1e-12f || (sign != 0 && turn * sign <= 0)) return false;
                sign = turn;
                var dot = Mathf.Abs(Vector3.Dot(next.normalized, previous.normalized));
                if (dot > 0.94f) return false; // Reject almost-collinear or needle-shaped corners.
                score += dot;
            }
            // Prefer rectangular/trapezoid grids, not arbitrary pairs of cap/fan triangles.
            return score < 1.65f;
        }
        private static Vector3 Corner(int i, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => i == 0 ? a : i == 1 ? b : i == 2 ? c : d;
    }
}
