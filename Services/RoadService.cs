using cmetro25.Models;
using cmetro25.Utils;
using cmetro25.Views; // Für MapCamera
using Microsoft.Xna.Framework;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Diagnostics; // Für Debug
using System.Linq;
using System.Threading.Tasks;

namespace cmetro25.Services
{
    /// <summary>
    /// Service-Klasse zur Verwaltung und Verarbeitung von Straßen.
    /// </summary>
    public class RoadService
    {
        private readonly List<Road> _roads;
        private readonly MapLoader _mapLoader;
        private Quadtree<Road> _roadQuadtree;
        private readonly object _quadtreeLock = new object();
        private Task _quadtreeUpdateTask;
        private MapCamera _camera; // NEU: Referenz auf die Kamera
        private float _lastInterpolationZoomTriggered = -1f; // NEU: Merkt sich den Zoom des letzten Updates

        /// <summary>
        /// Initialisiert eine neue Instanz der <see cref="RoadService"/> Klasse.
        /// </summary>
        /// <param name="roads">Die Liste der Straßen.</param>
        /// <param name="mapLoader">Der MapLoader zum Laden und Verarbeiten der Kartendaten.</param>
        /// <exception cref="ArgumentNullException">Wird ausgelöst, wenn <paramref name="roads"/> oder <paramref name="mapLoader"/> null ist.</exception>
        public RoadService(List<Road> roads, MapLoader mapLoader)
        {
            _roads = roads ?? throw new ArgumentNullException(nameof(roads));
            _mapLoader = mapLoader ?? throw new ArgumentNullException(nameof(mapLoader));
            // Quadtree wird initial gebaut, basierend auf den im MapLoader interpolierten Linien
            BuildRoadQuadtree();
        }

        /// <summary>
        /// Setzt die Kamera für den RoadService.
        /// </summary>
        /// <param name="camera">Die zu setzende Kamera.</param>
        public void SetCamera(MapCamera camera)
        {
            _camera = camera;
        }

        /// <summary>
        /// Baut den Quadtree für die Straßen neu auf.
        /// </summary>
        public void BuildRoadQuadtree()
        {
            // Sperren für den Fall, dass dies aus einem anderen Thread aufgerufen wird (obwohl es hier synchron ist)
            lock (_quadtreeLock)
            {
                _roadQuadtree = BuildRoadQuadtreeInternal();
                Debug.WriteLine($"Quadtree built/rebuilt. Root bounds: {_roadQuadtree?.Bounds}");
            }
        }

        /// <summary>
        /// Interne Methode zum Aufbau des Quadtrees. Diese Methode wird von außen gesperrt.
        /// </summary>
        /// <returns>Der neu aufgebaute Quadtree.</returns>
        private Quadtree<Road> BuildRoadQuadtreeInternal()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasPoints = false;

            // Berechne Gesamtgrenzen basierend auf den BoundingBoxes der Straßen
            foreach (var road in _roads)
            {
                foreach (var box in road.BoundingBoxes) // Nutze die bereits berechneten Boxen
                {
                    if (box.Width <= 0 || box.Height <= 0) continue; // Ignoriere leere Boxen

                    if (box.Left < minX) minX = box.Left;
                    if (box.Top < minY) minY = box.Top;
                    if (box.Right > maxX) maxX = box.Right;
                    if (box.Bottom > maxY) maxY = box.Bottom;
                    hasPoints = true;
                }
            }

            if (!hasPoints)
            {
                Debug.WriteLine("[Warning] No valid road bounds found for Quadtree.");
                // Fallback auf eine Standardgröße oder Fehler werfen?
                // Erstellen eines leeren Baumes mit Standardgrenzen
                return new Quadtree<Road>(new RectangleF(0, 0, 1000, 1000));
            }

            // Füge einen kleinen Puffer hinzu
            float buffer = 10f;
            minX -= buffer;
            minY -= buffer;
            maxX += buffer;
            maxY += buffer;

            RectangleF overallBounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);

            // Sicherstellen, dass Breite und Höhe positiv sind
            if (overallBounds.Width <= 0) overallBounds.Width = 1;
            if (overallBounds.Height <= 0) overallBounds.Height = 1;

            Quadtree<Road> newTree = new Quadtree<Road>(overallBounds);

            // Füge Straßen basierend auf ihren BoundingBoxes ein
            foreach (var road in _roads)
            {
                // Berechne eine Gesamt-BoundingBox für die Straße aus ihren Segment-Boxen
                RectangleF roadOverallBox = GetRoadBoundingBox(road);
                if (!roadOverallBox.IsEmpty) // Nur einfügen, wenn die Straße eine gültige Box hat
                {
                    newTree.Insert(road, roadOverallBox);
                }
            }
            return newTree;
        }

        /// <summary>
        /// Aktualisiert die Interpolationen der Straßen asynchron basierend auf dem aktuellen Zoom und den sichtbaren Grenzen.
        /// </summary>
        /// <param name="zoom">Der aktuelle Zoomfaktor.</param>
        /// <param name="visibleBounds">Die sichtbaren Grenzen.</param>
        public void UpdateRoadInterpolationsAsync(float zoom, RectangleF visibleBounds)
        {
            if (_quadtreeUpdateTask != null && !_quadtreeUpdateTask.IsCompleted)
                return;

            _lastInterpolationZoomTriggered = zoom; // NEU: Speichere den Zoom, für den dieses Update gestartet wird

            _quadtreeUpdateTask = Task.Run(() =>
            {
                bool needsRebuild = false;
                // OPTIMIERUNG: Frage zuerst den Quadtree nach potenziell sichtbaren Straßen
                List<Road> potentiallyVisibleRoads;
                lock (_quadtreeLock) // Sicherer Zugriff auf den Quadtree
                {
                    if (_roadQuadtree == null) return; // Baum noch nicht bereit
                    // Erweitere visibleBounds leicht, um Straßen am Rand sicher zu erwischen
                    RectangleF queryBounds = new RectangleF(
                        visibleBounds.X - 50 / zoom, // Puffer in Weltkoordinaten
                        visibleBounds.Y - 50 / zoom,
                        visibleBounds.Width + 100 / zoom,
                        visibleBounds.Height + 100 / zoom);
                    potentiallyVisibleRoads = _roadQuadtree.Query(queryBounds).Distinct().ToList(); // Distinct, falls Straße mehrfach drin ist
                }

                // Parallelisierte Aktualisierung nur für potenziell sichtbare Straßen
                Parallel.ForEach(potentiallyVisibleRoads, road =>
                {
                    // Prüfe, ob eine Neuberechnung für diesen Zoom überhaupt nötig ist
                    if (Math.Abs(road.CachedZoom - zoom) < 0.05f)
                    {
                        return;
                    }

                    // Neuberechnung der interpolierten Linien und BoundingBoxen
                    List<List<Vector2>> newLines = new List<List<Vector2>>();
                    List<RectangleF> newBoxes = new List<RectangleF>();
                    foreach (var originalLine in road.OriginalLines)
                    {
                        if (originalLine == null || originalLine.Count == 0) continue;

                        List<Vector2> newInterpolatedLine = _mapLoader.InterpolateLineWithOverlap(originalLine, _mapLoader.BaseMaxDistance, zoom);
                        newLines.Add(newInterpolatedLine);
                        if (newInterpolatedLine.Count > 0)
                            newBoxes.Add(_mapLoader.ComputeBoundingBox(newInterpolatedLine));
                        else
                            newBoxes.Add(RectangleF.Empty); // Leere Box für leere Linien
                    }

                    road.Lines = newLines;
                    road.BoundingBoxes = newBoxes;
                    road.LastInterpolationZoom = zoom; // Setze den aktuellen Zoom
                    road.CachedInterpolatedLines = new List<List<Vector2>>(newLines); // Kopien erstellen
                    road.CachedBoundingBoxes = new List<RectangleF>(newBoxes);       // Kopien erstellen
                    road.CachedZoom = zoom;
                    needsRebuild = true; // Markiere, dass der Quadtree neu gebaut werden muss
                });

                if (needsRebuild)
                {
                    Quadtree<Road> newQuadtree = BuildRoadQuadtreeInternal();
                    lock (_quadtreeLock)
                    {
                        _roadQuadtree = newQuadtree;
                    }
                }
            });
        }

        /// <summary>
        /// Gibt den aktuellen Quadtree zurück.
        /// </summary>
        /// <returns>Der aktuelle Quadtree.</returns>
        public Quadtree<Road> GetQuadtree()
        {
            lock (_quadtreeLock)
            {
                return _roadQuadtree;
            }
        }

        /// <summary>
        /// Berechnet die Gesamt-BoundingBox einer Straße aus ihren Segment-Boxen.
        /// </summary>
        /// <param name="road">Die Straße, für die die BoundingBox berechnet werden soll.</param>
        /// <returns>Die berechnete BoundingBox.</returns>
        public RectangleF GetRoadBoundingBox(Road road)
        {
            if (road.BoundingBoxes == null || road.BoundingBoxes.Count == 0)
                return RectangleF.Empty;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool validBoxFound = false;

            foreach (var box in road.BoundingBoxes)
            {
                if (box.IsEmpty) continue; // Überspringe leere Boxen

                if (box.Left < minX) minX = box.Left;
                if (box.Top < minY) minY = box.Top;
                if (box.Right > maxX) maxX = box.Right;
                if (box.Bottom > maxY) maxY = box.Bottom;
                validBoxFound = true;
            }

            if (!validBoxFound) return RectangleF.Empty;

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Gibt den Zoomfaktor zurück, bei dem die letzte Interpolation ausgelöst wurde.
        /// </summary>
        /// <returns>Der Zoomfaktor der letzten Interpolation.</returns>
        public float GetLastInterpolationZoom()
        {
            return _lastInterpolationZoomTriggered;
        }
    }
}
