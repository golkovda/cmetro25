// Utils/LineMeshBuilder.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace cmetro25.Utils;

/// <summary>Extrudiert eine Polyline zu einem Dreiecks-Mesh mit Miter-/Bevel-Joins.</summary>
internal static class LineMeshBuilder
{
    public static void AddThickLine(
        IList<Vector2> pts,
        float halfWidth,
        Color col,
        List<VertexPositionColor> vOut,
        List<int> iOut)
    {
        if (pts == null || pts.Count < 2) return;

        // wir brauchen pro Punkt zwei Offsets (links / rechts)
        var left = new Vector2[pts.Count];
        var right = new Vector2[pts.Count];

        // 1) Normalen berechnen (einfacher 2D-Perp)
        Vector2 GetNormal(Vector2 a, Vector2 b)
        {
            var d = b - a;
            d.Normalize();
            return new Vector2(-d.Y, d.X);
        }

        // erste / letzte Normal
        var n0 = GetNormal(pts[0], pts[1]);
        var n1 = GetNormal(pts[^2], pts[^1]);

        left[0] = pts[0] + n0 * halfWidth;
        right[0] = pts[0] - n0 * halfWidth;
        left[^1] = pts[^1] + n1 * halfWidth;
        right[^1] = pts[^1] - n1 * halfWidth;

        // 2) Innere Punkte – Miter ≈ bisector-Verfahren
        for (var i = 1; i < pts.Count - 1; i++)
        {
            var nPrev = GetNormal(pts[i - 1], pts[i]);
            var nNext = GetNormal(pts[i], pts[i + 1]);

            var bisec = Vector2.Normalize(nPrev + nNext);
            var dot = Vector2.Dot(bisec, nPrev);

            // Bei sehr spitzem Winkel → Bevel
            const float dotEpsilon = 0.15f; // ~> 160°  bzw. 20°
            if (MathF.Abs(dot) < dotEpsilon)
            {
                left[i] = pts[i] + nPrev * halfWidth;
                right[i] = pts[i] - nPrev * halfWidth;
            }
            else
            {
                var miterLen = halfWidth / dot;
                left[i] = pts[i] + bisec * miterLen;
                right[i] = pts[i] - bisec * miterLen;
            }
        }

        // 3) Vertices + Indices füllen
        var baseIdx = vOut.Count;
        for (var i = 0; i < pts.Count; i++)
        {
            vOut.Add(new VertexPositionColor(
                new Vector3(left[i], 0), col));
            vOut.Add(new VertexPositionColor(
                new Vector3(right[i], 0), col));
        }

        // Quad-Streifen → 2 Triangles pro Segment
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var i0 = (short)(baseIdx + i * 2);
            var i1 = (short)(i0 + 1);
            var i2 = (short)(i0 + 2);
            var i3 = (short)(i0 + 3);

            iOut.AddRange([
                baseIdx, baseIdx + 2, baseIdx + 1,
                baseIdx + 1, baseIdx + 2, baseIdx + 3
            ]);
        }
    }
}