using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Splines;
using UnityJigs.Extensions;

namespace UnityJigs.Splines
{
    public static class SplineUtils
    {
        public static void GetNearestPointTo(this SplineContainer path, Vector3 source, out float t,
            out Vector3 point, out float distance, int resolution = SplineUtility.PickResolutionDefault,
            int iterations = 2)
        {
            var localPoint = path.transform.InverseTransformPoint(source);
            distance = SplineUtility.GetNearestPoint(
                path.Spline,
                localPoint,
                out var currentPos,
                out t,
                resolution,
                iterations);
            point = path.transform.TransformPoint(currentPos);
        }

        [MustDisposeResource]
        public static PooledObject<List<(Vector3, float)>> GetLocalMinima(
            this SplineContainer path,
            out List<(Vector3, float)> localMinima,
            Vector3 source,
            int count,
            float? coarseResolutionLength = null,
            int resolution = SplineUtility.PickResolutionDefault)
        {
            var localPoint = path.transform.InverseTransformPoint(source);
            var spline = path.Spline;
            var length = spline.GetLength();
            var scale = path.transform.lossyScale.Min();
            var coarseRes = coarseResolutionLength / scale ?? length / SplineUtility.PickResolutionDefault;
            using var pool = ListPool<(float3, float)>.Get(out var list);

            GetLocalMinima(
                spline,
                localPoint,
                list,
                count,
                coarseRes,
                resolution);


            var resPool = ListPool<(Vector3, float)>.Get(out localMinima);
            foreach (var (point, t) in list)
            {
                localMinima.Add((path.transform.TransformPoint(point), t));
            }

            return resPool;
        }

        private static void GetLocalMinima<T>(
            this T spline,
            float3 point,
            List<(float3, float)> results,
            int count,
            float coarseSegmentLength,
            int fineSegmentResolution) where T : ISpline
        {
            if (count == 0) return;

            // Clamp fine resolution similar to single-nearest API
            var fineRes = math.min(math.max(SplineUtility.PickResolutionMin, fineSegmentResolution),
                SplineUtility.PickResolutionMax);

            var totalLen = spline.GetLength();
            var closed = spline.Closed;

            // --- Coarse sampling (uniform t) derived from target coarse length ---
            var l = math.max(1e-5f, coarseSegmentLength);
            var coarseSegments = (int)math.max(2, math.ceil(totalLen / l));
            var points = closed ? coarseSegments : coarseSegments + 1;
            var dt = closed ? 1f / coarseSegments : 1f / (points - 1);

            const float epsLen = 1e-7f;
            const float epsD2 = 1e-6f;

            // Pooled scratch: sample positions and d^2
            using var posPool = ListPool<float3>.Get(out var posBuf);
            using var d2Pool = ListPool<float>.Get(out var d2Buf);
            for (var i = 0; i < points; i++)
            {
                var t = i * dt;
                var p = spline.EvaluatePosition(t);
                posBuf.Add(p);
                d2Buf.Add(math.distancesq(p, point));
            }

            // Pooled candidate buffers: (t, p, d or d^2 pre-refine)
            var cap = math.min(4 * coarseSegments, 2048);
            using var tPool = ListPool<float>.Get(out var tCand);
            using var pPool = ListPool<float3>.Get(out var pCand);
            using var dPool = ListPool<float>.Get(out var dCand); // holds d^2 (coarse), then true d (refined)

            // On-insert dedupe thresholds
            var tEps = 0.5f * dt; // within half a coarse chord in t
            var pEps2 = 0.25f * l; // ~ 1/4 coarse length in world units
            pEps2 *= pEps2;

            // Small helper: dedupe/update-or-insert a candidate
            static void InsertCandidate(
                List<float> tCand, List<float3> pCand, List<float> dCand,
                float tNew, float3 pNew, float dNew,
                float tEps, float pEps2, bool closed, int cap, float epsD2Local)
            {
                var dup = false;
                for (var k = 0; k < tCand.Count; k++)
                {
                    var dtWrap = math.abs(tNew - tCand[k]);
                    if (closed) dtWrap = math.min(dtWrap, 1f - dtWrap);

                    if (dtWrap > tEps || math.distancesq(pNew, pCand[k]) > pEps2) continue;


                    if (dNew + epsD2Local < dCand[k])
                    {
                        tCand[k] = tNew;
                        pCand[k] = pNew;
                        dCand[k] = dNew;
                    }

                    dup = true;
                    break;
                }

                if (!dup && tCand.Count < cap)
                {
                    tCand.Add(tNew);
                    pCand.Add(pNew);
                    dCand.Add(dNew);
                }
            }

            // --- Seed detection: interior chord minima + discrete sample minima ---
            var chordCount = closed ? points : points - 1;

            for (var i = 0; i < chordCount; i++)
            {
                var i1 = closed ? (i + 1) % points : i + 1;

                var t0 = i * dt;
                var t1 = i1 * dt;

                var a = posBuf[i];
                var b = posBuf[i1];
                var d2A = d2Buf[i];
                var d2B = d2Buf[i1];

                // Interior chord minimum
                var pSeg = SplineMath.PointLineNearestPoint(point, a, b, out var lineParam);
                var ab = b - a;
                var lenSq = math.lengthsq(ab);
                if (lenSq > epsLen * epsLen)
                {
                    var len = math.sqrt(lenSq);
                    if (lineParam > 0f && lineParam < len)
                    {
                        var d2Seg = math.distancesq(pSeg, point);
                        if (d2Seg + epsD2 < d2A && d2Seg + epsD2 < d2B)
                        {
                            var frac = lineParam / len; // [0,1] along chord
                            var tStar = closed ? t0 + frac * dt : math.lerp(t0, t1, frac);
                            InsertCandidate(tCand, pCand, dCand, tStar, pSeg, d2Seg, tEps, pEps2, closed, cap, epsD2);
                        }
                    }
                }

                // Discrete sample local minimum at i0 (compare neighbors)
                var il = closed ? (i - 1 + points) % points : i - 1;
                var ir = closed ? (i + 1) % points : i + 1;
                if (il >= 0 && ir < points)
                {
                    var d2L = d2Buf[il];
                    var d2R = d2Buf[ir];
                    if (d2A + epsD2 <= d2L && d2A + epsD2 <= d2R)
                    {
                        InsertCandidate(tCand, pCand, dCand, t0, a, d2A, tEps, pEps2, closed, cap, epsD2);
                    }
                }
            }

            // NEW: Endpoint minima seeding for OPEN splines (fixes misses at t==0 and t==1)
            if (!closed && points >= 2)
            {
                // Left endpoint t=0: compare against its only neighbor (right)
                var d2Left = d2Buf[0];
                var d2RightNeighbor = d2Buf[1];
                if (d2Left + epsD2 <= d2RightNeighbor)
                {
                    InsertCandidate(tCand, pCand, dCand,
                        0f, posBuf[0], d2Left, tEps, pEps2, closed, cap, epsD2);
                }

                // Right endpoint t=1: compare against its only neighbor (left)
                var last = points - 1;
                var d2Right = d2Buf[last];
                var d2LeftNeighbor = d2Buf[last - 1];
                if (d2Right + epsD2 <= d2LeftNeighbor)
                {
                    InsertCandidate(tCand, pCand, dCand,
                        1f, posBuf[last], d2Right, tEps, pEps2, closed, cap, epsD2);
                }
            }

            // Fallback to global nearest if no seeds
            if (tCand.Count == 0)
            {
                _ = SplineUtility.GetNearestPoint(spline, point, out var nearest, out var tNearest, fineRes);
                results.Add((nearest, tNearest));
                return;
            }

            // --- Refinement window (in t) derived from length target (~ two coarse chords) ---
            var halfRangeT = math.saturate(2f * l / math.max(1e-5f, totalLen));
            halfRangeT = math.max(halfRangeT, 0.5f * dt);

            // Static local function (no captures): refines a single non-wrapping range
            static void RefineRange<TSpline>(
                TSpline splineArg, float3 pointArg, float totalLenArg, int fineResArg, bool closedArg,
                float start, float end,
                ref float bestD, ref float3 bestP, ref float bestT) where TSpline : ISpline
            {
                start = math.clamp(start, 0f, 1f);
                end = math.clamp(end, 0f, 1f);
                if (end <= start) return;

                var range = new Segment(start, end - start);
                var d = float.PositiveInfinity;
                var p = bestP;
                var t = bestT;

                var seg = range;
                for (var it = 0; it < 2; it++)
                {
                    var segs = math.max(2, SplineUtility.GetSubdivisionCount(totalLenArg * seg.Length, fineResArg));
                    seg = GetNearestPoint(splineArg, pointArg, seg, out d, out p, out t, segs);
                }

                if (d >= bestD) return;
                bestD = d;
                bestP = p;
                bestT = closedArg && t >= 1f ? 0f : t; // seam-fix for closed
            }

            // Refine each seed; overwrite candidates with refined (t, p, d)
            for (var i = 0; i < tCand.Count; i++)
            {
                var tC = tCand[i];
                var start = tC - halfRangeT;
                var end = tC + halfRangeT;

                var bestD = float.PositiveInfinity;
                var bestP = pCand[i];
                var bestT = tC;

                if (!closed)
                {
                    RefineRange(spline, point, totalLen, fineRes, closed, start, end, ref bestD, ref bestP, ref bestT);
                }
                else
                {
                    // Split if the window wraps around the seam
                    if (start < 0f)
                    {
                        RefineRange(spline, point, totalLen, fineRes, closed, 0f, end, ref bestD, ref bestP, ref bestT);
                        RefineRange(spline, point, totalLen, fineRes, closed, 1f + start, 1f, ref bestD, ref bestP,
                            ref bestT);
                    }
                    else if (end > 1f)
                    {
                        RefineRange(spline, point, totalLen, fineRes, closed, start, 1f, ref bestD, ref bestP,
                            ref bestT);
                        RefineRange(spline, point, totalLen, fineRes, closed, 0f, end - 1f, ref bestD, ref bestP,
                            ref bestT);
                    }
                    else
                    {
                        RefineRange(spline, point, totalLen, fineRes, closed, start, end, ref bestD, ref bestP,
                            ref bestT);
                    }
                }

                tCand[i] = bestT;
                pCand[i] = bestP;
                dCand[i] = bestD; // now true distance
            }

            // --- Post-refinement dedupe (simple O(n^2), n is small) ---
            var tEps2 = 0.25f * dt; // tighter in t after refinement
            var pEps2Ref = 0.1f * l; // tighter in world space after refinement
            pEps2Ref *= pEps2Ref;

            var m = 0;
            for (var i = 0; i < tCand.Count; i++)
            {
                var ti = tCand[i];
                var pi = pCand[i];
                var di = dCand[i];
                var dup = false;

                for (var k = 0; k < m; k++)
                {
                    var dtWrap = math.abs(ti - tCand[k]);
                    if (closed) dtWrap = math.min(dtWrap, 1f - dtWrap);

                    if (dtWrap > tEps2 || math.distancesq(pi, pCand[k]) > pEps2Ref) continue;


                    if (di + 1e-6f < dCand[k])
                    {
                        tCand[k] = ti;
                        pCand[k] = pi;
                        dCand[k] = di;
                    }

                    dup = true;
                    break;
                }

                if (dup) continue;
                tCand[m] = ti;
                pCand[m] = pi;
                dCand[m] = di;
                m++;
            }

            // --- Emit nearest→farthest using partial selection (no full sort) ---
            var want = math.min(count, m);
            for (var outIdx = 0; outIdx < want; outIdx++)
            {
                var best = outIdx;
                for (var j = outIdx + 1; j < m; j++)
                    if (dCand[j] < dCand[best])
                        best = j;

                if (best != outIdx)
                {
                    (tCand[outIdx], tCand[best]) = (tCand[best], tCand[outIdx]);
                    (pCand[outIdx], pCand[best]) = (pCand[best], pCand[outIdx]);
                    (dCand[outIdx], dCand[best]) = (dCand[best], dCand[outIdx]);
                }

                results.Add((pCand[outIdx], tCand[outIdx]));
            }
        }

        static Segment GetNearestPoint<T>(T spline,
            float3 point,
            Segment range,
            out float distance, out float3 nearest, out float time,
            int segments) where T : ISpline
        {
            distance = float.PositiveInfinity;
            nearest = float.PositiveInfinity;
            time = float.PositiveInfinity;
            var segment = new Segment(-1f, 0f);

            var t0 = range.Start;
            var a = spline.EvaluatePosition(t0);


            for (var i = 1; i < segments; i++)
            {
                var t1 = range.Start + range.Length * (i / (segments - 1f));
                var b = spline.EvaluatePosition(t1);
                var p = SplineMath.PointLineNearestPoint(point, a, b, out var lineParam);
                var dsqr = math.distancesq(p, point);

                if (dsqr < distance)
                {
                    segment.Start = t0;
                    segment.Length = t1 - t0;
                    time = segment.Start + segment.Length * lineParam;
                    distance = dsqr;

                    nearest = p;
                }

                t0 = t1;
                a = b;
            }

            distance = math.sqrt(distance);
            return segment;
        }
    }

    struct Segment
    {
        public float Start, Length;

        public Segment(float start, float length)
        {
            Start = start;
            Length = length;
        }
    }
}
