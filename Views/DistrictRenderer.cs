using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using cmetro25.Core;
using cmetro25.Models;
using cmetro25.Utils;
using cmetro25.Views; // Für MapCamera

namespace cmetro25.Views
{
    /// <summary>
    /// Renderer für die Darstellung von Distrikten auf der Karte.
    /// </summary>
    public class DistrictRenderer
    {
        private readonly Texture2D _pixelTexture;
        private readonly SpriteFont _font;
        private readonly Color _borderColor;
        private readonly Color _labelColor;
        private readonly float _minTextScale;
        private readonly float _maxTextScale;
        private readonly float _baseZoom;

        /// <summary>
        /// Initialisiert eine neue Instanz der <see cref="DistrictRenderer"/> Klasse.
        /// </summary>
        /// <param name="pixelTexture">Die Textur für das Zeichnen von Linien.</param>
        /// <param name="font">Die Schriftart für die Distriktnamen.</param>
        /// <param name="borderColor">Die Farbe der Distriktgrenzen.</param>
        /// <param name="labelColor">Die Farbe der Distriktnamen.</param>
        /// <param name="minTextScale">Die minimale Skalierung der Distriktnamen.</param>
        /// <param name="maxTextScale">Die maximale Skalierung der Distriktnamen.</param>
        /// <param name="baseZoom">Der Basiszoom für die Textskalierung.</param>
        public DistrictRenderer(Texture2D pixelTexture, SpriteFont font, Color borderColor, Color labelColor, float minTextScale, float maxTextScale, float baseZoom)
        {
            _pixelTexture = pixelTexture;
            _font = font;
            _borderColor = borderColor;
            _labelColor = labelColor;
            _minTextScale = minTextScale;
            _maxTextScale = maxTextScale;
            _baseZoom = baseZoom;
        }

        /// <summary>
        /// Zeichnet die Polygone der Distrikte.
        /// </summary>
        /// <param name="spriteBatch">Das SpriteBatch zum Zeichnen.</param>
        /// <param name="districts">Die Liste der zu zeichnenden Distrikte.</param>
        /// <param name="camera">Die aktuelle Kamera.</param>
        public void DrawPolygons(SpriteBatch spriteBatch, List<District> districts, MapCamera camera)
        {
            foreach (var district in districts)
            {
                foreach (var polygon in district.Polygons)
                {
                    DrawPolygonOutline(spriteBatch, polygon, _borderColor, 1f / camera.Zoom); // Dicke an Zoom anpassen
                }
            }
        }

        /// <summary>
        /// Zeichnet die Labels der Distrikte.
        /// </summary>
        /// <param name="spriteBatch">Das SpriteBatch zum Zeichnen.</param>
        /// <param name="districts">Die Liste der zu zeichnenden Distrikte.</param>
        /// <param name="camera">Die aktuelle Kamera.</param>
        public void DrawLabels(SpriteBatch spriteBatch, List<District> districts, MapCamera camera)
        {
            spriteBatch.Begin(transformMatrix: camera.TransformMatrix, sortMode: SpriteSortMode.Deferred); // Deferred ist oft schneller für Text

            float currentZoom = camera.Zoom; // Zoom einmal abrufen

            foreach (var district in districts)
            {
                if (!camera.BoundingRectangle.Intersects(district.BoundingBox)) continue;

                float textScale = TextUtils.CalculateTextScale(currentZoom, _baseZoom, _minTextScale, _maxTextScale);
                if (textScale > _minTextScale) // Größer als Minimalskala
                {
                    Vector2 textSize = _font.MeasureString(district.Name);
                    spriteBatch.DrawString(_font, district.Name, district.TextPosition, _labelColor,
                                           0, // Rotation
                                           textSize * 0.5f, // Origin (Mitte des Textes)
                                           textScale, // Skalierung
                                           SpriteEffects.None, 0.1f); // LayerDepth (leicht vor anderen Elementen)
                }
            }
            spriteBatch.End();
        }

        /// <summary>
        /// Zeichnet die Umrisse eines Polygons.
        /// </summary>
        /// <param name="spriteBatch">Das SpriteBatch zum Zeichnen.</param>
        /// <param name="polygon">Die Liste der Punkte des Polygons.</param>
        /// <param name="color">Die Farbe des Umrisses.</param>
        /// <param name="thickness">Die Dicke des Umrisses.</param>
        private void DrawPolygonOutline(SpriteBatch spriteBatch, List<Vector2> polygon, Color color, float thickness = 1f)
        {
            if (polygon == null || polygon.Count < 2)
                return;

            thickness = Math.Max(0.1f, thickness);

            float overlapFactor = GameSettings.DistrictPolygonOverlapFactor;

            for (var i = 0; i < polygon.Count; i++)
            {
                Vector2 p1 = polygon[i];
                Vector2 p2 = polygon[(i + 1) % polygon.Count]; // Modulo für geschlossene Polygone

                Vector2 direction = p2 - p1;
                float distance = direction.Length();

                if (distance < 0.01f) continue;

                float angle = (float)System.Math.Atan2(direction.Y, direction.X);

                Vector2 p2_overlapped = p2 + direction * (overlapFactor / distance); // Skaliere Overlap relativ zur Distanz

                float drawDistance = Vector2.Distance(p1, p2_overlapped);

                spriteBatch.Draw(_pixelTexture,
                                 p1, // Position
                                 null, // Source Rectangle
                                 color,
                                 angle, // Rotation
                                 Vector2.Zero, // Origin (obere linke Ecke der Textur)
                                 new Vector2(drawDistance, thickness), // Skalierung (Länge, Dicke)
                                 SpriteEffects.None,
                                 0); // Layer Depth
            }
        }
    }
}
