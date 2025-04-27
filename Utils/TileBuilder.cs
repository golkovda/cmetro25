// Utils/TileBuilder.cs

using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using cmetro25.Models;
using cmetro25.Views;
using cmetro25.Core;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace cmetro25.Utils
{
    internal static class TileBuilder
    {
        public static TileBuildResult BuildTile(
            (int zoom, int x, int y) key,
            RectangleF worldRect,
            int tilePx,
            List<WaterBody> water,
            List<District> districts,
            List<PolylineElement> generics,
            List<Road> roads,
            List<PointElement> points,
            float camZoom)
        {
            float PxToWorld(float px) => (px / tilePx) * worldRect.Width;

            var res = new TileBuildResult
            {
                Key = key,
                WorldX = worldRect.X,
                WorldY = worldRect.Y,
                WorldW = worldRect.Width,
                WorldH = worldRect.Height
            };

            if (GameSettings.ShowWaterBodies) 
                GenerateWaterBodies(water, res);

            if (GameSettings.ShowDistricts)
                GenerateDistricts(districts, res);

            if (GameSettings.ShowRails)
                GenerateRails(generics, res);

            GenerateGenerics(generics, res);

            if (GameSettings.ShowRoads)
                GenerateRoads(roads, res);

            if (GameSettings.ShowStations)
                GenerateStations(points, res);

            return res;
            
        }

        private static void GenerateWaterBodies(List<WaterBody> water, TileBuildResult res)
        {
            foreach (var wb in water)
            foreach (var ring in wb.Polygons)
                TessellatePolygon(ring, GameSettings.WaterBodyColor,
                    res.FillVerts, res.FillIndices);
        }

        private static void GenerateDistricts(List<District> districts, TileBuildResult res)
        {
            if (!GameSettings.PolylineStyle.TryGetValue("district", out var style))
                style = (2f, Color.White);

            float halfW = (style.width * 0.5f);

            foreach (var d in districts)
            {
                foreach (var ring in d.Polygons)
                    LineMeshBuilder.AddThickLine(
                        ring, halfW, GameSettings.DistrictBorderColor,
                        res.FillVerts, res.FillIndices);

                // Label weiterhin später per SpriteBatch
            }
        }

        private static void GenerateRails(List<PolylineElement> generics, TileBuildResult res)
        {
            const float dashPx = 4f;                   // visible length of one dash
            float halfWpx = GameSettings.PolylineStyle["rail"].width * 0.5f;
            float halfWworld = halfWpx ;    // constant screen thickness
            float dashWorld = dashPx;    // constant screen length

            foreach (var rail in generics.Where(r => r.Kind == "rail"))
            foreach (var line in rail.Lines)
            {
                // walk along the whole polyline ------------------------------------
                bool dark = true;                       // start with dark dash
                for (int i = 0; i < line.Count - 1; i++)
                {
                    Vector2 a = line[i], b = line[i + 1];
                    float segLen = Vector2.Distance(a, b);
                    if (segLen < 1e-3f) continue;

                    Vector2 dir = Vector2.Normalize(b - a);
                    float done = 0f;

                    // split the segment into dash-sized chunks ---------------------
                    while (done < segLen)
                    {
                        float len = Math.Min(dashWorld, segLen - done);
                        Vector2 p0 = a + dir * done;
                        Vector2 p1 = p0 + dir * len;

                        Color col = dark ? GameSettings.RailDark
                            : GameSettings.RailLight;
                        LineMeshBuilder.AddThickLine(
                            new[] { p0, p1 }, halfWworld, col,
                            res.FillVerts, res.FillIndices);

                        dark = !dark;
                        done += len;
                    }
                }
            }
        }

        private static void GenerateStations(List<PointElement> points, TileBuildResult res)
        {
            foreach (var p in points)
            {
                var col = p.Kind == "station" ? GameSettings.StationColor : Color.White;
                res.Points.Add((p.Position, col, 5f));
            }
        }

        private static void GenerateRoads(List<Road> roads, TileBuildResult res)
        {
            /* ----------  Sortierreihenfolge: unten → oben  ---------- */
            var ordered = roads.OrderBy(rd =>
            {
                string t = rd.RoadType ?? "";
                if (t.EndsWith("_link", StringComparison.OrdinalIgnoreCase))
                    t = t[..^5];                                 // „primary_link“ → „primary“

                return GameSettings.RoadDrawOrder.TryGetValue(t, out var prio)
                    ? prio
                    : GameSettings.DefaultRoadDrawPriority;
            });

            /* ----------  Extrusion & Caps  ---------- */
            foreach (var rd in ordered)
            {
                string keyType = rd.RoadType ?? "";
                if (keyType.EndsWith("_link", StringComparison.OrdinalIgnoreCase))
                    keyType = keyType[..^5];

                if (!GameSettings.RoadStyle.TryGetValue(keyType, out var style))
                    style = (GameSettings.RoadWidthDefault, GameSettings.RoadColorDefault);

                var halfW = GetHalfW(keyType);
                // da Tile-Renderer schon in Screen-Space endet

                for (int s = 0; s < rd.Lines.Count; s++)
                {
                        var seg = rd.Lines[s];
                        bool addEnd = rd.FreeEnd[s];

                        LineMeshBuilder.AddThickLine(seg, halfW, style.color,
                                                     res.FillVerts, res.FillIndices);

                        bool debugmode = true;
                        if (debugmode)
                        {
                            LineMeshBuilder.AddSolidCircle(seg[0], halfW, Color.Cyan,
                                res.FillVerts, res.FillIndices);

                            if (addEnd)
                                LineMeshBuilder.AddSolidCircle(seg[^1], halfW, Color.SandyBrown,
                                    res.FillVerts, res.FillIndices);
                        }
                        else
                        {
                            LineMeshBuilder.AddSolidCircle(seg[0], halfW, style.color,
                                                    res.FillVerts, res.FillIndices);

                        if (addEnd)
                            LineMeshBuilder.AddSolidCircle(seg[^1], halfW, style.color,
                                                    res.FillVerts, res.FillIndices);
                        }
                }
            }
        }

        private static float GetHalfW(string keyType)
        {
            float px = GameSettings.RoadTargetPx.TryGetValue(keyType, out var pVal)
                ? pVal
                : GameSettings.RoadWidthDefault;

            px = Math.Clamp(px,
                GameSettings.RoadGlobalMinPx,
                GameSettings.RoadGlobalMaxPx);

            float halfW = (px * 0.5f);          // Welt-Breite; keine Zoom-Skalierung nötig,
            return halfW;
        }

        private static void GenerateGenerics(List<PolylineElement> generics, TileBuildResult res)
        {
            /* ---- Rivers / Rails ---- */
            foreach (var g in generics.Where(r => r.Kind != "rail"))
            {
                if ((g.Kind == "rail" && !GameSettings.ShowRails) ||
                    (g.Kind == "river" && !GameSettings.ShowRivers))
                    continue;

                if (!GameSettings.PolylineStyle.TryGetValue(g.Kind, out var style))
                    style = (2f, Color.White);                // fallback

                float halfW = (style.width * 0.5f);

                foreach (var seg in g.Lines)
                    LineMeshBuilder.AddThickLine(
                        seg, halfW, style.color,
                        res.FillVerts, res.FillIndices);
            }
        }

        /* ------------ helpers ------------ */
        private static void TessellatePolygon(IList<Vector2> ring, Color col,
                                              List<VertexPositionColor> vOut,
                                              List<int> iOut)
        {
            var tess = new LibTessDotNet.Tess();
            var cont = new LibTessDotNet.ContourVertex[ring.Count];
            for (int i = 0; i < ring.Count; i++)
                cont[i].Position = new LibTessDotNet.Vec3(ring[i].X, ring[i].Y, 0);
            tess.AddContour(cont);
            tess.Tessellate();

            int baseIndex = vOut.Count;
            foreach (var v in tess.Vertices)
                vOut.Add(new VertexPositionColor(
                    new Vector3(v.Position.X, v.Position.Y, 0), col));

            foreach (var idx in tess.Elements)
                iOut.Add(baseIndex + idx);
        }
    }
}
