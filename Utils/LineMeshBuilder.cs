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
                const float MITER_LIMIT = 4f;          // max = 4 × Linien­breite
                if (miterLen > halfWidth * MITER_LIMIT)
                {
                    // zu lang → auf Bevel umschalten
                    left[i] = pts[i] + nPrev * halfWidth;
                    right[i] = pts[i] - nPrev * halfWidth;
                    continue;
                }
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
        for (int i = 0; i < pts.Count - 1; i++)
        {
            int i0 = baseIdx + i * 2;   // links  Punkt i
            int i1 = i0 + 1;            // rechts Punkt i
            int i2 = i0 + 2;            // links  Punkt i+1
            int i3 = i0 + 3;            // rechts Punkt i+1

            // Dreieck 1
            iOut.Add(i0);
            iOut.Add(i2);
            iOut.Add(i1);

            // Dreieck 2
            iOut.Add(i1);
            iOut.Add(i2);
            iOut.Add(i3);
        }

    }

    public static void AddThickLineWithCaps(
     IList<Vector2> pts, float r, Color col,
     bool startCap, bool endCap,
     List<VertexPositionColor> v, List<int> idx)
    {
        if (pts.Count < 2) return;

        var d0 = Vector2.Normalize(pts[1] - pts[0]);
        var d1 = Vector2.Normalize(pts[^1] - pts[^2]);

        // ► Kopie evtl. einkürzen
        var tmp = new Vector2[pts.Count];
        pts.CopyTo(tmp, 0);
        if (startCap) tmp[0] += d0 * r;
        if (endCap) tmp[^1] -= d1 * r;

        AddThickLine(tmp, r, col, v, idx);

        if (startCap) AddRoundCap(tmp[0], -d0, r, col, v, idx);
        if (endCap) AddRoundCap(tmp[^1], d1, r, col, v, idx);
    }

    public static void AddSolidCircle(
    Vector2 center, float radius, Color col,
    List<VertexPositionColor> vOut, List<int> iOut,
    int seg = 12)
    {
        int baseIdx = vOut.Count;
        vOut.Add(new VertexPositionColor(new Vector3(center, 0), col));   // center

        for (int i = 0; i <= seg; i++)
        {
            float a = MathF.Tau * i / seg;
            var p = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            vOut.Add(new VertexPositionColor(new Vector3(p, 0), col));

            if (i > 0)
            {
                iOut.Add(baseIdx);          // Fan-Dreieck
                iOut.Add(baseIdx + i);
                iOut.Add(baseIdx + i + 1);
            }
        }
    }

    public static void AddRoundCap(
    Vector2 center, Vector2 dir, float r, Color col,
    List<VertexPositionColor> v, List<int> idx,
    int seg = 12)          // 12 ≈ 15°-Schritte ⇒ glatte Halb­kappe
    {
        dir = Vector2.Normalize(dir);
        var n = new Vector2(-dir.Y, dir.X);   // linke Normalen­richtung

        int baseIdx = v.Count;
        v.Add(new VertexPositionColor(new Vector3(center, 0), col));    // Mittelpunkt

        for (int i = 0; i <= seg; i++)
        {
            float a = (-MathF.PI * 0.5f) + i * (MathF.PI / seg);        // −π/2 … +π/2
            var off = dir * MathF.Cos(a) + n * MathF.Sin(a);            // **TAUSCH**
            v.Add(new VertexPositionColor(new Vector3(center + off * r, 0), col));

            if (i > 0)
            {
                idx.Add(baseIdx);          // Fan-Dreieck
                idx.Add(baseIdx + i);
                idx.Add(baseIdx + i + 1);
            }
        }
    }

}