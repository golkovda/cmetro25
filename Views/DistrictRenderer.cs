using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using cmetro25.Models;
using cmetro25.Utils;
using cmetro25.Views; // Für MapCamera

namespace cmetro25.Views
{
    public class DistrictRenderer
    {
        private readonly Texture2D _pixelTexture;
        private readonly SpriteFont _font;
        private readonly Color _borderColor;
        private readonly Color _labelColor;
        private readonly float _minTextScale;
        private readonly float _maxTextScale;
        private readonly float _baseZoom;

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


        // NEU: Separate Methode nur für Polygone (wird von TileManager aufgerufen)
        public void DrawPolygons(SpriteBatch spriteBatch, List<District> districts, MapCamera camera)
        {
            // Annahme: spriteBatch.Begin wurde bereits außerhalb aufgerufen
            foreach (var district in districts)
            {
                // OPTIONAL: Prüfen, ob Distrikt-BoundingBox sichtbar ist (obwohl TileManager schon filtert)
                // if (!camera.BoundingRectangle.Intersects(district.BoundingBox)) continue;

                foreach (var polygon in district.Polygons)
                {
                    DrawPolygonOutline(spriteBatch, polygon, _borderColor, 1f / camera.Zoom); // Dicke an Zoom anpassen
                }
            }
            // Annahme: spriteBatch.End wird außerhalb aufgerufen
        }

        // NEU: Separate Methode nur für Labels (wird von TileManager aufgerufen, NACH Straßen)
        public void DrawLabels(SpriteBatch spriteBatch, List<District> districts, MapCamera camera)
        {
            // Eigener Begin/End für Labels, da sie über anderen Elementen liegen sollen
            // und potenziell andere SpriteSortMode benötigen könnten.
            spriteBatch.Begin(transformMatrix: camera.TransformMatrix, sortMode: SpriteSortMode.Deferred); // Deferred ist oft schneller für Text

            float currentZoom = camera.Zoom; // Zoom einmal abrufen

            foreach (var district in districts)
            {
                // OPTIONAL: Prüfen, ob Distrikt-BoundingBox sichtbar ist
                if (!camera.BoundingRectangle.Intersects(district.BoundingBox)) continue;

                // Text nur zeichnen, wenn er eine minimale Größe erreicht
                float textScale = TextUtils.CalculateTextScale(currentZoom, _baseZoom, _minTextScale, _maxTextScale);
                if (textScale > _minTextScale) // Größer als Minimalskala
                {
                    Vector2 textSize = _font.MeasureString(district.Name);
                    // Zeichne mit Skalierung und zentriertem Ursprung
                    spriteBatch.DrawString(_font, district.Name, district.TextPosition, _labelColor,
                                           0, // Rotation
                                           textSize * 0.5f, // Origin (Mitte des Textes)
                                           textScale, // Skalierung
                                           SpriteEffects.None, 0.1f); // LayerDepth (leicht vor anderen Elementen)
                }
            }
            spriteBatch.End();
        }

        // Umbenannt und angepasst für variable Dicke
        private void DrawPolygonOutline(SpriteBatch spriteBatch, List<Vector2> polygon, Color color, float thickness = 1f)
        {
            if (polygon == null || polygon.Count < 2)
                return;

            // Stelle sicher, dass Dicke nicht negativ oder zu klein wird
            thickness = Math.Max(0.1f, thickness);

            // Kleiner Overlap-Faktor, um Lücken zwischen Segmenten zu vermeiden
            // Dieser Faktor sollte *nicht* mit dem Zoom skalieren, da er Pixel-basiert ist.
            float overlapFactor = 0.02f; // Klein, nur um Lücken zu füllen

            for (var i = 0; i < polygon.Count; i++)
            {
                Vector2 p1 = polygon[i];
                Vector2 p2 = polygon[(i + 1) % polygon.Count]; // Modulo für geschlossene Polygone

                Vector2 direction = p2 - p1;
                float distance = direction.Length();

                // Überspringe Segmente mit Länge 0
                if (distance < 0.01f) continue;

                float angle = (float)System.Math.Atan2(direction.Y, direction.X);

                // Füge Overlap hinzu, um Lücken zu schließen
                Vector2 p2_overlapped = p2 + direction * (overlapFactor / distance); // Skaliere Overlap relativ zur Distanz

                // Berechne die neue Distanz für die Skalierung der Textur
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