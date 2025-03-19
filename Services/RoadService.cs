using cmetro25.Models;
using cmetro25.Utils;
using Microsoft.Xna.Framework;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace cmetro25.Services
{
    public class RoadService
    {
        private readonly List<Road> _roads;
        private readonly MapLoader _mapLoader;
        private Quadtree<Road> _roadQuadtree;
        private int _visibleLines = 0;

        private readonly object _quadtreeLock = new object();
        private Task _quadtreeUpdateTask;

        public RoadService(List<Road> roads, MapLoader mapLoader)
        {
            _roads = roads;
            _mapLoader = mapLoader;
            BuildRoadQuadtree();
        }

        /*public void BuildRoadQuadtree()
        {
            RectangleF overall = GetOverallRoadBounds(_roads);
            _roadQuadtree = new Quadtree<Road>(overall);
            foreach (var road in _roads)
            {
                RectangleF rb = GetRoadBoundingBox(road);
                _roadQuadtree.Insert(road, rb);
            }
        }*/

        public void BuildRoadQuadtree()
        {
            _roadQuadtree = BuildRoadQuadtreeInternal();
        }

        // Baut den Quadtree auf Basis der aktuell berechneten Straßen-Daten neu auf und gibt ihn zurück.
        private Quadtree<Road> BuildRoadQuadtreeInternal()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var road in _roads)
            {
                foreach (var polyline in road.Lines)
                {
                    for (int i = 0; i < polyline.Count - 1; i++)
                    {
                        Vector2 p1 = polyline[i];
                        Vector2 p2 = polyline[i + 1];
                        float segMinX = Math.Min(p1.X, p2.X);
                        float segMinY = Math.Min(p1.Y, p2.Y);
                        float segMaxX = Math.Max(p1.X, p2.X);
                        float segMaxY = Math.Max(p1.Y, p2.Y);
                        if (segMinX < minX) minX = segMinX;
                        if (segMinY < minY) minY = segMinY;
                        if (segMaxX > maxX) maxX = segMaxX;
                        if (segMaxY > maxY) maxY = segMaxY;
                    }
                }
            }
            RectangleF overallBounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            Quadtree<Road> newTree = new Quadtree<Road>(overallBounds);

            float margin = 2f;
            foreach (var road in _roads)
            {
                foreach (var polyline in road.Lines)
                {
                    for (int i = 0; i < polyline.Count - 1; i++)
                    {
                        Vector2 p1 = polyline[i];
                        Vector2 p2 = polyline[i + 1];
                        float segMinX = Math.Min(p1.X, p2.X);
                        float segMinY = Math.Min(p1.Y, p2.Y);
                        float segMaxX = Math.Max(p1.X, p2.X);
                        float segMaxY = Math.Max(p1.Y, p2.Y);
                        RectangleF segmentBox = new RectangleF(
                            segMinX - margin,
                            segMinY - margin,
                            (segMaxX - segMinX) + 2 * margin,
                            (segMaxY - segMinY) + 2 * margin);
                        newTree.Insert(road, segmentBox);
                    }
                }
            }
            return newTree;
        }

        public void UpdateRoadInterpolationsAsync(float zoom, RectangleF visibleBounds)
        {
            // Verhindere, dass mehrere Tasks gleichzeitig laufen.
            if (_quadtreeUpdateTask != null && !_quadtreeUpdateTask.IsCompleted)
                return;

            _quadtreeUpdateTask = Task.Run(() =>
            {
                // Parallelisierte Aktualisierung der Straßen
                Parallel.ForEach(_roads, road =>
                {
                    // Wenn der Unterschied zwischen dem gecachten Zoom und dem aktuellen Zoom klein ist, verwende die gecachten Werte
                    if (Math.Abs(road.CachedZoom - zoom) < 0.1f)
                    {
                        road.Lines = road.CachedInterpolatedLines;
                        road.BoundingBoxes = road.CachedBoundingBoxes;
                        return;
                    }

                    // Sichtbarkeitsprüfung: Nur aktualisieren, wenn die Straße (teilweise) im sichtbaren Bereich liegt
                    bool isVisible = road.BoundingBoxes.Any(bbox => visibleBounds.Intersects(bbox));
                    if (!isVisible)
                        return;


                    // Ansonsten: Neuberechnung der interpolierten Linien und BoundingBoxen
                    List<List<Vector2>> newLines = new List<List<Vector2>>();
                    List<RectangleF> newBoxes = new List<RectangleF>();
                    foreach (var originalLine in road.OriginalLines)
                    {
                        List<Vector2> newInterpolatedLine = _mapLoader.InterpolateLineWithOverlap(originalLine, _mapLoader.BaseMaxDistance, zoom);
                        newLines.Add(newInterpolatedLine);
                        newBoxes.Add(_mapLoader.ComputeBoundingBox(newInterpolatedLine));
                    }
                    // Aktualisiere sowohl die "live" als auch die gecachten Werte
                    road.Lines = newLines;
                    road.BoundingBoxes = newBoxes;
                    road.LastInterpolationZoom = zoom;
                    road.CachedInterpolatedLines = newLines;
                    road.CachedBoundingBoxes = newBoxes;
                    road.CachedZoom = zoom;
                });

                Quadtree<Road> newQuadtree = BuildRoadQuadtreeInternal();
                lock (_quadtreeLock)
                {
                    _roadQuadtree = newQuadtree;
                }
            });
        }
        public Quadtree<Road> GetQuadtree()
        {
            lock (_quadtreeLock)
            {
                return _roadQuadtree;
            }
        }

        public RectangleF GetOverallRoadBounds(List<Road> roads)
        {
            if (roads.Count == 0)
                return new RectangleF();
            RectangleF first = GetRoadBoundingBox(roads[0]);
            float minX = first.X, minY = first.Y;
            float maxX = first.X + first.Width, maxY = first.Y + first.Height;
            foreach (var road in roads)
            {
                RectangleF rb = GetRoadBoundingBox(road);
                if (rb.X < minX) minX = rb.X;
                if (rb.Y < minY) minY = rb.Y;
                if (rb.X + rb.Width > maxX) maxX = rb.X + rb.Width;
                if (rb.Y + rb.Height > maxY) maxY = rb.Y + rb.Height;
            }
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public RectangleF GetRoadBoundingBox(Road road)
        {
            if (road.BoundingBoxes.Count == 0)
                return new RectangleF();
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var box in road.BoundingBoxes)
            {
                if (box.X < minX) minX = box.X;
                if (box.Y < minY) minY = box.Y;
                if (box.X + box.Width > maxX) maxX = box.X + box.Width;
                if (box.Y + box.Height > maxY) maxY = box.Y + box.Height;
            }
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public void UpdateRoadInterpolations(float zoom)
        {
            foreach (var road in _roads)
            {
                road.Lines.Clear();
                road.BoundingBoxes.Clear();
                foreach (var originalLine in road.OriginalLines)
                {
                    List<Vector2> newInterpolatedLine = _mapLoader.InterpolateLineWithOverlap(originalLine, _mapLoader.BaseMaxDistance, zoom);
                    road.Lines.Add(newInterpolatedLine);
                    road.BoundingBoxes.Add(_mapLoader.ComputeBoundingBox(newInterpolatedLine));
                }
                road.LastInterpolationZoom = zoom;
            }
            BuildRoadQuadtree();
        }

        public int GetBoundingBoxRoadLineCount() => _visibleLines;
    }
}
