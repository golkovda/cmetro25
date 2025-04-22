﻿// ----------  Views/PolylineRenderer.cs  ----------
using System;
using System.Collections.Generic;
using System.Linq;
using cmetro25.Core;
using cmetro25.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace cmetro25.Views
{
    /// <summary>
    ///     Einziger Linien‑Renderer für ALLE Polylines:
    ///     – generic <see cref="PolylineElement"/> (rivers, rails …)
    ///     – <see cref="Road"/> inkl. Glättung & Breiten / Farben.
    /// </summary>
    public sealed class PolylineRenderer
    {
        private readonly Texture2D _px;
        private int _visibleSegments;

        public PolylineRenderer(Texture2D pixel) => _px = pixel;

        public int VisibleSegmentsLastDraw => _visibleSegments;

        /*------------------------------------------------------------*/

        public void Draw(SpriteBatch sb,
                 IEnumerable<PolylineElement> generic,
                 IEnumerable<Road> roads,
                 RectangleF visible,
                 MapCamera cam)
        {
            _visibleSegments = 0;

            if (generic != null)
                foreach (var el in generic.Where(e => e.BoundingBoxes.Any(b => b.Intersects(visible))))
                    DrawGeneric(sb, el, cam.Zoom);

            if (roads != null)
                foreach (var r in roads)
                    DrawRoad(sb, r, cam.Zoom);
        }


        /*----------------  Generic  ----------------*/

        private void DrawGeneric(SpriteBatch sb, PolylineElement el, float zoom)
        {
            if (!GameSettings.PolylineStyle.TryGetValue(el.Kind, out var s))
                s = (1f, Color.White);

            float thick = Math.Clamp(s.width / MathF.Sqrt(Math.Max(0.1f, zoom)),
                                     GameSettings.RoadMinPixelWidth, 6f);

            foreach (var line in el.Lines)
                DrawLine(sb, line, thick, s.color);
        }

        /*----------------  Roads  ----------------*/

        private void DrawRoad(SpriteBatch sb, Road r, float zoom)
        {
            if (!GameSettings.RoadStyle.TryGetValue(r.RoadType ?? "", out var s))
                s = (GameSettings.RoadWidthDefault, GameSettings.RoadColorDefault);

            float thick = Math.Clamp(s.width / MathF.Sqrt(Math.Max(0.1f, zoom)),
                                     GameSettings.RoadMinPixelWidth,
                                     GameSettings.RoadMaxPixelWidth);

            foreach (var l in r.Lines)
            {
                if (GameSettings.UseRoadSmoothing)
                    DrawLine(sb, Spline(l, GameSettings.RoadCurveSegments, zoom),
                             thick, s.color);
                else
                    DrawLine(sb, l, thick, s.color);
            }
        }

        /*----------------  Low‑Level  ----------------*/

        private void DrawLine(SpriteBatch sb, IList<Vector2> pts,
                              float thick, Color col)
        {
            if (pts == null || pts.Count < 2) return;

            const float overlap = GameSettings.RoadDrawPolylineOverlap;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var dir = p2 - p1;
                if (dir.LengthSquared() < 1e-4f) continue;

                float ang  = MathF.Atan2(dir.Y, dir.X);
                float dist = dir.Length();
                var   p2o  = p2 + dir * (overlap / dist);
                float d2   = Vector2.Distance(p1, p2o);

                sb.Draw(_px, p1, null, col, ang, Vector2.Zero,
                        new Vector2(d2, thick),
                        SpriteEffects.None, 0f);

                _visibleSegments++;
            }
        }

        /*----------------  Catmull‑Rom  ----------------*/

        private List<Vector2> Spline(List<Vector2> pts, int seg, float zoom)
        {
            if (pts.Count < 3) return pts;

            var res = new List<Vector2>(pts.Count * seg);
            float zFac = MathF.Sqrt(Math.Max(1f, zoom));
            int   s    = Math.Clamp((int)MathF.Ceiling(seg * zFac), seg, seg * 5);

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = i == 0             ? pts[i]     : pts[i - 1];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = i + 2 < pts.Count ? pts[i + 2] : pts[i + 1];

                for (int j = i == 0 ? 0 : 1; j <= s; j++)
                {
                    float t  = j / (float)s;
                    float t2 = t * t;
                    float t3 = t2 * t;

                    var v = 0.5f * ((2 * p1) +
                                    (-p0 + p2) * t +
                                    (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                                    (-p0 + 3 * p1 - 3 * p2 + p3) * t3);

                    if (res.Count == 0 || Vector2.DistanceSquared(res[^1], v) > 0.01f)
                        res.Add(v);
                }
            }
            return res;
        }
    }
}
