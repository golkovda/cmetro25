using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using cmetro25.Models;
using MonoGame.Extended;

namespace cmetro25.Views
{
    public class RoadRenderer(Texture2D pixelTexture, float baseOverlapFactor, float baseMaxDistance = 1f, bool useSmoothing = true, int curvesegments = 10)
    {

        private int _visibleItemCount = 0;
        public void Draw(SpriteBatch spriteBatch, List<Road> roads, RectangleF visibleBounds, MapCamera camera)
        {
            int ic = 0;
            foreach (Road road in roads)
            {
                // Synchronisiere den Cache: Falls die Anzahl der cached Spline-Segmente nicht der aktuellen Anzahl entspricht,
                // lösche den Cache und berechne alle glatten Linien neu.
                if (road.CachedSmoothedLines.Count != road.Lines.Count)
                {
                    road.CachedSmoothedLines.Clear();
                    for (int j = 0; j < road.Lines.Count; j++)
                    {
                        List<Vector2> smoothed = GenerateCatmullRomSpline(road.Lines[j], curvesegments);
                        road.CachedSmoothedLines.Add(smoothed);
                    }
                    road.CachedSmoothZoom = camera.Zoom;
                }
                //foreach (var linecollection in road.Lines)
                //    ic += linecollection.Count;

                for (int i = 0; i < road.Lines.Count; i++)
                {
                    var line = road.Lines[i];
                    RectangleF lineBox = road.BoundingBoxes[i];
                    bool lineVisible = visibleBounds.Intersects(lineBox) || IsPolylineVisible(line, visibleBounds);
                    if (!lineVisible)
                        continue;

                    ic += i;

                    float roadWidth;
                    Color roadColor;
                    switch (road.RoadType)
                    {
                        case "motorway":
                            roadWidth = 5f;
                            roadColor = Color.DarkOrange;
                            break;
                        case "primary":
                        case "trunk":
                            roadWidth = 4f;
                            roadColor = Color.DarkGoldenrod;
                            break;
                        case "secondary":
                        case "tertiary":
                            roadWidth = 2f;
                            roadColor = Color.LightGray;
                            break;
                        default:
                            roadWidth = 2f;
                            roadColor = Color.Gray;
                            break;
                    }

                    if (useSmoothing)
                    {
                        // Falls der Zoom sich signifikant geändert hat, aktualisiere den Cache für dieses Segment.
                        if (Math.Abs(road.CachedSmoothZoom - camera.Zoom) > 0.1f)
                        {
                            List<Vector2> smoothed = GenerateCatmullRomSpline(line, curvesegments);
                            road.CachedSmoothedLines[i] = smoothed;
                            road.CachedSmoothZoom = camera.Zoom;
                        }

                        List<Vector2> smoothedLine = road.CachedSmoothedLines[i];
                        DrawSmoothedPolyline(spriteBatch, smoothedLine, roadColor, roadWidth, camera);
                    }
                    else
                        DrawPolyline(spriteBatch, line, roadColor, roadWidth, baseOverlapFactor, camera);
                }
            }

            _visibleItemCount = ic;
        }

        /// <summary>
        /// Zeichnet eine glatte Kurve basierend auf einem Catmull-Rom-Spline.
        /// </summary>
        private void DrawSmoothedPolyline(SpriteBatch spriteBatch, List<Vector2> polyline, Color color, float thickness, MapCamera camera)
        {
            // Erzeuge einen Spline mit z. B. 10 Segmenten pro Kurvenabschnitt
            List<Vector2> smoothedPoints = GenerateCatmullRomSpline(polyline, 10);
            for (int i = 0; i < smoothedPoints.Count - 1; i++)
            {
                Vector2 p1 = smoothedPoints[i];
                Vector2 p2 = smoothedPoints[i + 1];
                Vector2 direction = p2 - p1;
                float distance = direction.Length();
                float angle = (float)Math.Atan2(direction.Y, direction.X);
                // Anpassung der Dicke an den Zoom
                float adjustedThickness = thickness / camera.Zoom;
                spriteBatch.Draw(pixelTexture, p1, null, color, angle, Vector2.Zero, new Vector2(distance, adjustedThickness), SpriteEffects.None, 0);
            }
        }

        /// <summary>
        /// Erzeugt einen Catmull-Rom-Spline, der die Originalpunkte glättet.
        /// </summary>
        /// <param name="points">Die Original-Punkte der Polyline.</param>
        /// <param name="segmentsPerCurve">Anzahl der Segmente zwischen zwei Punkten.</param>
        /// <returns>Eine Liste von interpolierten Punkten.</returns>
        private List<Vector2> GenerateCatmullRomSpline(List<Vector2> points, int segmentsPerCurve)
        {
            List<Vector2> splinePoints = new List<Vector2>();
            if (points.Count < 2)
                return points;

            // Für jeden Punkt (außer dem letzten) berechnen wir einen Spline-Abschnitt.
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = (i == 0) ? points[i] : points[i - 1];
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = (i + 2 < points.Count) ? points[i + 2] : points[i + 1];

                for (int j = 0; j <= segmentsPerCurve; j++)
                {
                    float t = j / (float)segmentsPerCurve;
                    Vector2 pt = CatmullRom(p0, p1, p2, p3, t);
                    splinePoints.Add(pt);
                }
            }
            return splinePoints;
        }

        /// <summary>
        /// Berechnet einen Punkt auf dem Catmull-Rom-Spline.
        /// </summary>
        private Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2 * p1) +
                           (-p0 + p2) * t +
                           (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                           (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        /// <summary>
        /// Alt: Zeichnet die Polyline segmentweise (ohne Glättung).
        /// </summary>
        private void DrawPolyline(SpriteBatch spriteBatch, List<Vector2> polyline, Color color, float baseThickness, float baseOverlapFactor, MapCamera camera)
        {
            if (polyline.Count < 2)
                return;

            float adjustedThickness = baseThickness / camera.Zoom;
            float minThickness = baseThickness * 0.5f;
            if (adjustedThickness < minThickness)
                adjustedThickness = minThickness;
            float adjustedOverlapFactor = Math.Min(baseOverlapFactor / camera.Zoom, 0.5f);

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector2 p1 = polyline[i];
                Vector2 p2 = polyline[i + 1];
                Vector2 direction = p2 - p1;
                p2 += direction * adjustedOverlapFactor;
                float distance = Vector2.Distance(p1, p2);
                float angle = (float)Math.Atan2(direction.Y, direction.X);
                spriteBatch.Draw(pixelTexture, p1, null, color, angle, Vector2.Zero, new Vector2(distance, adjustedThickness), SpriteEffects.None, 0);
            }
        }

        /// <summary>
        /// Prüft, ob mindestens ein Punkt der Polyline im sichtbaren Bereich liegt oder ein Segment den Bereich schneidet.
        /// </summary>
        private bool IsPolylineVisible(List<Vector2> polyline, RectangleF visibleBounds)
        {
            foreach (Vector2 point in polyline)
                if (visibleBounds.Contains(point))
                    return true;

            Vector2 topLeft = new Vector2(visibleBounds.Left, visibleBounds.Top);
            Vector2 topRight = new Vector2(visibleBounds.Right, visibleBounds.Top);
            Vector2 bottomLeft = new Vector2(visibleBounds.Left, visibleBounds.Bottom);
            Vector2 bottomRight = new Vector2(visibleBounds.Right, visibleBounds.Bottom);
            var rectangleEdges = new List<(Vector2, Vector2)>
            {
                (topLeft, topRight),
                (topRight, bottomRight),
                (bottomRight, bottomLeft),
                (bottomLeft, topLeft)
            };

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector2 p1 = polyline[i];
                Vector2 p2 = polyline[i + 1];
                foreach (var edge in rectangleEdges)
                    if (LineSegmentsIntersect(p1, p2, edge.Item1, edge.Item2))
                        return true;
            }
            return false;
        }

        private static bool LineSegmentsIntersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
        {
            Vector2 r = p2 - p;
            Vector2 s = q2 - q;
            float rxs = r.X * s.Y - r.Y * s.X;
            Vector2 qp = q - p;
            float qpxr = qp.X * r.Y - qp.Y * r.X;
            if (Math.Abs(rxs) < 0.0001f && Math.Abs(qpxr) < 0.0001f)
            {
                float rDotR = Vector2.Dot(r, r);
                float t0 = Vector2.Dot(qp, r) / rDotR;
                float t1 = t0 + Vector2.Dot(s, r) / rDotR;
                if (t0 > t1)
                    (t1, t0) = (t0, t1);
                return (t0 <= 1 && t1 >= 0);
            }
            if (Math.Abs(rxs) < 0.0001f && Math.Abs(qpxr) >= 0.0001f)
                return false;
            float t = (qp.X * s.Y - qp.Y * s.X) / rxs;
            float u = (qp.X * r.Y - qp.Y * r.X) / rxs;
            return (t is >= 0 and <= 1 && u is >= 0 and <= 1);
        }
        public int GetVisibleLineCount() => _visibleItemCount;
    }
}
