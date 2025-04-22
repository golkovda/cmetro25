﻿// ----------  Views/PolygonRenderer.cs  ----------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LibTessDotNet;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using cmetro25.Core;
using cmetro25.Models;
using cmetro25.Utils;
using cmetro25.Views;

namespace cmetro25.Views
{
    /// <summary>
    ///     Zeichnet gefüllte oder umrandete Polygone beliebiger Art.
    ///     – Kind "water"  ⇒ Flächenfüllung mit GameSettings.WaterBodyColor
    ///     – Kind "district" ⇒ nur Outline + Label
    ///     Alle anderen Kinds kannst du frei in <see cref="GameSettings.PolygonStyle"/> hinterlegen.
    /// </summary>
    public sealed class PolygonRenderer
    { 
        private readonly GraphicsDevice _gd;
        private readonly Texture2D _px;
        private readonly SpriteFont _font;
        private readonly BasicEffect _fx;

        public PolygonRenderer(GraphicsDevice gd, Texture2D pixel, SpriteFont font)
        {
            _gd  = gd;
            _px  = pixel;
            _font = font;

            _fx = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                TextureEnabled     = false,
                LightingEnabled    = false
            };
        }

        /*--------------------------------------------------------------------
         * Öffentliche API
         *------------------------------------------------------------------*/

        public void DrawWaterBodies(List<WaterBody> wbs, MapCamera cam)
        {
            if (wbs == null || wbs.Count == 0) return;

            SetMatricesForFill(cam);

            foreach (var pass in _fx.CurrentTechnique.Passes)
            {
                pass.Apply();

                foreach (var wb in wbs.Where(w => cam.BoundingRectangle.Intersects(w.BoundingBox)))
                    foreach (var poly in wb.Polygons)
                        DrawFilledPolygon(poly, GameSettings.WaterBodyColor);
            }
        }

        public void DrawDistricts(SpriteBatch sb, List<District> dists,
                                  MapCamera cam, bool drawLabels = true)
        {
            if (dists == null || dists.Count == 0) return;

            var outlineCol = GameSettings.DistrictBorderColor;
            var labelCol = GameSettings.DistrictLabelColor;
            float thick = Math.Max(0.4f, 1f / cam.Zoom);
            float txtScale = TextUtils.CalculateTextScale(cam.Zoom,
                                                          GameSettings.BaseZoomForTextScaling,
                                                          GameSettings.MinTextScale,
                                                          GameSettings.MaxTextScale);

            foreach (var d in dists.Where(d => cam.BoundingRectangle.Intersects(d.BoundingBox)))
            {
                // Outline
                foreach (var ring in d.Polygons)
                    DrawPolyline(sb, ring, thick, outlineCol);

                // Label
                if (drawLabels && txtScale > GameSettings.MinTextScale)
                {
                    var size = _font.MeasureString(d.Name);
                    sb.DrawString(_font, d.Name, d.TextPosition, labelCol,
                                  0f, size * 0.5f, txtScale,
                                  SpriteEffects.None, 0f);
                }
            }
        }


        /*--------------------------------------------------------------------
         * interne Helfer
         *------------------------------------------------------------------*/

        private void SetMatricesForFill(MapCamera cam)
        {
            // SpriteBatch‑Matrix → Screen‑>Clip anfügen
            var worldToScreen = cam.TransformMatrix;
            var screenToClip  = Matrix.CreateOrthographicOffCenter(
                                    0, cam.ViewportWidth,
                                    cam.ViewportHeight, 0,
                                    0f, 1f);

            _fx.World      = Matrix.Identity;
            _fx.View       = Matrix.Identity;
            _fx.Projection = worldToScreen * screenToClip;
        }

        private void DrawFilledPolygon(List<Vector2> ring, Color col)
        {
            if (ring == null || ring.Count < 3) return;

            var tess = new Tess();
            var cont = new ContourVertex[ring.Count];

            for (int i = 0; i < ring.Count; i++)
                cont[i].Position = new Vec3(ring[i].X, ring[i].Y, 0);

            tess.AddContour(cont, ContourOrientation.Clockwise);
            tess.Tessellate();

            if (tess.ElementCount == 0) return;

            var verts = tess.Vertices.Select(v => new VertexPositionColor(
                                new Vector3(v.Position.X, v.Position.Y, 0), col))
                                     .ToArray();
            var idxs  = tess.Elements.Select(i => (short)i).ToArray();

            _gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,
                                          verts, 0, verts.Length,
                                          idxs, 0, idxs.Length / 3);
        }

        private void DrawPolyline(SpriteBatch sb, IList<Vector2> pts,
                                  float thick, Color col)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var dir = p2 - p1;
                if (dir.LengthSquared() < 0.0001f) continue;

                float ang  = MathF.Atan2(dir.Y, dir.X);
                float dist = dir.Length();

                sb.Draw(_px, p1, null, col, ang, Vector2.Zero,
                        new Vector2(dist, thick),
                        SpriteEffects.None, 0f);
            }
        }
    }
}
