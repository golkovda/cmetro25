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

            /* ---- Rivers / Rails ---- */
            foreach (var g in generics)
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

            /* ----- Roads ----- */
            if (GameSettings.ShowRoads)
            {
                foreach (var rd in roads)
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
                    float halfWidthWorld = (screenPx * 0.5f) / 1;

                    /* d) Mesh extrudieren */
                    foreach (var seg in rd.Lines)
                        LineMeshBuilder.AddThickLine(seg, halfWidthWorld, s.color,
                                                     res.FillVerts, res.FillIndices);
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
