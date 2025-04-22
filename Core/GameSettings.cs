using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;

namespace cmetro25.Core // Oder z.B. cmetro25.Settings
{
    /// <summary>
    /// Zentrale Konfigurationsklasse für Spieleinstellungen und Konstanten.
    /// </summary>
    public static class GameSettings
    {
        #region Camera & Input Settings
        /// <summary>Minimaler Zoomfaktor der Kamera.</summary>
        public static readonly float CameraZoomMin = 0.35f;
        /// <summary>Maximaler Zoomfaktor der Kamera.</summary>
        //@@warn Zoom ab 31f löst Probleme mit der Tile-Generierung aus.
        public static readonly float CameraZoomMax = 31f;
        /// <summary>Multiplikator für Zoomänderungen per Mausrad.</summary>
        public static readonly float CameraZoomStepFactor = 1.1f;
        #endregion

        #region Map Loading & Coordinates
        /// <summary>Referenz X-Koordinate für die Transformation.</summary>
        public static readonly double ReferenceCoordX = 388418.7;
        /// <summary>Referenz Y-Koordinate für die Transformation.</summary>
        public static readonly double ReferenceCoordY = 5713965.5;
        /// <summary>Skalierungsfaktor für die Koordinatentransformation.</summary>
        public static readonly float CoordScaleFactor = 0.1f;
        #endregion

        #region Tile System Settings
        /// <summary>Größe der Kacheln in Pixeln.</summary>
        public static readonly int TileSize = 4096; // Kleinere Größe empfohlen zum Testen
        /// <summary>Anzahl der Kacheln als Puffer um den sichtbaren Bereich bei der Anforderung.</summary>
        public static readonly int TileGenerationBuffer = 1;
        /// <summary>Maximale Anzahl von Kacheln, die pro Update-Frame generiert werden.</summary>
        public static readonly int MaxTilesToGeneratePerFrame = 1;
        /// <summary>Maximaler berechneter Zoomlevel für das Tiling-System.</summary>
        public static readonly int MaxTileZoomLevel = 5;
        /// <summary>Minimale Kamerabewegung (in Bildschirm-Pixeln), um neue Kacheln anzufordern.</summary>
        public static readonly float CameraMoveThresholdForTileRequest = 100f;
        /// <summary>Minimale Zoomänderung (absolut), um neue Kacheln anzufordern.</summary>
        public static readonly float CameraZoomThresholdForTileRequest = 0.4f;
        /// <summary>QueryBuffer in Weltkoordinaten</summary>
        public static readonly float TileBoundsQueryBuffer = 30f;
        #endregion

        #region Road Rendering & Interpolation Settings
        /// <summary>Basis-Maximalabstand für die Straßeninterpolation.</summary>
        public static readonly float RoadBaseMaxInterpolationDistance = 0.5f;
        /// <summary>Basis-Überlappungsfaktor für Straßen (Interpolation & Rendering).</summary>
        public static readonly float RoadBaseOverlapFactor = 0.1f;
        /// <summary>Anzahl der Segmente für geglättete Straßenkurven.</summary>
        public static readonly int RoadCurveSegments = 16;
        /// <summary>Gibt an, ob Straßenglättung (Smoothing) verwendet werden soll.</summary>
        public static readonly bool UseRoadSmoothing = true;
        /// <summary>Minimaler Zoomunterschied, um eine Prüfung der Straßeninterpolation auszulösen.</summary>
        public static readonly float RoadInterpolationZoomThreshold = 0.05f;
        /// <summary>Zeit in Sekunden, die der Zoom stabil sein muss, bevor die Interpolation aktualisiert wird.</summary>
        public static readonly double RoadInterpolationUpdateDebounce = 0.2;
        /// <summary>Minimale Breite einer Straße in Pixeln (unabhängig vom Zoom).</summary>
        public static readonly float RoadMinPixelWidth = 0.5f;
        /// <summary>Maximale Breite einer Straße in Pixeln (unabhängig vom Zoom).</summary>
        public static readonly float RoadMaxPixelWidth = 15f;
        /// <summary>Pixel-Überlappung für nicht geglättete Liniensegmente im RoadRenderer.</summary>
        public const float RoadDrawPolylineOverlap = 0.07f;

        // Basisbreiten und Farben pro Straßentyp
        public static readonly float RoadWidthMotorway = 5f;
        public static readonly Color RoadColorMotorway = Color.DarkOrange;
        public static readonly float RoadWidthPrimaryTrunk = 4f;
        public static readonly Color RoadColorPrimaryTrunk = Color.DarkGoldenrod;
        public static readonly float RoadWidthSecondaryTertiary = 3f;
        public static readonly Color RoadColorSecondaryTertiary = Color.LightGray;
        public static readonly float RoadWidthResidentialUnclassified = 2f;
        public static readonly Color RoadColorResidentialUnclassified = Color.Gray;
        public static readonly float RoadWidthDefault = 1.5f;
        public static readonly Color RoadColorDefault = Color.DarkGray;
        #endregion

        #region District Rendering Settings
        /// <summary>Farbe für Distriktgrenzen.</summary>
        public static readonly Color DistrictBorderColor = new Color(143, 37, 37);
        /// <summary>Farbe für Distriktlabels.</summary>
        public static readonly Color DistrictLabelColor = new Color(143, 37, 37);
        /// <summary>Pixel-Überlappung für Distrikt-Polygongrenzen.</summary>
        public static readonly float DistrictPolygonOverlapFactor = 0.02f;
        #endregion

        #region Text Rendering Settings
        /// <summary>Minimale Skalierung für Text.</summary>
        public static readonly float MinTextScale = 0.1f;
        /// <summary>Maximale Skalierung für Text.</summary>
        public static readonly float MaxTextScale = 1.8f;
        /// <summary>Basis-Zoomfaktor, bei dem Text seine Originalgröße hat.</summary>
        public static readonly float BaseZoomForTextScaling = 1.0f;
        #endregion

        #region General Rendering Settings
        /// <summary>Hintergrundfarbe der Karte.</summary>
        public static readonly Color MapBackgroundColor = new Color(31, 31, 31);
        /// <summary>Hintergrundfarbe für generierte Kacheln.</summary>
        public static readonly Color TileBackgroundColor = new Color(31, 31, 31);
        public static readonly Color WaterBodyColor = new Color(113, 153, 235); //NOTE: Blau
        public static readonly Color RailColor = Color.Magenta; //NOTE: Grau
        #endregion

        #region Quadtree Settings
        /// <summary>Maximale Anzahl von Elementen pro Quadtree-Knoten.</summary>
        public static readonly int QuadtreeMaxItems = 4;
        /// <summary>Maximale Tiefe des Quadtrees.</summary>
        public static readonly int QuadtreeMaxDepth = 8;
        /// <summary>Puffer (in Weltkoordinaten) um die Straßen beim Erstellen des Quadtree-Root-Knotens.</summary>
        public static readonly float QuadtreeRoadServiceBuffer = 10f;
        /// <summary>Puffer (in Bildschirm-Pixeln) für die Quadtree-Abfrage bei der Straßeninterpolation.</summary>
        public static readonly float QuadtreeRoadServiceQueryPixelBuffer = 50f;
        #endregion

        #region Style Maps – zentral für alle Renderer
        public static readonly Dictionary<string, (float width, Color color)> RoadStyle
            = new()
            {
                ["motorway"] = (RoadWidthMotorway, RoadColorMotorway),
                ["primary"] = (RoadWidthPrimaryTrunk, RoadColorPrimaryTrunk),
                ["trunk"] = (RoadWidthPrimaryTrunk, RoadColorPrimaryTrunk),
                ["secondary"] = (RoadWidthSecondaryTertiary, RoadColorSecondaryTertiary),
                ["tertiary"] = (RoadWidthSecondaryTertiary, RoadColorSecondaryTertiary),
                ["residential"] = (RoadWidthResidentialUnclassified, RoadColorResidentialUnclassified),
                ["unclassified"] = (RoadWidthResidentialUnclassified, RoadColorResidentialUnclassified)
            };

        public static readonly Dictionary<string, (float width, Color color)> PolylineStyle
            = new()
            {
                ["river"] = (3f, WaterBodyColor),
                ["rail"] = (2f, RailColor)
            };

        public static readonly Color StationColor = Color.LightGreen; // allgemeine Punktfarbe
        #endregion

        #region File Paths
        // ... (WaterGeoJsonPath von oben) ...
        private static readonly string basePath = AppContext.BaseDirectory;
        public static readonly string WaterGeoJsonPath = Path.Combine(basePath, "GeoJson", "dortmund_lakes_finished.geojson");
        public static readonly string DistrictGeoJsonPath = Path.Combine(basePath, "GeoJson", "dortmund_boundaries_census_finished.geojson");
        public static readonly string RoadGeoJsonPath = Path.Combine(basePath, "GeoJson", "dortmund_roads_finished.geojson");
        public static readonly string RailsGeoJsonPath = Path.Combine(basePath, "GeoJson", "dortmund_rails_finished.geojson");
        public static readonly string RiversGeoJsonPath = Path.Combine(basePath, "GeoJson", "dortmund_rivers_finished.geojson");
        public static readonly string StationsGeoJsonPath = Path.Combine(basePath, "GeoJson", "dortmund_stations_finished.geojson");
        #endregion
    }
}
