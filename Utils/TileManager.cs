using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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

    public int GenerationQueueSize => _tileGenerationQueue.Count;
    public int BuildQueueSize => _buildTasks.Count;
    public int CompletedQueueSize => _completed.Count;
    public int TileCacheCount => _tileCache.Count;
    public int LastDrawnTileCount { get; private set; }

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

    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _drawPixel;       // rein weiß
    private readonly Texture2D _placeholderTile;
    private int _activeQueueZoomLevel = -1;

    private readonly BasicEffect _meshFx;


    // --- NEU: Für Queued Generation ---
    private readonly ConcurrentQueue<(int zoom, int x, int y)> _tileGenerationQueue = new();

    // ConcurrentDictionary wird als thread-sicheres Set verwendet, um doppelte Einträge in der Queue zu vermeiden. Der bool-Wert ist irrelevant.
    private readonly ConcurrentDictionary<(int zoom, int x, int y), bool> _queuedOrProcessingKeys = new();
    private Task _tileRequestTask = Task.CompletedTask; // Task zur Verwaltung der Anfragen

    private readonly ConcurrentDictionary<(int zoom, int x, int y), Task<TileBuildResult>> _buildTasks = new();
    private readonly ConcurrentQueue<TileBuildResult> _completed = new();

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

        _meshFx = new BasicEffect(_graphicsDevice)
        {
            VertexColorEnabled = true,
            TextureEnabled = false,
            LightingEnabled = false
        };

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

        _spriteBatch = new SpriteBatch(graphicsDevice);

        _drawPixel = new Texture2D(graphicsDevice, 1, 1);
        _drawPixel.SetData([Color.White]);

        _placeholderTile = new Texture2D(graphicsDevice, 1, 1);
        _placeholderTile.SetData([GameSettings.MapBackgroundColor]);

        _tileCache = new Dictionary<(int zoom, int tileX, int tileY), RenderTarget2D>();
        _mapBounds = ComputeGlobalMapBounds();
    }

    /// <summary>
    ///     Liefert die Anzahl Rand‑Tiles, die zusätzlich angefordert werden sollen.
    ///     Ab Zoom 0–2 ein Tile Puffer, ab Zoom 3 kein Puffer.
    /// </summary>
    private static int GetTileBufferForZoom(int zoomLevel)
    {
        return zoomLevel >= 3 ? 0 : 1;
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

    /// <summary>
    ///     Startet einen Task, um benötigte Tiles zu identifizieren und in die Queue zu legen.
    /// </summary>
    /// <param name="area">Der Bereich, für den Tiles generiert werden sollen.</param>
    /// <param name="zoomLevel">Der Zoomlevel der Tiles.</param>
    /// <summary>
    ///     Markiert alle Tiles, die für den aktuellen Viewport benötigt werden, und
    ///     startet pro Tile einen Build‑Task (CPU‑seitige Vorarbeit) im ThreadPool.
    /// </summary>
    public void RequestTileGeneration(RectangleF area, int zoomLevel)
    {
        /* ---------- Zoom‑Wechsel → Queue & Marker leeren ---------- */
        if (_activeQueueZoomLevel != zoomLevel)
        {
            while (_tileGenerationQueue.TryDequeue(out _)) { }
            _queuedOrProcessingKeys.Clear();
            _activeQueueZoomLevel = zoomLevel;
        }

        /* ---------- nur einen Sammel‑Task gleichzeitig ---------- */
        if (_tileRequestTask is { IsCompleted: false })
            return;

        _tileRequestTask = Task.Run(() =>
        {
            if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0) return;

            int nTiles = 1 << zoomLevel;
            float tileWorldW = _mapBounds.Width / nTiles;
            float tileWorldH = _mapBounds.Height / nTiles;

            int buf = GetTileBufferForZoom(zoomLevel);   // 0 ab Zoom 3, sonst 1
            int minTx = Math.Max(0, (int)Math.Floor((area.Left - _mapBounds.Left) / tileWorldW) - buf);
            int maxTx = Math.Min(nTiles - 1, (int)Math.Floor((area.Right - _mapBounds.Left) / tileWorldW) + buf);
            int minTy = Math.Max(0, (int)Math.Floor((area.Top - _mapBounds.Top) / tileWorldH) - buf);
            int maxTy = Math.Min(nTiles - 1, (int)Math.Floor((area.Bottom - _mapBounds.Top) / tileWorldH) + buf);

            int requested = 0;

            for (int ty = minTy; ty <= maxTy; ty++)
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    var key = (zoomLevel, tx, ty);

                    if (_tileCache.ContainsKey(key) || _queuedOrProcessingKeys.ContainsKey(key))
                        continue;

                    if (!_queuedOrProcessingKeys.TryAdd(key, true))
                        continue;

                    /* ---- Welt‑Rect der Kachel ---- */
                    float wx = _mapBounds.X + tx * tileWorldW;
                    float wy = _mapBounds.Y + ty * tileWorldH;
                    var worldRect = new RectangleF(wx, wy, tileWorldW, tileWorldH);

                    /* ---- Build‑Task anlegen ---- */
                    var buildTask = Task.Run(() =>
                {
                    var dists = QueryDistrictsInBounds(worldRect);
                    var roads = _roadService.GetQuadtree()?.Query(worldRect).Distinct().ToList() ?? [];
                    var water = QueryWaterBodiesInBounds(worldRect);
                    var rivers = _rivers.Where(r => r.BoundingBoxes.Any(b => b.Intersects(worldRect))).ToList();
                    var rails = _rails.Where(r => r.BoundingBoxes.Any(b => b.Intersects(worldRect))).ToList();
                    var points = _stations.Where(s => worldRect.Contains(s.Position)).ToList();

                    return TileBuilder.BuildTile(
                        key, worldRect, _tileSize,
                        water, dists,
                        rivers.Concat(rails).ToList(),
                        roads, points);
                });

                    /* ---- Continuation separat anhängen ---- */
                    buildTask.ContinueWith(t =>
                {
                    _buildTasks.TryRemove(key, out _);

                    if (t.Status == TaskStatus.RanToCompletion)
                        _completed.Enqueue(t.Result);                 // fertig → Main‑Thread
                    else
                        _queuedOrProcessingKeys.TryRemove(key, out _); // Fehlgeschlagen
                });

                    /* ---- ursprüngliches Task im Dictionary merken ---- */
                    _buildTasks[key] = buildTask;

                    requested++;
                }

            if (requested > 0)
                Debug.WriteLine($"Requested {requested} tiles for zoom {zoomLevel}. Build queue: {_buildTasks.Count}");
        });
    }

    public void ProcessBuildResults()
    {
        var builtThisFrame = 0;
        while (builtThisFrame < GameSettings.MaxTilesToGeneratePerFrame &&
               _completed.TryDequeue(out var res))
        {
            // schon existierendes Tile? → skip
            if (_tileCache.ContainsKey(res.Key))
                continue;

            var rt = new RenderTarget2D(_graphicsDevice, _tileSize, _tileSize);
            _graphicsDevice.SetRenderTarget(rt);
            _graphicsDevice.Clear(GameSettings.TileBackgroundColor);

            // Welt→Tile-Skalierung + Translation (hast du schon)
            var matWorldToTile = res.CalcTransformMatrix(_tileSize);

            // Orthografische Projektion für genau eine Kachel (Y-Down!)
            var ortho = Matrix.CreateOrthographicOffCenter(
                           0, _tileSize,   // left,   right
                           _tileSize, 0,          // bottom, top
                           0f, 1f);               // near,   far

            _meshFx.World = matWorldToTile;   // Welt  → Pixel in dieser Kachel
            _meshFx.View = Matrix.Identity;
            _meshFx.Projection = ortho;            // Pixel → Clip-Space

            if (res.FillVerts.Count > 0 && res.FillIndices.Count > 0)
            {
                foreach (var pass in _meshFx.CurrentTechnique.Passes)
                {
                    pass.Apply();

                    _graphicsDevice.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        res.FillVerts.ToArray(), 0,               // Vertex-Array, Offset
                        res.FillVerts.Count,                      // VertexCount
                        res.FillIndices.ToArray(), 0,             // Index-Array, Offset
                        res.FillIndices.Count / 3);               // PrimitiveCount
                }
            }

            /*_graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.None;
            _graphicsDevice.RasterizerState = RasterizerState.CullNone;*/

            // b) Lines & Points via SpriteBatch
            _spriteBatch.Begin(transformMatrix: matWorldToTile,
                samplerState: SamplerState.AnisotropicClamp);

            foreach (var ln in res.Lines)
                DrawFastLine(_spriteBatch, ln.p1, ln.p2, ln.col, ln.thick);

            foreach (var pt in res.Points)
                _spriteBatch.DrawCircle(
                    new CircleF(pt.pos, pt.radius),
                    12, pt.col, pt.radius);

            _spriteBatch.End();

            _graphicsDevice.SetRenderTarget(null);
            _tileCache[res.Key] = rt;
            _queuedOrProcessingKeys.TryRemove(res.Key, out _);

            builtThisFrame++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawFastLine(SpriteBatch sb, Vector2 p1, Vector2 p2,
                           Color tint, float thickness)
    {
        var d = p2 - p1;
        if (d.LengthSquared() < 1e-4f) return;
        float angle = MathF.Atan2(d.Y, d.X);

        sb.Draw(_drawPixel, p1, null, tint, angle, Vector2.Zero,
                new Vector2(d.Length(), thickness),
                SpriteEffects.None, 0f);
    }


    /// <summary>
    ///     Zeichnet die vorhandenen Tiles basierend auf der aktuellen Kameraposition und dem Zoomlevel.
    /// </summary>
    /// <param name="spriteBatch">Das SpriteBatch zum Zeichnen der Tiles.</param>
    /// <param name="camera">Die aktuelle Kamera.</param>
    /// <param name="zoomLevel">Der Zoomlevel der Tiles.</param>
    public void DrawTiles(SpriteBatch spriteBatch, MapCamera cam, int zoomLevel)
    {
        if (_mapBounds.Width <= 0 || _mapBounds.Height <= 0) return;

        var view = cam.BoundingRectangle;

        var nTiles = 1 << zoomLevel;
        var tileWorldW = _mapBounds.Width / nTiles;
        var tileWorldH = _mapBounds.Height / nTiles;

        var minTx = Math.Max(0, (int)Math.Floor((view.Left - _mapBounds.Left) / tileWorldW));
        var maxTx = Math.Min(nTiles - 1, (int)Math.Floor((view.Right - _mapBounds.Left) / tileWorldW));
        var minTy = Math.Max(0, (int)Math.Floor((view.Top - _mapBounds.Top) / tileWorldH));
        var maxTy = Math.Min(nTiles - 1, (int)Math.Floor((view.Bottom - _mapBounds.Top) / tileWorldH));

        int drawn = 0;
        for (var ty = minTy; ty <= maxTy; ty++)
        for (var tx = minTx; tx <= maxTx; tx++)
        {
            var key = (zoomLevel, tx, ty);
            var hasTile = _tileCache.TryGetValue(key, out var rt) && !rt.IsDisposed;
                
            if (hasTile)
                drawn++;

            var worldX = _mapBounds.X + tx * tileWorldW;
            var worldY = _mapBounds.Y + ty * tileWorldH;

            var tex = hasTile ? rt : _placeholderTile;
            var color = Color.White;

            spriteBatch.Draw(tex,
                new Vector2(worldX, worldY),
                null, color, 0f, Vector2.Zero,
                new Vector2(tileWorldW / tex.Width, tileWorldH / tex.Height),
                SpriteEffects.None, 0f);
        }
        LastDrawnTileCount = drawn;
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