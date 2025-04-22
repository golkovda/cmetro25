using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using cmetro25.Core;
using cmetro25.Models;
using cmetro25.Services;
using cmetro25.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
// NEU: Für ConcurrentQueue/Dictionary

namespace cmetro25.Utils;

/// <summary>
///     Verwaltet die Kachelgenerierung und das Caching für die Kartendarstellung.
/// </summary>
public class TileManager
{
    // --- Bestehende Member ---
    private readonly GraphicsDevice _graphicsDevice;
    private readonly List<District> _districts;
    private readonly RoadService _roadService;
    private MapLoader _mapLoader;
    private readonly int _tileSize;
    private RectangleF _mapBounds;
    private readonly Dictionary<(int zoom, int tileX, int tileY), RenderTarget2D> _tileCache;
    private readonly List<WaterBody> _waterBodies;
    private readonly List<PolylineElement> _rivers;
    private readonly List<PolylineElement> _rails;
    private readonly List<PointElement> _stations;

    private readonly PolygonRenderer _polygonRenderer;
    private readonly PolylineRenderer _polylineRenderer;
    private readonly PointRenderer _pointRenderer;

    // --- NEU: Für Queued Generation ---
    private readonly ConcurrentQueue<(int zoom, int x, int y)> _tileGenerationQueue = new();

    // ConcurrentDictionary wird als thread-sicheres Set verwendet, um doppelte Einträge in der Queue zu vermeiden. Der bool-Wert ist irrelevant.
    private readonly ConcurrentDictionary<(int zoom, int x, int y), bool> _queuedOrProcessingKeys = new();
    private Task _tileRequestTask = Task.CompletedTask; // Task zur Verwaltung der Anfragen

    public TileManager(GraphicsDevice graphicsDevice,
        List<District> districts,
        List<WaterBody> waterBodies,
        RoadService roadService,
        MapLoader mapLoader,
        PolygonRenderer polyRenderer,
        PolylineRenderer lineRenderer,
        PointRenderer pointRenderer,
        int tileSize,
        List<PolylineElement> rivers,
        List<PolylineElement> rails,
        List<PointElement> stations)
    {
        _graphicsDevice = graphicsDevice;
        _districts = districts;
        _waterBodies = waterBodies;
        _roadService = roadService;
        _mapLoader = mapLoader;
        _polygonRenderer = polyRenderer;
        _polylineRenderer = lineRenderer;
        _pointRenderer = pointRenderer;
        _tileSize = tileSize;
        _rivers = rivers;
        _rails = rails;
        _stations = stations;

        _tileCache = new Dictionary<(int zoom, int tileX, int tileY), RenderTarget2D>();
        _mapBounds = ComputeGlobalMapBounds();
    }

    public int LastPolylineSegmentCount
        => _polylineRenderer.VisibleSegmentsLastDraw;

    /// <summary>
    ///     Ermittelt die globalen Karten­grenzen, indem alle bislang
    ///     bekannten Geometrien (Distrikte, Straßen, Wasser, Flüsse,
    ///     Gleise und Stationen) berücksichtigt werden.
    /// </summary>
    private RectangleF ComputeGlobalMapBounds()
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        var hasData = false;

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
        var width = maxX - minX;
        var height = maxY - minY;

        if (width > height)
        {
            var diff = width - height;
            minY -= diff * 0.5f;
            height = width;
        }

        if (height > width)
        {
            var diff = height - width;
            minX -= diff * 0.5f;
            width = height;
        }

        const float buffer = 50f;
        minX -= buffer;
        minY -= buffer;
        width += buffer * 2;
        height += buffer * 2;

        return new RectangleF(minX, minY,
            Math.Max(1, width),
            Math.Max(1, height));
    }

    /// <summary>
    ///     Liefert ein vorhandenes Tile oder null, wenn es noch nicht generiert wurde.
    /// </summary>
    /// <param name="zoom">Der Zoomlevel der Kachel.</param>
    /// <param name="tileX">Die X-Koordinate der Kachel.</param>
    /// <param name="tileY">Die Y-Koordinate der Kachel.</param>
    /// <returns>Das RenderTarget2D der Kachel oder null, wenn es noch nicht generiert wurde.</returns>
    public RenderTarget2D GetExistingTile(int zoom, int tileX, int tileY)
    {
        var key = (zoom, tileX, tileY);
        _tileCache.TryGetValue(key, out var tileRT);
        return tileRT;
    }

    private RenderTarget2D GenerateTile(int zoom, int tileX, int tileY)
    {
        var key = (zoom, tileX, tileY);
        if (_tileCache.ContainsKey(key))
        {
            _queuedOrProcessingKeys.TryRemove(key, out _);
            return _tileCache[key];
        }

        RenderTarget2D rt = null;
        try
        {
            rt = new RenderTarget2D(_graphicsDevice, _tileSize, _tileSize);

            _graphicsDevice.SetRenderTarget(rt);
            _graphicsDevice.Clear(GameSettings.TileBackgroundColor);

            /*------------- Weltkoordinaten der Kachel -------------*/
            var n = 1 << zoom;
            var w = _mapBounds.Width / n;
            var h = _mapBounds.Height / n;
            var wx = _mapBounds.X + tileX * w;
            var wy = _mapBounds.Y + tileY * h;
            var world = new RectangleF(wx, wy, w, h);

            /*------------- lokale Kamera -------------*/
            var cam = new MapCamera(_tileSize, _tileSize)
            {
                Zoom = _tileSize / w
            };
            cam.CenterOn(new Vector2(wx + w * .5f, wy + h * .5f));

            /*------------- Abfragen -------------*/
            var districts = QueryDistrictsInBounds(world);
            var roads = _roadService.GetQuadtree()?.Query(world).Distinct().ToList()
                        ?? new List<Road>();
            var water = QueryWaterBodiesInBounds(world);
            var riversIn = _rivers.Where(r => r.BoundingBoxes.Any(b => b.Intersects(world))).ToList();
            var railsIn = _rails.Where(r => r.BoundingBoxes.Any(b => b.Intersects(world))).ToList();
            var stationsIn = _stations.Where(s => world.Contains(s.Position)).ToList();

            /*------------- Zeichnen -------------*/
            _polygonRenderer.DrawWaterBodies(water, cam);

            // District‑Outlines & Labels
            _polygonRenderer.DrawDistricts(new SpriteBatch(_graphicsDevice), districts, cam);

            // Linien (Straßen, Flüsse, Gleise)
            _polylineRenderer.Draw(new SpriteBatch(_graphicsDevice),
                riversIn.Concat(railsIn), // generic
                roads, // roads
                world, cam);

            // Punkte
            _pointRenderer.Draw(new SpriteBatch(_graphicsDevice), stationsIn, cam);

            _graphicsDevice.SetRenderTarget(null);
            _tileCache[key] = rt;
        }
        catch
        {
            rt?.Dispose();
            rt = null;
        }
        finally
        {
            _queuedOrProcessingKeys.TryRemove(key, out _);
        }

        return rt;
    }


    /// <summary>
    ///     Startet einen Task, um benötigte Tiles zu identifizieren und in die Queue zu legen.
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

            var numTiles = 1 << zoomLevel;
            var tileWorldWidth = _mapBounds.Width / numTiles;
            var tileWorldHeight = _mapBounds.Height / numTiles;

            // Berechne benötigte Tile-Indizes mit Puffer
            var buf = GameSettings.TileGenerationBuffer;
            var minTileX = Math.Max(0, (int)Math.Floor((area.Left - _mapBounds.Left) / tileWorldWidth) - buf);
            var maxTileX = Math.Min(numTiles - 1,
                (int)Math.Ceiling((area.Right - _mapBounds.Left) / tileWorldWidth) + buf);
            var minTileY = Math.Max(0, (int)Math.Floor((area.Top - _mapBounds.Top) / tileWorldHeight) - buf);
            var maxTileY = Math.Min(numTiles - 1,
                (int)Math.Ceiling((area.Bottom - _mapBounds.Top) / tileWorldHeight) + buf);

            var requestedCount = 0;
            for (var ty = minTileY; ty <= maxTileY; ty++)
            for (var tx = minTileX; tx <= maxTileX; tx++)
            {
                var key = (zoomLevel, tx, ty);

                // Prüfe Cache UND Queue, bevor Schlüssel hinzugefügt wird
                if (!_tileCache.ContainsKey(key) && !_queuedOrProcessingKeys.ContainsKey(key))
                    // Füge zum Set hinzu, um Duplikate zu verhindern, bevor es in die Queue geht
                    if (_queuedOrProcessingKeys.TryAdd(key, true))
                    {
                        _tileGenerationQueue.Enqueue(key);
                        requestedCount++;
                    }
            }

            if (requestedCount > 0)
                Debug.WriteLine(
                    $"Requested {requestedCount} new tiles for zoom {zoomLevel}. Queue size: {_tileGenerationQueue.Count}");
        });
    }

    /// <summary>
    ///     Verarbeitet die Tile-Generierungs-Queue im Hauptthread. Sollte in CMetro.Update aufgerufen werden.
    /// </summary>
    public void ProcessTileGenerationQueue()
    {
        var processedCount = 0;
        while (processedCount < GameSettings.MaxTilesToGeneratePerFrame &&
               _tileGenerationQueue.TryDequeue(out var keyToGenerate))
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

    /// <summary>
    ///     Zeichnet die vorhandenen Tiles basierend auf der aktuellen Kameraposition und dem Zoomlevel.
    /// </summary>
    /// <param name="spriteBatch">Das SpriteBatch zum Zeichnen der Tiles.</param>
    /// <param name="camera">Die aktuelle Kamera.</param>
    /// <param name="zoomLevel">Der Zoomlevel der Tiles.</param>
    public void DrawTiles(SpriteBatch spriteBatch, MapCamera camera, int zoomLevel)
    {
        if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0) return;

        var visibleWorld = camera.BoundingRectangle;
        var numTiles = 1 << zoomLevel;
        var tileWorldWidth = _mapBounds.Width / numTiles;
        var tileWorldHeight = _mapBounds.Height / numTiles;

        var minTileX = Math.Max(0, (int)Math.Floor((visibleWorld.Left - _mapBounds.Left) / tileWorldWidth));
        var maxTileX = Math.Min(numTiles - 1,
            (int)Math.Ceiling((visibleWorld.Right - _mapBounds.Left) / tileWorldWidth));
        var minTileY = Math.Max(0, (int)Math.Floor((visibleWorld.Top - _mapBounds.Top) / tileWorldHeight));
        var maxTileY = Math.Min(numTiles - 1,
            (int)Math.Ceiling((visibleWorld.Bottom - _mapBounds.Top) / tileWorldHeight));

        for (var ty = minTileY; ty <= maxTileY; ty++)
        for (var tx = minTileX; tx <= maxTileX; tx++)
        {
            // Hole vorhandenes Tile oder null
            var tileTexture = GetExistingTile(zoomLevel, tx, ty);

            if (tileTexture != null && !tileTexture.IsDisposed)
            {
                // Zeichne das vorhandene Tile
                var tileWorldX = _mapBounds.X + tx * tileWorldWidth;
                var tileWorldY = _mapBounds.Y + ty * tileWorldHeight;
                spriteBatch.Draw(tileTexture,
                    new Vector2(tileWorldX, tileWorldY),
                    null, Color.White, 0f, Vector2.Zero,
                    new Vector2(tileWorldWidth / _tileSize, tileWorldHeight / _tileSize),
                    SpriteEffects.None, 0f);
            }
        }
    }

    /// <summary>
    ///     Leert den Tile-Cache und gibt die Ressourcen frei.
    /// </summary>
    public void ClearCache()
    {
        // Leere auch die Queue und das Set
        while (_tileGenerationQueue.TryDequeue(out _))
        {
        }

        _queuedOrProcessingKeys.Clear();

        foreach (var tile in _tileCache.Values) tile.Dispose();
        _tileCache.Clear();
        Debug.WriteLine("Tile cache, queue, and processing keys cleared.");
    }

    /// <summary>
    ///     Gibt die aktuelle Größe der Generierungsqueue zurück (für Debug/UI).
    /// </summary>
    /// <returns>Die Größe der Generierungsqueue.</returns>
    public int GetGenerationQueueSize()
    {
        return _tileGenerationQueue.Count;
    }

    /// <summary>
    ///     Fragt die Distrikte ab, die sich innerhalb der angegebenen Grenzen befinden.
    /// </summary>
    /// <param name="bounds">Die Grenzen, innerhalb derer die Distrikte abgefragt werden sollen.</param>
    /// <returns>Eine Liste der Distrikte innerhalb der angegebenen Grenzen.</returns>
    private List<District> QueryDistrictsInBounds(RectangleF bounds)
    {
        var result = new List<District>();
        foreach (var district in _districts)
            if (district.BoundingBox.Intersects(bounds))
                result.Add(district);

        return result;
    }

    private List<WaterBody> QueryWaterBodiesInBounds(RectangleF bounds)
    {
        var result = new List<WaterBody>();
        if (_waterBodies == null) return result; // Sicherheitscheck

        foreach (var wb in _waterBodies)
            // Prüfe, ob die BoundingBox die Abfragegrenzen schneidet
            if (wb.BoundingBox.Intersects(bounds))
                result.Add(wb);

        return result;
    }
}