using cmetro25.Models;
using cmetro25.Views;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MonoGame.Extended;

namespace cmetro25.Services
{
    public class MapLoader(MapCamera camera, float baseMaxDistance = 5f)
    {
        public float BaseMaxDistance { get; private set; } = baseMaxDistance;

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
                    districts.Add(district);
                }
            }
            return districts;
        }

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
                        if (feature.geometry != null)
                        {
                            if (feature.geometry.type == "MultiLineString")
                            {
                                var multiLineString = feature.geometry.CoordsAsMultiLineString();
                                if (multiLineString != null)
                                {
                                    foreach (var lineString in multiLineString)
                                        TransformAndInterpolateLineString(lineString, road);
                                }
                            }
                            else if (feature.geometry.type == "LineString")
                            {
                                var lineString = feature.geometry.CoordsAsLineString();
                                if (lineString != null)
                                    TransformAndInterpolateLineString(lineString, road);
                            }
                            else
                            {
                                Debug.WriteLine("[F!] Unsupported geometry.type in roads: " + feature.geometry.type);
                            }
                        }
                        roads.Add(road);
                    }
                }
            }
            return roads;
        }

        private void TransformAndInterpolateLineString(List<List<double>> lineString, Road road)
        {
            List<Vector2> convertedLine = new List<Vector2>();
            foreach (var point in lineString)
            {
                Vector2 transformedPoint = TransformCoordinates(point[0], point[1]);
                if (convertedLine.Count == 0 || convertedLine[^1] != transformedPoint)
                    convertedLine.Add(transformedPoint);
            }
            road.OriginalLines.Add(convertedLine);
            List<Vector2> interpolatedLine = InterpolateLineWithOverlap(convertedLine, BaseMaxDistance, camera.Zoom);
            road.Lines.Add(interpolatedLine);
            road.BoundingBoxes.Add(ComputeBoundingBox(interpolatedLine));
            road.LastInterpolationZoom = camera.Zoom;
        }

        public RectangleF ComputeBoundingBox(List<Vector2> points)
        {
            float minX = points[0].X, minY = points[0].Y;
            float maxX = points[0].X, maxY = points[0].Y;
            foreach (var pt in points)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y > maxY) maxY = pt.Y;
            }
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public List<Vector2> InterpolateLineWithOverlap(List<Vector2> points, float bMd, float zoom)
        {
            if (zoom < 0.5f)
                bMd *= 2f;

            float adjustedMaxDistance = bMd / zoom;

            if (points.Count < 2)
                return points;
            List<Vector2> interpolatedPoints = new List<Vector2> { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                float distance = Vector2.Distance(p1, p2);
                int segments = (int)Math.Ceiling(distance / adjustedMaxDistance);
                if (segments > 1)
                {
                    for (int j = 1; j < segments; j++)
                    {
                        float t = (float)j / segments;
                        Vector2 interpolatedPoint = Vector2.Lerp(p1, p2, t);
                        interpolatedPoints.Add(interpolatedPoint);
                    }
                }
            }
            if (points.Count >= 2)
            {
                Vector2 direction = points[^1] - points[^2];
                float baseOverlapFactor = 0.1f;
                float adjustedOverlapFactor = baseOverlapFactor / zoom;
                Vector2 overlappedP2 = points[^1] + direction * adjustedOverlapFactor;
                interpolatedPoints.Add(overlappedP2);
            }
            return interpolatedPoints;
        }

        private Vector2 TransformCoordinates(double x, double y)
        {
            double referenceX = 388418.7;
            double referenceY = 5713965.5;
            double deltaX = x - referenceX;
            double deltaY = y - referenceY;
            float scaleFactor = 0.1f;
            float transformedX = (float)(deltaX * scaleFactor);
            float transformedY = (float)(-deltaY * scaleFactor);
            return new Vector2(transformedX, transformedY);
        }
    }
}
