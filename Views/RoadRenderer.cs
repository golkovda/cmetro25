using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq; // Für Any()
using cmetro25.Models;
using MonoGame.Extended;
using cmetro25.Views; // Für MapCamera

namespace cmetro25.Views
{
    public class RoadRenderer
    {
        private readonly Texture2D _pixelTexture;
        private readonly float _baseOverlapFactor;
        private readonly bool _useSmoothing;
        private readonly int _curveSegments;
        private int _visibleLineSegmentsDrawn = 0; // Zählt gezeichnete Segmente pro Frame

        // baseMaxDistance wird hier nicht direkt verwendet, kommt über Road.Lines
        public RoadRenderer(Texture2D pixelTexture, float baseOverlapFactor, float baseMaxDistance = 1f, bool useSmoothing = true, int curveSegments = 10)
        {
            _pixelTexture = pixelTexture;
            _baseOverlapFactor = baseOverlapFactor; // Wird in DrawPolyline verwendet
            _useSmoothing = useSmoothing;
            _curveSegments = curveSegments;
        }


        // OPTIMIERUNG: Nimmt jetzt gefilterte Liste von Straßen entgegen
        public void Draw(SpriteBatch spriteBatch, List<Road> roadsToDraw, RectangleF visibleBounds, MapCamera camera)
        {
            _visibleLineSegmentsDrawn = 0; // Reset counter for this frame
            float currentZoom = camera.Zoom; // Zoom einmal abrufen

            // Annahme: spriteBatch.Begin wurde bereits außerhalb aufgerufen (von TileManager oder CMetro.Draw)

            foreach (Road road in roadsToDraw) // Iteriere nur über die potenziell sichtbaren Straßen
            {
                // Caching für geglättete Linien (wenn Smoothing aktiv ist)
                if (_useSmoothing)
                {
                    // Prüfe, ob Cache für diese Straße initialisiert werden muss oder veraltet ist
                    bool needsCacheUpdate = road.CachedSmoothedLines == null ||
                                            road.CachedSmoothedLines.Count != road.Lines.Count ||
                                            Math.Abs(road.CachedSmoothZoom - currentZoom) > 0.1f; // Zoom-Schwellenwert

                    if (needsCacheUpdate)
                    {
                        road.CachedSmoothedLines ??= new List<List<Vector2>>(road.Lines.Count); // Initialisiere, falls null
                        // Fülle oder aktualisiere den Cache
                        while (road.CachedSmoothedLines.Count < road.Lines.Count) road.CachedSmoothedLines.Add(null); // Fülle Liste auf
                        for (int j = 0; j < road.Lines.Count; j++)
                        {
                            // Nur neu berechnen, wenn nötig (oder wenn Liste zu kurz war)
                            if (road.CachedSmoothedLines[j] == null || needsCacheUpdate)
                            {
                                if (road.Lines[j] != null && road.Lines[j].Count >= 2) // Nur für gültige Linien
                                {
                                    road.CachedSmoothedLines[j] = GenerateCatmullRomSpline(road.Lines[j], _curveSegments);
                                }
                                else
                                {
                                    road.CachedSmoothedLines[j] = new List<Vector2>(); // Leere Liste für ungültige Linien
                                }
                            }
                        }
                        road.CachedSmoothZoom = currentZoom; // Update Cache-Zoom
                    }
                }


                // Zeichne die Liniensegmente der Straße
                for (int i = 0; i < road.Lines.Count; i++)
                {
                    var line = road.Lines[i];
                    if (line == null || line.Count < 2) continue; // Überspringe ungültige Liniensegmente

                    // OPTIONAL: Zusätzliche Sichtbarkeitsprüfung pro Liniensegment (obwohl TileManager/Quadtree schon filtert)
                    // RectangleF lineBox = road.BoundingBoxes[i]; // BoundingBox des Segments
                    // if (!visibleBounds.Intersects(lineBox)) continue; // Überspringen, wenn Box nicht sichtbar

                    // Bestimme Farbe und Breite basierend auf Straßentyp
                    GetRoadStyle(road.RoadType, currentZoom, out float roadWidth, out Color roadColor);


                    if (_useSmoothing && road.CachedSmoothedLines != null && i < road.CachedSmoothedLines.Count && road.CachedSmoothedLines[i] != null)
                    {
                        // Zeichne geglättete Linie aus dem Cache
                        List<Vector2> smoothedLine = road.CachedSmoothedLines[i];
                        if (smoothedLine.Count >= 2)
                        {
                            DrawSmoothedPolyline(spriteBatch, smoothedLine, roadColor, roadWidth, currentZoom);
                            _visibleLineSegmentsDrawn += smoothedLine.Count - 1;
                        }
                    }
                    else
                    {
                        // Zeichne normale (nicht geglättete) Linie
                        DrawPolyline(spriteBatch, line, roadColor, roadWidth, _baseOverlapFactor, currentZoom);
                        _visibleLineSegmentsDrawn += line.Count - 1;
                    }
                }
            }
            // Annahme: spriteBatch.End wird außerhalb aufgerufen
        }

        // Hilfsmethode zum Abrufen von Stil-Informationen
        private void GetRoadStyle(string roadType, float zoom, out float width, out Color color)
        {
            // Basismaße
            float baseWidth;
            switch (roadType)
            {
                case "motorway": baseWidth = 5f; color = Color.DarkOrange; break;
                case "primary": case "trunk": baseWidth = 4f; color = Color.DarkGoldenrod; break;
                case "secondary": case "tertiary": baseWidth = 3f; color = Color.LightGray; break; // Etwas breiter gemacht
                case "residential": case "unclassified": baseWidth = 2f; color = Color.Gray; break;
                default: baseWidth = 1.5f; color = Color.DarkGray; break; // Schmaler für unbekannte/kleinere
            }

            // Skaliere Breite mit inverser Wurzel des Zooms (weniger starke Skalierung als 1/Zoom)
            width = baseWidth / MathF.Sqrt(Math.Max(0.1f, zoom));
            // Setze Mindest- und Maximalbreite in Pixeln (unabhängig vom Zoom)
            width = Math.Clamp(width, 0.5f, 15f); // Mindestens 0.5 Pixel, maximal 15 Pixel breit
        }


        private void DrawSmoothedPolyline(SpriteBatch spriteBatch, List<Vector2> smoothedPoints, Color color, float thickness, float zoom)
        {
            if (smoothedPoints.Count < 2) return;

            // Dicke muss nicht mehr angepasst werden, da GetRoadStyle dies bereits tut
            // float adjustedThickness = Math.Max(0.5f, thickness); // Mindestdicke

            for (int i = 0; i < smoothedPoints.Count - 1; i++)
            {
                Vector2 p1 = smoothedPoints[i];
                Vector2 p2 = smoothedPoints[i + 1];
                Vector2 direction = p2 - p1;
                float distance = direction.Length();

                if (distance < 0.01f) continue; // Überspringe Nulllängen-Segmente

                float angle = (float)Math.Atan2(direction.Y, direction.X);

                spriteBatch.Draw(_pixelTexture, p1, null, color, angle, Vector2.Zero, new Vector2(distance, thickness), SpriteEffects.None, 0);
            }
        }

        private List<Vector2> GenerateCatmullRomSpline(List<Vector2> points, int segmentsPerCurve)
        {
            // ... (wie bisher) ...
            List<Vector2> splinePoints = new List<Vector2>();
            if (points.Count < 2)
                return points; // Keine Glättung möglich

            // Füge den ersten Punkt hinzu
            splinePoints.Add(points[0]);


            // Für jeden Punkt (außer dem ersten und letzten) berechnen wir einen Spline-Abschnitt.
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = (i == 0) ? points[i] : points[i - 1]; // Wiederhole ersten Punkt am Anfang
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = (i + 2 < points.Count) ? points[i + 2] : points[i + 1]; // Wiederhole letzten Punkt am Ende

                // Füge interpolierte Punkte hinzu (starte bei j=1, da p1 schon drin ist oder gerade hinzugefügt wurde)
                for (int j = 1; j <= segmentsPerCurve; j++)
                {
                    float t = j / (float)segmentsPerCurve;
                    Vector2 pt = CatmullRom(p0, p1, p2, p3, t);
                    // Vermeide Duplikate
                    if (splinePoints.Count == 0 || Vector2.DistanceSquared(splinePoints[^1], pt) > 0.01f)
                    {
                        splinePoints.Add(pt);
                    }
                }
            }
            // Stelle sicher, dass der allerletzte Originalpunkt enthalten ist
            if (points.Count > 1 && Vector2.DistanceSquared(splinePoints[^1], points[^1]) > 0.01f)
            {
                splinePoints.Add(points[^1]);
            }

            return splinePoints;
        }

        private Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            // ... (wie bisher) ...
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2 * p1) +
                           (-p0 + p2) * t +
                           (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                           (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        private void DrawPolyline(SpriteBatch spriteBatch, List<Vector2> polyline, Color color, float thickness, float baseOverlapFactor, float zoom)
        {
            if (polyline.Count < 2) return;

            // Dicke wird bereits von GetRoadStyle angepasst
            // float adjustedThickness = Math.Max(0.5f, thickness);

            // Overlap sollte konstant in Pixeln sein, nicht mit Zoom skalieren
            float overlap = 0.1f; // Kleiner Pixel-Overlap

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector2 p1 = polyline[i];
                Vector2 p2 = polyline[i + 1];
                Vector2 direction = p2 - p1;
                float distance = direction.Length();

                if (distance < 0.01f) continue;

                float angle = (float)Math.Atan2(direction.Y, direction.X);

                // Füge Overlap hinzu
                Vector2 p2_overlapped = p2 + direction * (overlap / distance);
                float drawDistance = Vector2.Distance(p1, p2_overlapped);


                spriteBatch.Draw(_pixelTexture, p1, null, color, angle, Vector2.Zero, new Vector2(drawDistance, thickness), SpriteEffects.None, 0);
            }
        }

        // IsPolylineVisible und LineSegmentsIntersect werden nicht mehr benötigt,
        // da die Filterung jetzt durch TileManager/Quadtree erfolgt.

        public int GetVisibleLineCount() => _visibleLineSegmentsDrawn; // Gibt Anzahl gezeichneter Segmente zurück
    }
}