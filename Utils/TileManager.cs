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

namespace cmetro25.Utils
{
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
        private Color _tileBackgroundColor = new Color(31, 31, 31);

        // --- NEU: Für Queued Generation ---
        private readonly ConcurrentQueue<(int zoom, int x, int y)> _tileGenerationQueue = new();
        // ConcurrentDictionary wird als thread-sicheres Set verwendet, um doppelte Einträge in der Queue zu vermeiden. Der bool-Wert ist irrelevant.
        private readonly ConcurrentDictionary<(int zoom, int x, int y), bool> _queuedOrProcessingKeys = new();
        private Task _tileRequestTask = Task.CompletedTask; // Task zur Verwaltung der Anfragen
        private const int MaxTilesToGeneratePerFrame = 2; // Wieviele Tiles max. pro Update generiert werden

        public TileManager(GraphicsDevice graphicsDevice, List<District> districts, RoadService roadService, MapLoader mapLoader, DistrictRenderer dr, RoadRenderer rr, int tileSize = 512) // TileSize angepasst
        {
            _graphicsDevice = graphicsDevice;
            _districts = districts;
            _roadService = roadService;
            _mapLoader = mapLoader;
            _tileSize = tileSize;
            _tileCache = new Dictionary<(int, int, int), RenderTarget2D>();
            _districtRenderer = dr;
            _roadRenderer = rr;
            _mapBounds = ComputeGlobalMapBounds();
            Debug.WriteLine($"Global Map Bounds: {_mapBounds}");
        }

        private RectangleF ComputeGlobalMapBounds()
        {
            // ... (Implementierung wie zuvor) ...
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasBounds = false;

            foreach (var district in _districts)
            {
                if (!district.BoundingBox.IsEmpty)
                {
                    if (district.BoundingBox.Left < minX) minX = district.BoundingBox.Left;
                    if (district.BoundingBox.Top < minY) minY = district.BoundingBox.Top;
                    if (district.BoundingBox.Right > maxX) maxX = district.BoundingBox.Right;
                    if (district.BoundingBox.Bottom > maxY) maxY = district.BoundingBox.Bottom;
                    hasBounds = true;
                }
            }

            var quadtree = _roadService.GetQuadtree();
            if (quadtree != null && !quadtree.Bounds.IsEmpty)
            {
                RectangleF roadBounds = quadtree.Bounds;
                if (roadBounds.Left < minX) minX = roadBounds.Left;
                if (roadBounds.Top < minY) minY = roadBounds.Top;
                if (roadBounds.Right > maxX) maxX = roadBounds.Right;
                if (roadBounds.Bottom > maxY) maxY = roadBounds.Bottom;
                hasBounds = true;
            }

            if (!hasBounds) return new RectangleF(0, 0, 1000, 1000);

            float width = maxX - minX;
            float height = maxY - minY;
            if (width > height) { float diff = width - height; minY -= diff * 0.5f; height = width; }
            else if (height > width) { float diff = height - width; minX -= diff * 0.5f; width = height; }

            float buffer = 50f;
            minX -= buffer; minY -= buffer; width += 2 * buffer; height += 2 * buffer;

            return new RectangleF(minX, minY, Math.Max(1, width), Math.Max(1, height));
        }

        // Liefert ein vorhandenes Tile oder null, wenn es noch nicht generiert wurde.
        // Diese Methode wird jetzt nur noch von DrawTiles aufgerufen.
        public RenderTarget2D GetExistingTile(int zoom, int tileX, int tileY)
        {
            var key = (zoom, tileX, tileY);
            _tileCache.TryGetValue(key, out RenderTarget2D tileRT);
            // Optional: Prüfen, ob tileRT disposed wurde (sollte nicht passieren, wenn Cache korrekt verwaltet wird)
            // if (tileRT != null && tileRT.IsDisposed) return null;
            return tileRT;
        }

        // Generiert ein Tile (nur im Hauptthread aufrufen!)
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
                _graphicsDevice.Clear(_tileBackgroundColor);

                int numTiles = 1 << zoom;
                if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0) throw new InvalidOperationException("Invalid map bounds");

                float tileWorldWidth = _mapBounds.Width / numTiles;
                float tileWorldHeight = _mapBounds.Height / numTiles;
                float tileWorldX = _mapBounds.X + tileX * tileWorldWidth;
                float tileWorldY = _mapBounds.Y + tileY * tileWorldHeight;
                RectangleF tileWorldBounds = new RectangleF(tileWorldX, tileWorldY, tileWorldWidth, tileWorldHeight);

                MapCamera tileCamera = new MapCamera(_tileSize, _tileSize);
                tileCamera.Zoom = _tileSize / tileWorldWidth;
                tileCamera.CenterOn(new Vector2(tileWorldX + tileWorldWidth / 2, tileWorldY + tileWorldHeight / 2));

                List<District> districtsInTile = QueryDistrictsInBounds(tileWorldBounds);
                List<Road> roadsInTile = _roadService.GetQuadtree()?.Query(tileWorldBounds).Distinct().ToList() ?? new List<Road>();

                using (SpriteBatch sb = new SpriteBatch(_graphicsDevice))
                {
                    // Zeichne Polygone
                    sb.Begin(transformMatrix: tileCamera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
                    _districtRenderer.DrawPolygons(sb, districtsInTile, tileCamera);
                    sb.End();

                    // Zeichne Straßen
                    sb.Begin(transformMatrix: tileCamera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
                    _roadRenderer.Draw(sb, roadsInTile, tileWorldBounds, tileCamera);
                    sb.End();

                    // Zeichne Labels (separat, über Straßen)
                    _districtRenderer.DrawLabels(sb, districtsInTile, tileCamera); // Hat eigenes Begin/End
                }

                _graphicsDevice.SetRenderTarget(null);
                // --- Ende Grafikoperationen ---

                // Füge das erfolgreich generierte Tile dem Cache hinzu
                _tileCache[key] = tileRT;
                // Debug.WriteLine($"Generated Tile: {key}");

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


        // NEU: Startet einen Task, um benötigte Tiles zu identifizieren und in die Queue zu legen
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
                int buf = 1; // Puffer von 1 Kachel um den sichtbaren Bereich
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

        // NEU: Verarbeitet die Queue im Hauptthread (in CMetro.Update aufrufen)
        public void ProcessTileGenerationQueue()
        {
            int processedCount = 0;
            while (processedCount < MaxTilesToGeneratePerFrame && _tileGenerationQueue.TryDequeue(out var keyToGenerate))
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


        public void DrawTiles(SpriteBatch spriteBatch, MapCamera camera, int zoomLevel)
        {
            // ... (Berechnung von visibleWorld, numTiles, tileWorldWidth/Height, min/max TileX/Y wie zuvor) ...
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
                    else
                    {
                        // Optional: Zeichne einen Platzhalter oder tue nichts, wenn Tile noch nicht bereit ist
                        // Oder: Fordere das Tile erneut an (sollte durch RequestTileGeneration abgedeckt sein)
                        // float tileWorldX = _mapBounds.X + tx * tileWorldWidth;
                        // float tileWorldY = _mapBounds.Y + ty * tileWorldHeight;
                        // DrawPlaceholderTile(spriteBatch, tileWorldX, tileWorldY, tileWorldWidth, tileWorldHeight);
                    }
                }
            }
        }

        // Optional: Hilfsmethode für Platzhalter
        private void DrawPlaceholderTile(SpriteBatch spriteBatch, float x, float y, float width, float height)
        {
            // Zeichne z.B. einen einfachen Rahmen oder eine Füllfarbe
            // Denke daran, dass dies innerhalb von spriteBatch.Begin/End mit Kameratransformation aufgerufen wird
            // Beispiel: Zeichne einen dünnen grauen Rahmen
            // _districtRenderer.DrawRectangle(spriteBatch, new RectangleF(x, y, width, height), Color.DarkGray, 1f / camera.Zoom); // Annahme: Es gibt eine DrawRectangle Methode
        }


        private List<District> QueryDistrictsInBounds(RectangleF bounds)
        {
            // ... (Implementierung wie zuvor) ...
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

        // NEU: Gibt die aktuelle Größe der Generierungsqueue zurück (für Debug/UI)
        public int GetGenerationQueueSize()
        {
            return _tileGenerationQueue.Count;
        }
    }
}