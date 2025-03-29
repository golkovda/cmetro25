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
    public class RoadService
    {
        private readonly List<Road> _roads;
        private readonly MapLoader _mapLoader;
        private Quadtree<Road> _roadQuadtree;
        // private int _visibleLines = 0; // Wird in RoadRenderer gezählt

        private readonly object _quadtreeLock = new object();
        private Task _quadtreeUpdateTask;
        private MapCamera _camera; // NEU: Referenz auf die Kamera
        private float _lastInterpolationZoomTriggered = -1f; // NEU: Merkt sich den Zoom des letzten Updates


        public RoadService(List<Road> roads, MapLoader mapLoader)
        {
            _roads = roads ?? throw new ArgumentNullException(nameof(roads));
            _mapLoader = mapLoader ?? throw new ArgumentNullException(nameof(mapLoader));
            // Quadtree wird initial gebaut, basierend auf den im MapLoader interpolierten Linien
            BuildRoadQuadtree();
        }

        // NEU: Methode zum Setzen der Kamera nach der Initialisierung
        public void SetCamera(MapCamera camera)
        {
            _camera = camera;
        }

        public void BuildRoadQuadtree()
        {
            // Sperren für den Fall, dass dies aus einem anderen Thread aufgerufen wird (obwohl es hier synchron ist)
            lock (_quadtreeLock)
            {
                _roadQuadtree = BuildRoadQuadtreeInternal();
                Debug.WriteLine($"Quadtree built/rebuilt. Root bounds: {_roadQuadtree?.Bounds}");
            }
        }

        private Quadtree<Road> BuildRoadQuadtreeInternal() // Keine Sperre hier, wird von außen gesperrt
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
                    // Füge die Straße mit ihrer Gesamt-Box ein.
                    // Die Query-Methode muss dann prüfen, ob die *tatsächlichen* Segmente die Query-Area schneiden.
                    // Alternativ: Jedes Segment einzeln einfügen (kann Baum tiefer machen).
                    // Wir bleiben erstmal bei der Gesamt-Box pro Straße.
                    newTree.Insert(road, roadOverallBox);
                }
            }
            return newTree;
        }


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
                    // Toleranz kann angepasst werden
                    if (Math.Abs(road.CachedZoom - zoom) < 0.05f)
                    {
                        // Keine Neuberechnung, aber stelle sicher, dass die aktuellen Linien gesetzt sind
                        // (könnte durch vorherige Updates geändert worden sein)
                        // Dies ist eigentlich nicht nötig, wenn der Cache korrekt verwendet wird.
                        // road.Lines = road.CachedInterpolatedLines;
                        // road.BoundingBoxes = road.CachedBoundingBoxes;
                        return;
                    }

                    // Neuberechnung der interpolierten Linien und BoundingBoxen
                    List<List<Vector2>> newLines = new List<List<Vector2>>();
                    List<RectangleF> newBoxes = new List<RectangleF>();
                    foreach (var originalLine in road.OriginalLines)
                    {
                        // Stelle sicher, dass originalLine nicht leer ist
                        if (originalLine == null || originalLine.Count == 0) continue;

                        List<Vector2> newInterpolatedLine = _mapLoader.InterpolateLineWithOverlap(originalLine, _mapLoader.BaseMaxDistance, zoom);
                        newLines.Add(newInterpolatedLine);
                        // Berechne BoundingBox nur, wenn die Linie Punkte hat
                        if (newInterpolatedLine.Count > 0)
                            newBoxes.Add(_mapLoader.ComputeBoundingBox(newInterpolatedLine));
                        else
                            newBoxes.Add(RectangleF.Empty); // Leere Box für leere Linien
                    }

                    // Aktualisiere sowohl die "live" als auch die gecachten Werte
                    // WICHTIG: Direkter Zugriff auf road.Lines etc. in Parallel.ForEach ist okay,
                    // solange nicht *gleichzeitig* von woanders darauf geschrieben wird.
                    road.Lines = newLines;
                    road.BoundingBoxes = newBoxes;
                    road.LastInterpolationZoom = zoom; // Setze den aktuellen Zoom
                    // Aktualisiere Cache
                    road.CachedInterpolatedLines = new List<List<Vector2>>(newLines); // Kopien erstellen
                    road.CachedBoundingBoxes = new List<RectangleF>(newBoxes);       // Kopien erstellen
                    road.CachedZoom = zoom;
                    needsRebuild = true; // Markiere, dass der Quadtree neu gebaut werden muss
                });

                // Baue den Quadtree nur neu, wenn tatsächlich Straßen aktualisiert wurden.
                if (needsRebuild)
                {
                    Quadtree<Road> newQuadtree = BuildRoadQuadtreeInternal();
                    lock (_quadtreeLock)
                    {
                        _roadQuadtree = newQuadtree;
                    }
                    // Debug.WriteLine("Quadtree rebuilt after interpolation update.");
                }
            });
        }

        public Quadtree<Road> GetQuadtree()
        {
            lock (_quadtreeLock)
            {
                // Gib eine Referenz zurück. Der Aufrufer muss wissen, dass er sie nicht ändern darf
                // oder muss sie innerhalb eines Locks verwenden, wenn er länger damit arbeitet.
                return _roadQuadtree;
            }
        }

        // Berechnet die Gesamt-BoundingBox einer Straße aus ihren Segment-Boxen
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

        // NEU: Methode zum Abrufen des letzten ausgelösten Zooms
        public float GetLastInterpolationZoom()
        {
            return _lastInterpolationZoomTriggered;
        }


        // Diese synchrone Methode wird wahrscheinlich nicht mehr benötigt, wenn die asynchrone verwendet wird.
        // Falls doch, muss sie angepasst werden, um den Quadtree korrekt zu sperren/aktualisieren.
        /*
        public void UpdateRoadInterpolations(float zoom)
        {
            bool needsRebuild = false;
            foreach (var road in _roads)
            {
                // ... (Interpolationslogik wie in der Async-Version) ...
                 needsRebuild = true; // Wenn etwas geändert wurde
            }
            if (needsRebuild)
            {
                 BuildRoadQuadtree(); // Stellt sicher, dass die Sperre intern verwendet wird
            }
        }
        */

        // Wird jetzt im RoadRenderer gezählt
        // public int GetBoundingBoxRoadLineCount() => _visibleLines;
    }
}