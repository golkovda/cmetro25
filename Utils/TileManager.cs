using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent; // NEU: Für ConcurrentQueue/Dictionary
using cmetro25.Models;
using cmetro25.Services;
using cmetro25.Views;
using cmetro25.Core;

namespace cmetro25.Utils
{
    /// <summary>
    /// Verwaltet die Kachelgenerierung und das Caching für die Kartendarstellung.
    /// </summary>
    public class TileManager
    {
        // --- Bestehende Member ---
        private GraphicsDevice _graphicsDevice;
        private List<District> _districts;
        private RoadService _roadService;
        private MapLoader _mapLoader;
        private int _tileSize;
        private RectangleF _mapBounds;
        private Dictionary<(int zoom, int tileX, int tileY), RenderTarget2D> _tileCache;
        private DistrictRenderer _districtRenderer;
        private RoadRenderer _roadRenderer;
        private List<WaterBody> _waterBodies;
        private WaterBodyRenderer _waterBodyRenderer;
        private List<PolylineElement> _rivers, _rails;
        private List<PointElement> _stations;
        private PolylineRenderer _polyRenderer;
        private StationRenderer _stationRenderer;

        // --- NEU: Für Queued Generation ---
        private readonly ConcurrentQueue<(int zoom, int x, int y)> _tileGenerationQueue = new();
        // ConcurrentDictionary wird als thread-sicheres Set verwendet, um doppelte Einträge in der Queue zu vermeiden. Der bool-Wert ist irrelevant.
        private readonly ConcurrentDictionary<(int zoom, int x, int y), bool> _queuedOrProcessingKeys = new();
        private Task _tileRequestTask = Task.CompletedTask; // Task zur Verwaltung der Anfragen

        /// <summary>
        /// Initialisiert eine neue Instanz der <see cref="TileManager"/> Klasse.
        /// </summary>
        /// <param name="graphicsDevice">Das GraphicsDevice zum Rendern der Kacheln.</param>
        /// <param name="districts">Die Liste der Distrikte.</param>
        /// <param name="roadService">Der RoadService zum Verwalten der Straßen.</param>
        /// <param name="mapLoader">Der MapLoader zum Laden der Kartendaten.</param>
        /// <param name="dr">Der DistrictRenderer zum Rendern der Distrikte.</param>
        /// <param name="rr">Der RoadRenderer zum Rendern der Straßen.</param>
        /// <param name="tileSize">Die Größe der Kacheln in Pixeln.</param>
        public TileManager(GraphicsDevice graphicsDevice, List<District> districts, List<WaterBody> waterBodies, RoadService roadService, MapLoader mapLoader, DistrictRenderer dr, RoadRenderer rr, WaterBodyRenderer wbRenderer, int tileSize, Texture2D pixelTexture, List<PolylineElement> rivers, List<PolylineElement> rails, List<PointElement> stations)
        {
            _graphicsDevice = graphicsDevice;
            _districts = districts;
            _roadService = roadService;
            _mapLoader = mapLoader;
            _tileSize = tileSize;
            _tileCache = new Dictionary<(int, int, int), RenderTarget2D>();
            _districtRenderer = dr;
            _roadRenderer = rr;
            _waterBodies = waterBodies;
            _waterBodyRenderer = wbRenderer;
            _polyRenderer = new PolylineRenderer(pixelTexture);
            _stationRenderer = new StationRenderer(pixelTexture);
            _rivers = rivers;
            _rails = rails;
            _stations = stations;
            _mapBounds = ComputeGlobalMapBounds();
            Debug.WriteLine($"Global Map Bounds: {_mapBounds}");
        }

        /// <summary>
        /// Ermittelt die globalen Karten­grenzen, indem alle bislang
        /// bekannten Geometrien (Distrikte, Straßen, Wasser, Flüsse,
        /// Gleise und Stationen) berücksichtigt werden.
        /// </summary>
        private RectangleF ComputeGlobalMapBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasData = false;

            /* ---------- kleine Inliner‑Hilfsfunktionen ---------- */
            void Extend(Vector2 p)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                hasData = true;
            }

            void ExtendRect(in RectangleF r)
            {
                if (r.IsEmpty) return;
                Extend(new Vector2(r.Left, r.Top));
                Extend(new Vector2(r.Right, r.Bottom));
            }
            /* ---------------------------------------------------- */

            // Distrikte
            foreach (var d in _districts)
                ExtendRect(d.BoundingBox);

            // Straßen  –  am einfachsten über den Quadtree, falls schon vorhanden
            var roadTree = _roadService?.GetQuadtree();
            if (roadTree != null && !roadTree.Bounds.IsEmpty)
                ExtendRect(roadTree.Bounds);

            // Wasserflächen
            foreach (var wb in _waterBodies)
                ExtendRect(wb.BoundingBox);

            // Flüsse & Gleise
            foreach (var rv in _rivers)
                foreach (var box in rv.BoundingBoxes)
                    ExtendRect(box);

            foreach (var rl in _rails)
                foreach (var box in rl.BoundingBoxes)
                    ExtendRect(box);

            // Stationen (Punkte)
            foreach (var st in _stations)
                Extend(st.Position);

            /* ---------- Fallback, falls gar nichts gefunden wurde ---------- */
            if (!hasData)
                return new RectangleF(0, 0, 1000, 1000);

            /* ---------- Quadrat + Puffer, wie zuvor ---------- */
            float width = maxX - minX;
            float height = maxY - minY;

            if (width > height) { float diff = width - height; minY -= diff * 0.5f; height = width; }
            if (height > width) { float diff = height - width; minX -= diff * 0.5f; width = height; }

            const float buffer = 50f;
            minX -= buffer; minY -= buffer;
            width += buffer * 2; height += buffer * 2;

            return new RectangleF(minX, minY,
                                  Math.Max(1, width),
                                  Math.Max(1, height));
        }
        /// <summary>
        /// Liefert ein vorhandenes Tile oder null, wenn es noch nicht generiert wurde.
        /// </summary>
        /// <param name="zoom">Der Zoomlevel der Kachel.</param>
        /// <param name="tileX">Die X-Koordinate der Kachel.</param>
        /// <param name="tileY">Die Y-Koordinate der Kachel.</param>
        /// <returns>Das RenderTarget2D der Kachel oder null, wenn es noch nicht generiert wurde.</returns>
        public RenderTarget2D GetExistingTile(int zoom, int tileX, int tileY)
        {
            var key = (zoom, tileX, tileY);
            _tileCache.TryGetValue(key, out RenderTarget2D tileRT);
            return tileRT;
        }

        /// <summary>
        /// Generiert eine Kachel. Diese Methode sollte nur im Hauptthread aufgerufen werden.
        /// </summary>
        /// <param name="zoom">Der Zoomlevel der Kachel.</param>
        /// <param name="tileX">Die X-Koordinate der Kachel.</param>
        /// <param name="tileY">Die Y-Koordinate der Kachel.</param>
        /// <returns>Das generierte RenderTarget2D der Kachel.</returns>
        private RenderTarget2D GenerateTile(int zoom, int tileX, int tileY)
        {
            var key = (zoom, tileX, tileY);

            // Doppelte Prüfung: Ist es vielleicht doch schon im Cache?
            if (_tileCache.ContainsKey(key))
            {
                // Aus dem "queued" Set entfernen, falls es noch drin war
                _queuedOrProcessingKeys.TryRemove(key, out _);
                return _tileCache[key];
            }

            RenderTarget2D tileRT = null;
            try
            {
                // --- Grafikoperationen: Nur im Hauptthread! ---
                tileRT = new RenderTarget2D(
                    _graphicsDevice, _tileSize, _tileSize, false,
                    SurfaceFormat.Color, DepthFormat.None, 0,
                    RenderTargetUsage.DiscardContents); // DiscardContents ist oft performanter

                _graphicsDevice.SetRenderTarget(tileRT);
                _graphicsDevice.Clear(GameSettings.TileBackgroundColor);

                int numTiles = 1 << zoom;
                if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0)
                    throw new InvalidOperationException("Invalid map bounds");

                float tileWorldWidth = _mapBounds.Width / numTiles;
                float tileWorldHeight = _mapBounds.Height / numTiles;
                float tileWorldX = _mapBounds.X + tileX * tileWorldWidth;
                float tileWorldY = _mapBounds.Y + tileY * tileWorldHeight;
                RectangleF tileWorldBounds = new RectangleF(tileWorldX, tileWorldY, tileWorldWidth, tileWorldHeight);
                MapCamera tileCamera = new MapCamera(_tileSize, _tileSize);
                tileCamera.Zoom = _tileSize / tileWorldWidth;
                tileCamera.CenterOn(new Vector2(tileWorldX + tileWorldWidth / 2, tileWorldY + tileWorldHeight / 2));

                // --- NEU: Dynamischer Query-Puffer ---
                // Berechne den Puffer basierend auf der maximalen Straßenbreite in Pixeln
                // und dem Zoom der Tile-Kamera, plus einem festen Puffer für Smoothing.
                float maxThicknessInWorld =
                    (GameSettings.RoadMaxPixelWidth / tileCamera.Zoom); // Maximale Dicke in Weltkoordinaten
                float thicknessBuffer = maxThicknessInWorld / 2.0f; // Hälfte für jede Seite
                float smoothingBuffer = 15f; // Fester Puffer für Smoothing-Abweichung (Weltkoordinaten, anpassen!)
                float queryBuffer = thicknessBuffer + smoothingBuffer;

                // Stelle sicher, dass der Puffer nicht negativ wird (falls Zoom extrem hoch ist)
                queryBuffer = Math.Max(10f, queryBuffer); // Mindestens 5 Welt-Einheiten Puffer

                RectangleF queryBounds = new RectangleF(
                    tileWorldBounds.X - queryBuffer,
                    tileWorldBounds.Y - queryBuffer,
                    tileWorldBounds.Width + 2 * queryBuffer,
                    tileWorldBounds.Height + 2 * queryBuffer);
                // --- Ende NEU ---

                // Verwende queryBounds für die Abfragen
                List<District> districtsInTile = QueryDistrictsInBounds(queryBounds);
                List<Road> roadsInTile = _roadService.GetQuadtree()?.Query(queryBounds).Distinct().ToList() ??
                                         new List<Road>();
                List<WaterBody> waterBodiesInTile = QueryWaterBodiesInBounds(queryBounds); // NEU
                var riversInTile = _rivers.Where(r => r.BoundingBoxes.Any(b => b.Intersects(queryBounds))).ToList();
                var railsInTile = _rails.Where(r => r.BoundingBoxes.Any(b => b.Intersects(queryBounds))).ToList();
                var stationsInTile = _stations.Where(s => queryBounds.Contains(s.Position)).ToList();

                // --- Wasserflächen direkt mit GraphicsDevice zeichnen ---
                // Setze RenderStates für BasicEffect (optional, aber zur Sicherheit)
                _graphicsDevice.BlendState = BlendState.Opaque; // Wasser ist opak
                _graphicsDevice.DepthStencilState = DepthStencilState.None;
                _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                _waterBodyRenderer.Draw(waterBodiesInTile, tileCamera);

                // --- NEU: GraphicsDevice-Zustände für SpriteBatch zurücksetzen ---
                _graphicsDevice.BlendState = BlendState.AlphaBlend; // Standard für SpriteBatch
                _graphicsDevice.DepthStencilState = DepthStencilState.None; // Standard für SpriteBatch (2D)
                _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise; // Standard für SpriteBatch
                _graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp; // Standard-Sampler für SpriteBatch (wird von Begin oft überschrieben, aber schadet nicht)
                                                                             // --- Ende NEU ---

                using (SpriteBatch sb = new SpriteBatch(_graphicsDevice))
                {
                    // Zeichne Distrikt-Polygone
                    sb.Begin(transformMatrix: tileCamera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
                    _districtRenderer.DrawPolygons(sb, districtsInTile, tileCamera);
                    sb.End();

                    // Zeichne Straßen
                    sb.Begin(transformMatrix: tileCamera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
                    _roadRenderer.Draw(sb, roadsInTile, tileWorldBounds, tileCamera);
                    sb.End();

                    // Linien
                    sb.Begin(transformMatrix: tileCamera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
                    _polyRenderer.Draw(sb, riversInTile, tileWorldBounds, tileCamera);
                    _polyRenderer.Draw(sb, railsInTile, tileWorldBounds, tileCamera);
                    sb.End();

                    // Stationen (über alles drüber)
                    sb.Begin(transformMatrix: tileCamera.TransformMatrix);
                    _stationRenderer.Draw(sb, stationsInTile, tileCamera);
                    sb.End();

                    // Zeichne Distrikt-Labels (über Straßen)
                    _districtRenderer.DrawLabels(sb, districtsInTile, tileCamera); // Hat eigenes Begin/End
                }

                _graphicsDevice.SetRenderTarget(null);
                // --- Ende Grafikoperationen ---

                // Füge das erfolgreich generierte Tile dem Cache hinzu
                _tileCache[key] = tileRT;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to generate tile {key}: {ex.Message}");
                // Dispose RenderTarget, wenn bei Erstellung/Zeichnung Fehler auftrat
                tileRT?.Dispose();
                tileRT = null; // Gib null zurück im Fehlerfall
            }
            finally
            {
                // Entferne den Schlüssel aus dem "queued" Set, egal ob erfolgreich oder nicht,
                // damit ein erneuter Versuch gestartet werden kann, falls fehlgeschlagen.
                _queuedOrProcessingKeys.TryRemove(key, out _);
                // Sicherstellen, dass RenderTarget zurückgesetzt ist
                if (_graphicsDevice.GetRenderTargets().Length > 0)
                {
                    _graphicsDevice.SetRenderTarget(null);
                }
            }

            return tileRT;
        }

        /// <summary>
        /// Startet einen Task, um benötigte Tiles zu identifizieren und in die Queue zu legen.
        /// </summary>
        /// <param name="area">Der Bereich, für den Tiles generiert werden sollen.</param>
        /// <param name="zoomLevel">Der Zoomlevel der Tiles.</param>
        public void RequestTileGeneration(RectangleF area, int zoomLevel)
        {
            // Nur einen Request-Task gleichzeitig laufen lassen
            if (!_tileRequestTask.IsCompleted) return;

            _tileRequestTask = Task.Run(() =>
            {
                if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0) return;

                int numTiles = 1 << zoomLevel;
                float tileWorldWidth = _mapBounds.Width / numTiles;
                float tileWorldHeight = _mapBounds.Height / numTiles;

                // Berechne benötigte Tile-Indizes mit Puffer
                int buf = GameSettings.TileGenerationBuffer;
                int minTileX = Math.Max(0, (int)Math.Floor((area.Left - _mapBounds.Left) / tileWorldWidth) - buf);
                int maxTileX = Math.Min(numTiles - 1, (int)Math.Ceiling((area.Right - _mapBounds.Left) / tileWorldWidth) + buf);
                int minTileY = Math.Max(0, (int)Math.Floor((area.Top - _mapBounds.Top) / tileWorldHeight) - buf);
                int maxTileY = Math.Min(numTiles - 1, (int)Math.Ceiling((area.Bottom - _mapBounds.Top) / tileWorldHeight) + buf);

                int requestedCount = 0;
                for (int ty = minTileY; ty <= maxTileY; ty++)
                {
                    for (int tx = minTileX; tx <= maxTileX; tx++)
                    {
                        var key = (zoomLevel, tx, ty);

                        // Prüfe Cache UND Queue, bevor Schlüssel hinzugefügt wird
                        if (!_tileCache.ContainsKey(key) && !_queuedOrProcessingKeys.ContainsKey(key))
                        {
                            // Füge zum Set hinzu, um Duplikate zu verhindern, bevor es in die Queue geht
                            if (_queuedOrProcessingKeys.TryAdd(key, true))
                            {
                                _tileGenerationQueue.Enqueue(key);
                                requestedCount++;
                            }
                        }
                    }
                }
                if (requestedCount > 0)
                    Debug.WriteLine($"Requested {requestedCount} new tiles for zoom {zoomLevel}. Queue size: {_tileGenerationQueue.Count}");
            });
        }

        /// <summary>
        /// Verarbeitet die Tile-Generierungs-Queue im Hauptthread. Sollte in CMetro.Update aufgerufen werden.
        /// </summary>
        public void ProcessTileGenerationQueue()
        {
            int processedCount = 0;
            while (processedCount < GameSettings.MaxTilesToGeneratePerFrame && _tileGenerationQueue.TryDequeue(out var keyToGenerate))
            {
                // Erneute Prüfung im Hauptthread, ob es zwischenzeitlich generiert wurde
                if (!_tileCache.ContainsKey(keyToGenerate))
                {
                    GenerateTile(keyToGenerate.zoom, keyToGenerate.x, keyToGenerate.y);
                    processedCount++;
                }
                else
                {
                    // War schon im Cache, nur aus dem queued Set entfernen
                    _queuedOrProcessingKeys.TryRemove(keyToGenerate, out _);
                }
            }
        }

        /// <summary>
        /// Zeichnet die vorhandenen Tiles basierend auf der aktuellen Kameraposition und dem Zoomlevel.
        /// </summary>
        /// <param name="spriteBatch">Das SpriteBatch zum Zeichnen der Tiles.</param>
        /// <param name="camera">Die aktuelle Kamera.</param>
        /// <param name="zoomLevel">Der Zoomlevel der Tiles.</param>
        public void DrawTiles(SpriteBatch spriteBatch, MapCamera camera, int zoomLevel)
        {
            if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0) return;

            RectangleF visibleWorld = camera.BoundingRectangle;
            int numTiles = 1 << zoomLevel;
            float tileWorldWidth = _mapBounds.Width / numTiles;
            float tileWorldHeight = _mapBounds.Height / numTiles;

            int minTileX = Math.Max(0, (int)Math.Floor((visibleWorld.Left - _mapBounds.Left) / tileWorldWidth));
            int maxTileX = Math.Min(numTiles - 1, (int)Math.Ceiling((visibleWorld.Right - _mapBounds.Left) / tileWorldWidth));
            int minTileY = Math.Max(0, (int)Math.Floor((visibleWorld.Top - _mapBounds.Top) / tileWorldHeight));
            int maxTileY = Math.Min(numTiles - 1, (int)Math.Ceiling((visibleWorld.Bottom - _mapBounds.Top) / tileWorldHeight));

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    // Hole vorhandenes Tile oder null
                    RenderTarget2D tileTexture = GetExistingTile(zoomLevel, tx, ty);

                    if (tileTexture != null && !tileTexture.IsDisposed)
                    {
                        // Zeichne das vorhandene Tile
                        float tileWorldX = _mapBounds.X + tx * tileWorldWidth;
                        float tileWorldY = _mapBounds.Y + ty * tileWorldHeight;
                        spriteBatch.Draw(tileTexture,
                                         new Vector2(tileWorldX, tileWorldY),
                                         null, Color.White, 0f, Vector2.Zero,
                                         new Vector2(tileWorldWidth / _tileSize, tileWorldHeight / _tileSize),
                                         SpriteEffects.None, 0f);
                    }
                }
            }
        }

        /// <summary>
        /// Leert den Tile-Cache und gibt die Ressourcen frei.
        /// </summary>
        public void ClearCache()
        {
            // Leere auch die Queue und das Set
            while (_tileGenerationQueue.TryDequeue(out _)) { }
            _queuedOrProcessingKeys.Clear();

            foreach (var tile in _tileCache.Values)
            {
                tile.Dispose();
            }
            _tileCache.Clear();
            Debug.WriteLine("Tile cache, queue, and processing keys cleared.");
        }

        /// <summary>
        /// Gibt die aktuelle Größe der Generierungsqueue zurück (für Debug/UI).
        /// </summary>
        /// <returns>Die Größe der Generierungsqueue.</returns>
        public int GetGenerationQueueSize()
        {
            return _tileGenerationQueue.Count;
        }

        /// <summary>
        /// Fragt die Distrikte ab, die sich innerhalb der angegebenen Grenzen befinden.
        /// </summary>
        /// <param name="bounds">Die Grenzen, innerhalb derer die Distrikte abgefragt werden sollen.</param>
        /// <returns>Eine Liste der Distrikte innerhalb der angegebenen Grenzen.</returns>
        private List<District> QueryDistrictsInBounds(RectangleF bounds)
        {
            List<District> result = new List<District>();
            foreach (var district in _districts)
            {
                if (district.BoundingBox.Intersects(bounds))
                {
                    result.Add(district);
                }
            }
            return result;
        }

        private List<WaterBody> QueryWaterBodiesInBounds(RectangleF bounds)
        {
            List<WaterBody> result = new List<WaterBody>();
            if (_waterBodies == null) return result; // Sicherheitscheck

            foreach (var wb in _waterBodies)
            {
                // Prüfe, ob die BoundingBox die Abfragegrenzen schneidet
                if (wb.BoundingBox.Intersects(bounds))
                {
                    result.Add(wb);
                }
            }
            return result;
        }
    }
}