using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// The input of <see cref="SnowMesher"/>: snow on a grid of square texels (row by row from <see cref="Min"/>, x then
    /// z). <see cref="Depth"/> is the depth that defines the snow — its <see cref="SnowMesher.Settings.MinDepth"/>
    /// contour is the drawn outline, so pass the same (smoothed) depth the game reads, and drawn edge = gameplay edge;
    /// <see cref="Ground"/> is the height the snow stands on. Output positions are in this grid's space: x/z = Min +
    /// texel × CellSize, y = Ground + snow.
    /// </summary>
    public sealed class SnowGrid
    {
        public readonly int Width, Height;
        public float CellSize = 0.25f;

        /// <summary>The corner of texel (0, 0).</summary>
        public Vector2 Min;

        /// <summary>Snow depth per texel, metres.</summary>
        public readonly float[] Depth;

        /// <summary>The ground under the snow per texel, metres (output y).</summary>
        public readonly float[] Ground;

        /// <summary>The PAINTED depth per texel before any smoothing (metres), or null. With it: tiers come from the
        /// painted levels, and texels that are snow only because the smoothing spread it there take the shape of the
        /// nearest painted texel.</summary>
        public float[]? Paint;

        /// <summary>The shape (index into the mesher's profiles) per texel, or null (all 0).</summary>
        public byte[]? Profile;

        public SnowGrid(int width, int height)
        {
            Width = width;
            Height = height;
            Depth = new float[width * height];
            Ground = new float[width * height];
        }
    }

    /// <summary>
    /// Snow drifts as meshes, tile by tile (<see cref="SnowProfile"/> has the shape). Each drift's outline is the depth
    /// grid's <see cref="Settings.MinDepth"/> contour (marching squares on the texel centres with linear crossings: the
    /// bilinear field's contour along every texel edge); a profile is swept along it (rings of vertices, analytic
    /// normals) and the innermost ring is capped (earcut, then Lawson flips — the constrained Delaunay triangulation —
    /// then Rivara refinement to <see cref="SnowProfile.CapEdge"/>), domed by a distance transform from its edge and
    /// lumped by noise.
    /// <para><b>Drifts.</b> Snow texels with the same shape whose grounds differ by at most one
    /// <see cref="Settings.LevelStep"/> from a neighbour are one drift (a drift flows over a one-step rise); a shape
    /// boundary or a rise of two steps or more is a drift boundary, where both outlines run along the texel border. With
    /// <see cref="SnowGrid.Paint"/>, painted levels at least <see cref="SnowProfile.TierStep"/> apart inside one drift
    /// stack as tiers.</para>
    /// <para><b>Tiles.</b> <see cref="BuildTile"/> builds the mesh of one square tile of the grid, reading the grid a
    /// margin (<see cref="ReadMargin"/>) around it, and cuts the result exactly on the tile's lines and on any cut
    /// polygons (e.g. gate regions): everything that decides a vertex near a tile line is computed from global
    /// positions, so two tiles meet with the same vertices along their shared line, and baking one tile or all of them
    /// gives the same mesh.</para>
    /// <para><b>No crossings.</b> Each outline vertex's rings are compressed to stay short of the drift's medial axis
    /// (a signed distance field), then every ring is checked exactly against itself, the other loops' rings and the next
    /// ring in; the vertices of a crossing are compressed further, and as a last resort flattened onto the outline (which
    /// never crosses), so no crossing survives (<see cref="TileStats.Crossings"/> counts any that did).</para>
    /// Not thread-bound: no Unity object is touched (a worker thread may run it).
    /// </summary>
    public sealed class SnowMesher
    {
        public sealed class Settings
        {
            /// <summary>The depth contour that is the outline (the game's "in snow" depth), metres.</summary>
            public float MinDepth = 0.02f;

            /// <summary>Grounds within this of each other are one level (a drift flows across), metres.</summary>
            public float LevelStep = 0.125f;

            /// <summary>The tile size, metres (a whole number of texels).</summary>
            public float TileSize = 32f;

            /// <summary>Outline loops smaller than this (m²) are dropped.</summary>
            public float MinLoopArea = 0.1f;

            /// <summary>How far the outline may stray from the depth contour when it drops contour points (metres; the
            /// contour of a depth stored in whole centimetres zig-zags by about a centimetre).</summary>
            public float OutlineTolerance = 0.025f;

            /// <summary>The lumps' noise reads (rotate by NoiseYaw, then add NoiseOffset) of the grid position: give the
            /// position in a frame shared by the neighbouring grids so the lumps agree across their seams.</summary>
            public Vector2 NoiseOffset;

            public float NoiseYaw;
        }

        /// <summary>What the last tile cost (ms: the whole tile, then its parts) and made. Repaired = outline vertices the
        /// crossing check compressed; Crossings = crossings left after it (always 0 — the proof); CapFailures = tops that
        /// failed to triangulate (never expected).</summary>
        public struct TileStats
        {
            public double Ms, WindowMs, TraceMs, SweepMs, SdfMs, CheckMs, StripMs, CapMs, PuffMs, ClipMs, EarcutMs, FlipMs,
                LatticeMs, DomeMs;
            public int Layers, Loops, OutlineVertices, Repaired, Crossings, CapFailures, Vertices, Triangles, CapTriangles;
            /// <summary>Outline inside the tile (all layers): metres, and its vertices.</summary>
            public float OutlineLength;
            public int OutlineInside;
        }

        /// <summary>The last built tile: positions, normals, triangles (Unity's winding: clockwise seen from outside).</summary>
        public readonly List<Vector3> Vertices = new();

        public readonly List<Vector3> Normals = new();
        public readonly List<int> Triangles = new();
        public TileStats Stats;

        /// <summary>The last tile's drawn outlines (ring 0 of each drift's bottom layer — tiers excluded), loop after
        /// loop: loop k is OutlinePoints[OutlineStarts[k] .. OutlineStarts[k + 1]). Every loop the tile's window traced,
        /// inside on the left (debug drawing, tests).</summary>
        public readonly List<Vector2> OutlinePoints = new();

        public readonly List<int> OutlineStarts = new();

        public double PrepareMs { get; private set; }
        public int TilesX { get; private set; }
        public int TilesZ { get; private set; }
        /// <summary>The drifts the last Prepare found.</summary>
        public int Regions => _regions.Count - 1;

        /// <summary>How far past a line (a tile edge, a gate) the grid must hold the real snow for the mesh on this side
        /// to be exact up to the line: the widest profile + its dome's reach + a metre.</summary>
        public static float ReadMargin(IReadOnlyList<SnowProfile> profiles)
        {
            var m = 0f;
            foreach (var p in profiles)
                if (p.Draws)
                    m = Mathf.Max(m, p.Reach() + Mathf.Max(0f, p.PuffReach));
            return m + 1f;
        }

        // ---- prepared grid -------------------------------------------------------------------------------------------

        private sealed class Region
        {
            public int Profile;
            public int X0, Z0, X1, Z1; // texel bounds, inclusive
            public float MaxDepth;
            public readonly List<(float Threshold, float Cap)> Layers = new();
        }

        private SnowGrid _grid = null!;
        private SnowProfile[] _profiles = Array.Empty<SnowProfile>();
        private Settings _settings = new();
        private int[] _label = Array.Empty<int>();
        private byte[] _prof = Array.Empty<byte>();
        private readonly List<Region> _regions = new() { new Region() }; // 0 = no snow
        private float _margin;
        private int _tile;
        private float[] _reach = Array.Empty<float>();

        /// <summary>Reads the grid once (shapes, drifts, tiers): call before building its tiles, and again after the
        /// grid changes.</summary>
        public void Prepare(SnowGrid grid, IReadOnlyList<SnowProfile> profiles, Settings settings)
        {
            var sw = Stopwatch.StartNew();
            _grid = grid;
            _settings = settings;
            _profiles = new SnowProfile[profiles.Count];
            for (var i = 0; i < profiles.Count; i++) _profiles[i] = profiles[i];
            _margin = ReadMargin(_profiles);
            _reach = new float[_profiles.Length];
            for (var i = 0; i < _profiles.Length; i++) _reach[i] = _profiles[i].Reach();
            _tile = Mathf.Max(1, Mathf.RoundToInt(settings.TileSize / grid.CellSize));
            TilesX = (grid.Width + _tile - 1) / _tile;
            TilesZ = (grid.Height + _tile - 1) / _tile;

            int w = grid.Width, h = grid.Height, n = w * h;
            var depth = grid.Depth;
            var ground = grid.Ground;
            var minDepth = settings.MinDepth;
            var step = settings.LevelStep + 1e-4f;
            _prof = new byte[n];
            if (grid.Profile != null) Array.Copy(grid.Profile, _prof, n);
            for (var i = 0; i < n; i++)
                if (_prof[i] >= _profiles.Length)
                    _prof[i] = 0;

            // Texels that are snow only through the smoothing take the shape of the nearest painted texel (same level).
            var paint = grid.Paint;
            if (paint != null)
            {
                var queue = new int[n];
                var seen = new bool[n];
                int head = 0, tail = 0;
                for (var i = 0; i < n; i++)
                {
                    if (depth[i] < minDepth || paint[i] < minDepth) continue;
                    seen[i] = true;
                    queue[tail++] = i;
                }
                while (head < tail)
                {
                    var i = queue[head++];
                    int x = i % w, z = i / w;
                    for (var k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        var j = nz * w + nx;
                        if (seen[j] || depth[j] < minDepth || Mathf.Abs(ground[j] - ground[i]) > step) continue;
                        seen[j] = true;
                        _prof[j] = _prof[i];
                        queue[tail++] = j;
                    }
                }
            }

            // Drifts: union-find over 4-neighbours with the same shape within one level.
            var parent = new int[n];
            for (var i = 0; i < n; i++) parent[i] = depth[i] >= minDepth ? i : -1;
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a == b) return;
                if (a < b) parent[b] = a;
                else parent[a] = b;
            }
            for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var i = z * w + x;
                if (parent[i] < 0) continue;
                if (x + 1 < w && parent[i + 1] >= 0 && _prof[i + 1] == _prof[i] && Mathf.Abs(ground[i + 1] - ground[i]) <= step)
                    Union(i, i + 1);
                if (z + 1 < h && parent[i + w] >= 0 && _prof[i + w] == _prof[i] && Mathf.Abs(ground[i + w] - ground[i]) <= step)
                    Union(i, i + w);
            }

            _label = new int[n];
            _regions.Clear();
            _regions.Add(new Region());
            var rootLabel = new Dictionary<int, int>();
            for (var i = 0; i < n; i++)
            {
                if (parent[i] < 0) continue;
                var root = Find(i);
                if (!rootLabel.TryGetValue(root, out var l))
                {
                    l = _regions.Count;
                    rootLabel[root] = l;
                    _regions.Add(new Region { Profile = _prof[i], X0 = int.MaxValue, Z0 = int.MaxValue, X1 = -1, Z1 = -1 });
                }
                _label[i] = l;
                var r = _regions[l];
                if (depth[i] > r.MaxDepth) r.MaxDepth = depth[i];
                int x = i % w, z = i / w;
                if (x < r.X0) r.X0 = x;
                if (x > r.X1) r.X1 = x;
                if (z < r.Z0) r.Z0 = z;
                if (z > r.Z1) r.Z1 = z;
            }

            // Tiers: the drift's painted depths (to the cm) clustered by gaps of TierStep; tier k starts halfway between
            // cluster k−1's deepest and cluster k's shallowest, and every tier but the top one is capped at its cluster.
            var hist = new int[256 * _regions.Count];
            if (paint != null)
                for (var i = 0; i < n; i++)
                {
                    var l = _label[i];
                    if (l == 0 || paint[i] < minDepth) continue;
                    hist[l * 256 + Mathf.Clamp(Mathf.RoundToInt(paint[i] * 100f), 0, 255)]++;
                }
            var lows = new List<int>();
            var highs = new List<int>();
            for (var l = 1; l < _regions.Count; l++)
            {
                var r = _regions[l];
                var tierStep = Mathf.RoundToInt(_profiles[r.Profile].TierStep * 100f);
                lows.Clear();
                highs.Clear();
                if (paint != null && tierStep > 0)
                    for (var b = 0; b < 256; b++)
                    {
                        if (hist[l * 256 + b] == 0) continue;
                        if (highs.Count == 0 || b - highs[highs.Count - 1] >= tierStep)
                        {
                            lows.Add(b);
                            highs.Add(b);
                        }
                        else highs[highs.Count - 1] = b;
                    }
                if (lows.Count < 2)
                {
                    r.Layers.Add((minDepth, float.PositiveInfinity));
                    continue;
                }
                for (var k = 0; k < lows.Count; k++)
                {
                    var threshold = k == 0 ? minDepth : (highs[k - 1] + lows[k]) * 0.005f;
                    var cap = k == lows.Count - 1 ? float.PositiveInfinity : highs[k] * 0.01f;
                    r.Layers.Add((threshold, cap));
                }
            }
            PrepareMs = sw.Elapsed.TotalMilliseconds;
        }

        // ---- one tile --------------------------------------------------------------------------------------------------

        // The window: the tile's texels plus the margin, [Wx0, Wx0 + Ww) × [Wz0, Wz0 + Wh).
        private int _wx0, _wz0, _ww, _wh;
        private float[] _dp = Array.Empty<float>(), _base = Array.Empty<float>(), _tmp = Array.Empty<float>();

        // The clip region: the tile rectangle (convex, CCW), then the cut polygons (convex, CCW) taken away.
        private readonly Vector2[] _rect = new Vector2[4];
        private readonly List<Vector2[]> _cuts = new();
        private readonly List<(Vector2 Min, Vector2 Max)> _cutBounds = new();

        /// <summary>
        /// Builds tile (tx, tz) into <see cref="Vertices"/> / <see cref="Normals"/> / <see cref="Triangles"/>: the snow
        /// within the tile's square, minus the inside of every `cuts` polygon (convex, grid space, any winding).
        /// </summary>
        public void BuildTile(int tx, int tz, IReadOnlyList<Vector2[]>? cuts = null)
        {
            var sw = Stopwatch.StartNew();
            Vertices.Clear();
            Normals.Clear();
            Triangles.Clear();
            OutlinePoints.Clear();
            OutlineStarts.Clear();
            OutlineStarts.Add(0);
            Stats = default;
            var g = _grid;
            var c = g.CellSize;
            int tileX0 = tx * _tile, tileZ0 = tz * _tile;
            float x0 = TileLine(tx, g.Min.x, TilesX), z0 = TileLine(tz, g.Min.y, TilesZ);
            float x1 = TileLine(tx + 1, g.Min.x, TilesX), z1 = TileLine(tz + 1, g.Min.y, TilesZ);
            _rect[0] = new Vector2(x0, z0);
            _rect[1] = new Vector2(x1, z0);
            _rect[2] = new Vector2(x1, z1);
            _rect[3] = new Vector2(x0, z1);
            _cuts.Clear();
            _cutBounds.Clear();
            if (cuts != null)
                foreach (var cut in cuts)
                {
                    if (cut.Length < 3) continue;
                    var ccw = (Vector2[])cut.Clone();
                    if (PolygonTriangulator.SignedArea(ccw) < 0) Array.Reverse(ccw);
                    Vector2 mn = ccw[0], mx = ccw[0];
                    foreach (var p in ccw)
                    {
                        mn = Vector2.Min(mn, p);
                        mx = Vector2.Max(mx, p);
                    }
                    if (mx.x < x0 || mn.x > x1 || mx.y < z0 || mn.y > z1) continue;
                    _cuts.Add(ccw);
                    _cutBounds.Add((mn, mx));
                }

            var margin = Mathf.CeilToInt(_margin / c);
            _wx0 = Mathf.Max(0, tileX0 - margin);
            _wz0 = Mathf.Max(0, tileZ0 - margin);
            var wx1 = Mathf.Min(g.Width, tileX0 + _tile + margin);
            var wz1 = Mathf.Min(g.Height, tileZ0 + _tile + margin);
            _ww = wx1 - _wx0;
            _wh = wz1 - _wz0;
            if (_ww <= 0 || _wh <= 0) return;

            // The drifts that can reach the tile.
            _layerRegions.Clear();
            for (var l = 1; l < _regions.Count; l++)
            {
                var r = _regions[l];
                var p = _profiles[r.Profile];
                if (!p.Draws) continue;
                var reach = Mathf.CeilToInt(_reach[r.Profile] / c) + 1;
                if (r.X1 + reach < tileX0 || r.X0 - reach >= tileX0 + _tile || r.Z1 + reach < tileZ0 ||
                    r.Z0 - reach >= tileZ0 + _tile) continue;
                if (r.X1 < _wx0 || r.X0 >= wx1 || r.Z1 < _wz0 || r.Z0 >= wz1) continue;
                _layerRegions.Add(l);
            }
            if (_layerRegions.Count == 0)
            {
                Stats.Ms = sw.Elapsed.TotalMilliseconds;
                return;
            }

            var t0 = sw.Elapsed.TotalMilliseconds;
            WindowRasters();
            Stats.WindowMs = sw.Elapsed.TotalMilliseconds - t0;

            foreach (var l in _layerRegions)
            {
                var r = _regions[l];
                for (var k = 0; k < r.Layers.Count; k++) BuildLayer(l, r, r.Layers[k].Threshold, r.Layers[k].Cap);
            }
            Compact();
            Stats.Vertices = Vertices.Count;
            Stats.Triangles = Triangles.Count / 3;
            Stats.Ms = sw.Elapsed.TotalMilliseconds;
        }

        private readonly List<int> _layerRegions = new();

        /// <summary>The grid-space x (or z) of the line between tile k − 1 and tile k. Tile lines run through texel
        /// CENTRES (an outline never runs along one: a drift boundary that follows texel borders never lies on a tile
        /// line); the grid's outer tiles reach past its edge.</summary>
        public float TileLine(int k, float min, int tiles)
        {
            var c = _grid.CellSize;
            if (k <= 0) return min - _margin - 2f * c;
            if (k >= tiles) return min + (tiles * _tile + 1) * c + _margin;
            return min + (k * _tile + 0.5f) * c;
        }

        private int Gi(int wx, int wz) => (_wz0 + wz) * _grid.Width + _wx0 + wx;

        // Per window texel: the drift's depth spread by up to 1 m within the drift (the depth a profile stands for), and
        // the BASE the snow sits on: the ground, max-filtered then box-blurred within one level, so a drift's surface rises
        // smoothly over a one-step rise and never dips under the ground.
        private void WindowRasters()
        {
            var g = _grid;
            int ww = _ww, wh = _wh, n = ww * wh;
            if (_dp.Length < n)
            {
                _dp = new float[n];
                _base = new float[n];
                _tmp = new float[n];
            }
            var c = g.CellSize;
            var spread = Mathf.Max(1, Mathf.RoundToInt(1f / c));
            var step = _settings.LevelStep + 1e-4f;
            var depth = g.Depth;
            var ground = g.Ground;
            var label = _label;

            // Depth spread: max within the drift, separable.
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                var gi = Gi(x, z);
                var l = label[gi];
                var m = 0f;
                if (l != 0)
                    for (int k = Mathf.Max(0, x - spread), e = Mathf.Min(ww - 1, x + spread); k <= e; k++)
                    {
                        var gk = gi + k - x;
                        if (label[gk] == l && depth[gk] > m) m = depth[gk];
                    }
                _tmp[z * ww + x] = m;
            }
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                var gi = Gi(x, z);
                var l = label[gi];
                var m = 0f;
                if (l != 0)
                    for (int k = Mathf.Max(0, z - spread), e = Mathf.Min(wh - 1, z + spread); k <= e; k++)
                    {
                        if (label[gi + (k - z) * g.Width] != l) continue;
                        var v = _tmp[k * ww + x];
                        if (v > m) m = v;
                    }
                _dp[z * ww + x] = m;
            }

            // Base: max over ±(blur + 1) within one level, then a box blur over ±blur within one level, then ≥ the ground.
            // Where the ground doesn't change within everything those passes read (3 · blur + 1 texels), base = ground.
            var blur = Mathf.Max(1, Mathf.RoundToInt(0.5f / c));
            var grow = blur + 1;
            FlatGround(3 * blur + 1);
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                var gi = Gi(x, z);
                var g0 = ground[gi];
                var m = g0;
                if (_flat[z * ww + x])
                {
                    _tmp[z * ww + x] = g0;
                    continue;
                }
                for (int k = Mathf.Max(0, x - grow), e = Mathf.Min(ww - 1, x + grow); k <= e; k++)
                {
                    var v = ground[gi + k - x];
                    if (v > m && v - g0 <= step) m = v;
                }
                _tmp[z * ww + x] = m;
            }
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                var gi = Gi(x, z);
                var g0 = ground[gi];
                var m = _tmp[z * ww + x];
                if (_flat[z * ww + x])
                {
                    _base[z * ww + x] = g0;
                    continue;
                }
                for (int k = Mathf.Max(0, z - grow), e = Mathf.Min(wh - 1, z + grow); k <= e; k++)
                {
                    if (Mathf.Abs(ground[gi + (k - z) * g.Width] - g0) > step) continue;
                    var v = Mathf.Min(_tmp[k * ww + x], g0 + step);
                    if (v > m) m = v;
                }
                _base[z * ww + x] = m;
            }
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                var gi = Gi(x, z);
                var g0 = ground[gi];
                if (_flat[z * ww + x])
                {
                    _tmp[z * ww + x] = g0;
                    continue;
                }
                float s = 0f, cnt = 0f;
                for (int k = Mathf.Max(0, x - blur), e = Mathf.Min(ww - 1, x + blur); k <= e; k++)
                {
                    if (Mathf.Abs(ground[gi + k - x] - g0) > step) continue;
                    s += _base[z * ww + k];
                    cnt++;
                }
                _tmp[z * ww + x] = s / cnt;
            }
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                var gi = Gi(x, z);
                var g0 = ground[gi];
                if (_flat[z * ww + x])
                {
                    _base[z * ww + x] = g0;
                    continue;
                }
                float s = 0f, cnt = 0f;
                for (int k = Mathf.Max(0, z - blur), e = Mathf.Min(wh - 1, z + blur); k <= e; k++)
                {
                    if (Mathf.Abs(ground[gi + (k - z) * g.Width] - g0) > step) continue;
                    s += _tmp[k * ww + x];
                    cnt++;
                }
                _base[z * ww + x] = Mathf.Max(g0, s / cnt);
            }
        }

        // Per window texel: no ground change between any two neighbours within ±radius (an integral image of the changes).
        private void FlatGround(int radius)
        {
            int ww = _ww, wh = _wh, iw = ww + 1;
            if (_flat.Length < ww * wh) _flat = new bool[Mathf.NextPowerOfTwo(ww * wh)];
            if (_changes.Length < iw * (wh + 1)) _changes = new int[Mathf.NextPowerOfTwo(iw * (wh + 1))];
            var ground = _grid.Ground;
            for (var x = 0; x <= ww; x++) _changes[x] = 0;
            for (var z = 0; z < wh; z++)
            {
                var row = 0;
                _changes[(z + 1) * iw] = 0;
                for (var x = 0; x < ww; x++)
                {
                    var gi = Gi(x, z);
                    var d = 0;
                    if (x + 1 < ww && ground[gi + 1] != ground[gi]) d++;
                    if (z + 1 < wh && ground[gi + _grid.Width] != ground[gi]) d++;
                    row += d;
                    _changes[(z + 1) * iw + x + 1] = _changes[z * iw + x + 1] + row;
                }
            }
            for (var z = 0; z < wh; z++)
            for (var x = 0; x < ww; x++)
            {
                int x0 = Mathf.Max(0, x - radius - 1), x1 = Mathf.Min(ww, x + radius + 1);
                int z0 = Mathf.Max(0, z - radius - 1), z1 = Mathf.Min(wh, z + radius + 1);
                var sum = _changes[z1 * iw + x1] - _changes[z0 * iw + x1] - _changes[z1 * iw + x0] + _changes[z0 * iw + x0];
                _flat[z * ww + x] = sum == 0;
            }
        }

        private bool[] _flat = Array.Empty<bool>();
        private int[] _changes = Array.Empty<int>();

        // ---- one layer (a drift, or one tier of it) -------------------------------------------------------------------

        // Outline vertices of the layer, loop after loop.
        private readonly List<Vector2> _pos = new();
        private readonly List<float> _foot = new();
        private readonly List<int> _ref = new(); // the window texel inside the drift the crossing belongs to
        private readonly List<int> _loopStart = new();
        private int[] _next = Array.Empty<int>(), _prev = Array.Empty<int>(); // per vertex: its neighbours along its loop
        private Vector2[] _nrm = Array.Empty<Vector2>();
        private float[] _kIn = Array.Empty<float>(), _kOut = Array.Empty<float>();

        // Rings, vertex v ring r at v * R + r.
        private Vector2[] _ringPos = Array.Empty<Vector2>();
        private float[] _px = Array.Empty<float>(), _py = Array.Empty<float>(), _ptx = Array.Empty<float>(),
            _pty = Array.Empty<float>();
        private int[] _ringOut = Array.Empty<int>();
        private float[] _ringY = Array.Empty<float>();
        private Vector3[] _ringN = Array.Empty<Vector3>();

        private int _layerLabel;
        private float _layerCap;
        private SnowProfile _p;
        private float _layerReach;
        private int _R, _wallRings, _shoulderRings;

        private void BuildLayer(int label, Region region, float threshold, float cap)
        {
            var sw = Stopwatch.StartNew();
            _layerLabel = label;
            _layerCap = cap;
            _p = _profiles[region.Profile];
            _layerReach = _reach[region.Profile];
            _p.RingsFor(Mathf.Min(region.MaxDepth, cap), out _wallRings, out _shoulderRings);
            _R = _wallRings + 1 + _shoulderRings;
            Trace(region, threshold);
            var nv = _pos.Count;
            Stats.TraceMs += sw.Elapsed.TotalMilliseconds;
            if (nv == 0) return;
            Stats.Layers++;
            Stats.Loops += _loopStart.Count - 1;
            if (threshold == _settings.MinDepth)
                for (var l = 0; l + 1 < _loopStart.Count; l++)
                {
                    for (var v = _loopStart[l]; v < _loopStart[l + 1]; v++) OutlinePoints.Add(_pos[v]);
                    OutlineStarts.Add(OutlinePoints.Count);
                }
            Stats.OutlineVertices += nv;
            for (var l = 0; l + 1 < _loopStart.Count; l++)
                for (int a = _loopStart[l + 1] - 1, b = _loopStart[l]; b < _loopStart[l + 1]; a = b++)
                {
                    var mid = (_pos[a] + _pos[b]) * 0.5f;
                    if (mid.x < _rect[0].x || mid.x > _rect[2].x || mid.y < _rect[0].y || mid.y > _rect[2].y) continue;
                    Stats.OutlineLength += (_pos[b] - _pos[a]).magnitude;
                    Stats.OutlineInside++;
                }

            var t = sw.Elapsed.TotalMilliseconds;
            Sweep();
            Stats.SweepMs += sw.Elapsed.TotalMilliseconds - t;
            t = sw.Elapsed.TotalMilliseconds;
            CheckAndRepair();
            Stats.CheckMs += sw.Elapsed.TotalMilliseconds - t;
            t = sw.Elapsed.TotalMilliseconds;
            Strips();
            Stats.StripMs += sw.Elapsed.TotalMilliseconds - t;
            t = sw.Elapsed.TotalMilliseconds;
            Cap();
            Stats.CapMs += sw.Elapsed.TotalMilliseconds - t;
        }

        // -- trace: marching squares on texel centres, one layer, inside on the left -------------------------------------

        private int[] _edgeNext = Array.Empty<int>(), _edgeVert = Array.Empty<int>(), _edgeMid = Array.Empty<int>();
        private readonly List<int> _crossEdges = new();

        private bool In(int wx, int wz, float threshold)
        {
            if (wx < 0 || wz < 0 || wx >= _ww || wz >= _wh) return false;
            var gi = Gi(wx, wz);
            return _label[gi] == _layerLabel && _grid.Depth[gi] >= threshold;
        }

        private void Trace(Region region, float threshold)
        {
            _pos.Clear();
            _foot.Clear();
            _ref.Clear();
            _key.Clear();
            _loopStart.Clear();
            // The drift's box in window texels, with a ring of padding.
            var bx0 = Mathf.Max(0, region.X0 - _wx0) - 1;
            var bz0 = Mathf.Max(0, region.Z0 - _wz0) - 1;
            var bx1 = Mathf.Min(_ww - 1, region.X1 - _wx0) + 1;
            var bz1 = Mathf.Min(_wh - 1, region.Z1 - _wz0) + 1;
            if (bx1 - bx0 < 2 || bz1 - bz0 < 2)
            {
                _loopStart.Add(0);
                return;
            }
            int ew = bx1 - bx0 + 1, eh = bz1 - bz0 + 1, half = ew * eh;
            if (_edgeNext.Length < 2 * half)
            {
                _edgeNext = new int[2 * half];
                _edgeVert = new int[2 * half];
                _edgeMid = new int[2 * half];
            }
            Array.Fill(_edgeNext, -1, 0, 2 * half);
            Array.Fill(_edgeVert, -1, 0, 2 * half);
            _crossEdges.Clear();
            int HEdge(int i, int j) => (j - bz0) * ew + (i - bx0); // texel (i, j) – (i + 1, j)
            int VEdge(int i, int j) => half + (j - bz0) * ew + (i - bx0); // texel (i, j) – (i, j + 1)

            // Corners c0 (i, j), c1 (i + 1, j), c2 (i + 1, j + 1), c3 (i, j + 1); edge k runs from corner k to k + 1.
            Span<int> eid = stackalloc int[4];
            Span<bool> cin = stackalloc bool[4];
            for (var j = bz0; j < bz1; j++)
            for (var i = bx0; i < bx1; i++)
            {
                cin[0] = In(i, j, threshold);
                cin[1] = In(i + 1, j, threshold);
                cin[2] = In(i + 1, j + 1, threshold);
                cin[3] = In(i, j + 1, threshold);
                var mask = (cin[0] ? 1 : 0) | (cin[1] ? 2 : 0) | (cin[2] ? 4 : 0) | (cin[3] ? 8 : 0);
                if (mask == 0 || mask == 15) continue;
                eid[0] = HEdge(i, j);
                eid[1] = VEdge(i + 1, j);
                eid[2] = HEdge(i, j + 1);
                eid[3] = VEdge(i, j);
                // exits: in → out going round the cell counter-clockwise; entries: out → in.
                if (mask == 5 || mask == 10)
                {
                    var centre = Centre(i, j, threshold);
                    for (var k = 0; k < 4; k++)
                    {
                        if (!cin[k]) continue;
                        // corner k in: edge k is an exit, edge k − 1 an entry.
                        var exit = k;
                        var entry = centre ? (k + 1) % 4 : (k + 3) % 4;
                        Link(i, j, eid, exit, entry, threshold, false);
                    }
                }
                else
                {
                    int exit = -1, entry = -1;
                    for (var k = 0; k < 4; k++)
                    {
                        var a = cin[k];
                        var b = cin[(k + 1) % 4];
                        if (a && !b) exit = k;
                        else if (!a && b) entry = k;
                    }
                    Link(i, j, eid, exit, entry, threshold, true);
                }
            }

            // Loops.
            var minArea = _settings.MinLoopArea;
            foreach (var e0 in _crossEdges)
            {
                if (_edgeNext[e0] < 0) continue;
                var start = _pos.Count;
                var e = e0;
                var guard = 0;
                while (e >= 0 && _edgeNext[e] >= 0 && guard++ < 1 << 22)
                {
                    var v = _edgeVert[e];
                    _pos.Add(_crossPos[v]);
                    _foot.Add(_crossFoot[v]);
                    _ref.Add(_crossRef[v]);
                    _key.Add(_crossKey[v]);
                    var mid = _edgeMid[e];
                    if (mid >= 0)
                    {
                        _pos.Add(_crossPos[mid]);
                        _foot.Add(_crossFoot[mid]);
                        _ref.Add(_crossRef[mid]);
                        _key.Add(_crossKey[mid]);
                    }
                    var nxt = _edgeNext[e];
                    _edgeNext[e] = -1;
                    e = nxt;
                }
                Smooth(start, _pos.Count - start);
                var count = Thin(start, _pos.Count - start);
                var area = 0f;
                for (int a = start + count - 1, b = start; b < start + count; a = b++)
                    area += _pos[a].x * _pos[b].y - _pos[b].x * _pos[a].y;
                if (count < 3 || Mathf.Abs(area * 0.5f) < minArea)
                {
                    _pos.RemoveRange(start, count);
                    _foot.RemoveRange(start, count);
                    _ref.RemoveRange(start, count);
                    _key.RemoveRange(start, count);
                    continue;
                }
                _loopStart.Add(start);
            }
            _loopStart.Add(_pos.Count);
            _crossPos.Clear();
            _crossFoot.Clear();
            _crossRef.Clear();
            _crossKey.Clear();
        }

        // Two 1-2-1 passes over the contour points: the contour of a depth stored in whole centimetres zig-zags by about a
        // centimetre, which would serrate the silhouette and jitter the normals. Each pass moves a point at most half the
        // OutlineTolerance; local, so every window agrees.
        private void Smooth(int start, int count)
        {
            if (count < 4) return;
            if (_smooth.Length < count) _smooth = new Vector2[Mathf.NextPowerOfTwo(count)];
            var reach = 0.5f * _settings.OutlineTolerance; // per pass: a small loop must not shrink away
            for (var it = 0; it < 2; it++)
            {
                for (var i = 0; i < count; i++)
                {
                    var a = _pos[start + (i - 1 + count) % count];
                    var b = _pos[start + i];
                    var c = _pos[start + (i + 1) % count];
                    var move = (a + 2f * b + c) * 0.25f - b;
                    if (move.sqrMagnitude > reach * reach) move *= reach / move.magnitude;
                    _smooth[i] = b + move;
                }
                for (var i = 0; i < count; i++) _pos[start + i] = _smooth[i];
            }
        }

        private Vector2[] _smooth = Array.Empty<Vector2>();

        // Drops contour points the outline doesn't need (within OutlineTolerance of the chord of its neighbours, the chord
        // no longer than the shape's Spacing), three rounds, each removing only points that rank below both neighbours by a hash of
        // their texel edge: a point's fate depends on its neighbourhood alone, so every window keeps the same points.
        private int Thin(int start, int count)
        {
            var maxChord2 = _p.Spacing * _p.Spacing;
            var tolerance = _settings.OutlineTolerance;
            for (var round = 0; round < 3 && count > 6 && maxChord2 > 0f; round++)
            {
                _drop.Clear();
                for (var i = 0; i < count; i++)
                {
                    int u = start + (i - 1 + count) % count, v = start + i, w = start + (i + 1) % count;
                    if ((_pos[w] - _pos[u]).sqrMagnitude > maxChord2) continue;
                    if (PolygonSimplify.DistanceToSegment(_pos[v], _pos[u], _pos[w]) > tolerance) continue;
                    var rank = Rank(_key[v]);
                    if (rank >= Rank(_key[u]) || rank >= Rank(_key[w])) continue;
                    _drop.Add(i);
                }
                if (_drop.Count == 0) break;
                var k = start;
                var d = 0;
                for (var i = 0; i < count; i++)
                {
                    if (d < _drop.Count && _drop[d] == i)
                    {
                        d++;
                        continue;
                    }
                    _pos[k] = _pos[start + i];
                    _foot[k] = _foot[start + i];
                    _ref[k] = _ref[start + i];
                    _key[k] = _key[start + i];
                    k++;
                }
                var removed = count - (k - start);
                _pos.RemoveRange(k, removed);
                _foot.RemoveRange(k, removed);
                _ref.RemoveRange(k, removed);
                _key.RemoveRange(k, removed);
                count -= removed;
            }
            return count;
        }

        private readonly List<int> _drop = new();

        private static ulong Rank(long key)
        {
            var x = (ulong)key * 0x9E3779B97F4A7C15ul;
            x ^= x >> 31;
            x *= 0xBF58476D1CE4E5B9ul;
            return x ^ (x >> 29);
        }

        private readonly List<Vector2> _crossPos = new();
        private readonly List<float> _crossFoot = new();
        private readonly List<int> _crossRef = new();
        private readonly List<long> _crossKey = new();
        private readonly List<long> _key = new();

        // A saddle's centre is inside if the four corners' depths average to at least the threshold (texels outside the
        // drift count as no snow).
        private bool Centre(int i, int j, float threshold)
        {
            var s = 0f;
            for (var k = 0; k < 4; k++)
            {
                int x = i + (k == 1 || k == 2 ? 1 : 0), z = j + (k >= 2 ? 1 : 0);
                if (x < 0 || z < 0 || x >= _ww || z >= _wh) continue;
                var gi = Gi(x, z);
                if (_label[gi] == _layerLabel || _label[gi] == 0) s += _grid.Depth[gi];
            }
            return s * 0.25f >= threshold;
        }

        private void Link(int i, int j, Span<int> eid, int exit, int entry, float threshold, bool curve)
        {
            if (exit < 0 || entry < 0) return;
            var a = Crossing(i, j, eid[exit], exit, threshold);
            var b = Crossing(i, j, eid[entry], entry, threshold);
            if (_edgeNext[eid[exit]] < 0) _crossEdges.Add(eid[exit]);
            _edgeNext[eid[exit]] = eid[entry];
            _edgeMid[eid[exit]] = curve ? CellMidpoint(i, j, a, b, threshold) : -1;
        }

        // Inside a cell the field's contour is a hyperbola, not the segment between its two edge crossings (up to a
        // quarter cell off where the depth sits near the threshold): add the contour's own point across the segment's
        // middle, when all four corners are interpolated (same drift or snow-free ground of its level). −1 = none.
        private int CellMidpoint(int i, int j, int ca, int cb, float threshold)
        {
            Vector2 a = _crossPos[ca], b = _crossPos[cb];
            var g = _grid;
            float v0 = 0f, v1 = 0f, v2 = 0f, v3 = 0f;
            var g0 = float.NaN;
            for (var k = 0; k < 4; k++)
            {
                int x = i + (k & 1), z = j + (k >> 1);
                if (x < 0 || z < 0 || x >= _ww || z >= _wh) return -1;
                var gi = Gi(x, z);
                var l = _label[gi];
                if (l != _layerLabel && l != 0) return -1;
                if (float.IsNaN(g0)) g0 = g.Ground[gi];
                else if (Mathf.Abs(g.Ground[gi] - g0) > _settings.LevelStep + 1e-4f) return -1;
                var vk = g.Depth[gi] - threshold;
                if (k == 0) v0 = vk;
                else if (k == 1) v1 = vk;
                else if (k == 2) v2 = vk;
                else v3 = vk;
            }
            var c = g.CellSize;
            var o = new Vector2(g.Min.x + (_wx0 + i + 0.5f) * c, g.Min.y + (_wz0 + j + 0.5f) * c);
            var m = (a + b) * 0.5f;
            var d = b - a;
            if (d.sqrMagnitude < 1e-10f) return -1;
            var n = new Vector2(-d.y, d.x).normalized;
            float F(float s)
            {
                var p = m + n * s;
                float u = (p.x - o.x) / c, w = (p.y - o.y) / c;
                return Mathf.Lerp(Mathf.Lerp(v0, v1, u), Mathf.Lerp(v2, v3, u), w);
            }
            float lo = -0.35f * c, hi = 0.35f * c;
            float flo = F(lo), fhi = F(hi);
            if ((flo > 0f) == (fhi > 0f)) return -1;
            for (var it = 0; it < 24; it++)
            {
                var mid = 0.5f * (lo + hi);
                var fm = F(mid);
                if ((fm > 0f) == (flo > 0f))
                {
                    lo = mid;
                    flo = fm;
                }
                else hi = mid;
            }
            var q = m + n * (0.5f * (lo + hi));
            var qu = (q.x - o.x) / c;
            var qw = (q.y - o.y) / c;
            if (qu < 0f || qu > 1f || qw < 0f || qw > 1f) return -1;
            var id = _crossPos.Count;
            _crossPos.Add(q);
            _crossFoot.Add(Mathf.Min(_crossFoot[ca], _crossFoot[cb]));
            _crossRef.Add(_crossRef[ca]);
            _crossKey.Add(_crossKey[ca] ^ 1L << 62);
            return id;
        }

        // The crossing on cell (i, j)'s edge k (made once per edge). Against the same drift below the threshold, or snow-free
        // ground of the same level: where the depth crosses the threshold (linear between the texel centres: the bilinear
        // field's contour on that edge). Against another drift, another level or the window's edge: the texel border.
        private int Crossing(int i, int j, int edge, int k, float threshold)
        {
            if (_edgeVert[edge] >= 0) return _edgeVert[edge];
            int ax = i + (k == 1 || k == 2 ? 1 : 0), az = j + (k >= 2 ? 1 : 0);
            int bx = i + (k == 0 || k == 1 ? 1 : 0), bz = j + (k == 1 || k == 2 ? 1 : 0);
            // corner k = (ax, az), corner k + 1 = (bx, bz)
            if (!In(ax, az, threshold))
            {
                (ax, bx) = (bx, ax);
                (az, bz) = (bz, az);
            }
            var g = _grid;
            var ia = Gi(ax, az);
            var t = 0.5f;
            var foot = g.Ground[ia];
            var outside = bx < 0 || bz < 0 || bx >= _ww || bz >= _wh;
            int gbx = _wx0 + bx, gbz = _wz0 + bz;
            if (gbx < 0 || gbz < 0 || gbx >= g.Width || gbz >= g.Height)
            {
                // Past the grid's own edge the field reads no snow: its contour runs between the last texel and nothing.
                var va = g.Depth[ia] - threshold;
                t = Mathf.Clamp(va / (va + threshold), 0.05f, 0.95f);
            }
            else if (!outside)
            {
                var ib = Gi(bx, bz);
                var sameLevel = Mathf.Abs(g.Ground[ib] - g.Ground[ia]) <= _settings.LevelStep + 1e-4f;
                var lb = _label[ib];
                if (sameLevel && (lb == _layerLabel || lb == 0))
                {
                    var va = g.Depth[ia] - threshold;
                    var vb = g.Depth[ib] - threshold;
                    t = vb < va ? Mathf.Clamp(va / (va - vb), 0.05f, 0.95f) : 0.5f;
                }
                if (sameLevel) foot = Mathf.Min(foot, g.Ground[ib]);
            }
            // From the lower-index texel, so every window computes the same point.
            int lx = Mathf.Min(ax, bx), lz = Mathf.Min(az, bz);
            var tl = ax == lx && az == lz ? t : 1f - t;
            var c = g.CellSize;
            var gx = _wx0 + lx;
            var gz = _wz0 + lz;
            var p = ax == bx
                ? new Vector2(g.Min.x + (gx + 0.5f) * c, g.Min.y + (gz + 0.5f + tl) * c)
                : new Vector2(g.Min.x + (gx + 0.5f + tl) * c, g.Min.y + (gz + 0.5f) * c);
            var v = _crossPos.Count;
            _crossPos.Add(p);
            _crossFoot.Add(foot);
            _crossRef.Add(az * _ww + ax);
            _crossKey.Add(((long)(gz * g.Width + gx) << 1) | (ax == bx ? 1L : 0L));
            _edgeVert[edge] = v;
            return v;
        }

        // -- sweep ---------------------------------------------------------------------------------------------------------

        private float[] _sdf = Array.Empty<float>();
        private int _sx0, _sz0, _snx, _snz; // SDF nodes: global node (sx0 + u, sz0 + v) at Min + (index + 0.5) · h
        private float _sh;

        private void Sweep()
        {
            var nv = _pos.Count;
            var R = _R;
            EnsureVertexArrays(nv, R);
            var loops = _loopStart.Count - 1;
            for (var l = 0; l < loops; l++)
            {
                int s = _loopStart[l], e = _loopStart[l + 1], n = e - s;
                for (var i = 0; i < n; i++)
                {
                    _next[s + i] = s + (i + 1) % n;
                    _prev[s + i] = s + (i - 1 + n) % n;
                }
                // Outward normals (inside is on the left: outward = right of the walk), smoothed twice.
                for (var i = 0; i < n; i++)
                {
                    var a = _pos[s + (i - 1 + n) % n];
                    var b = _pos[s + (i + 1) % n];
                    var d = (b - a).normalized;
                    _nrm[s + i] = new Vector2(d.y, -d.x);
                }
                for (var it = 0; it < 2; it++)
                {
                    var prev = _nrm[s + n - 1];
                    var first = _nrm[s];
                    for (var i = 0; i < n; i++)
                    {
                        var cur = _nrm[s + i];
                        var nxt = i + 1 < n ? _nrm[s + i + 1] : first;
                        var m = prev + 2f * cur + nxt;
                        _nrm[s + i] = m.sqrMagnitude > 1e-12f ? m.normalized : cur;
                        prev = cur;
                    }
                }
            }

            var sws = Stopwatch.StartNew();
            BuildSdf();
            Stats.SdfMs += sws.Elapsed.TotalMilliseconds;
            var h = _sh;
            for (var v = 0; v < nv; v++)
            {
                var o = _pos[v];
                var nrm = _nrm[v];
                var d = Mathf.Max(SampleDp(o - nrm * 0.5f, _ref[v]), _settings.MinDepth);
                if (d > _layerCap) d = _layerCap;
                var b = v * R;
                _p.Evaluate(d, _wallRings, _shoulderRings, _px.AsSpan(b, R), _py.AsSpan(b, R), _ptx.AsSpan(b, R),
                    _pty.AsSpan(b, R));
                float needIn = 0f, needOut = 0f;
                for (var r = 0; r < R; r++)
                {
                    var x = _px[b + r];
                    if (x > needIn) needIn = x;
                    if (-x > needOut) needOut = -x;
                }
                _kIn[v] = Clamp(o, -nrm, needIn, h, 1f);
                _kOut[v] = Clamp(o, nrm, needOut, h, -1f);
            }
            // The polyline's own curvature: offsetting a vertex inward by more than its edge's length over the turn of the
            // normals across that edge folds the ring (outward likewise where the outline turns the other way).
            for (var l = 0; l < loops; l++)
            {
                int s = _loopStart[l], e = _loopStart[l + 1], n = e - s;
                for (var i = 0; i < n; i++)
                {
                    int a = s + i, b = s + (i + 1) % n;
                    var turn = Vector2.SignedAngle(_nrm[a], _nrm[b]) * Mathf.Deg2Rad;
                    if (Mathf.Abs(turn) < 1e-4f) continue;
                    var rho = 0.8f * (_pos[b] - _pos[a]).magnitude / Mathf.Abs(turn);
                    var k = turn > 0f ? _kIn : _kOut;
                    var need = turn > 0f ? NeedIn(a) : NeedOut(a);
                    if (need > rho) k[a] = Mathf.Min(k[a], rho / need);
                    need = turn > 0f ? NeedIn(b) : NeedOut(b);
                    if (need > rho) k[b] = Mathf.Min(k[b], rho / need);
                }
            }
            // Smooth the compression along each loop (it only ever shrinks).
            for (var l = 0; l < loops; l++)
            {
                int s = _loopStart[l], e = _loopStart[l + 1];
                SmoothK(_kIn, s, e);
                SmoothK(_kOut, s, e);
            }
            PlaceRings();
        }

        // The compression that keeps a ring vertex `need` metres along `dir` short of the medial axis: the SDF (sign:
        // +1 inside, −1 outside) grows with r while the vertex's own edge is the nearest; once something else is nearer,
        // the axis is crossed.
        private float Clamp(Vector2 o, Vector2 dir, float need, float h, float sign)
        {
            if (need < 1e-4f) return 1f;
            var rho = 0f;
            var end = need * 1.5f + h;
            for (var r = h; r <= end; r += h * 0.5f)
            {
                var s = sign * SampleSdf(o + dir * r);
                if (s < 0.8f * r - h) break;
                rho = r;
            }
            return Mathf.Min(1f, 0.8f * rho / need);
        }

        // The compression's smoothing, only within a few vertices of the ones just compressed (so a crossing elsewhere in
        // the window never moves these rings: every window agrees).
        private void SmoothAround(HashSet<int> centres)
        {
            _near.Clear();
            foreach (var c in centres)
            {
                var v = c;
                for (var k = 0; k < 4; k++) v = _prev[v];
                for (var k = 0; k < 9; k++)
                {
                    _near.Add(v);
                    v = _next[v];
                }
            }
            if (_kTmp.Length < 2 * _near.Count) _kTmp = new float[Mathf.NextPowerOfTwo(2 * _near.Count)];
            for (var it = 0; it < 3; it++)
            {
                // All at once (the set's order never matters).
                var i = 0;
                foreach (var v in _near)
                {
                    int a = _prev[v], b = _next[v];
                    _kTmp[i++] = Mathf.Min(_kIn[v], (_kIn[a] + _kIn[v] + _kIn[b]) / 3f + 0.05f);
                    _kTmp[i++] = Mathf.Min(_kOut[v], (_kOut[a] + _kOut[v] + _kOut[b]) / 3f + 0.05f);
                }
                i = 0;
                foreach (var v in _near)
                {
                    _kIn[v] = _kTmp[i++];
                    _kOut[v] = _kTmp[i++];
                }
            }
        }

        private readonly HashSet<int> _near = new();
        private float[] _kTmp = Array.Empty<float>();

        private float NeedIn(int v)
        {
            var m = 0f;
            for (var r = 0; r < _R; r++) m = Mathf.Max(m, _px[v * _R + r]);
            return m;
        }

        private float NeedOut(int v)
        {
            var m = 0f;
            for (var r = 0; r < _R; r++) m = Mathf.Max(m, -_px[v * _R + r]);
            return m;
        }

        private static void SmoothK(float[] k, int s, int e)
        {
            var n = e - s;
            if (n < 3) return;
            for (var it = 0; it < 3; it++)
            {
                var prev = k[e - 1];
                var first = k[s];
                for (var i = 0; i < n; i++)
                {
                    var cur = k[s + i];
                    var nxt = i + 1 < n ? k[s + i + 1] : first;
                    var avg = (prev + cur + nxt) / 3f + 0.05f;
                    if (avg < cur) k[s + i] = avg;
                    prev = cur;
                }
            }
        }

        private void PlaceRings()
        {
            var nv = _pos.Count;
            var R = _R;
            for (var v = 0; v < nv; v++)
            {
                var o = _pos[v];
                var nrm = _nrm[v];
                var b = v * R;
                for (var r = 0; r < R; r++)
                {
                    var x = _px[b + r];
                    x *= x > 0f ? _kIn[v] : _kOut[v];
                    _ringPos[b + r] = o - nrm * x;
                }
            }
        }

        private void EnsureVertexArrays(int nv, int R)
        {
            if (_next.Length < nv)
            {
                var cap = Mathf.NextPowerOfTwo(nv);
                _next = new int[cap];
                _prev = new int[cap];
                _nrm = new Vector2[cap];
                _kIn = new float[cap];
                _kOut = new float[cap];
            }
            if (_ringPos.Length < nv * R)
            {
                var cap = Mathf.NextPowerOfTwo(nv * R);
                _ringPos = new Vector2[cap];
                _px = new float[cap];
                _py = new float[cap];
                _ptx = new float[cap];
                _pty = new float[cap];
                _ringOut = new int[cap];
                _ringY = new float[cap];
                _ringN = new Vector3[cap];
            }
        }

        // The drift's own signed distance (metres, + inside) at nodes two texels apart over the layer's loops (+ the reach):
        // a first guess for the compression (the exact crossing check has the last word).
        private void BuildSdf()
        {
            var g = _grid;
            var h = 2f * g.CellSize;
            _sh = h;
            Vector2 mn = new(float.MaxValue, float.MaxValue), mx = new(float.MinValue, float.MinValue);
            foreach (var p in _pos)
            {
                mn = Vector2.Min(mn, p);
                mx = Vector2.Max(mx, p);
            }
            var pad = _layerReach * 1.5f + 4f * h;
            _sx0 = Mathf.FloorToInt((mn.x - pad - g.Min.x) / h);
            _sz0 = Mathf.FloorToInt((mn.y - pad - g.Min.y) / h);
            var sx1 = Mathf.CeilToInt((mx.x + pad - g.Min.x) / h);
            var sz1 = Mathf.CeilToInt((mx.y + pad - g.Min.y) / h);
            _snx = sx1 - _sx0 + 1;
            _snz = sz1 - _sz0 + 1;
            var n = _snx * _snz;
            if (_sdf.Length < n)
            {
                var cap = Mathf.NextPowerOfTwo(n);
                _sdf = new float[cap];
                _inside = new bool[cap];
                _edtA = new float[cap];
                _edtB = new float[cap];
            }
            Rasterize(_pos, _loopStart, g.Min.x + _sx0 * h, g.Min.y + _sz0 * h, h, _snx, _snz, _inside);
            DistanceTransform.Distance(_inside, true, _snx, _snz, _edtA);
            DistanceTransform.Distance(_inside, false, _snx, _snz, _edtB);
            for (var i = 0; i < n; i++) _sdf[i] = _inside[i] ? _edtA[i] * h - 0.5f * h : -(_edtB[i] * h - 0.5f * h);
        }

        private bool[] _inside = Array.Empty<bool>();
        private float[] _edtA = Array.Empty<float>(), _edtB = Array.Empty<float>();

        private float SampleSdf(Vector2 p)
        {
            var g = _grid;
            var gx = (p.x - g.Min.x) / _sh - 0.5f;
            var gz = (p.y - g.Min.y) / _sh - 0.5f;
            var x0 = Mathf.FloorToInt(gx);
            var z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0;
            x0 -= _sx0;
            z0 -= _sz0;
            float At(int x, int z) => _sdf[Mathf.Clamp(z, 0, _snz - 1) * _snx + Mathf.Clamp(x, 0, _snx - 1)];
            return Mathf.Lerp(Mathf.Lerp(At(x0, z0), At(x0 + 1, z0), fx), Mathf.Lerp(At(x0, z0 + 1), At(x0 + 1, z0 + 1), fx), fz);
        }

        // Bilinear over the window texels of this drift only; `fallback` (a window texel) when none of the four is.
        private float SampleDrift(float[] field, Vector2 p, int fallback)
        {
            var g = _grid;
            var c = g.CellSize;
            var gx = (p.x - g.Min.x) / c - 0.5f;
            var gz = (p.y - g.Min.y) / c - 0.5f;
            var x0 = Mathf.FloorToInt(gx);
            var z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0; // from global texel coordinates: every window gets the same bits
            x0 -= _wx0;
            z0 -= _wz0;
            float sum = 0f, wsum = 0f;
            for (var k = 0; k < 4; k++)
            {
                int x = x0 + (k & 1), z = z0 + (k >> 1);
                if (x < 0 || z < 0 || x >= _ww || z >= _wh) continue;
                if (_label[Gi(x, z)] != _layerLabel) continue;
                var w = ((k & 1) == 1 ? fx : 1f - fx) * ((k >> 1) == 1 ? fz : 1f - fz);
                sum += w * field[z * _ww + x];
                wsum += w;
            }
            if (wsum > 1e-6f) return sum / wsum;
            return field[fallback];
        }

        private float SampleDp(Vector2 p, int fallback) => SampleDrift(_dp, p, fallback);
        private float SampleBase(Vector2 p, int fallback) => SampleDrift(_base, p, fallback);

        private int NearestTexel(Vector2 p)
        {
            var g = _grid;
            var x = Mathf.Clamp(Mathf.FloorToInt((p.x - g.Min.x) / g.CellSize) - _wx0, 0, _ww - 1);
            var z = Mathf.Clamp(Mathf.FloorToInt((p.y - g.Min.y) / g.CellSize) - _wz0, 0, _wh - 1);
            return z * _ww + x;
        }

        // -- crossings: exact check, local compression ----------------------------------------------------------------

        private int[] _cellStart = Array.Empty<int>(), _cellItems = Array.Empty<int>(), _segBox = Array.Empty<int>();
        private readonly HashSet<int> _offenders = new();
        private readonly HashSet<int> _repaired = new();

        // Rounds of: find every crossing (after the first round, only those touching a vertex that moved), compress the
        // vertices of each by half (with a local smoothing) — from round 6 on, flatten them onto the outline instead. A
        // flattened vertex's rings lie on the outline, which never crosses, so every flattening round removes at least
        // one more vertex from play and the loop ends with no crossing left.
        private void CheckAndRepair()
        {
            _repaired.Clear();
            var nv = _pos.Count;
            if (_dirty.Length < nv) _dirty = new bool[Mathf.NextPowerOfTwo(nv)];
            var all = true;
            for (var round = 0; round <= nv + 6; round++)
            {
                FindCrossings(_offenders, all ? null : _dirty);
                if (_offenders.Count == 0) break;
                all = false;
                Array.Clear(_dirty, 0, nv);
                var flatten = round >= 6;
                foreach (var v in _offenders)
                {
                    _repaired.Add(v);
                    _kIn[v] = flatten ? 0f : _kIn[v] * 0.5f;
                    _kOut[v] = flatten ? 0f : _kOut[v] * 0.5f;
                    _dirty[v] = true;
                }
                if (!flatten)
                {
                    SmoothAround(_offenders);
                    foreach (var v in _near) _dirty[v] = true;
                }
                PlaceRings();
            }
            Stats.Repaired += _repaired.Count;
            if (_repaired.Count == 0) return;
            FindCrossings(_offenders, null); // the proof: a full check of the final rings
            Stats.Crossings += _offenders.Count;
        }

        private bool[] _dirty = Array.Empty<bool>();

        // Every ring segment (vertex v → next(v) on ring r, id v · R + r) against every other one of a ring ≤ 1 away,
        // through a 0.5 m grid; proper crossings only (touching, shared ends and collinear overlaps don't count).
        private void FindCrossings(HashSet<int> offenders, bool[]? dirty)
        {
            offenders.Clear();
            var nv = _pos.Count;
            var R = _R;
            var segs = nv * R;
            Vector2 mn = new(float.MaxValue, float.MaxValue), mx = new(float.MinValue, float.MinValue);
            for (var i = 0; i < segs; i++)
            {
                mn = Vector2.Min(mn, _ringPos[i]);
                mx = Vector2.Max(mx, _ringPos[i]);
            }
            const float cell = 0.3f;
            var cx = Mathf.Max(1, Mathf.CeilToInt((mx.x - mn.x) / cell) + 1);
            var cz = Mathf.Max(1, Mathf.CeilToInt((mx.y - mn.y) / cell) + 1);
            var cells = cx * cz;
            if (_cellStart.Length < cells + 1) _cellStart = new int[Mathf.NextPowerOfTwo(cells + 1)];
            if (_segBox.Length < segs * 4) _segBox = new int[Mathf.NextPowerOfTwo(segs * 4)];
            Array.Clear(_cellStart, 0, cells + 1);
            var total = 0;
            for (var s = 0; s < segs; s++)
            {
                var a = _ringPos[s];
                var b = _ringPos[_next[s / R] * R + s % R];
                int ix0 = (int)((Mathf.Min(a.x, b.x) - mn.x) / cell), ix1 = (int)((Mathf.Max(a.x, b.x) - mn.x) / cell);
                int iz0 = (int)((Mathf.Min(a.y, b.y) - mn.y) / cell), iz1 = (int)((Mathf.Max(a.y, b.y) - mn.y) / cell);
                _segBox[s * 4] = ix0;
                _segBox[s * 4 + 1] = iz0;
                _segBox[s * 4 + 2] = ix1;
                _segBox[s * 4 + 3] = iz1;
                for (var z = iz0; z <= iz1; z++)
                for (var x = ix0; x <= ix1; x++)
                {
                    _cellStart[z * cx + x + 1]++;
                    total++;
                }
            }
            for (var i = 0; i < cells; i++) _cellStart[i + 1] += _cellStart[i];
            if (_cellItems.Length < total) _cellItems = new int[Mathf.NextPowerOfTwo(total)];
            if (_fill.Length < cells) _fill = new int[Mathf.NextPowerOfTwo(cells)];
            Array.Copy(_cellStart, _fill, cells);
            for (var s = 0; s < segs; s++)
            {
                for (var z = _segBox[s * 4 + 1]; z <= _segBox[s * 4 + 3]; z++)
                for (var x = _segBox[s * 4]; x <= _segBox[s * 4 + 2]; x++)
                    _cellItems[_fill[z * cx + x]++] = s;
            }
            for (var cz0 = 0; cz0 < cz; cz0++)
            for (var cx0 = 0; cx0 < cx; cx0++)
            {
                var ci = cz0 * cx + cx0;
                int from = _cellStart[ci], to = _cellStart[ci + 1];
                for (var p = from; p < to; p++)
                {
                    var s = _cellItems[p];
                    var rs = s % R;
                    var vs = s / R;
                    var a1 = _ringPos[s];
                    var a2 = _ringPos[_next[vs] * R + rs];
                    for (var q = p + 1; q < to; q++)
                    {
                        var t = _cellItems[q];
                        var rt = t % R;
                        if (Math.Abs(rt - rs) > 1) continue;
                        // Test each pair once: in the first cell of the overlap of their boxes.
                        var ox = Math.Max(_segBox[s * 4], _segBox[t * 4]);
                        var oz = Math.Max(_segBox[s * 4 + 1], _segBox[t * 4 + 1]);
                        if (ox != cx0 || oz != cz0) continue;
                        var vt = t / R;
                        if (dirty != null && !dirty[vs] && !dirty[_next[vs]] && !dirty[vt] && !dirty[_next[vt]]) continue;
                        var b1 = _ringPos[t];
                        var b2 = _ringPos[_next[vt] * R + rt];
                        if (!ProperCross(a1, a2, b1, b2)) continue;
                        offenders.Add(vs);
                        offenders.Add(_next[vs]);
                        offenders.Add(vt);
                        offenders.Add(_next[vt]);
                    }
                }
            }
        }

        private int[] _fill = Array.Empty<int>();

        private static bool ProperCross(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            var d1 = Orient(q1, q2, p1);
            var d2 = Orient(q1, q2, p2);
            if (d1 == 0 || d2 == 0 || (d1 > 0) == (d2 > 0)) return false;
            var d3 = Orient(p1, p2, q1);
            var d4 = Orient(p1, p2, q2);
            return d3 != 0 && d4 != 0 && (d3 > 0) != (d4 > 0);
        }

        private static double Orient(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        // -- rings into the output, strips --------------------------------------------------------------------------------

        private void Strips()
        {
            var nv = _pos.Count;
            var R = _R;
            for (var v = 0; v < nv; v++)
            {
                var o3 = new Vector3(_nrm[v].x, 0f, _nrm[v].y);
                var b = v * R;
                for (var r = 0; r < R; r++)
                {
                    var p = _ringPos[b + r];
                    var y = r == 0 ? _foot[v] : SampleBase(p, _ref[v]) + _py[b + r];
                    var k = _ptx[b + r] > 0f || (_ptx[b + r] == 0f && _px[b + r] > 0f) ? _kIn[v] : _kOut[v];
                    var tx = _ptx[b + r] * k;
                    var ty = _pty[b + r];
                    var nrm = (o3 * ty + Vector3.up * tx).normalized;
                    if (nrm.sqrMagnitude < 0.5f) nrm = Vector3.up;
                    _ringY[b + r] = y;
                    _ringN[b + r] = nrm;
                    _ringOut[b + r] = Vertices.Count;
                    Vertices.Add(new Vector3(p.x, y, p.y));
                    Normals.Add(nrm);
                }
            }
            for (var v = 0; v < nv; v++)
            {
                var w = _next[v];
                for (var r = 0; r + 1 < R; r++)
                {
                    int a = _ringOut[v * R + r], b = _ringOut[w * R + r], c = _ringOut[w * R + r + 1], d = _ringOut[v * R + r + 1];
                    Facing(a, b, c);
                    Facing(a, c, d);
                }
            }
        }

        // A strip triangle, wound so its front faces the way its vertex normals do; degenerate ones dropped.
        private void Facing(int a, int b, int c)
        {
            var pa = Vertices[a];
            var fn = Vector3.Cross(Vertices[b] - pa, Vertices[c] - pa);
            if (fn.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(fn, Normals[a] + Normals[b] + Normals[c]) < 0f) (b, c) = (c, b);
            EmitClipped(a, b, c);
        }

        // -- clipping triangles to the tile minus the cuts ------------------------------------------------------------

        private struct CVert
        {
            public Vector3 P, N;
            public int Index; // an output vertex, or −1 (made when emitted)
        }

        private readonly List<CVert> _polyA = new(), _polyB = new();
        private readonly Stack<List<CVert>> _polyPool = new();

        private void EmitClipped(int a, int b, int c)
        {
            Vector3 pa = Vertices[a], pb = Vertices[b], pc = Vertices[c];
            float minX = Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)), maxX = Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x));
            float minZ = Mathf.Min(pa.z, Mathf.Min(pb.z, pc.z)), maxZ = Mathf.Max(pa.z, Mathf.Max(pb.z, pc.z));
            if (maxX < _rect[0].x || minX > _rect[2].x || maxZ < _rect[0].y || minZ > _rect[2].y) return;
            var inRect = minX >= _rect[0].x && maxX <= _rect[2].x && minZ >= _rect[0].y && maxZ <= _rect[2].y;
            var touchesCut = false;
            foreach (var (mn, mx) in _cutBounds)
                if (!(maxX < mn.x || minX > mx.x || maxZ < mn.y || minZ > mx.y))
                    touchesCut = true;
            if (inRect && !touchesCut)
            {
                Triangles.Add(a);
                Triangles.Add(b);
                Triangles.Add(c);
                return;
            }
            var poly = Rent();
            poly.Add(new CVert { P = pa, N = Normals[a], Index = a });
            poly.Add(new CVert { P = pb, N = Normals[b], Index = b });
            poly.Add(new CVert { P = pc, N = Normals[c], Index = c });
            if (!inRect)
                for (var e = 0; e < 4 && poly.Count >= 3; e++)
                    ClipPoly(poly, _rect[e], _rect[(e + 1) % 4], true);
            if (poly.Count >= 3) Subtract(poly, 0, minX, maxX, minZ, maxZ);
            Return(poly);
        }

        private void Subtract(List<CVert> poly, int cut, float minX, float maxX, float minZ, float maxZ)
        {
            if (cut == _cuts.Count)
            {
                Fan(poly);
                return;
            }
            var (mn, mx) = _cutBounds[cut];
            if (maxX < mn.x || minX > mx.x || maxZ < mn.y || minZ > mx.y)
            {
                Subtract(poly, cut + 1, minX, maxX, minZ, maxZ);
                return;
            }
            var q = _cuts[cut];
            var rest = Rent();
            rest.AddRange(poly);
            for (var e = 0; e < q.Length && rest.Count >= 3; e++)
            {
                var piece = Rent();
                piece.AddRange(rest);
                ClipPoly(piece, q[e], q[(e + 1) % q.Length], false); // the part outside this edge
                if (piece.Count >= 3) Subtract(piece, cut + 1, minX, maxX, minZ, maxZ);
                Return(piece);
                ClipPoly(rest, q[e], q[(e + 1) % q.Length], true);
            }
            Return(rest);
        }

        private List<CVert> Rent() => _polyPool.Count > 0 ? _polyPool.Pop() : new List<CVert>(8);

        private void Return(List<CVert> l)
        {
            l.Clear();
            _polyPool.Push(l);
        }

        // Sutherland–Hodgman against the line a → b: keeps its left (inside) or right side; points on it stay.
        private void ClipPoly(List<CVert> poly, Vector2 a, Vector2 b, bool keepLeft)
        {
            _polyA.Clear();
            var n = poly.Count;
            for (var i = 0; i < n; i++)
            {
                var u = poly[i];
                var w = poly[(i + 1) % n];
                var su = Side(a, b, u.P);
                var sw = Side(a, b, w.P);
                if (!keepLeft)
                {
                    su = -su;
                    sw = -sw;
                }
                if (su >= 0f) _polyA.Add(u);
                if ((su >= 0f) != (sw >= 0f)) _polyA.Add(Snap(Cut(u, w, su, sw), a, b));
            }
            poly.Clear();
            poly.AddRange(_polyA);
        }

        private static float Side(Vector2 a, Vector2 b, Vector3 p) => (b.x - a.x) * (p.z - a.y) - (b.y - a.y) * (p.x - a.x);

        // The point where u → w crosses side 0, computed from the lexicographically smaller end so every caller (both
        // tiles of a seam, the strip and the cap) gets the same bits.
        private static CVert Cut(CVert u, CVert w, float su, float sw)
        {
            if (Before(w.P, u.P))
            {
                (u, w) = (w, u);
                (su, sw) = (sw, su);
            }
            var t = su / (su - sw);
            return new CVert { P = u.P + (w.P - u.P) * t, N = (u.N + (w.N - u.N) * t).normalized, Index = -1 };
        }

        // A point made on an axis-aligned line (a tile edge) lies on it exactly.
        private static CVert Snap(CVert v, Vector2 a, Vector2 b)
        {
            if (a.x == b.x) v.P.x = a.x;
            else if (a.y == b.y) v.P.z = a.y;
            return v;
        }

        private static bool Before(Vector3 a, Vector3 b) => a.x < b.x || (a.x == b.x && (a.z < b.z || (a.z == b.z && a.y < b.y)));

        private void Fan(List<CVert> poly)
        {
            var n = poly.Count;
            for (var i = 0; i < n; i++)
            {
                if (poly[i].Index >= 0) continue;
                var v = poly[i];
                v.Index = Vertices.Count;
                Vertices.Add(v.P);
                Normals.Add(v.N);
                poly[i] = v;
            }
            for (var i = 1; i + 1 < n; i++)
            {
                // Only exactly flat pieces go: a thin sliver kept here is what the tile across the line clipped off, and
                // both sides must put the same vertices on the line.
                var fn = Vector3.Cross(poly[i].P - poly[0].P, poly[i + 1].P - poly[0].P);
                if (fn.sqrMagnitude == 0f) continue;
                Triangles.Add(poly[0].Index);
                Triangles.Add(poly[i].Index);
                Triangles.Add(poly[i + 1].Index);
            }
        }

        // -- the cap -------------------------------------------------------------------------------------------------------

        private struct CapPoint
        {
            public Vector2 P;
            public float Y;
            public Vector3 N;
            public int Index; // an output vertex already made, or −1
            public bool Fixed; // Y and N are known (from the rings); else the dome function gives them
            public int TagA, TagB; // the clip lines the point lies on (−1 = none)
        }

        private readonly List<List<CapPoint>> _capLoops = new(), _capNext = new();
        private readonly Stack<List<CapPoint>> _capPool = new();
        private float[] _puff = Array.Empty<float>();
        private int _px0, _pz0, _pnx, _pnz; // the dome's distance raster: texel-centre nodes
        private readonly List<Vector2[]> _tagLine = new(); // per clip tag: (from, to) of its line

        private void Cap()
        {
            var R = _R;
            var loops = _loopStart.Count - 1;
            foreach (var l in _capLoops) ReturnCap(l);
            _capLoops.Clear();
            for (var l = 0; l < loops; l++)
            {
                var loop = RentCap();
                for (var v = _loopStart[l]; v < _loopStart[l + 1]; v++)
                {
                    var i = v * R + R - 1;
                    loop.Add(new CapPoint
                    {
                        P = _ringPos[i], Y = _ringY[i], N = _ringN[i], Index = _ringOut[i], Fixed = true, TagA = -1, TagB = -1,
                    });
                }
                _capLoops.Add(loop);
            }
            var swc = Stopwatch.StartNew();
            BuildPuff();
            Stats.PuffMs += swc.Elapsed.TotalMilliseconds;
            swc.Restart();

            // Clip: inside the tile, then outside each cut.
            _tagLine.Clear();
            ClipLoops(_capLoops, _rect, true, _capNext);
            Swap();
            foreach (var q in _cuts)
            {
                ClipLoops(_capLoops, q, false, _capNext);
                Swap();
            }
            if (_capLoops.Count == 0) return;

            // Lattice points on every edge along a clip line, so both sides of a tile line share their vertices.
            var lattice = Mathf.Max(0.05f, _p.CapEdge * 0.5f);
            foreach (var loop in _capLoops) SplitOnLines(loop, lattice);

            // Outers and their holes.
            _outers.Clear();
            _holes.Clear();
            foreach (var loop in _capLoops)
            {
                _tmpPts.Clear();
                foreach (var cp in loop) _tmpPts.Add(cp.P);
                var a = PolygonTriangulator.SignedArea(_tmpPts);
                if (a > 1e-6f) _outers.Add((loop, a));
                else if (a < -1e-6f) _holes.Add(loop);
            }
            _holeOf.Clear();
            foreach (var hole in _holes)
            {
                var best = -1;
                var bestArea = float.MaxValue;
                for (var o = 0; o < _outers.Count; o++)
                {
                    if (_outers[o].Area >= bestArea || !Contains(_outers[o].Loop, hole[0].P)) continue;
                    best = o;
                    bestArea = _outers[o].Area;
                }
                _holeOf.Add(best);
            }
            Stats.ClipMs += swc.Elapsed.TotalMilliseconds;
            for (var o = 0; o < _outers.Count; o++) CapPolygon(o);
        }

        private readonly List<(List<CapPoint> Loop, float Area)> _outers = new();
        private readonly List<List<CapPoint>> _holes = new();
        private readonly List<int> _holeOf = new();
        private readonly List<Vector2> _tmpPts = new();

        private void Swap()
        {
            foreach (var l in _capLoops) ReturnCap(l);
            _capLoops.Clear();
            _capLoops.AddRange(_capNext);
            _capNext.Clear();
        }

        private List<CapPoint> RentCap() => _capPool.Count > 0 ? _capPool.Pop() : new List<CapPoint>(64);

        private void ReturnCap(List<CapPoint> l)
        {
            l.Clear();
            _capPool.Push(l);
        }

        private static bool Contains(List<CapPoint> poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[j].P;
                var b = poly[i].P;
                if ((a.y > p.y) == (b.y > p.y)) continue;
                if (p.x < a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x)) inside = !inside;
            }
            return inside;
        }

        private readonly List<Vector2> _cv = new();
        private readonly List<int> _ct = new();
        private readonly List<CapPoint> _cp = new();
        private readonly List<IReadOnlyList<Vector2>> _holeLists = new();
        private readonly List<List<Vector2>> _holeVecPool = new();

        private void CapPolygon(int o)
        {
            _cp.Clear();
            _cv.Clear();
            _ct.Clear();
            _holeLists.Clear();
            var outer = _outers[o].Loop;
            var outerPts = new List<Vector2>(outer.Count);
            foreach (var cp in outer)
            {
                outerPts.Add(cp.P);
                _cp.Add(cp);
            }
            var hv = 0;
            for (var h = 0; h < _holes.Count; h++)
            {
                if (_holeOf[h] != o || _holes[h].Count < 3) continue;
                if (hv == _holeVecPool.Count) _holeVecPool.Add(new List<Vector2>());
                var list = _holeVecPool[hv++];
                list.Clear();
                foreach (var cp in _holes[h])
                {
                    list.Add(cp.P);
                    _cp.Add(cp);
                }
                _holeLists.Add(list);
            }
            var swc = Stopwatch.StartNew();
            if (!PolygonTriangulator.TriangulateIndices(outerPts, _holeLists, _ct))
            {
                Stats.CapFailures++;
                return;
            }
            Stats.EarcutMs += swc.Elapsed.TotalMilliseconds;
            swc.Restart();
            foreach (var cp in _cp) _cv.Add(cp.P);
            var fixedCount = _cv.Count;
            _cdt.Reset(_cv, _ct);
            _cdt.MakeDelaunay();
            Stats.FlipMs += swc.Elapsed.TotalMilliseconds;
            swc.Restart();
            InsertLattice(outer, o);
            Stats.LatticeMs += swc.Elapsed.TotalMilliseconds;
            swc.Restart();
            _cv.Clear();
            _cv.AddRange(_cdt.Points);
            _ct.Clear();
            _ct.AddRange(_cdt.Triangles);

            // Vertices: the ring's own where the cap meets it, the dome function elsewhere.
            if (_capIndex.Length < _cv.Count) _capIndex = new int[Mathf.NextPowerOfTwo(_cv.Count)];
            for (var i = 0; i < _cv.Count; i++)
            {
                if (i < fixedCount && _cp[i].Fixed)
                {
                    var cp = _cp[i];
                    if (cp.Index >= 0) _capIndex[i] = cp.Index;
                    else
                    {
                        _capIndex[i] = Vertices.Count;
                        Vertices.Add(new Vector3(cp.P.x, cp.Y, cp.P.y));
                        Normals.Add(cp.N);
                    }
                    continue;
                }
                var p = _cv[i];
                var y = Dome(p, out var nrm);
                _capIndex[i] = Vertices.Count;
                Vertices.Add(new Vector3(p.x, y, p.y));
                Normals.Add(nrm);
            }
            Stats.DomeMs += swc.Elapsed.TotalMilliseconds;
            // earcut / flips give counter-clockwise (x, z) triangles: Unity's front face (seen from above) is the reverse.
            Stats.CapTriangles += _ct.Count / 3;
            for (var t = 0; t < _ct.Count; t += 3)
            {
                Triangles.Add(_capIndex[_ct[t]]);
                Triangles.Add(_capIndex[_ct[t + 2]]);
                Triangles.Add(_capIndex[_ct[t + 1]]);
            }
        }

        private int[] _capIndex = Array.Empty<int>();
        private readonly DelaunayMesh _cdt = new();

        // Interior points from one global triangular lattice (spacing CapEdge), kept clear of the cap's edge, the tile's
        // lines and the cuts: the top's triangles stay about CapEdge wide without refining down from the dense rim.
        private void InsertLattice(List<CapPoint> outer, int o)
        {
            var s = _p.CapEdge;
            if (s <= 0f) return;
            var clear = 0.45f * s;
            var row = s * 0.8660254f;
            Vector2 mn = outer[0].P, mx = outer[0].P;
            foreach (var cp in outer)
            {
                mn = Vector2.Min(mn, cp.P);
                mx = Vector2.Max(mx, cp.P);
            }
            mn = Vector2.Max(mn, _rect[0]);
            mx = Vector2.Min(mx, _rect[2]);
            var k0 = Mathf.CeilToInt(mn.y / row);
            var k1 = Mathf.FloorToInt(mx.y / row);
            for (var k = k0; k <= k1; k++)
            {
                var z = k * row;
                var shift = (k & 1) == 0 ? 0f : 0.5f * s;
                var j0 = Mathf.CeilToInt((mn.x - shift) / s);
                var j1 = Mathf.FloorToInt((mx.x - shift) / s);
                for (var j = j0; j <= j1; j++)
                {
                    var p = new Vector2(j * s + shift, z);
                    if (p.x - _rect[0].x < clear || _rect[2].x - p.x < clear || p.y - _rect[0].y < clear ||
                        _rect[2].y - p.y < clear) continue;
                    if (SamplePuff(p) < clear) continue;
                    if (NearCut(p, clear)) continue;
                    // Clear of the cap's edge (the dome raster), so inside it and in none of its holes; with several
                    // pieces (a cap cut by the tile or a cut), only this one's.
                    if (_outers.Count > 1 && !Contains(outer, p)) continue;
                    _cdt.Insert(p);
                }
            }
        }

        private bool NearCut(Vector2 p, float clear)
        {
            foreach (var q in _cuts)
            {
                var inside = true;
                for (var e = 0; e < q.Length; e++)
                {
                    var a = q[e];
                    var b = q[(e + 1) % q.Length];
                    if (SideXZ(a, b, p) < 0f) inside = false;
                    if (PolygonSimplify.DistanceToSegment(p, a, b) < clear) return true;
                }
                if (inside) return true;
            }
            return false;
        }

        // The top: base + depth · (1 + puff · f + lumps · noise · f), f rising from 0 at the cap's edge to 1 PuffReach in;
        // the normal from its gradient (so two tiles agree on every shared vertex).
        private float Dome(Vector2 p, out Vector3 normal)
        {
            var y = DomeY(p);
            const float e = 0.125f;
            var dx = DomeY(p + new Vector2(e, 0f)) - DomeY(p - new Vector2(e, 0f));
            var dz = DomeY(p + new Vector2(0f, e)) - DomeY(p - new Vector2(0f, e));
            normal = new Vector3(-dx / (2f * e), 1f, -dz / (2f * e)).normalized;
            return y;
        }

        private float DomeY(Vector2 p)
        {
            var fallback = NearestTexel(p);
            var b = SampleBase(p, fallback);
            var d = Mathf.Max(SampleDp(p, fallback), _settings.MinDepth);
            if (d > _layerCap) d = _layerCap;
            var f = 0f;
            if (_p.PuffReach > 1e-4f) f = Mathf.SmoothStep(0f, 1f, SamplePuff(p) / _p.PuffReach);
            else f = 1f;
            var lump = 0f;
            if (_p.Lumps != 0f && _p.LumpScale > 1e-3f)
            {
                var q = NoisePoint(p) / _p.LumpScale;
                lump = GradientNoise.Sample(q.x + 31.7f, q.y + 11.3f);
            }
            return b + d * (1f + _p.Puff * f + _p.Lumps * lump * f);
        }

        private Vector2 NoisePoint(Vector2 p)
        {
            var s = _settings;
            if (s.NoiseYaw != 0f)
            {
                var r = s.NoiseYaw * Mathf.Deg2Rad;
                float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
                p = new Vector2(cs * p.x + sn * p.y, -sn * p.x + cs * p.y);
            }
            return p + s.NoiseOffset;
        }

        // Distance in from the cap's own edge (the innermost rings, unclipped), at texel-centre nodes.
        private void BuildPuff()
        {
            var g = _grid;
            var c = 2f * g.CellSize; // the dome's distance is smooth over metres: half the texel resolution is plenty
            Vector2 mn = new(float.MaxValue, float.MaxValue), mx = new(float.MinValue, float.MinValue);
            var R = _R;
            for (var v = 0; v < _pos.Count; v++)
            {
                mn = Vector2.Min(mn, _ringPos[v * R + R - 1]);
                mx = Vector2.Max(mx, _ringPos[v * R + R - 1]);
            }
            _px0 = Mathf.FloorToInt((mn.x - g.Min.x) / c) - 1;
            _pz0 = Mathf.FloorToInt((mn.y - g.Min.y) / c) - 1;
            _pnx = Mathf.CeilToInt((mx.x - g.Min.x) / c) + 1 - _px0 + 1;
            _pnz = Mathf.CeilToInt((mx.y - g.Min.y) / c) + 1 - _pz0 + 1;
            var n = _pnx * _pnz;
            if (_puff.Length < n)
            {
                var cap = Mathf.NextPowerOfTwo(n);
                _puff = new float[cap];
                _puffIn = new bool[cap];
            }
            _innerPts.Clear();
            for (var v = 0; v < _pos.Count; v++) _innerPts.Add(_ringPos[v * R + R - 1]);
            Rasterize(_innerPts, _loopStart, g.Min.x + _px0 * c, g.Min.y + _pz0 * c, c, _pnx, _pnz, _puffIn);
            DistanceTransform.Distance(_puffIn, true, _pnx, _pnz, _puff);
            for (var i = 0; i < n; i++) _puff[i] = _puffIn[i] ? Mathf.Max(0f, _puff[i] * c - 0.5f * c) : 0f;
        }

        private bool[] _puffIn = Array.Empty<bool>();
        private readonly List<Vector2> _innerPts = new();

        private float SamplePuff(Vector2 p)
        {
            var g = _grid;
            var c = 2f * g.CellSize;
            var gx = (p.x - g.Min.x) / c - 0.5f;
            var gz = (p.y - g.Min.y) / c - 0.5f;
            var x0 = Mathf.FloorToInt(gx);
            var z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0;
            x0 -= _px0;
            z0 -= _pz0;
            float At(int x, int z) => _puff[Mathf.Clamp(z, 0, _pnz - 1) * _pnx + Mathf.Clamp(x, 0, _pnx - 1)];
            return Mathf.Lerp(Mathf.Lerp(At(x0, z0), At(x0 + 1, z0), fx), Mathf.Lerp(At(x0, z0 + 1), At(x0 + 1, z0 + 1), fx), fz);
        }

        // -- clipping the cap's loops by a convex polygon -----------------------------------------------------------------

        private sealed class Chain
        {
            public readonly List<CapPoint> Points = new();
            public float SIn, SOut; // perimeter positions (edge index + fraction) of its first and last point
            public bool Used;
        }

        private readonly List<Chain> _chains = new();
        private readonly Stack<Chain> _chainPool = new();

        // Clips the region the loops bound (disjoint simple loops, inside on the left) by convex CCW polygon `q`: keeps
        // the part inside (or outside) it. Points made on q's edges are tagged with the edge (for the lattice split).
        private void ClipLoops(List<List<CapPoint>> loops, Vector2[] q, bool keepInside, List<List<CapPoint>> result)
        {
            var tagBase = _tagLine.Count;
            for (var e = 0; e < q.Length; e++) _tagLine.Add(new[] { q[e], q[(e + 1) % q.Length] });
            foreach (var ch in _chains) ReleaseChain(ch);
            _chains.Clear();
            var m = q.Length;
            foreach (var loop in loops)
            {
                var n = loop.Count;
                // Find an edge whose kept part starts on q's boundary.
                var startEdge = -1;
                var anyKept = false;
                for (var i = 0; i < n && startEdge < 0; i++)
                {
                    Segment(loop[i], loop[(i + 1) % n], q, keepInside, out var pieces);
                    for (var k = 0; k < pieces; k++)
                    {
                        anyKept = true;
                        if (_pieceStartOn[k] >= 0)
                        {
                            startEdge = i;
                            break;
                        }
                    }
                }
                if (startEdge < 0)
                {
                    if (!anyKept) continue; // wholly cut away
                    var copy = RentCap();
                    copy.AddRange(loop);
                    result.Add(copy);
                    continue;
                }
                Chain? cur = null;
                for (var s = 0; s < n; s++)
                {
                    var i = (startEdge + s) % n;
                    Segment(loop[i], loop[(i + 1) % n], q, keepInside, out var pieces);
                    for (var k = 0; k < pieces; k++)
                    {
                        if (_pieceStartOn[k] >= 0)
                        {
                            if (cur != null) ReleaseChain(cur); // never expected: a chain left open
                            cur = RentChain();
                            var a = _pieceA[k];
                            a.TagA = tagBase + _pieceStartOn[k];
                            cur.Points.Add(a);
                            cur.SIn = _pieceStartOn[k] + EdgeFraction(q, _pieceStartOn[k], a.P);
                        }
                        else if (cur != null) cur.Points.Add(loop[i]);
                        if (cur == null) continue;
                        if (_pieceEndOn[k] >= 0)
                        {
                            var b = _pieceB[k];
                            b.TagA = tagBase + _pieceEndOn[k];
                            cur.Points.Add(b);
                            cur.SOut = _pieceEndOn[k] + EdgeFraction(q, _pieceEndOn[k], b.P);
                            if (cur.Points.Count >= 2 && !(cur.Points.Count == 2 && cur.Points[0].P == cur.Points[1].P))
                                _chains.Add(cur);
                            else ReleaseChain(cur);
                            cur = null;
                        }
                    }
                }
                if (cur != null) ReleaseChain(cur);
            }

            if (_chains.Count == 0)
            {
                // No loop crosses q's boundary: all of it is inside the region or none of it.
                if (RegionContains(loops, q[0]))
                {
                    var box = RentCap();
                    for (var k = 0; k < m; k++)
                    {
                        var e = keepInside ? k : m - 1 - k;
                        box.Add(Corner(q, e, tagBase));
                    }
                    result.Add(box);
                }
                return;
            }

            // Join chains along q's boundary: counter-clockwise when keeping the inside, clockwise when keeping the outside.
            foreach (var start in _chains)
            {
                if (start.Used) continue;
                var outLoop = RentCap();
                var ch = start;
                var guard = 0;
                while (!ch.Used && guard++ < 100000)
                {
                    ch.Used = true;
                    outLoop.AddRange(ch.Points);
                    Chain? best = null;
                    var bestD = float.MaxValue;
                    foreach (var o in _chains)
                    {
                        if (o.Used && o != start) continue;
                        var d = keepInside ? Mod(o.SIn - ch.SOut, m) : Mod(ch.SOut - o.SIn, m);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = o;
                        }
                    }
                    if (best == null) break;
                    // q's corners passed on the way.
                    if (keepInside)
                    {
                        var k = Mathf.FloorToInt(ch.SOut) + 1;
                        for (var passed = 0; passed < m; passed++, k++)
                        {
                            var ks = (float)k;
                            if (Mod(ks - ch.SOut, m) >= bestD || Mod(ks - ch.SOut, m) <= 0f) break;
                            outLoop.Add(Corner(q, ((k % m) + m) % m, tagBase));
                        }
                    }
                    else
                    {
                        var k = Mathf.CeilToInt(ch.SOut) - 1;
                        if (k == ch.SOut) k--;
                        for (var passed = 0; passed < m; passed++, k--)
                        {
                            var ks = (float)k;
                            if (Mod(ch.SOut - ks, m) >= bestD || Mod(ch.SOut - ks, m) <= 0f) break;
                            outLoop.Add(Corner(q, ((k % m) + m) % m, tagBase));
                        }
                    }
                    ch = best;
                }
                result.Add(outLoop);
            }
        }

        private static float Mod(float a, float m)
        {
            var r = a % m;
            return r < 0f ? r + m : r;
        }

        private CapPoint Corner(Vector2[] q, int k, int tagBase) => new()
        {
            P = q[k], Index = -1, Fixed = false, TagA = tagBase + k, TagB = tagBase + (k + q.Length - 1) % q.Length,
        };

        private static float EdgeFraction(Vector2[] q, int e, Vector2 p)
        {
            var a = q[e];
            var b = q[(e + 1) % q.Length];
            var ab = b - a;
            return Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
        }

        private bool RegionContains(List<List<CapPoint>> loops, Vector2 p)
        {
            var inside = false;
            foreach (var l in loops)
                if (Contains(l, p))
                    inside = !inside;
            return inside;
        }

        private Chain RentChain()
        {
            var c = _chainPool.Count > 0 ? _chainPool.Pop() : new Chain();
            c.Points.Clear();
            c.Used = false;
            return c;
        }

        private void ReleaseChain(Chain c)
        {
            c.Points.Clear();
            _chainPool.Push(c);
        }

        // The kept pieces of segment a → b against convex q (Cyrus–Beck): up to two, each with its ends, and for each end
        // the edge of q it lies on (−1 = it is the segment's own end).
        private readonly CapPoint[] _pieceA = new CapPoint[2], _pieceB = new CapPoint[2];
        private readonly int[] _pieceStartOn = new int[2], _pieceEndOn = new int[2];

        private void Segment(CapPoint a, CapPoint b, Vector2[] q, bool keepInside, out int pieces)
        {
            float tLo = 0f, tHi = 1f;
            int enter = -1, exit = -1;
            var outside = false;
            CapPoint pIn = default, pOut = default;
            for (var e = 0; e < q.Length && !outside; e++)
            {
                var qa = q[e];
                var qb = q[(e + 1) % q.Length];
                var sa = SideXZ(qa, qb, a.P);
                var sb = SideXZ(qa, qb, b.P);
                if (sa < 0f && sb < 0f)
                {
                    outside = true;
                    break;
                }
                if (sa < 0f)
                {
                    var t = Param(a, b, sa, sb, out var p);
                    p.P = SnapP(p.P, qa, qb);
                    if (t >= tLo)
                    {
                        tLo = t;
                        enter = e;
                        pIn = p;
                    }
                }
                else if (sb < 0f)
                {
                    var t = Param(a, b, sa, sb, out var p);
                    p.P = SnapP(p.P, qa, qb);
                    if (t <= tHi)
                    {
                        tHi = t;
                        exit = e;
                        pOut = p;
                    }
                }
            }
            if (!outside && tLo > tHi) outside = true;
            pieces = 0;
            if (keepInside)
            {
                if (outside) return;
                _pieceA[0] = enter >= 0 ? pIn : a;
                _pieceStartOn[0] = enter;
                _pieceB[0] = exit >= 0 ? pOut : b;
                _pieceEndOn[0] = exit;
                pieces = 1;
                return;
            }
            if (outside)
            {
                _pieceA[0] = a;
                _pieceStartOn[0] = -1;
                _pieceB[0] = b;
                _pieceEndOn[0] = -1;
                pieces = 1;
                return;
            }
            if (enter >= 0)
            {
                _pieceA[pieces] = a;
                _pieceStartOn[pieces] = -1;
                _pieceB[pieces] = pIn;
                _pieceEndOn[pieces] = enter;
                pieces++;
            }
            if (exit >= 0)
            {
                _pieceA[pieces] = pOut;
                _pieceStartOn[pieces] = exit;
                _pieceB[pieces] = b;
                _pieceEndOn[pieces] = -1;
                pieces++;
            }
        }

        private static Vector2 SnapP(Vector2 p, Vector2 a, Vector2 b)
        {
            if (a.x == b.x) p.x = a.x;
            else if (a.y == b.y) p.y = a.y;
            return p;
        }

        private static float SideXZ(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        // Where a → b crosses side 0: the point (Y and N interpolated; computed from the lexicographically smaller end, as
        // the strip clip does) and its parameter along a → b.
        private static float Param(CapPoint a, CapPoint b, float sa, float sb, out CapPoint p)
        {
            var swap = Before(new Vector3(b.P.x, b.Y, b.P.y), new Vector3(a.P.x, a.Y, a.P.y));
            CapPoint u = a, w = b;
            float su = sa, sw = sb;
            if (swap)
            {
                (u, w) = (w, u);
                (su, sw) = (sw, su);
            }
            var t = su / (su - sw);
            var pos3 = new Vector3(u.P.x, u.Y, u.P.y) + (new Vector3(w.P.x, w.Y, w.P.y) - new Vector3(u.P.x, u.Y, u.P.y)) * t;
            p = new CapPoint
            {
                P = new Vector2(pos3.x, pos3.z), Y = pos3.y, N = (u.N + (w.N - u.N) * t).normalized, Index = -1,
                Fixed = a.Fixed && b.Fixed, TagA = -1, TagB = -1,
            };
            return swap ? 1f - t : t;
        }

        // Extra points on every loop edge that lies along a clip line, at multiples of `spacing` along the line (tile lines:
        // in grid coordinates; other lines: from their first point).
        private void SplitOnLines(List<CapPoint> loop, float spacing)
        {
            _split.Clear();
            var n = loop.Count;
            for (var i = 0; i < n; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % n];
                _split.Add(a);
                var tag = Shared(a, b);
                if (tag < 0) continue;
                var line = _tagLine[tag];
                var axisX = line[0].y == line[1].y; // a horizontal tile line: lattice in x
                var axisZ = line[0].x == line[1].x;
                float ta, tb;
                Vector2 dir = default, origin = default;
                if (axisX)
                {
                    ta = a.P.x;
                    tb = b.P.x;
                }
                else if (axisZ)
                {
                    ta = a.P.y;
                    tb = b.P.y;
                }
                else
                {
                    origin = line[0];
                    dir = (line[1] - line[0]).normalized;
                    ta = Vector2.Dot(a.P - origin, dir);
                    tb = Vector2.Dot(b.P - origin, dir);
                }
                var lo = Mathf.Min(ta, tb);
                var hi = Mathf.Max(ta, tb);
                var k0 = Mathf.FloorToInt(lo / spacing) + 1;
                var k1 = Mathf.CeilToInt(hi / spacing) - 1;
                if (k1 < k0) continue;
                var up = tb > ta;
                for (var s = 0; s <= k1 - k0; s++)
                {
                    var k = up ? k0 + s : k1 - s;
                    var t = k * spacing;
                    if (t <= lo || t >= hi) continue;
                    Vector2 p;
                    if (axisX) p = new Vector2(t, line[0].y);
                    else if (axisZ) p = new Vector2(line[0].x, t);
                    else p = origin + dir * t;
                    _split.Add(new CapPoint { P = p, Index = -1, Fixed = false, TagA = tag, TagB = -1 });
                }
            }
            loop.Clear();
            loop.AddRange(_split);
        }

        private readonly List<CapPoint> _split = new();

        private static int Shared(CapPoint a, CapPoint b)
        {
            if (a.TagA >= 0 && (a.TagA == b.TagA || a.TagA == b.TagB)) return a.TagA;
            if (a.TagB >= 0 && (a.TagB == b.TagA || a.TagB == b.TagB)) return a.TagB;
            return -1;
        }

        // -- shared helpers -----------------------------------------------------------------------------------------------

        // Even-odd fill of the loops (points `pts`, loops from `starts`) at nodes (x0 + (u + 0.5) h, z0 + (v + 0.5) h).
        private int[] _rowCount = Array.Empty<int>();
        private float[] _rowX = Array.Empty<float>();

        private void Rasterize(List<Vector2> pts, List<int> starts, float x0, float z0, float h, int nx, int nz, bool[] inside)
        {
            Array.Clear(inside, 0, nx * nz);
            if (_rowCount.Length < nz + 1) _rowCount = new int[Mathf.NextPowerOfTwo(nz + 1)];
            Array.Clear(_rowCount, 0, nz + 1);
            var loops = starts.Count - 1;
            var total = 0;
            for (var pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    for (var v = 0; v < nz; v++) _rowCount[v + 1] += _rowCount[v];
                    total = _rowCount[nz];
                    if (_rowX.Length < total) _rowX = new float[Mathf.NextPowerOfTwo(Mathf.Max(1, total))];
                    if (_rowFill.Length < nz) _rowFill = new int[Mathf.NextPowerOfTwo(nz)];
                    Array.Copy(_rowCount, _rowFill, nz);
                }
                for (var l = 0; l < loops; l++)
                {
                    int s = starts[l], e = starts[l + 1];
                    for (var i = s; i < e; i++)
                    {
                        var a = pts[i];
                        var b = pts[i + 1 < e ? i + 1 : s];
                        if (a.y == b.y) continue;
                        var lo = Mathf.Min(a.y, b.y);
                        var hi = Mathf.Max(a.y, b.y);
                        var v0 = Mathf.Max(0, Mathf.CeilToInt((lo - z0) / h - 0.5f));
                        for (var v = v0; v < nz; v++)
                        {
                            var z = z0 + (v + 0.5f) * h;
                            if (z >= hi) break;
                            if (z < lo) continue;
                            if (pass == 0) _rowCount[v + 1]++;
                            else _rowX[_rowFill[v]++] = a.x + (z - a.y) * (b.x - a.x) / (b.y - a.y);
                        }
                    }
                }
            }
            for (var v = 0; v < nz; v++)
            {
                int from = _rowCount[v], to = _rowCount[v + 1];
                if (to - from < 2) continue;
                Array.Sort(_rowX, from, to - from);
                for (var k = from; k + 1 < to; k += 2)
                {
                    var u0 = Mathf.Max(0, Mathf.CeilToInt((_rowX[k] - x0) / h - 0.5f));
                    var u1 = Mathf.Min(nx - 1, Mathf.CeilToInt((_rowX[k + 1] - x0) / h - 0.5f) - 1);
                    for (var u = u0; u <= u1; u++) inside[v * nx + u] = true;
                }
            }
        }

        private int[] _rowFill = Array.Empty<int>();

        // Drops vertices no triangle uses (the window's rings outside the tile).
        private void Compact()
        {
            var n = Vertices.Count;
            if (_remap.Length < n) _remap = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, n))];
            Array.Fill(_remap, -1, 0, n);
            foreach (var t in Triangles) _remap[t] = 0;
            var k = 0;
            for (var i = 0; i < n; i++)
            {
                if (_remap[i] < 0) continue;
                _remap[i] = k;
                Vertices[k] = Vertices[i];
                Normals[k] = Normals[i];
                k++;
            }
            Vertices.RemoveRange(k, n - k);
            Normals.RemoveRange(k, n - k);
            for (var i = 0; i < Triangles.Count; i++) Triangles[i] = _remap[Triangles[i]];
        }

        private int[] _remap = Array.Empty<int>();
    }
}
