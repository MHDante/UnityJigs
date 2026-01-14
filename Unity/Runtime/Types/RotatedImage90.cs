using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace UnityJigs.Types
{
    [AddComponentMenu("UI/Rotated Image (90°)", 12)]
    public sealed class RotatedImage90 : Image
    {
        public enum Rotation90
        {
            Deg0 = 0,
            Deg90 = 1,   // clockwise
            Deg180 = 2,
            Deg270 = 3   // clockwise
        }

        [SerializeField]
        private Rotation90 Rotation = Rotation90.Deg0;

        public Rotation90 SpriteRotation
        {
            get => Rotation;
            set
            {
                if (Rotation == value)
                    return;

                Rotation = value;
                SetAllDirty();
            }
        }

        private bool IsOddRotation => Rotation == Rotation90.Deg90 || Rotation == Rotation90.Deg270;

        private Sprite? ActiveSprite => overrideSprite;

        private float MultipliedPixelsPerUnit => pixelsPerUnit * pixelsPerUnitMultiplier;

        public override float preferredWidth
        {
            get
            {
                var s = ActiveSprite;
                if (s == null)
                    return 0f;

                Vector2 sizePx;
                if (type == Type.Sliced || type == Type.Tiled)
                    sizePx = DataUtility.GetMinSize(s);
                else
                    sizePx = s.rect.size;

                if (IsOddRotation)
                    (sizePx.x, sizePx.y) = (sizePx.y, sizePx.x);

                return sizePx.x / pixelsPerUnit;
            }
        }

        public override float preferredHeight
        {
            get
            {
                var s = ActiveSprite;
                if (s == null)
                    return 0f;

                Vector2 sizePx;
                if (type == Type.Sliced || type == Type.Tiled)
                    sizePx = DataUtility.GetMinSize(s);
                else
                    sizePx = s.rect.size;

                if (IsOddRotation)
                    (sizePx.x, sizePx.y) = (sizePx.y, sizePx.x);

                return sizePx.y / pixelsPerUnit;
            }
        }

        public override void SetNativeSize()
        {
            var s = ActiveSprite;
            if (s == null)
                return;

            var w = s.rect.width / pixelsPerUnit;
            var h = s.rect.height / pixelsPerUnit;

            if (IsOddRotation)
                (w, h) = (h, w);

            rectTransform.anchorMax = rectTransform.anchorMin;
            rectTransform.sizeDelta = new Vector2(w, h);
            SetAllDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetAllDirty();
        }
#endif

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            var s = ActiveSprite;
            if (s == null)
            {
                base.OnPopulateMesh(toFill);
                return;
            }

            switch (type)
            {
                case Type.Simple:
                    if (!useSpriteMesh)
                        GenerateSimpleSprite(toFill, preserveAspect, s);
                    else
                        GenerateSpriteMesh(toFill, preserveAspect, s);
                    break;

                case Type.Sliced:
                    GenerateSlicedSprite(toFill, s);
                    break;

                case Type.Tiled:
                    GenerateTiledSprite(toFill, s);
                    break;

                case Type.Filled:
                    GenerateFilledSprite(toFill, preserveAspect, s);
                    break;

                default:
                    base.OnPopulateMesh(toFill);
                    break;
            }
        }

        // -----------------------------
        // Rotation helpers (metadata)
        // -----------------------------

        private Vector2 GetRotatedSpriteSizePx(Sprite s)
        {
            var size = s.rect.size;
            if (IsOddRotation)
                (size.x, size.y) = (size.y, size.x);
            return size;
        }

        private Vector4 GetRotatedEdges(Vector4 v) // v = (L, B, R, T) in pixels
        {
            // CW mapping:
            // Deg90:  L'=B, B'=R, R'=T, T'=L
            // Deg180: L'=R, B'=T, R'=L, T'=B
            // Deg270: L'=T, B'=L, R'=B, T'=R
            return Rotation switch
            {
                Rotation90.Deg0 => v,
                Rotation90.Deg90 => new Vector4(v.y, v.z, v.w, v.x),
                Rotation90.Deg180 => new Vector4(v.z, v.w, v.x, v.y),
                Rotation90.Deg270 => new Vector4(v.w, v.x, v.y, v.z),
                _ => v
            };
        }

        private Vector4 GetRotatedBorderPx(Sprite s) => GetRotatedEdges(s.border);

        private Vector4 GetRotatedPaddingPx(Sprite s) => GetRotatedEdges(DataUtility.GetPadding(s));

        private Vector2 GetRotatedPivotNormalized(Sprite s)
        {
            var sizeOld = s.rect.size;          // (W,H)
            var pivotOld = s.pivot;             // (px,py) from bottom-left

            Vector2 pivotNewPx;
            Vector2 sizeNewPx;

            switch (Rotation)
            {
                default:
                case Rotation90.Deg0:
                    pivotNewPx = pivotOld;
                    sizeNewPx = sizeOld;
                    break;

                case Rotation90.Deg90: // CW
                    // New size: (H,W), pivot': (py, W - px)
                    pivotNewPx = new Vector2(pivotOld.y, sizeOld.x - pivotOld.x);
                    sizeNewPx = new Vector2(sizeOld.y, sizeOld.x);
                    break;

                case Rotation90.Deg180:
                    // New size: (W,H), pivot': (W - px, H - py)
                    pivotNewPx = new Vector2(sizeOld.x - pivotOld.x, sizeOld.y - pivotOld.y);
                    sizeNewPx = sizeOld;
                    break;

                case Rotation90.Deg270: // CW
                    // New size: (H,W), pivot': (H - py, px)
                    pivotNewPx = new Vector2(sizeOld.y - pivotOld.y, pivotOld.x);
                    sizeNewPx = new Vector2(sizeOld.y, sizeOld.x);
                    break;
            }

            if (sizeNewPx.x <= 0f || sizeNewPx.y <= 0f)
                return rectTransform.pivot;

            return new Vector2(pivotNewPx.x / sizeNewPx.x, pivotNewPx.y / sizeNewPx.y);
        }

        // -----------------------------
        // Rotation helpers (UV / points)
        // -----------------------------

        private Vector2 TransformUv(Vector2 uv, Vector4 outerUv)
        {
            var du = outerUv.z - outerUv.x;
            var dv = outerUv.w - outerUv.y;
            if (Mathf.Approximately(du, 0f) || Mathf.Approximately(dv, 0f))
                return uv;

            // Normalize into sprite UV space (can exceed 0..1 for repeat-mode).
            var uPrime = (uv.x - outerUv.x) / du;
            var vPrime = (uv.y - outerUv.y) / dv;

            // We want the *appearance* rotated clockwise by Rotation:
            // so to sample from source we apply the inverse rotation to coords.
            float u, v;
            switch (Rotation)
            {
                default:
                case Rotation90.Deg0:
                    u = uPrime;
                    v = vPrime;
                    break;

                case Rotation90.Deg90: // CW appearance => source = CCW(u',v') = (1-v', u')
                    u = 1f - vPrime;
                    v = uPrime;
                    break;

                case Rotation90.Deg180:
                    u = 1f - uPrime;
                    v = 1f - vPrime;
                    break;

                case Rotation90.Deg270: // CW appearance => source = CW(u',v') = (v', 1-u')
                    u = vPrime;
                    v = 1f - uPrime;
                    break;
            }

            return new Vector2(outerUv.x + u * du, outerUv.y + v * dv);
        }

        private Vector2 RotateSpriteCoordToSource(Vector2 coordRot, Vector2 oldSizePx)
        {
            // coordRot is relative to the rotated sprite rect (origin at (0,0)).
            // Return relative to the original sprite rect (origin at (0,0)).
            switch (Rotation)
            {
                default:
                case Rotation90.Deg0:
                    return coordRot;

                case Rotation90.Deg90: // CW appearance: source = (W - y', x')
                    return new Vector2(oldSizePx.x - coordRot.y, coordRot.x);

                case Rotation90.Deg180:
                    return new Vector2(oldSizePx.x - coordRot.x, oldSizePx.y - coordRot.y);

                case Rotation90.Deg270: // CW appearance: source = (y', H - x')
                    return new Vector2(coordRot.y, oldSizePx.y - coordRot.x);
            }
        }

        // -----------------------------
        // Drawing dimensions (rot-aware)
        // -----------------------------

        private void PreserveSpriteAspectRatio(ref Rect rect, Vector2 spriteSizePx)
        {
            var spriteRatio = spriteSizePx.x / spriteSizePx.y;
            var rectRatio = rect.width / rect.height;

            if (spriteRatio > rectRatio)
            {
                var oldHeight = rect.height;
                rect.height = rect.width * (1.0f / spriteRatio);
                rect.y += (oldHeight - rect.height) * rectTransform.pivot.y;
            }
            else
            {
                var oldWidth = rect.width;
                rect.width = rect.height * spriteRatio;
                rect.x += (oldWidth - rect.width) * rectTransform.pivot.x;
            }
        }

        private Vector4 GetDrawingDimensions(bool shouldPreserveAspect, Sprite s)
        {
            var paddingPx = GetRotatedPaddingPx(s);
            var sizePx = GetRotatedSpriteSizePx(s);

            var r = GetPixelAdjustedRect();

            var spriteW = Mathf.RoundToInt(sizePx.x);
            var spriteH = Mathf.RoundToInt(sizePx.y);

            var v = new Vector4(
                paddingPx.x / spriteW,
                paddingPx.y / spriteH,
                (spriteW - paddingPx.z) / spriteW,
                (spriteH - paddingPx.w) / spriteH);

            if (shouldPreserveAspect && sizePx.sqrMagnitude > 0.0f)
                PreserveSpriteAspectRatio(ref r, sizePx);

            v = new Vector4(
                r.x + r.width * v.x,
                r.y + r.height * v.y,
                r.x + r.width * v.z,
                r.y + r.height * v.w);

            return v;
        }

        // -----------------------------
        // Simple quad
        // -----------------------------

        private void GenerateSimpleSprite(VertexHelper vh, bool preserve, Sprite s)
        {
            var v = GetDrawingDimensions(preserve, s);
            var outer = DataUtility.GetOuterUV(s);

            var color32 = color;
            vh.Clear();

            var uv0 = TransformUv(new Vector2(outer.x, outer.y), outer);
            var uv1 = TransformUv(new Vector2(outer.x, outer.w), outer);
            var uv2 = TransformUv(new Vector2(outer.z, outer.w), outer);
            var uv3 = TransformUv(new Vector2(outer.z, outer.y), outer);

            vh.AddVert(new Vector3(v.x, v.y), color32, uv0);
            vh.AddVert(new Vector3(v.x, v.w), color32, uv1);
            vh.AddVert(new Vector3(v.z, v.w), color32, uv2);
            vh.AddVert(new Vector3(v.z, v.y), color32, uv3);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        // -----------------------------
        // Sprite mesh (tight)
        // -----------------------------

        private void GenerateSpriteMesh(VertexHelper vh, bool preserve, Sprite s)
        {
            var spriteSizePxRot = GetRotatedSpriteSizePx(s);
            if (spriteSizePxRot.sqrMagnitude <= 0.0f)
            {
                GenerateSimpleSprite(vh, preserve, s);
                return;
            }

            var rectPivot = rectTransform.pivot;
            var spritePivotRot = GetRotatedPivotNormalized(s);

            var r = GetPixelAdjustedRect();
            if (preserve)
                PreserveSpriteAspectRatio(ref r, spriteSizePxRot);

            var drawingSize = new Vector2(r.width, r.height);

            var spriteBoundSize = s.bounds.size;
            var spriteBoundSizeRot = spriteBoundSize;
            if (IsOddRotation)
                spriteBoundSizeRot = new Vector3(spriteBoundSize.y, spriteBoundSize.x, spriteBoundSize.z);

            if (Mathf.Approximately(spriteBoundSizeRot.x, 0f) || Mathf.Approximately(spriteBoundSizeRot.y, 0f))
            {
                GenerateSimpleSprite(vh, preserve, s);
                return;
            }

            var drawOffset = (rectPivot - spritePivotRot) * drawingSize;

            var outer = DataUtility.GetOuterUV(s);
            var color32 = color;

            vh.Clear();

            var vertices = s.vertices;
            var uvs = s.uv;

            for (var i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];

                // Rotate the sprite mesh geometry in sprite-local space (origin at pivot).
                var pr = Rotation switch
                {
                    Rotation90.Deg0 => new Vector2(p.x, p.y),
                    Rotation90.Deg90 => new Vector2(p.y, -p.x),
                    Rotation90.Deg180 => new Vector2(-p.x, -p.y),
                    Rotation90.Deg270 => new Vector2(-p.y, p.x),
                    _ => new Vector2(p.x, p.y)
                };

                var x = (pr.x / spriteBoundSizeRot.x) * drawingSize.x - drawOffset.x;
                var y = (pr.y / spriteBoundSizeRot.y) * drawingSize.y - drawOffset.y;

                var uv = TransformUv(new Vector2(uvs[i].x, uvs[i].y), outer);
                vh.AddVert(new Vector3(x, y, 0f), color32, uv);
            }

            var tris = s.triangles;
            for (var i = 0; i < tris.Length; i += 3)
                vh.AddTriangle(tris[i + 0], tris[i + 1], tris[i + 2]);
        }

        // -----------------------------
        // Sliced / Tiled support
        // -----------------------------

        private static readonly Vector2[] VertScratch = new Vector2[4];
        private static readonly Vector2[] UvScratch = new Vector2[4];

        private Vector4 GetAdjustedBorders(Vector4 border, Rect adjustedRect)
        {
            var originalRect = rectTransform.rect;

            for (var axis = 0; axis <= 1; axis++)
            {
                float borderScaleRatio;

                if (!Mathf.Approximately(originalRect.size[axis], 0f))
                {
                    borderScaleRatio = adjustedRect.size[axis] / originalRect.size[axis];
                    border[axis] *= borderScaleRatio;
                    border[axis + 2] *= borderScaleRatio;
                }

                var combinedBorders = border[axis] + border[axis + 2];
                if (adjustedRect.size[axis] < combinedBorders && !Mathf.Approximately(combinedBorders, 0f))
                {
                    borderScaleRatio = adjustedRect.size[axis] / combinedBorders;
                    border[axis] *= borderScaleRatio;
                    border[axis + 2] *= borderScaleRatio;
                }
            }

            return border;
        }

        private void AddQuadRotated(VertexHelper vh, Vector2 posMin, Vector2 posMax, Color32 c, Vector2 uvMin, Vector2 uvMax, Vector4 outer)
        {
            var startIndex = vh.currentVertCount;

            var uv0 = TransformUv(new Vector2(uvMin.x, uvMin.y), outer);
            var uv1 = TransformUv(new Vector2(uvMin.x, uvMax.y), outer);
            var uv2 = TransformUv(new Vector2(uvMax.x, uvMax.y), outer);
            var uv3 = TransformUv(new Vector2(uvMax.x, uvMin.y), outer);

            vh.AddVert(new Vector3(posMin.x, posMin.y, 0f), c, uv0);
            vh.AddVert(new Vector3(posMin.x, posMax.y, 0f), c, uv1);
            vh.AddVert(new Vector3(posMax.x, posMax.y, 0f), c, uv2);
            vh.AddVert(new Vector3(posMax.x, posMin.y, 0f), c, uv3);

            vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }

        private void GenerateSlicedSprite(VertexHelper toFill, Sprite s)
        {
            if (!hasBorder)
            {
                GenerateSimpleSprite(toFill, false, s);
                return;
            }

            var outer = DataUtility.GetOuterUV(s);
            var inner = DataUtility.GetInnerUV(s);

            var paddingPx = GetRotatedPaddingPx(s);
            var borderPx = GetRotatedBorderPx(s);

            var rect = GetPixelAdjustedRect();

            var adjustedBorders = GetAdjustedBorders(borderPx / MultipliedPixelsPerUnit, rect);
            var padding = paddingPx / MultipliedPixelsPerUnit;

            VertScratch[0] = new Vector2(padding.x, padding.y);
            VertScratch[3] = new Vector2(rect.width - padding.z, rect.height - padding.w);

            VertScratch[1].x = adjustedBorders.x;
            VertScratch[1].y = adjustedBorders.y;

            VertScratch[2].x = rect.width - adjustedBorders.z;
            VertScratch[2].y = rect.height - adjustedBorders.w;

            for (var i = 0; i < 4; i++)
            {
                VertScratch[i].x += rect.x;
                VertScratch[i].y += rect.y;
            }

            UvScratch[0] = new Vector2(outer.x, outer.y);
            UvScratch[1] = new Vector2(inner.x, inner.y);
            UvScratch[2] = new Vector2(inner.z, inner.w);
            UvScratch[3] = new Vector2(outer.z, outer.w);

            toFill.Clear();

            for (var x = 0; x < 3; x++)
            {
                var x2 = x + 1;

                for (var y = 0; y < 3; y++)
                {
                    if (!fillCenter && x == 1 && y == 1)
                        continue;

                    var y2 = y + 1;

                    if (VertScratch[x2].x - VertScratch[x].x <= 0f)
                        continue;
                    if (VertScratch[y2].y - VertScratch[y].y <= 0f)
                        continue;

                    AddQuadRotated(
                        toFill,
                        new Vector2(VertScratch[x].x, VertScratch[y].y),
                        new Vector2(VertScratch[x2].x, VertScratch[y2].y),
                        color,
                        new Vector2(UvScratch[x].x, UvScratch[y].y),
                        new Vector2(UvScratch[x2].x, UvScratch[y2].y),
                        outer);
                }
            }
        }

        private void GenerateTiledSprite(VertexHelper toFill, Sprite s)
        {
            var outer = DataUtility.GetOuterUV(s);
            var inner = DataUtility.GetInnerUV(s);

            var spriteSizePxRot = GetRotatedSpriteSizePx(s);
            var borderPxRot = GetRotatedBorderPx(s);

            var rect = GetPixelAdjustedRect();

            var tileWidth = (spriteSizePxRot.x - borderPxRot.x - borderPxRot.z) / MultipliedPixelsPerUnit;
            var tileHeight = (spriteSizePxRot.y - borderPxRot.y - borderPxRot.w) / MultipliedPixelsPerUnit;

            var border = GetAdjustedBorders(borderPxRot / MultipliedPixelsPerUnit, rect);

            var uvMin = new Vector2(inner.x, inner.y);
            var uvMax = new Vector2(inner.z, inner.w);

            var xMin = border.x;
            var xMax = rect.width - border.z;
            var yMin = border.y;
            var yMax = rect.height - border.w;

            toFill.Clear();

            // if either width is zero we cant tile so just assume it was the full width.
            if (tileWidth <= 0f)
                tileWidth = xMax - xMin;
            if (tileHeight <= 0f)
                tileHeight = yMax - yMin;

            var clipped = uvMax;

            if (hasBorder || s.packed || (s.texture != null && s.texture.wrapMode != TextureWrapMode.Repeat))
            {
                long nTilesW;
                long nTilesH;

                if (fillCenter)
                {
                    nTilesW = (long)Math.Ceiling((xMax - xMin) / tileWidth);
                    nTilesH = (long)Math.Ceiling((yMax - yMin) / tileHeight);

                    double nVertices;
                    if (hasBorder)
                        nVertices = (nTilesW + 2.0) * (nTilesH + 2.0) * 4.0;
                    else
                        nVertices = nTilesW * nTilesH * 4.0;

                    if (nVertices > 65000.0)
                    {
                        Debug.LogError($"Too many sprite tiles on Image \"{name}\". The tile size will be increased. To remove the limit on the number of tiles, set the Wrap mode to Repeat in the Image Import Settings", this);

                        const double maxTiles = 65000.0 / 4.0;
                        double imageRatio;
                        if (hasBorder)
                            imageRatio = (nTilesW + 2.0) / (nTilesH + 2.0);
                        else
                            imageRatio = (double)nTilesW / nTilesH;

                        var targetTilesW = Math.Sqrt(maxTiles / imageRatio);
                        var targetTilesH = targetTilesW * imageRatio;

                        if (hasBorder)
                        {
                            targetTilesW -= 2;
                            targetTilesH -= 2;
                        }

                        nTilesW = (long)Math.Floor(targetTilesW);
                        nTilesH = (long)Math.Floor(targetTilesH);

                        if (nTilesW > 0)
                            tileWidth = (xMax - xMin) / nTilesW;
                        if (nTilesH > 0)
                            tileHeight = (yMax - yMin) / nTilesH;
                    }
                }
                else
                {
                    if (hasBorder)
                    {
                        nTilesW = (long)Math.Ceiling((xMax - xMin) / tileWidth);
                        nTilesH = (long)Math.Ceiling((yMax - yMin) / tileHeight);

                        var nVertices = (nTilesH + nTilesW + 2.0) * 2.0 * 4.0;
                        if (nVertices > 65000.0)
                        {
                            Debug.LogError($"Too many sprite tiles on Image \"{name}\". The tile size will be increased. To remove the limit on the number of tiles, set the Wrap mode to Repeat in the Image Import Settings", this);

                            const double maxTiles = 65000.0 / 4.0;
                            var imageRatio = (double)nTilesW / nTilesH;

                            var targetTilesW = (maxTiles - 4) / (2 * (1.0 + imageRatio));
                            var targetTilesH = targetTilesW * imageRatio;

                            nTilesW = (long)Math.Floor(targetTilesW);
                            nTilesH = (long)Math.Floor(targetTilesH);

                            if (nTilesW > 0)
                                tileWidth = (xMax - xMin) / nTilesW;
                            if (nTilesH > 0)
                                tileHeight = (yMax - yMin) / nTilesH;
                        }
                    }
                    else
                    {
                        nTilesH = 0;
                        nTilesW = 0;
                    }
                }

                if (fillCenter)
                {
                    for (long j = 0; j < nTilesH; j++)
                    {
                        var y1 = yMin + j * tileHeight;
                        var y2 = yMin + (j + 1) * tileHeight;

                        if (y2 > yMax)
                        {
                            clipped.y = uvMin.y + (uvMax.y - uvMin.y) * (yMax - y1) / (y2 - y1);
                            y2 = yMax;
                        }

                        clipped.x = uvMax.x;

                        for (long i = 0; i < nTilesW; i++)
                        {
                            var x1 = xMin + i * tileWidth;
                            var x2 = xMin + (i + 1) * tileWidth;

                            if (x2 > xMax)
                            {
                                clipped.x = uvMin.x + (uvMax.x - uvMin.x) * (xMax - x1) / (x2 - x1);
                                x2 = xMax;
                            }

                            AddQuadRotated(toFill,
                                new Vector2(x1, y1) + rect.position,
                                new Vector2(x2, y2) + rect.position,
                                color,
                                uvMin,
                                clipped,
                                outer);
                        }
                    }
                }

                if (hasBorder)
                {
                    // Left and right tiled border
                    clipped = uvMax;

                    for (long j = 0; j < nTilesH; j++)
                    {
                        var y1 = yMin + j * tileHeight;
                        var y2 = yMin + (j + 1) * tileHeight;

                        if (y2 > yMax)
                        {
                            clipped.y = uvMin.y + (uvMax.y - uvMin.y) * (yMax - y1) / (y2 - y1);
                            y2 = yMax;
                        }

                        AddQuadRotated(toFill,
                            new Vector2(0, y1) + rect.position,
                            new Vector2(xMin, y2) + rect.position,
                            color,
                            new Vector2(outer.x, uvMin.y),
                            new Vector2(uvMin.x, clipped.y),
                            outer);

                        AddQuadRotated(toFill,
                            new Vector2(xMax, y1) + rect.position,
                            new Vector2(rect.width, y2) + rect.position,
                            color,
                            new Vector2(uvMax.x, uvMin.y),
                            new Vector2(outer.z, clipped.y),
                            outer);
                    }

                    // Bottom and top tiled border
                    clipped = uvMax;

                    for (long i = 0; i < nTilesW; i++)
                    {
                        var x1 = xMin + i * tileWidth;
                        var x2 = xMin + (i + 1) * tileWidth;

                        if (x2 > xMax)
                        {
                            clipped.x = uvMin.x + (uvMax.x - uvMin.x) * (xMax - x1) / (x2 - x1);
                            x2 = xMax;
                        }

                        AddQuadRotated(toFill,
                            new Vector2(x1, 0) + rect.position,
                            new Vector2(x2, yMin) + rect.position,
                            color,
                            new Vector2(uvMin.x, outer.y),
                            new Vector2(clipped.x, uvMin.y),
                            outer);

                        AddQuadRotated(toFill,
                            new Vector2(x1, yMax) + rect.position,
                            new Vector2(x2, rect.height) + rect.position,
                            color,
                            new Vector2(uvMin.x, uvMax.y),
                            new Vector2(clipped.x, outer.w),
                            outer);
                    }

                    // Corners
                    AddQuadRotated(toFill,
                        new Vector2(0, 0) + rect.position,
                        new Vector2(xMin, yMin) + rect.position,
                        color,
                        new Vector2(outer.x, outer.y),
                        new Vector2(uvMin.x, uvMin.y),
                        outer);

                    AddQuadRotated(toFill,
                        new Vector2(xMax, 0) + rect.position,
                        new Vector2(rect.width, yMin) + rect.position,
                        color,
                        new Vector2(uvMax.x, outer.y),
                        new Vector2(outer.z, uvMin.y),
                        outer);

                    AddQuadRotated(toFill,
                        new Vector2(0, yMax) + rect.position,
                        new Vector2(xMin, rect.height) + rect.position,
                        color,
                        new Vector2(outer.x, uvMax.y),
                        new Vector2(uvMin.x, outer.w),
                        outer);

                    AddQuadRotated(toFill,
                        new Vector2(xMax, yMax) + rect.position,
                        new Vector2(rect.width, rect.height) + rect.position,
                        color,
                        new Vector2(uvMax.x, uvMax.y),
                        new Vector2(outer.z, outer.w),
                        outer);
                }
            }
            else
            {
                // Texture has no border, is in repeat mode and not packed. Use texture tiling.
                var uvScale = new Vector2((xMax - xMin) / tileWidth, (yMax - yMin) / tileHeight);

                if (fillCenter)
                {
                    var scaledMin = Vector2.Scale(uvMin, uvScale);
                    var scaledMax = Vector2.Scale(uvMax, uvScale);

                    AddQuadRotated(toFill,
                        new Vector2(xMin, yMin) + rect.position,
                        new Vector2(xMax, yMax) + rect.position,
                        color,
                        scaledMin,
                        scaledMax,
                        outer);
                }
            }
        }

        // -----------------------------
        // Filled support
        // -----------------------------

        private static readonly Vector3[] Xy = new Vector3[4];
        private static readonly Vector3[] Uv = new Vector3[4];

        private void AddQuadRotated(VertexHelper vh, Vector3[] quadPositions, Color32 c, Vector3[] quadUvs, Vector4 outer)
        {
            var startIndex = vh.currentVertCount;

            for (var i = 0; i < 4; i++)
            {
                var uv = TransformUv(new Vector2(quadUvs[i].x, quadUvs[i].y), outer);
                vh.AddVert(quadPositions[i], c, uv);
            }

            vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }

        private void GenerateFilledSprite(VertexHelper toFill, bool preserve, Sprite s)
        {
            toFill.Clear();

            if (fillAmount < 0.001f)
                return;

            var v = GetDrawingDimensions(preserve, s);
            var outer = DataUtility.GetOuterUV(s);

            var color32 = color;

            var tx0 = outer.x;
            var ty0 = outer.y;
            var tx1 = outer.z;
            var ty1 = outer.w;

            if (fillMethod == FillMethod.Horizontal)
            {
                var fill = (tx1 - tx0) * fillAmount;
                if (fillOrigin == 1)
                {
                    v.x = v.z - (v.z - v.x) * fillAmount;
                    tx0 = tx1 - fill;
                }
                else
                {
                    v.z = v.x + (v.z - v.x) * fillAmount;
                    tx1 = tx0 + fill;
                }
            }
            else if (fillMethod == FillMethod.Vertical)
            {
                var fill = (ty1 - ty0) * fillAmount;
                if (fillOrigin == 1)
                {
                    v.y = v.w - (v.w - v.y) * fillAmount;
                    ty0 = ty1 - fill;
                }
                else
                {
                    v.w = v.y + (v.w - v.y) * fillAmount;
                    ty1 = ty0 + fill;
                }
            }

            Xy[0] = new Vector2(v.x, v.y);
            Xy[1] = new Vector2(v.x, v.w);
            Xy[2] = new Vector2(v.z, v.w);
            Xy[3] = new Vector2(v.z, v.y);

            Uv[0] = new Vector2(tx0, ty0);
            Uv[1] = new Vector2(tx0, ty1);
            Uv[2] = new Vector2(tx1, ty1);
            Uv[3] = new Vector2(tx1, ty0);

            if (fillAmount < 1f && fillMethod != FillMethod.Horizontal && fillMethod != FillMethod.Vertical)
            {
                if (fillMethod == FillMethod.Radial90)
                {
                    if (RadialCut(Xy, Uv, fillAmount, fillClockwise, fillOrigin))
                        AddQuadRotated(toFill, Xy, color32, Uv, outer);
                }
                else if (fillMethod == FillMethod.Radial180)
                {
                    for (var side = 0; side < 2; side++)
                    {
                        float fx0, fx1, fy0, fy1;
                        var even = fillOrigin > 1 ? 1 : 0;

                        if (fillOrigin == 0 || fillOrigin == 2)
                        {
                            fy0 = 0f;
                            fy1 = 1f;
                            if (side == even)
                            {
                                fx0 = 0f;
                                fx1 = 0.5f;
                            }
                            else
                            {
                                fx0 = 0.5f;
                                fx1 = 1f;
                            }
                        }
                        else
                        {
                            fx0 = 0f;
                            fx1 = 1f;
                            if (side == even)
                            {
                                fy0 = 0.5f;
                                fy1 = 1f;
                            }
                            else
                            {
                                fy0 = 0f;
                                fy1 = 0.5f;
                            }
                        }

                        Xy[0].x = Mathf.Lerp(v.x, v.z, fx0);
                        Xy[1].x = Xy[0].x;
                        Xy[2].x = Mathf.Lerp(v.x, v.z, fx1);
                        Xy[3].x = Xy[2].x;

                        Xy[0].y = Mathf.Lerp(v.y, v.w, fy0);
                        Xy[1].y = Mathf.Lerp(v.y, v.w, fy1);
                        Xy[2].y = Xy[1].y;
                        Xy[3].y = Xy[0].y;

                        Uv[0].x = Mathf.Lerp(tx0, tx1, fx0);
                        Uv[1].x = Uv[0].x;
                        Uv[2].x = Mathf.Lerp(tx0, tx1, fx1);
                        Uv[3].x = Uv[2].x;

                        Uv[0].y = Mathf.Lerp(ty0, ty1, fy0);
                        Uv[1].y = Mathf.Lerp(ty0, ty1, fy1);
                        Uv[2].y = Uv[1].y;
                        Uv[3].y = Uv[0].y;

                        var val = fillClockwise ? fillAmount * 2f - side : fillAmount * 2f - (1 - side);

                        if (RadialCut(Xy, Uv, Mathf.Clamp01(val), fillClockwise, ((side + fillOrigin + 3) % 4)))
                            AddQuadRotated(toFill, Xy, color32, Uv, outer);
                    }
                }
                else if (fillMethod == FillMethod.Radial360)
                {
                    for (var corner = 0; corner < 4; corner++)
                    {
                        float fx0, fx1, fy0, fy1;

                        if (corner < 2)
                        {
                            fx0 = 0f;
                            fx1 = 0.5f;
                        }
                        else
                        {
                            fx0 = 0.5f;
                            fx1 = 1f;
                        }

                        if (corner == 0 || corner == 3)
                        {
                            fy0 = 0f;
                            fy1 = 0.5f;
                        }
                        else
                        {
                            fy0 = 0.5f;
                            fy1 = 1f;
                        }

                        Xy[0].x = Mathf.Lerp(v.x, v.z, fx0);
                        Xy[1].x = Xy[0].x;
                        Xy[2].x = Mathf.Lerp(v.x, v.z, fx1);
                        Xy[3].x = Xy[2].x;

                        Xy[0].y = Mathf.Lerp(v.y, v.w, fy0);
                        Xy[1].y = Mathf.Lerp(v.y, v.w, fy1);
                        Xy[2].y = Xy[1].y;
                        Xy[3].y = Xy[0].y;

                        Uv[0].x = Mathf.Lerp(tx0, tx1, fx0);
                        Uv[1].x = Uv[0].x;
                        Uv[2].x = Mathf.Lerp(tx0, tx1, fx1);
                        Uv[3].x = Uv[2].x;

                        Uv[0].y = Mathf.Lerp(ty0, ty1, fy0);
                        Uv[1].y = Mathf.Lerp(ty0, ty1, fy1);
                        Uv[2].y = Uv[1].y;
                        Uv[3].y = Uv[0].y;

                        var val = fillClockwise
                            ? fillAmount * 4f - ((corner + fillOrigin) % 4)
                            : fillAmount * 4f - (3 - ((corner + fillOrigin) % 4));

                        if (RadialCut(Xy, Uv, Mathf.Clamp01(val), fillClockwise, ((corner + 2) % 4)))
                            AddQuadRotated(toFill, Xy, color32, Uv, outer);
                    }
                }
            }
            else
            {
                AddQuadRotated(toFill, Xy, color32, Uv, outer);
            }
        }

        private static bool RadialCut(Vector3[] xy, Vector3[] uv, float fill, bool invert, int corner)
        {
            if (fill < 0.001f)
                return false;

            if ((corner & 1) == 1)
                invert = !invert;

            if (!invert && fill > 0.999f)
                return true;

            var angle = Mathf.Clamp01(fill);
            if (invert)
                angle = 1f - angle;

            angle *= 90f * Mathf.Deg2Rad;

            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);

            RadialCut(xy, cos, sin, invert, corner);
            RadialCut(uv, cos, sin, invert, corner);
            return true;
        }

        private static void RadialCut(Vector3[] xy, float cos, float sin, bool invert, int corner)
        {
            var i0 = corner;
            var i1 = (corner + 1) % 4;
            var i2 = (corner + 2) % 4;
            var i3 = (corner + 3) % 4;

            if ((corner & 1) == 1)
            {
                if (sin > cos)
                {
                    cos /= sin;
                    sin = 1f;

                    if (invert)
                    {
                        xy[i1].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
                        xy[i2].x = xy[i1].x;
                    }
                }
                else if (cos > sin)
                {
                    sin /= cos;
                    cos = 1f;

                    if (!invert)
                    {
                        xy[i2].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
                        xy[i3].y = xy[i2].y;
                    }
                }
                else
                {
                    cos = 1f;
                    sin = 1f;
                }

                if (!invert)
                    xy[i3].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
                else
                    xy[i1].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
            }
            else
            {
                if (cos > sin)
                {
                    sin /= cos;
                    cos = 1f;

                    if (!invert)
                    {
                        xy[i1].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
                        xy[i2].y = xy[i1].y;
                    }
                }
                else if (sin > cos)
                {
                    cos /= sin;
                    sin = 1f;

                    if (invert)
                    {
                        xy[i2].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
                        xy[i3].x = xy[i2].x;
                    }
                }
                else
                {
                    cos = 1f;
                    sin = 1f;
                }

                if (invert)
                    xy[i3].y = Mathf.Lerp(xy[i0].y, xy[i2].y, sin);
                else
                    xy[i1].x = Mathf.Lerp(xy[i0].x, xy[i2].x, cos);
            }
        }

        // -----------------------------
        // Alpha hit test parity
        // -----------------------------

        public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (alphaHitTestMinimumThreshold <= 0f)
                return true;

            if (alphaHitTestMinimumThreshold > 1f)
                return false;

            var s = ActiveSprite;
            if (s == null)
                return true;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local))
                return false;

            var rect = GetPixelAdjustedRect();

            // Match rendering: preserve aspect using the rotated sprite rect size.
            if (preserveAspect)
            {
                var spriteSizePxRot = GetRotatedSpriteSizePx(s);
                PreserveSpriteAspectRatio(ref rect, spriteSizePxRot);
            }

            // Convert to lower-left reference.
            local.x += rectTransform.pivot.x * rect.width;
            local.y += rectTransform.pivot.y * rect.height;

            // Map into rotated sprite-rect pixel coordinates (relative to spriteRect origin).
            var coordRot = MapCoordinateRotated(local, rect, s);

            // Convert rotated sprite coords back to source sprite coords, then into texture space.
            var spriteRect = s.rect;
            var coordSrc = RotateSpriteCoordToSource(coordRot, spriteRect.size) + spriteRect.position;

            var tex = s.texture;
            if (tex == null)
                return true;

            var x = coordSrc.x / tex.width;
            var y = coordSrc.y / tex.height;

            if (x < 0f || x > 1f || y < 0f || y > 1f)
                return true;

            try
            {
                return tex.GetPixelBilinear(x, y).a >= alphaHitTestMinimumThreshold;
            }
            catch (UnityException e)
            {
                Debug.LogError("alphaHitTestMinimumThreshold > 0 on Image whose sprite texture cannot be read. " + e.Message, this);
                return true;
            }
        }

        private Vector2 MapCoordinateRotated(Vector2 local, Rect rect, Sprite s)
        {
            // This is Image.MapCoordinate, but treating the sprite rect size + borders as rotated,
            // and returning coordinates relative to the rotated sprite rect origin (0..rotW, 0..rotH).
            var rotSize = GetRotatedSpriteSizePx(s);

            if (type == Type.Simple || type == Type.Filled)
                return new Vector2(local.x * rotSize.x / rect.width, local.y * rotSize.y / rect.height);

            var borderPx = GetRotatedBorderPx(s);
            var adjustedBorder = GetAdjustedBorders(borderPx / pixelsPerUnit, rect);

            for (var i = 0; i < 2; i++)
            {
                if (local[i] <= adjustedBorder[i])
                    continue;

                if (rect.size[i] - local[i] <= adjustedBorder[i + 2])
                {
                    local[i] -= (rect.size[i] - rotSize[i]);
                    continue;
                }

                if (type == Type.Sliced)
                {
                    var lerp = Mathf.InverseLerp(adjustedBorder[i], rect.size[i] - adjustedBorder[i + 2], local[i]);
                    local[i] = Mathf.Lerp(borderPx[i], rotSize[i] - borderPx[i + 2], lerp);
                }
                else
                {
                    local[i] -= adjustedBorder[i];
                    local[i] = Mathf.Repeat(local[i], rotSize[i] - borderPx[i] - borderPx[i + 2]);
                    local[i] += borderPx[i];
                }
            }

            // Return relative to spriteRect origin (not including spriteRect.position).
            return local;
        }
    }
}
