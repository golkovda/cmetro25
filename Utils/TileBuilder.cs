// Utils/TileBuilder.cs

using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
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

            /* ---- Wasser → Tesselation ---- */
            if (GameSettings.ShowWaterBodies)
                foreach (var wb in water)
                    foreach (var ring in wb.Polygons)
                        TessellatePolygon(ring, GameSettings.WaterBodyColor,
                                      res.FillVerts, res.FillIndices);

            /* ---- Distrikt-Outlines & Labels ---- */
            if (GameSettings.ShowDistricts)
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

            /* ---- Rails (striped) ---- */
            if (GameSettings.ShowRails)
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

            var endCount = new Dictionary<Point, int>();

            foreach (var rd in roads)
                foreach (var seg in rd.Lines)
                {
                    foreach (var p in new[] { seg[0], seg[^1] })
                    {
                        var k = new Point((int)(p.X * 10), (int)(p.Y * 10)); // 0.1-Quantisierung
                        endCount[k] = endCount.TryGetValue(k, out var c) ? c + 1 : 1;
                    }
                }

            /* ----- Roads ----- */
            if (GameSettings.ShowRoads)
            {
                // ► Reihenfolge nach Priority sortieren
                var ordered = roads.OrderBy(rd =>
                {
                    string t = rd.RoadType ?? "";
                    if (t.EndsWith("_link", StringComparison.OrdinalIgnoreCase))
                        t = t[..^5];                      // link erbt Basis-Typ
                    return GameSettings.RoadDrawOrder.TryGetValue(t, out var p)
                           ? p
                           : GameSettings.DefaultRoadDrawPriority;
                });

                var nodeDict = new Dictionary<Point,(float rad,int prio,Color col)>();

                foreach (var rd in ordered)
                {
                    // TileBuilder.BuildTile – direkt vor dem TryGetValue-Aufruf
                    string keyType = rd.RoadType ?? "";
                    if (keyType.EndsWith("_link", StringComparison.OrdinalIgnoreCase))
                        keyType = keyType[..^5];          // "_link" abschneiden

                    // dann wie bisher:
                    if (!GameSettings.RoadStyle.TryGetValue(keyType, out var s))
                        s = (GameSettings.RoadWidthDefault, GameSettings.RoadColorDefault);

                    float screenPx = GameSettings.RoadTargetPx.TryGetValue(keyType, out var p)
                                     ? p : GameSettings.RoadWidthDefault;

                    /* b) global clamp (Bildschirm-Ebene!) */
                    screenPx = Math.Clamp(screenPx,
                                          GameSettings.RoadGlobalMinPx,
                                          GameSettings.RoadGlobalMaxPx);

                    /* c) in Welt-Einheiten umrechnen:
                           L_world = L_screen / Zoom                         */
                    float halfW = (screenPx * 0.5f) / 1;

                    /* d) Mesh extrudieren */
                    foreach (var seg in rd.Lines)
                    {
                        // … Breite/Farbe wie gehabt …
                        bool CapNeeded(Vector2 pt)
                        {
                            var k = new Point((int)(pt.X * 10), (int)(pt.Y * 10));
                            return endCount[k] == 1;               // nur Einzel­ende bekommt Cap
                        }

                        // Start- und End­punkt einzeln behandeln
                        if (CapNeeded(seg[0]) && CapNeeded(seg[^1]))
                            LineMeshBuilder.AddThickLineWithCaps(seg, halfW, s.color,
                                                                 res.FillVerts, res.FillIndices);
                        else
                        {
                            // zuerst Linie
                            LineMeshBuilder.AddThickLine(seg, halfW, s.color,
                                                         res.FillVerts, res.FillIndices);

                            // dann ggf. nur eine der beiden Kappen
                            if (CapNeeded(seg[0]))
                                LineMeshBuilder.AddRoundCap(seg[0], -Vector2.Normalize(seg[1] - seg[0]),
                                                            halfW, s.color,
                                                            res.FillVerts, res.FillIndices);

                            if (CapNeeded(seg[^1]))
                                LineMeshBuilder.AddRoundCap(seg[^1], Vector2.Normalize(seg[^1] - seg[^2]),
                                                            halfW, s.color,
                                                            res.FillVerts, res.FillIndices);
                        }
                    }

                    // 3) Alle Knoten-Kreise extrudieren (nach Road-Pass!)
                    /*foreach (var kv in nodeDict)
                        LineMeshBuilder.AddSolidCircle(
                            new Vector2(kv.Key.X / 10f, kv.Key.Y / 10f),
                            kv.Value.rad, kv.Value.col,
                            res.FillVerts, res.FillIndices);*/
                }
            }

            /* ---- Points ---- */
            if (GameSettings.ShowStations)
            {
                foreach (var p in points)
                {
                    var col = p.Kind == "station" ? GameSettings.StationColor : Color.White;
                    res.Points.Add((p.Position, col, 5f));
                }
            }

            return res;
            
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
