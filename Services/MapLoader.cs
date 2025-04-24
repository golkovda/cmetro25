using cmetro25.Models;
using cmetro25.Views;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq; // Für Min/Max benötigt
using MonoGame.Extended;
using cmetro25.Core;
using Newtonsoft.Json.Linq;

namespace cmetro25.Services
{
    /// <summary>
    /// Lädt und verarbeitet Kartendaten (Distrikte und Straßen) aus GeoJSON-Dateien.
    /// </summary>
    public class MapLoader
    {
        /// <summary>
        /// Maximale Basisdistanz für die Interpolation von Straßen.
        /// </summary>
        public float BaseMaxDistance { get; private set; }

        private MapCamera _camera; // Kann null sein während des initialen Ladens
        private float _initialLoadZoom; // Zoom für die allererste Interpolation

        /// <summary>
        /// Initialisiert eine neue Instanz der <see cref="MapLoader"/> Klasse.
        /// </summary>
        /// <param name="baseMaxDistance">Die maximale Basisdistanz für die Interpolation von Straßen.</param>
        /// <param name="initialLoadZoom">Der Zoomfaktor für die initiale Interpolation.</param>
        public MapLoader(float baseMaxDistance = 5f, float initialLoadZoom = 1.0f)
        {
            BaseMaxDistance = baseMaxDistance;
            _initialLoadZoom = initialLoadZoom; // Standard-Zoom für initiales Laden
        }

        private PolylineElement BuildPolyline(string kind, Geometry g)
        {
            var lines = new List<List<Vector2>>();
            if (g.type == "MultiLineString")
            {
                foreach (var line in g.CoordsAsMultiLineString())
                    lines.Add(line.Select(p => TransformCoordinates(p[0], p[1])).ToList());
            }
            else if (g.type == "LineString")
            {
                lines.Add(g.CoordsAsLineString()
                          .Select(p => TransformCoordinates(p[0], p[1])).ToList());
            }
            return new PolylineElement(kind, lines);
        }

        public List<PolylineElement> LoadRivers(string fp) => LoadPolylines(fp, "river");
        public List<PolylineElement> LoadRails(string fp) => LoadPolylines(fp, "rail");
        private List<PolylineElement> LoadPolylines(string path, string kind)
        {
            var list = new List<PolylineElement>();
            if (!File.Exists(path)) return list;
            var root = JsonConvert.DeserializeObject<Root>(File.ReadAllText(path));
            foreach (var f in root.features)
                if (f.geometry != null) list.Add(BuildPolyline(kind, f.geometry));
            // Bounding‑Box vorab berechnen
            foreach (var l in list)
                l.BoundingBoxes.AddRange(l.Lines.Select(ComputeBoundingBox));
            return list;
        }

        public List<PointElement> LoadStations(string fp)
        {
            var pts = new List<PointElement>();
            if (!File.Exists(fp)) return pts;
            var root = JsonConvert.DeserializeObject<Root>(File.ReadAllText(fp));
            foreach (var f in root.features)
                if (f.geometry?.type == "Point")
                {
                    var c = ((JToken)f.geometry.Coordinates).ToObject<List<double>>();
                    pts.Add(new PointElement("station",
                            TransformCoordinates(c[0], c[1])));
                }
            return pts;
        }

        /// <summary>
        /// Setzt die Kamera für den MapLoader.
        /// </summary>
        /// <param name="camera">Die zu setzende Kamera.</param>
        public void SetCamera(MapCamera camera)
        {
            _camera = camera;
        }

        /// <summary>
        /// Lädt Distrikte aus einer GeoJSON-Datei.
        /// </summary>
        /// <param name="filePath">Der Pfad zur GeoJSON-Datei.</param>
        /// <returns>Eine Liste von geladenen Distrikten.</returns>
        public List<District> LoadDistricts(string filePath)
        {
            List<District> districts = new List<District>();
            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"File not found: {filePath}");
                return districts;
            }
            string jsonContent = File.ReadAllText(filePath);
            Root root = JsonConvert.DeserializeObject<Root>(jsonContent);
            if (root?.features != null)
            {
                foreach (var feature in root.features)
                {
                    District district = new District();
                    // ... (Eigenschaften laden wie bisher) ...
                    if (feature.properties != null)
                    {
                        district.Name = feature.properties.name;
                        district.ReferenceId = feature.properties.id;
                        district.AdminTitle = feature.properties.admin_title;
                        if (feature.properties.population != null && int.TryParse(feature.properties.population, out int population))
                            district.Population = population;
                    }

                    if (feature.geometry != null)
                    {
                        // ... (Polygon-Parsing wie bisher) ...
                        if (feature.geometry.type == "MultiPolygon")
                        {
                            var multiPolygon = feature.geometry.CoordsAsMultiPolygonString();
                            if (multiPolygon != null)
                            {
                                foreach (var polygon in multiPolygon)
                                {
                                    foreach (var ring in polygon)
                                    {
                                        List<Vector2> convertedPolygon = new List<Vector2>();
                                        foreach (var point in ring)
                                        {
                                            Vector2 transformedPoint = TransformCoordinates(point[0], point[1]);
                                            if (convertedPolygon.Count == 0 || convertedPolygon[^1] != transformedPoint)
                                                convertedPolygon.Add(transformedPoint);
                                        }
                                        if (convertedPolygon.Count > 1)
                                            district.Polygons.Add(convertedPolygon);
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine("[F!] Unsupported geometry.type in districts: " + feature.geometry.type);
                        }
                    }
                    district.TextPosition = CalculateCentroidOfTransformedPolygons(district.Polygons);

                    // NEU: Berechne die Bounding Box für den Distrikt
                    district.BoundingBox = CalculateDistrictBoundingBox(district.Polygons);

                    districts.Add(district);
                }
            }
            return districts;
        }

        public List<WaterBody> LoadWaterBodies(string filePath)
        {
            List<WaterBody> waterBodies = new List<WaterBody>();
            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"[Warning] Water body file not found: {filePath}");
                return waterBodies;
            }
            string jsonContent = File.ReadAllText(filePath);
            // Annahme: Die Root-Struktur ist dieselbe wie bei Distrikten/Straßen
            Root root = JsonConvert.DeserializeObject<Root>(jsonContent);
            if (root?.features != null)
            {
                foreach (var feature in root.features)
                {
                    // Prüfe, ob es sich um eine Wasserfläche handelt (basierend auf 'natural' oder 'water' Tag)
                    if (feature.properties != null && (feature.properties.natural == "water" || !string.IsNullOrEmpty(feature.properties.water)))
                    {
                        WaterBody wb = new WaterBody();
                        wb.Id = feature.properties.id;
                        wb.Name = feature.properties.name; // Kann null sein

                        // Bestimme den Typ (vereinfacht)
                        if (!string.IsNullOrEmpty(feature.properties.water))
                        {
                            wb.Type = feature.properties.water; // z.B. harbour, river, canal, lake
                        }
                        else if (feature.properties.natural == "water")
                        {
                            wb.Type = "water"; // Generischer Typ, wenn 'water' Tag fehlt
                        }
                        else
                        {
                            wb.Type = "unknown"; // Sollte eigentlich nicht passieren wegen der if-Bedingung oben
                        }


                        if (feature.geometry != null && feature.geometry.type == "MultiPolygon")
                        {
                            var multiPolygon = feature.geometry.CoordsAsMultiPolygonString();
                            if (multiPolygon != null)
                            {
                                foreach (var polygon in multiPolygon)
                                {
                                    foreach (var ring in polygon) // Behandle äußere und innere Ringe gleich
                                    {
                                        List<Vector2> convertedPolygon = new List<Vector2>();
                                        foreach (var point in ring)
                                        {
                                            Vector2 transformedPoint = TransformCoordinates(point[0], point[1]);
                                            // Vermeide Duplikate direkt nacheinander
                                            if (convertedPolygon.Count == 0 || Vector2.DistanceSquared(convertedPolygon[^1], transformedPoint) > 0.01f)
                                                convertedPolygon.Add(transformedPoint);
                                        }
                                        // Schließe den Polygonring, falls nicht schon geschehen (optional, aber gut für Renderer)
                                        if (convertedPolygon.Count > 1 && Vector2.DistanceSquared(convertedPolygon[0], convertedPolygon[^1]) > 0.01f)
                                        {
                                            convertedPolygon.Add(convertedPolygon[0]);
                                        }

                                        if (convertedPolygon.Count > 2) // Braucht mind. 3 Punkte für ein Polygon
                                            wb.Polygons.Add(convertedPolygon);
                                    }
                                }
                            }
                        }
                        else if (feature.geometry != null && feature.geometry.type == "Polygon") // Falls auch einfache Polygone vorkommen
                        {
                            var simplePolygon = feature.geometry.CoordsAsMultiLineString();
                            if (simplePolygon != null)
                            {
                                foreach (var ring in simplePolygon)
                                {
                                    List<Vector2> convertedPolygon = new List<Vector2>();
                                    foreach (var point in ring)
                                    {
                                        Vector2 transformedPoint = TransformCoordinates(point[0], point[1]);
                                        if (convertedPolygon.Count == 0 || Vector2.DistanceSquared(convertedPolygon[^1], transformedPoint) > 0.01f)
                                            convertedPolygon.Add(transformedPoint);
                                    }
                                    if (convertedPolygon.Count > 1 && Vector2.DistanceSquared(convertedPolygon[0], convertedPolygon[^1]) > 0.01f)
                                    {
                                        convertedPolygon.Add(convertedPolygon[0]);
                                    }
                                    if (convertedPolygon.Count > 2)
                                        wb.Polygons.Add(convertedPolygon);
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[Warning] Unsupported geometry type for water body: {feature.geometry?.type}");
                        }

                        // Berechne Bounding Box nur, wenn Polygone vorhanden sind
                        if (wb.Polygons.Any())
                        {
                            wb.BoundingBox = CalculateObjectBoundingBox(wb.Polygons); // Verwende Hilfsmethode
                            waterBodies.Add(wb);
                        }
                    }
                }
            }
            return waterBodies;
        }

        // Hilfsmethode zur Berechnung der Bounding Box für beliebige Polygon-Listen
        // (Kann auch für Distrikte verwendet werden, um Code zu vereinheitlichen)
        private RectangleF CalculateObjectBoundingBox(List<List<Vector2>> polygons)
        {
            if (polygons == null || !polygons.Any() || !polygons.First().Any())
                return RectangleF.Empty;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool validPointFound = false;

            foreach (var polygon in polygons)
            {
                foreach (var point in polygon)
                {
                    if (point.X < minX) minX = point.X;
                    if (point.Y < minY) minY = point.Y;
                    if (point.X > maxX) maxX = point.X;
                    if (point.Y > maxY) maxY = point.Y;
                    validPointFound = true;
                }
            }

            if (!validPointFound) return RectangleF.Empty;

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        // Optional: Ersetze den Code in CalculateDistrictBoundingBox durch einen Aufruf an CalculateObjectBoundingBox
        private RectangleF CalculateDistrictBoundingBox(List<List<Vector2>> polygons)
        {
            return CalculateObjectBoundingBox(polygons);
        }

        /// <summary>
        /// Berechnet den Schwerpunkt der transformierten Polygone eines Distrikts.
        /// </summary>
        /// <param name="polygons">Die Polygone des Distrikts.</param>
        /// <returns>Der berechnete Schwerpunkt.</returns>
        private Vector2 CalculateCentroidOfTransformedPolygons(List<List<Vector2>> polygons)
        {
            Vector2 centroid = Vector2.Zero;
            int pointCount = 0;
            foreach (var polygon in polygons)
            {
                foreach (var point in polygon)
                {
                    centroid += point;
                    pointCount++;
                }
            }
            if (pointCount > 0)
                centroid /= pointCount;
            return centroid;
        }

        /// <summary>
        /// Lädt Straßen aus einer GeoJSON-Datei.
        /// </summary>
        /// <param name="filePath">Der Pfad zur GeoJSON-Datei.</param>
        /// <returns>Eine Liste von geladenen Straßen.</returns>
        public List<Road> LoadRoads(string filePath)
        {
            List<Road> roads = new List<Road>();
            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"File not found: {filePath}");
                return roads;
            }
            string jsonContent = File.ReadAllText(filePath);
            Root root = JsonConvert.DeserializeObject<Root>(jsonContent);
            if (root?.features != null)
            {
                foreach (var feature in root.features)
                {
                    if (feature.properties?.highway != null)
                    {
                        Road road = new Road();
                        road.Name = feature.properties.name;
                        road.RoadType = feature.properties.highway;
                        road.MaxSpeed = feature.properties.maxspeed;

                        if (feature.geometry != null)
                        {
                            if (feature.geometry.type == "MultiLineString")
                            {
                                var multiLineString = feature.geometry.CoordsAsMultiLineString();
                                if (multiLineString != null)
                                {
                                    foreach (var lineString in multiLineString)
                                        // OPTIMIERUNG: Übergebe _initialLoadZoom für die erste Interpolation
                                        TransformAndInterpolateLineString(lineString, road, _initialLoadZoom);
                                }
                            }
                            else if (feature.geometry.type == "LineString")
                            {
                                var lineString = feature.geometry.CoordsAsLineString();
                                if (lineString != null)
                                    // OPTIMIERUNG: Übergebe _initialLoadZoom für die erste Interpolation
                                    TransformAndInterpolateLineString(lineString, road, _initialLoadZoom);
                            }
                            else
                            {
                                Debug.WriteLine("[F!] Unsupported geometry.type in roads: " + feature.geometry.type);
                            }
                        }
                        // OPTIMIERUNG: Setze initiale Cache-Werte basierend auf der ersten Interpolation
                        road.CachedInterpolatedLines = new List<List<Vector2>>(road.Lines);
                        road.CachedBoundingBoxes = new List<RectangleF>(road.BoundingBoxes);
                        road.CachedZoom = _initialLoadZoom;
                        road.LastInterpolationZoom = _initialLoadZoom;

                        roads.Add(road);
                    }
                }
            }

            ClusterEndNodes(roads);

            return roads;
        }

        private static void ClusterEndNodes(List<Road> roads)
        {
            // -----------------------------------------------------------------
            //  End-Knoten clustern  (einmalig, global)
            // -----------------------------------------------------------------
            const float tol = 0.8f;                     // Welt-Einheit (≈ 0.8 m)

            Point Key(Vector2 p) =>
                new((int)MathF.Round(p.X / tol),
                    (int)MathF.Round(p.Y / tol));

            var nodeCounter = new Dictionary<Point, int>();

            // 1)  zählen
            foreach (var rd in roads)
            foreach (var line in rd.OriginalLines)
            {
                if (line.Count == 0) continue;

                foreach (var pt in line)           // alle Punkte!
                {
                    var k = Key(pt);
                    nodeCounter[k] = nodeCounter.TryGetValue(k, out var c) ? c + 1 : 1;
                }
            }

            // 2)  Flag pro(!) End-Knoten setzen
            foreach (var rd in roads)
            foreach (var line in rd.OriginalLines)
            {
                if (line.Count == 0) continue;

                rd.FreeStart ??= new();            // Liste pro Road anlegen
                rd.FreeEnd ??= new();

                rd.FreeStart.Add(nodeCounter[Key(line[0])] == 1);
                rd.FreeEnd.Add(nodeCounter[Key(line[^1])] == 1);
            }
        }

        /// <summary>
        /// Transformiert und interpoliert eine Linienfolge basierend auf dem gegebenen Zoomfaktor.
        /// </summary>
        /// <param name="lineString">Die zu transformierende und zu interpolierende Linienfolge.</param>
        /// <param name="road">Die Straße, zu der die Linienfolge gehört.</param>
        /// <param name="interpolationZoom">Der Zoomfaktor für die Interpolation.</param>
        private void TransformAndInterpolateLineString(List<List<double>> lineString, Road road, float interpolationZoom)
        {
            List<Vector2> convertedLine = new List<Vector2>();
            foreach (var point in lineString)
            {
                Vector2 transformedPoint = TransformCoordinates(point[0], point[1]);
                if (convertedLine.Count == 0 || convertedLine[^1] != transformedPoint)
                    convertedLine.Add(transformedPoint);
            }
            road.OriginalLines.Add(new List<Vector2>(convertedLine)); // Kopie speichern

            // OPTIMIERUNG: Nutze den übergebenen Zoom für die Interpolation
            List<Vector2> interpolatedLine = InterpolateLineWithOverlap(convertedLine, BaseMaxDistance, interpolationZoom);
            road.Lines.Add(interpolatedLine);
            road.BoundingBoxes.Add(ComputeBoundingBox(interpolatedLine));
            // road.LastInterpolationZoom wird jetzt außerhalb gesetzt, nachdem alle Linien geladen wurden
        }

        /// <summary>
        /// Berechnet die Bounding Box einer Liste von Punkten.
        /// </summary>
        /// <param name="points">Die Punkte, für die die Bounding Box berechnet werden soll.</param>
        /// <returns>Die berechnete Bounding Box.</returns>
        public RectangleF ComputeBoundingBox(List<Vector2> points)
        {
            if (points == null || points.Count == 0) return RectangleF.Empty;
            float minX = points[0].X, minY = points[0].Y;
            float maxX = points[0].X, maxY = points[0].Y;
            foreach (var pt in points.Skip(1)) // Skip first point as it's already assigned
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y > maxY) maxY = pt.Y;
            }
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Interpoliert eine Linie mit einem bestimmten Überlappungsfaktor basierend auf dem Zoomfaktor.
        /// </summary>
        /// <param name="points">Die zu interpolierende Linie.</param>
        /// <param name="bMd">Der Basis-Maximalabstand für die Interpolation.</param>
        /// <param name="zoom">Der Zoomfaktor für die Interpolation.</param>
        /// <returns>Die interpolierte Linie.</returns>
        public List<Vector2> InterpolateLineWithOverlap(List<Vector2> points, float bMd, float zoom)
        {
            if (zoom <= 0) zoom = 0.01f; // Sicherheitscheck gegen Division durch Null

            // Leichte Anpassung der bMd-Skalierung basierend auf Zoom, kann angepasst werden
            float adjustedMaxDistance = bMd / MathF.Sqrt(zoom); // Experimentiere mit Sqrt oder lasse es weg

            if (points.Count < 2)
                return new List<Vector2>(points); // Kopie zurückgeben

            List<Vector2> interpolatedPoints = new List<Vector2> { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                float distance = Vector2.Distance(p1, p2);
                int segments = (int)Math.Ceiling(distance / adjustedMaxDistance);

                if (segments > 1)
                {
                    for (int j = 1; j < segments; j++) // Beginne bei 1, da p1 schon drin ist
                    {
                        float t = (float)j / segments;
                        interpolatedPoints.Add(Vector2.Lerp(p1, p2, t));
                    }
                }
                // Füge p2 immer hinzu, außer es ist der letzte Punkt und Overlap wird hinzugefügt
                if (i < points.Count - 2 || segments <= 1)
                {
                    interpolatedPoints.Add(p2);
                }
            }

            // Overlap nur hinzufügen, wenn mehr als ein Punkt existiert
            if (points.Count >= 2)
            {
                Vector2 pStart = points[^2]; // Vorletzter Punkt
                Vector2 pEnd = points[^1];   // Letzter Punkt
                Vector2 direction = pEnd - pStart;
                if (direction.LengthSquared() > 0.001f) // Nur wenn Richtung gültig ist
                {
                    direction.Normalize();
                    float baseOverlapFactor = GameSettings.RoadBaseOverlapFactor;
                    // Skaliere Overlap invers zum Zoom, aber begrenze ihn nach unten
                    float adjustedOverlap = Math.Max(0.5f, baseOverlapFactor / zoom);
                    Vector2 overlappedP2 = pEnd + direction * adjustedOverlap;
                    interpolatedPoints.Add(overlappedP2);
                }
                else
                {
                    // Falls Richtung ungültig (gleiche Punkte), füge einfach den letzten Punkt hinzu
                    if (interpolatedPoints.Count == 0 || interpolatedPoints[^1] != pEnd)
                        interpolatedPoints.Add(pEnd);
                }
            }
            else if (points.Count == 1)
            {
                // Falls nur ein Punkt, stelle sicher, dass er drin ist
                if (interpolatedPoints.Count == 0) interpolatedPoints.Add(points[0]);
            }

            return interpolatedPoints;
        }

        /// <summary>
        /// Transformiert geographische Koordinaten in das lokale Koordinatensystem.
        /// </summary>
        /// <param name="x">Die geographische X-Koordinate.</param>
        /// <param name="y">Die geographische Y-Koordinate.</param>
        /// <returns>Die transformierte Koordinate im lokalen Koordinatensystem.</returns>
        private Vector2 TransformCoordinates(double x, double y)
        {
            double referenceX = GameSettings.ReferenceCoordX;
            double referenceY = GameSettings.ReferenceCoordY;
            double deltaX = x - referenceX;
            double deltaY = y - referenceY;
            float scaleFactor = GameSettings.CoordScaleFactor;
            float transformedX = (float)(deltaX * scaleFactor);
            float transformedY = (float)(-deltaY * scaleFactor);
            return new Vector2(transformedX, transformedY);
        }
    }
}
