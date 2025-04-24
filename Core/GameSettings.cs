// cmetro25.Core/GameSettings.cs – Einheitliche Settings mit Theme‑Paletten
// -----------------------------------------------------------------------------
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace cmetro25.Core
{
    /// <summary>
    /// Zentrale Spielkonstanten & Themes.
    /// Alle farbabhängigen Eigenschaften leiten sich aus der aktuellen
    /// Theme‑Palette ab (Light&nbsp;/ Dark). Umschaltbar zur Laufzeit.
    /// </summary>
    public static class GameSettings
    {
        /* =====================================================================
         * 1)  THEME PALETTEN
         * ===================================================================*/
        public record ThemePalette(
            Color MapBackground,
            Color TileBackground,
            Color WaterBody,
            Color Rail,
            Color DistrictBorder,
            Color DistrictLabel,
            Color Station);

        public static readonly ThemePalette LightPalette = new(
            MapBackground : new Color(245, 245, 245),          // sehr helles Grau
            TileBackground: new Color(245, 245, 245),
            WaterBody     : new Color(116, 174, 219),           // helles Blau
            Rail          : new Color(120, 120, 120),           // Mittelgrau
            DistrictBorder: new Color(120, 120, 120),
            DistrictLabel : Color.Black,
            Station       : new Color( 40, 140,  40));

        public static readonly ThemePalette DarkPalette = new(
            MapBackground : new Color(31, 31, 31),             // bestehendes Dunkelgrau
            TileBackground: new Color(31, 31, 31),
            WaterBody     : new Color(113, 153, 235),          // sattes Blau
            Rail          : Color.Gray,
            DistrictBorder: new Color(143, 37, 37),
            DistrictLabel : new Color(220, 220, 220),
            Station       : Color.LightGreen);

        // === helpers ===============================================================
        private static Color Lighten(Color c, float f) =>    // f > 1 → brighter
            new Color(Math.Clamp((int)(c.R * f), 0, 255),
                      Math.Clamp((int)(c.G * f), 0, 255),
                      Math.Clamp((int)(c.B * f), 0, 255));

        // === dynamic colours =======================================================
        public static Color RailDark => _current.Rail;              // old one
        public static Color RailLight => Lighten(_current.Rail, 1.7f);

        private static ThemePalette _current = DarkPalette;
        public static ThemePalette CurrentPalette => _current;
        public static bool IsDarkTheme => _current == DarkPalette;
        public static void ToggleTheme() => _current = IsDarkTheme ? LightPalette : DarkPalette;


        /* =====================================================================
         * 2)  FARBEIGENSCHAFTEN – leiten sich aus Palette ab
         * ===================================================================*/
        public static Color MapBackgroundColor   => _current.MapBackground;
        public static Color TileBackgroundColor  => _current.TileBackground;
        public static Color WaterBodyColor       => _current.WaterBody;
        public static Color RailColor           => _current.Rail;
        public static Color DistrictBorderColor => _current.DistrictBorder;
        public static Color DistrictLabelColor  => _current.DistrictLabel;
        public static Color StationColor        => _current.Station;


        /* =====================================================================
         * 3)  CAMERA & INPUT
         * ===================================================================*/
        public static readonly float CameraZoomMin = 0.35f;
        public static readonly float CameraZoomMax = 31f;   // >31 führt zu Tile‑Artefakten
        public static readonly float CameraZoomStepFactor = 1.1f;

        /* =====================================================================
         * 4)  MAP LOADING & KOORD‑TRANSFORM
         * ===================================================================*/
        public static readonly double ReferenceCoordX = 388_418.7;
        public static readonly double ReferenceCoordY = 5_713_965.5;
        public static readonly float  CoordScaleFactor = 0.1f;

        /* =====================================================================
         * 5)  TILE‑SYSTEM
         * ===================================================================*/
        public static readonly int   TileSize                       = 4096;
        public static readonly int   MaxTilesToGeneratePerFrame     = 1;
        public static readonly int   MaxTileZoomLevel               = 5;
        public static readonly float CameraMoveThresholdForTileRequest = 100f;
        public static readonly float CameraZoomThresholdForTileRequest = 0.4f;
        public static readonly float TileBoundsQueryBuffer          = 30f;

        /* =====================================================================
         * 6)  ROAD RENDERING (Breiten & Farben)
         * ===================================================================*/
        public const float RoadGlobalMinPx = 1f;
        public const float RoadGlobalMaxPx = 10f;

        public static readonly Dictionary<string,float> RoadTargetPx = new()
        {
            ["motorway"]     = 3f,
            ["primary"]      = 2f,
            ["trunk"]        = 2f,
            ["secondary"]    = 1.6f,
            ["tertiary"]     = 1f,
            ["residential"]  = 1f,
            ["unclassified"] = 1f
        };

        // ► Road‑Breiten & Farben (World, nicht Screen)
        public static readonly float  RoadWidthDefault               = 1f;
        public static readonly Color  RoadColorDefault               = new Color(200,  200,  200);
        public static readonly Color  RoadColorMotorway              = new Color(209,  96,  32);
        public static readonly Color  RoadColorPrimaryTrunk          = new Color(219, 144,  32);
        public static readonly Color  RoadColorSecondaryTertiary     = new Color(180, 180, 180);
        public static readonly Color  RoadColorResidential           = new Color(120, 120, 120);

        public static readonly Dictionary<string,(float width,Color color)> RoadStyle = new()
        {
            ["motorway"]     = (1f, RoadColorMotorway),
            ["primary"]      = (1f, RoadColorPrimaryTrunk),
            ["trunk"]        = (1f, RoadColorPrimaryTrunk),
            ["secondary"]    = (1f, RoadColorSecondaryTertiary),
            ["tertiary"]     = (1f, RoadColorSecondaryTertiary),
            ["residential"]  = (1f, RoadColorResidential),
            ["unclassified"] = (1f, RoadColorResidential)
        };

        public static readonly float RoadBaseMaxInterpolationDistance = 0.5f;
        public static readonly float RoadBaseOverlapFactor           = 0f;
        public static readonly int   RoadCurveSegments               = 16;
        public static readonly bool  UseRoadSmoothing                = true;
        public static readonly float RoadInterpolationZoomThreshold  = 0.05f;
        public static readonly double RoadInterpolationUpdateDebounce= 0.2;
        public static readonly float RoadMinPixelWidth               = 10f;
        public const float RoadDrawPolylineOverlap                  = 0.07f;

        /* =====================================================================
         * 7)  POLYLINE‑STYLES (rivers, rails, district)
         * ===================================================================*/
        public static readonly Dictionary<string,(float width,Color color)> PolylineStyle = new()
        {
            ["river"]    = (3f, WaterBodyColor),
            ["rail"]     = (1f, RailColor),
            ["district"] = (3f, DistrictBorderColor)
        };

        /* =====================================================================
         * 8)  QUADTREE
         * ===================================================================*/
        public static readonly int   QuadtreeMaxItems                 = 4;
        public static readonly int   QuadtreeMaxDepth                 = 8;
        public static readonly float QuadtreeRoadServiceBuffer        = 10f;
        public static readonly float QuadtreeRoadServiceQueryPixelBuffer = 50f;

        /* =====================================================================
         * 9)  TEXT SCALING
         * ===================================================================*/
        public static readonly float MinTextScale = 0.1f;
        public static readonly float MaxTextScale = 1.8f;
        public static readonly float BaseZoomForTextScaling = 1.0f;

        /* =====================================================================
         * 10) LAYER‑VISIBILITY (zur Laufzeit togglbar)
         * ===================================================================*/
        public static bool ShowDistricts   = true;
        public static bool ShowWaterBodies = true;
        public static bool ShowRails       = true;
        public static bool ShowRivers      = true;
        public static bool ShowRoads       = true;
        public static bool ShowStations    = true;

        /* =====================================================================
         * 11)  FILE PFADS (bleiben unverändert)
         * ===================================================================*/
        private static readonly string BasePath = AppContext.BaseDirectory;
        public static readonly string WaterGeoJsonPath    = System.IO.Path.Combine(BasePath, "GeoJSON", "dortmund_lakes_finished.geojson");
        public static readonly string DistrictGeoJsonPath = System.IO.Path.Combine(BasePath, "GeoJSON", "dortmund_boundaries_census_finished.geojson");
        public static readonly string RoadGeoJsonPath     = System.IO.Path.Combine(BasePath, "GeoJSON", "dortmund_roads_finished.geojson");
        public static readonly string RailsGeoJsonPath    = System.IO.Path.Combine(BasePath, "GeoJSON", "dortmund_rails_finished.geojson");
        public static readonly string RiversGeoJsonPath   = System.IO.Path.Combine(BasePath, "GeoJSON", "dortmund_rivers_finished.geojson");
        public static readonly string StationsGeoJsonPath = System.IO.Path.Combine(BasePath, "GeoJSON", "dortmund_stations_finished.geojson");

        #region Road draw hierarchy  (0 = ganz unten)
        public static readonly Dictionary<string, int> RoadDrawOrder = new()
        {
            ["residential"] = 0,
            ["unclassified"] = 0,

            ["tertiary"] = 1,
            ["secondary"] = 2,

            ["trunk"] = 3,
            ["primary"] = 3,

            ["motorway"] = 4          // liegt am höchsten
        };
        public const int DefaultRoadDrawPriority = 0;
        #endregion
    }
}
