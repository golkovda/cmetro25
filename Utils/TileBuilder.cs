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
            var res = new TileBuildResult
            {
                Key = key,
                WorldX = worldRect.X,
                WorldY = worldRect.Y,
                WorldW = worldRect.Width,
                WorldH = worldRect.Height
            };

            /* ---- Wasser → Tesselation ---- */
            foreach (var wb in water)
                foreach (var ring in wb.Polygons)
                    TessellatePolygon(ring, GameSettings.WaterBodyColor,
                                      res.FillVerts, res.FillIndices);

            /* ---- Distrikt‑Outlines + Labels ---- */
            foreach (var d in districts)
            {
                foreach (var ring in d.Polygons)
                    for (int i = 0; i < ring.Count - 1; i++)
                        res.Lines.Add((ring[i], ring[i + 1],
                                       GameSettings.DistrictBorderColor,
                                       Math.Max(0.4f, 1f)));

                // Label später im Draw‑Call (Text geht nicht prebuild)
            }

            /* ---- Generische Linien (Rivers/Rails) ---- */
            foreach (var g in generics)
            {
                if (!GameSettings.PolylineStyle.TryGetValue(g.Kind, out var s))
                    s = (1f, Color.White);

                foreach (var ln in g.Lines)
                    for (int i = 0; i < ln.Count - 1; i++)
                        res.Lines.Add((ln[i], ln[i + 1], s.color, s.width));
            }

            /* ----- Roads ----- */
            foreach (var rd in roads)
            {
                // Style
                if (!GameSettings.RoadStyle.TryGetValue(rd.RoadType ?? "", out var s))
                    s = (GameSettings.RoadWidthDefault, GameSettings.RoadColorDefault);

                /* 1) Wunsch-Pixelbreite für diesen Zoom
                 *    – exakt derselbe Ausdruck wie früher im SpriteBatch-Renderer */
                float pxWidth = Math.Clamp(
                    s.width / MathF.Sqrt(MathF.Max(0.1f, camZoom)),
                    GameSettings.RoadMinPixelWidth,
                    GameSettings.RoadMaxPixelWidth);

                /* 2) Pixel  →  Welt  (für diese Kachel)
                 *    pxPerWorld =   tile-Pixel / tile-Weltbreite                */
                float pxPerWorld = tilePx / worldRect.Width;
                float halfWidthW = (pxWidth * 0.5f) / pxPerWorld;

                /* 3) extrudieren – Segment für Segment */
                foreach (var seg in rd.Lines)
                    LineMeshBuilder.AddThickLine(seg, halfWidthW, s.color,
                                                 res.FillVerts, res.FillIndices);
            }

            /* ---- Points ---- */
            foreach (var p in points)
            {
                var col = p.Kind == "station" ? GameSettings.StationColor : Color.White;
                res.Points.Add((p.Position, col, 5f));
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
