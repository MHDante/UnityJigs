using System;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// One snow SHAPE for <see cref="SnowMesher"/>: the profile swept along a drift's outline — a wall (optionally leaning
    /// out over a tucked base, with a belly) and a rounded shoulder up to the top — and the domed, lumpy top inside it.
    /// Horizontal sizes are metres; heights are fractions of the local snow depth, so one shape fits shallow and deep snow.
    /// </summary>
    [Serializable]
    public struct SnowProfile
    {
        [Tooltip("False: gameplay-only snow — it bakes no mesh (it still ends the outlines of the snow beside it).")]
        public bool Draws;

        [Tooltip("Fraction of the depth that rises as the side wall (0 = no wall).")]
        public float Wall;

        [Tooltip("Wall steepness, degrees from horizontal: 90 = vertical, above 90 leans OUT (a tucked base under an " +
                 "overhanging lip).")]
        public float WallAngle;

        [Tooltip("Outward belly of the wall at mid-height, fraction of the depth.")]
        public float Bulge;

        [Tooltip("Width of the rounded shoulder as a multiple of its height (the depth above the wall).")]
        public float Shoulder;

        [Tooltip("The shoulder's width is clamped to this range, metres.")]
        public float ShoulderMin, ShoulderMax;

        [Tooltip("Extra dome over the top, fraction of the depth.")]
        public float Puff;

        [Tooltip("Distance in from the shoulder over which the dome reaches its full height, metres.")]
        public float PuffReach;

        [Tooltip("Soft unevenness of the top, fraction of the depth.")]
        public float Lumps;

        [Tooltip("Wavelength of the lumps, metres.")]
        public float LumpScale;

        [Tooltip("Longest outline edge, metres: the outline keeps every contour point it needs to stay on the depth contour " +
                 "(within the mesher's OutlineTolerance) and drops the others up to this spacing.")]
        public float Spacing;

        [Tooltip("Rings of vertices up the wall.")]
        public int WallRings;

        [Tooltip("Rings of vertices around the shoulder.")]
        public int ShoulderRings;

        [Tooltip("Longest step between rings, metres: shallow snow gets fewer rings than WallRings / ShoulderRings " +
                 "(0 = always all of them).")]
        public float RingStep;

        [Tooltip("Longest edge of the top's triangles, metres.")]
        public float CapEdge;

        [Tooltip("Painted depths at least this far apart become stacked TIERS — the deeper paint gets its own wall and lip " +
                 "on top of the shallower snow, like stacked hills (0 = off).")]
        public float TierStep;

        /// <summary>The painted-snow default: matches the Blender snow blobs (a leaning, tucked base, a long soft dome).</summary>
        public static SnowProfile Marshmallow => new()
        {
            Draws = true, Wall = 0.5f, WallAngle = 112f, Bulge = 0.04f, Shoulder = 5f, ShoulderMin = 0.05f,
            ShoulderMax = 1.5f, Puff = 0.08f, PuffReach = 3f, Lumps = 0.05f, LumpScale = 5f, Spacing = 0.7f, WallRings = 3,
            ShoulderRings = 5, RingStep = 0.2f, CapEdge = 1.5f, TierStep = 0.15f,
        };

        /// <summary>Flat-topped snow hills: a near-vertical wall, a short rounded shoulder (matches the Blender SnowHills).</summary>
        public static SnowProfile Mesa => new()
        {
            Draws = true, Wall = 0.6f, WallAngle = 84f, Bulge = 0f, Shoulder = 1.3f, ShoulderMin = 0.05f, ShoulderMax = 1.2f,
            Puff = 0.04f, PuffReach = 2f, Lumps = 0.06f, LumpScale = 6f, Spacing = 0.8f, WallRings = 2, ShoulderRings = 4,
            RingStep = 0.2f, CapEdge = 2.5f,
            TierStep = 0.15f,
        };

        /// <summary>Snow the game reads but nothing draws.</summary>
        public static SnowProfile GameplayOnly => new() { Draws = false };

        /// <summary>The most rings the profile has (at any depth).</summary>
        public int Rings => Mathf.Max(0, WallRings) + 1 + Mathf.Max(1, ShoulderRings);

        /// <summary>The rings used for snow up to `depth` deep: as few as keep every step within RingStep, at most
        /// WallRings / ShoulderRings (a wall that exists keeps at least one ring, the shoulder at least two).</summary>
        public void RingsFor(float depth, out int wallRings, out int shoulderRings)
        {
            wallRings = Mathf.Max(0, WallRings);
            shoulderRings = Mathf.Max(1, ShoulderRings);
            if (RingStep <= 0f) return;
            var wallTop = Wall * depth;
            var rise = depth - wallTop;
            var width = Mathf.Clamp(Shoulder * rise, ShoulderMin, ShoulderMax);
            if (wallRings > 0) wallRings = Mathf.Clamp(Mathf.CeilToInt(wallTop / RingStep), 1, wallRings);
            shoulderRings = Mathf.Clamp(Mathf.CeilToInt((width + rise) / RingStep), Mathf.Min(2, shoulderRings), shoulderRings);
        }

        /// <summary>
        /// The profile for depth `depth` at full width: `x` = how far in from the outline each ring sits (negative = out,
        /// past the outline: a leaning wall or a belly), `y` = its height above the ground, and the profile's unit tangent
        /// (dx, dy) there. Ring 0 is the outline itself; the last ring is the top's edge (flat: tangent (1, 0)).
        /// </summary>
        public void Evaluate(float depth, Span<float> x, Span<float> y, Span<float> tx, Span<float> ty) =>
            Evaluate(depth, Mathf.Max(0, WallRings), Mathf.Max(1, ShoulderRings), x, y, tx, ty);

        /// <summary><see cref="Evaluate(float, Span{float}, Span{float}, Span{float}, Span{float})"/> with chosen ring
        /// counts (wallRings + 1 + shoulderRings values are written).</summary>
        public void Evaluate(float depth, int wallRings, int shoulderRings, Span<float> x, Span<float> y, Span<float> tx,
            Span<float> ty)
        {
            var angle = Mathf.Clamp(WallAngle, 10f, 135f) * Mathf.Deg2Rad;
            var cot = Mathf.Abs(WallAngle - 90f) < 0.01f ? 0f : 1f / Mathf.Tan(angle);
            var wallTop = Wall * depth;
            var n = 0;
            if (wallRings > 0 && wallTop > 1e-5f)
            {
                for (var i = 0; i <= wallRings; i++)
                {
                    var u = (float)i / wallRings;
                    var r = wallTop * u * cot - Bulge * depth * Mathf.Sin(Mathf.PI * u);
                    var dr = wallTop * cot - Bulge * depth * Mathf.PI * Mathf.Cos(Mathf.PI * u);
                    Set(x, y, tx, ty, n++, r, wallTop * u, dr, wallTop);
                }
            }
            else
            {
                wallTop = 0f;
                for (var i = 0; i <= wallRings; i++) Set(x, y, tx, ty, n++, 0f, 0f, cot, 1f);
            }

            // The shoulder: a cubic Bezier from the wall's top (leaving at the wall's angle) to the flat top.
            var rise = depth - wallTop;
            var width = Mathf.Clamp(Shoulder * rise, ShoulderMin, ShoulderMax);
            var p0 = new Vector2(x[n - 1], wallTop);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var lambda = 0.55f * rise / Mathf.Max(dir.y, 1e-3f);
            if (lambda * dir.x > 0.9f * width) lambda = 0.9f * width / Mathf.Max(dir.x, 1e-4f);
            var p3 = new Vector2(p0.x + width, depth);
            var p1 = p0 + dir * lambda;
            var p2 = p3 - new Vector2(0.55f * width, 0f);
            for (var i = 1; i <= shoulderRings; i++)
            {
                var t = (float)i / shoulderRings;
                var mt = 1f - t;
                var q = mt * mt * mt * p0 + 3f * mt * mt * t * p1 + 3f * mt * t * t * p2 + t * t * t * p3;
                var dq = 3f * mt * mt * (p1 - p0) + 6f * mt * t * (p2 - p1) + 3f * t * t * (p3 - p2);
                if (i == shoulderRings) dq = new Vector2(1f, 0f);
                Set(x, y, tx, ty, n++, q.x, q.y, dq.x, dq.y);
            }
        }

        private static void Set(Span<float> x, Span<float> y, Span<float> tx, Span<float> ty, int i, float px, float py,
            float dx, float dy)
        {
            var len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9f)
            {
                dx = 0f;
                dy = 1f;
                len = 1f;
            }
            x[i] = px;
            y[i] = py;
            tx[i] = dx / len;
            ty[i] = dy / len;
        }

        /// <summary>The farthest any ring sits from the outline (in or out) for depths up to `maxDepth`, metres.</summary>
        public float Reach(float maxDepth = 2.55f)
        {
            if (!Draws) return 0f;
            var n = Rings;
            float[] x = new float[n], y = new float[n], tx = new float[n], ty = new float[n];
            var reach = 0f;
            for (var d = 0.05f; d <= maxDepth + 1e-4f; d += 0.05f)
            {
                Evaluate(d, x, y, tx, ty);
                for (var i = 0; i < n; i++) reach = Mathf.Max(reach, Mathf.Abs(x[i]));
            }
            return reach;
        }
    }
}
