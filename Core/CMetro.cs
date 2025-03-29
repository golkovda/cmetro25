using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks; // Für Task benötigt
using cmetro25.Models;
using cmetro25.Services;
using cmetro25.UI;
using cmetro25.Views;
using cmetro25.Utils;
using cmetro25.Core;
using System.Collections.Concurrent; // Sicherstellen, dass dies vorhanden ist


namespace cmetro25.Core
{
    public class CMetro : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // --- Daten (werden asynchron geladen) ---
        private List<District> _districts;
        private List<Road> _roads;

        // --- Services und Renderer (werden nach dem Laden initialisiert) ---
        private MapLoader _mapLoader;
        private RoadService _roadService;
        private DistrictRenderer _districtRenderer;
        private RoadRenderer _roadRenderer;
        private TileManager _tileManager;

        // --- UI & Grundlegende Komponenten ---
        private PerformanceUI _performanceUI;
        private MapCamera _camera;
        private SpriteFont _font;
        private Texture2D _pixelTexture; // 1x1 weißer Pixel für Linien etc.

        // --- Ladezustand ---
        private Task _loadingTask;
        private bool _isLoading = false; // Zeigt an, ob der Ladevorgang aktiv ist
        private bool _mapDataReady = false; // Zeigt an, ob Daten geladen und Komponenten initialisiert sind
        private string _loadingError = null; // Für Fehlermeldungen beim Laden

        // --- UI & Debug ---
        private bool _showPerformanceMenu = true;
        private int _frameCounter = 0;
        private int _fps = 0;
        private int _updateCounter = 0;
        private int _updatesPerSecond = 0;
        private double _elapsedTime = 0.0;
        private KeyboardState _previousKeyboardState;

        // --- Zoom-Handling für Road Interpolation ---
        private float _lastZoomForInterpolationUpdate; // Letzter Zoom, bei dem Interpolation ausgelöst wurde
        private double _timeSinceLastSignificantZoomChange = 0.0;
        private const double ZoomInterpolationUpdateThreshold = 0.2; // Sekunden, die der Zoom stabil sein muss
        private const float ZoomDifferenceThreshold = 0.05f; // Minimale Zoom-Änderung, um Timer zurückzusetzen

        // --- Konstanten ---
        private const float BaseMaxDistance = 0.5f; // Für initiale Road-Interpolation
        private const float BaseOverlapFactor = 0.1f; // Für RoadRenderer
        private const int CurveSegments = 10; // Für RoadRenderer Smoothing
        private const float MinTextScale = 0.1f;
        private const float MaxTextScale = 1.8f;
        private const float BaseZoomForText = 1.0f;
        private readonly Color _districtBorderColor = new Color(143, 37, 37);
        private readonly Color _districtLabelColor = new Color(143, 37, 37);
        private readonly Color _mapBackgroundColor = new Color(31, 31, 31);
        private const int TileSize = 4096; // Angepasste Tile-Größe (testen!)

        // NEU: Für Kamera-Bewegungserkennung -> Tile-Anforderung
        private Vector2 _lastCameraPositionForTileRequest;
        private float _lastCameraZoomForTileRequest;
        private const float CameraMoveThresholdForTileRequest = 100f; // Pixel-Bewegungsschwelle
        private const float CameraZoomThresholdForTileRequest = 0.1f; // Zoom-Änderungsschwelle

        public CMetro()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            _graphics.PreferredBackBufferWidth = 1600;
            _graphics.PreferredBackBufferHeight = 800;
            // OPTIONAL: VSync deaktivieren für reine FPS-Messung (kann Tearing verursachen)
            // _graphics.SynchronizeWithVerticalRetrace = false;
            // IsFixedTimeStep = false;
        }

        protected override void Initialize()
        {
            // Initialisiere nur Dinge, die keine geladenen Daten benötigen
            _camera = new MapCamera(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
            _camera.CenterOn(new Vector2(555, 555)); // Startposition (Beispiel)
            _lastZoomForInterpolationUpdate = _camera.Zoom;

            // KEIN Laden der Kartendaten hier!
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("CMFont");

            // Erstelle die Basistextur
            _pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
            _pixelTexture.SetData(new[] { Color.White });

            // Initialisiere UI-Komponenten, die keine Kartendaten brauchen
            _performanceUI = new PerformanceUI(_font);

            // NEU: Starte den asynchronen Ladevorgang
            StartLoadingMapData();
        }

        // NEU: Methode zum Starten des Ladevorgangs
        private void StartLoadingMapData()
        {
            if (_loadingTask == null || _loadingTask.IsCompleted)
            {
                _isLoading = true;
                _mapDataReady = false;
                _loadingError = null;
                Debug.WriteLine("Starting background map loading...");
                _loadingTask = Task.Run(() => LoadMapDataAsync());
            }
        }

        // NEU: Asynchrone Methode zum Laden der Daten (läuft im Hintergrund)
        private async Task LoadMapDataAsync()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // 1. MapLoader erstellen (ohne Kamera erstmal)
                // OPTIMIERUNG: Übergebe den Basis-Zoom für die initiale Interpolation
                _mapLoader = new MapLoader(BaseMaxDistance, _camera.Zoom); // Nutze initialen Kamera-Zoom

                // 2. Pfade zu den Daten
                string basePath = AppContext.BaseDirectory;
                string districtPath = Path.Combine(basePath, "GeoJson", "dortmund_boundaries_census_finished.geojson");
                string roadPath = Path.Combine(basePath, "GeoJson", "dortmund_roads_finished.geojson");

                // 3. Daten laden und verarbeiten (dies dauert am längsten)
                Debug.WriteLine("Loading districts...");
                _districts = _mapLoader.LoadDistricts(districtPath);
                Debug.WriteLine($"Districts loaded: {_districts?.Count ?? 0}");

                Debug.WriteLine("Loading roads...");
                _roads = _mapLoader.LoadRoads(roadPath);
                Debug.WriteLine($"Roads loaded: {_roads?.Count ?? 0}");

                if (_districts == null || _roads == null)
                {
                    throw new InvalidOperationException("Failed to load district or road data.");
                }

                // 4. RoadService erstellen (baut initialen Quadtree)
                Debug.WriteLine("Building road service and quadtree...");
                _roadService = new RoadService(_roads, _mapLoader);
                Debug.WriteLine("Road service initialized.");

                stopwatch.Stop();
                Debug.WriteLine($"Background loading finished in {stopwatch.ElapsedMilliseconds} ms");

                // WICHTIG: Keine GraphicsDevice-Operationen hier!
                // Die Initialisierung von Renderern und TileManager erfolgt im Hauptthread.
            }
            catch (Exception ex)
            {
                // Fehler speichern, um ihn im Hauptthread anzuzeigen
                _loadingError = $"Error loading map data: {ex.Message}\n{ex.StackTrace}";
                Debug.WriteLine(_loadingError);
                // Task wird als fehlerhaft markiert
                throw; // Wichtig, damit IsFaulted im Hauptthread true wird
            }
        }

        // NEU: Methode zur Initialisierung der Komponenten, die geladene Daten benötigen (läuft im Hauptthread)
        private void InitializeMapComponents()
        {
            try
            {
                Debug.WriteLine("Initializing map components (Renderers, TileManager)...");
                var stopwatch = Stopwatch.StartNew();

                _districtRenderer = new DistrictRenderer(_pixelTexture, _font, _districtBorderColor, _districtLabelColor, MinTextScale, MaxTextScale, BaseZoomForText);
                _roadRenderer = new RoadRenderer(_pixelTexture, BaseOverlapFactor, BaseMaxDistance, true, CurveSegments);
                _tileManager = new TileManager(GraphicsDevice, _districts, _roadService, _mapLoader, _districtRenderer, _roadRenderer, TileSize);

                _mapLoader.SetCamera(_camera);
                _roadService.SetCamera(_camera);

                stopwatch.Stop();
                Debug.WriteLine($"Map components initialized in {stopwatch.ElapsedMilliseconds} ms");

                _mapDataReady = true;

                // NEU: Initiale Tile-Anforderung nach dem Laden
                RequestTilesForCurrentView();
                _lastCameraPositionForTileRequest = _camera.Position;
                _lastCameraZoomForTileRequest = _camera.Zoom;

            }
            catch (Exception ex)
            {
                _loadingError = $"Error initializing map components: {ex.Message}\n{ex.StackTrace}";
                Debug.WriteLine(_loadingError);
                _mapDataReady = false;
            }
        }


        protected override void UnloadContent()
        {
            _pixelTexture?.Dispose();
            _tileManager?.ClearCache(); // Cache leeren und RenderTargets freigeben
            // Weitere Dispose-Aufrufe für andere Ressourcen hier
            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            // --- Ladezustand prüfen ---
            if (_isLoading)
            {
                if (_loadingTask.IsCompleted)
                {
                    _isLoading = false;
                    if (_loadingTask.IsFaulted) { /* ... Fehlerbehandlung ... */ }
                    else if (_loadingTask.IsCanceled) { /* ... Abbruchbehandlung ... */ }
                    else
                    {
                        // Ladevorgang erfolgreich, initialisiere Komponenten
                        InitializeMapComponents();
                        // Die initiale Tile-Anforderung geschieht jetzt in InitializeMapComponents
                    }
                }
                else
                {
                    base.Update(gameTime);
                    return;
                }
            }

            // --- Reguläres Update (nur wenn Daten bereit und kein Fehler) ---
            if (!_mapDataReady)
            {
                HandleBasicInput();
                base.Update(gameTime);
                return;
            }

            // --- Volles Update ---
            HandleGameInput();
            _camera.Update(gameTime);

            // NEU: Prüfe, ob Kamera sich signifikant bewegt hat und fordere neue Tiles an
            CheckCameraMovementAndRequestTiles();

            // NEU: Verarbeite die Tile-Generierungs-Queue
            _tileManager.ProcessTileGenerationQueue();

            HandleZoomDebounce(gameTime);
            UpdatePerformanceCounters(gameTime);

            base.Update(gameTime);
        }


        // NEU: Aufgeteiltes Input Handling
        private void HandleBasicInput()
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
               Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();
            // Hier könnte man z.B. mit F5 das Neuladen triggern, falls _loadingError != null
        }

        // NEU: Input Handling für das laufende Spiel
        private void HandleGameInput()
        {
            HandleBasicInput(); // Exit-Check

            var currentKeyboardState = Keyboard.GetState();

            // Toggle Performance-Menü (F3)
            if (currentKeyboardState.IsKeyDown(Keys.F3) && !_previousKeyboardState.IsKeyDown(Keys.F3))
            {
                _showPerformanceMenu = !_showPerformanceMenu;
            }

            _previousKeyboardState = currentKeyboardState;
        }

        // NEU: Zoom Debounce Logik ausgelagert
        private void HandleZoomDebounce(GameTime gameTime)
        {
            float currentZoom = _camera.Zoom;
            // Prüfe auf signifikante Zoom-Änderung
            if (Math.Abs(currentZoom - _lastZoomForInterpolationUpdate) > ZoomDifferenceThreshold)
            {
                _timeSinceLastSignificantZoomChange = 0.0; // Reset timer
                _lastZoomForInterpolationUpdate = currentZoom; // Merke diesen Zoom als "letzten bewegten"
            }
            else
            {
                // Zoom ist relativ stabil
                _timeSinceLastSignificantZoomChange += gameTime.ElapsedGameTime.TotalSeconds;

                // Wenn Zoom lange genug stabil war, löse Update aus
                if (_timeSinceLastSignificantZoomChange >= ZoomInterpolationUpdateThreshold)
                {
                    // Nur auslösen, wenn sich der Zoom seit dem letzten Update tatsächlich geändert hat
                    if (Math.Abs(currentZoom - _roadService.GetLastInterpolationZoom()) > 0.01f) // GetLastInterpolationZoom() in RoadService hinzufügen
                    {
                        RectangleF visibleBounds = CalculateVisibleBounds(100f); // Größerer Puffer für Interpolation
                        _roadService.UpdateRoadInterpolationsAsync(currentZoom, visibleBounds);
                        // Wichtig: Zeit zurücksetzen, damit nicht sofort wieder ausgelöst wird
                        _timeSinceLastSignificantZoomChange = -ZoomInterpolationUpdateThreshold; // Negativ setzen, um erneutes Auslösen zu verzögern
                    }
                }
            }
        }
        // In RoadService.cs hinzufügen:
        // private float _lastInterpolationZoomTriggered = -1f;
        // public float GetLastInterpolationZoom() => _lastInterpolationZoomTriggered;
        // In UpdateRoadInterpolationsAsync, vor Task.Run: _lastInterpolationZoomTriggered = zoom;


        // NEU: Performance Counter Logik ausgelagert
        private void UpdatePerformanceCounters(GameTime gameTime)
        {
            _elapsedTime += gameTime.ElapsedGameTime.TotalSeconds;
            _frameCounter++; // Wird im Draw inkrementiert

            if (_elapsedTime >= 1.0)
            {
                _fps = _frameCounter;
                _updatesPerSecond = _updateCounter; // UpdateCounter muss noch inkrementiert werden
                _frameCounter = 0;
                _updateCounter = 0; // Reset Update Counter
                _elapsedTime -= 1.0; // Subtrahiere genau 1 Sekunde
            }
            _updateCounter++; // Zähle jeden Update-Durchlauf
        }

        // NEU: Prüft Kameraänderung und fordert Tiles an
        private void CheckCameraMovementAndRequestTiles()
        {
            bool zoomChanged = Math.Abs(_camera.Zoom - _lastCameraZoomForTileRequest) > CameraZoomThresholdForTileRequest;
            // Prüfe Distanz in Weltkoordinaten oder Bildschirmkoordinaten? Bildschirm ist oft intuitiver.
            float cameraMoveDistanceScreen = Vector2.Distance(_camera.WorldToScreen(_lastCameraPositionForTileRequest), _camera.WorldToScreen(_camera.Position));
            bool positionChanged = cameraMoveDistanceScreen > CameraMoveThresholdForTileRequest;


            if (zoomChanged || positionChanged)
            {
                RequestTilesForCurrentView();
                _lastCameraPositionForTileRequest = _camera.Position;
                _lastCameraZoomForTileRequest = _camera.Zoom;
                // Debug.WriteLine("Camera moved significantly, requested new tiles.");
            }
        }

        // NEU: Kapselt die Tile-Anforderung
        private void RequestTilesForCurrentView()
        {
            if (_tileManager != null && _camera != null)
            {
                int tileZoomLevel = ComputeTileZoomLevel(_camera.Zoom);
                RectangleF viewBounds = CalculateVisibleBounds(TileSize); // Puffer = 1 Tile-Größe in Pixeln
                _tileManager.RequestTileGeneration(viewBounds, tileZoomLevel);
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(_mapBackgroundColor);

            // --- Ladebildschirm / Fehler ---
            if (_isLoading || !_mapDataReady)
            {
                // ... (Zeichnen wie zuvor) ...
                _spriteBatch.Begin();
                string message = _isLoading ? "Lade Kartendaten..." : (_loadingError ?? "Initialisierung...");
                if (_loadingError != null && !_isLoading) message = $"Fehler beim Laden:\n{_loadingError.Split('\n')[0]}";

                Vector2 textSize = _font.MeasureString(message);
                Vector2 position = new Vector2((_graphics.PreferredBackBufferWidth - textSize.X) / 2,
                                               (_graphics.PreferredBackBufferHeight - textSize.Y) / 2);
                _spriteBatch.DrawString(_font, message, position, _loadingError != null && !_isLoading ? Color.Red : Color.White);
                _spriteBatch.End();

                base.Draw(gameTime);
                return;
            }

            // --- Reguläres Zeichnen ---
            int tileZoomLevel = ComputeTileZoomLevel(_camera.Zoom);

            _spriteBatch.Begin(transformMatrix: _camera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
            // DrawTiles holt jetzt nur vorhandene Tiles
            _tileManager.DrawTiles(_spriteBatch, _camera, tileZoomLevel);
            _spriteBatch.End();

            // --- UI / Performance ---
            if (_showPerformanceMenu)
            {
                int visibleRoadSegments = _roadRenderer?.GetVisibleLineCount() ?? 0;
                int queueSize = _tileManager?.GetGenerationQueueSize() ?? 0; // Queue-Größe anzeigen
                // Füge queueSize zur PerformanceUI.Draw hinzu
                _performanceUI.Draw(_spriteBatch, _fps, _updatesPerSecond, _camera, _districts, _roads, visibleRoadSegments, tileZoomLevel, queueSize);
            }

            base.Draw(gameTime);
        }

        // Berechnet den diskreten Zoomlevel für das Tiling
        private int ComputeTileZoomLevel(float zoom)
        {
            // Experimentiere mit der Basis (z.B. 1.0 statt 1.2) und dem Logarithmus-Basis (2)
            // Ziel: Sinnvolle Anzahl von Kacheln pro Zoomstufe
            int level = (int)Math.Floor(Math.Log(Math.Max(1.0, zoom), 2)); // Log Basis 2
            // Begrenze den Level, z.B. basierend auf TileSize und erwarteter Kartengröße
            // MaxLevel könnte z.B. 8 sein (2^8 = 256 Tiles pro Dimension)
            int maxLevel = 10; // Beispielwert, anpassen!
            return Math.Clamp(level, 0, maxLevel);
        }


        // Berechnet die sichtbaren Weltkoordinaten mit einem Rand
        private RectangleF CalculateVisibleBounds(float pixelMargin = 50f)
        {
            if (_camera == null) return RectangleF.Empty; // Kamera noch nicht bereit

            float marginWorld = pixelMargin / Math.Max(0.1f, _camera.Zoom); // Verhindere Division durch Null
            RectangleF visibleBounds = _camera.BoundingRectangle;

            // Stelle sicher, dass visibleBounds gültig ist
            if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
            {
                // Versuche, basierend auf Viewport und Zoom zu schätzen
                float worldWidth = _graphics.PreferredBackBufferWidth / Math.Max(0.1f, _camera.Zoom);
                float worldHeight = _graphics.PreferredBackBufferHeight / Math.Max(0.1f, _camera.Zoom);
                Vector2 center = _camera.Position;
                visibleBounds = new RectangleF(center.X - worldWidth / 2, center.Y - worldHeight / 2, worldWidth, worldHeight);
            }


            // Erweitere um den Rand
            visibleBounds = new RectangleF(
                visibleBounds.X - marginWorld,
                visibleBounds.Y - marginWorld,
                visibleBounds.Width + 2 * marginWorld,
                visibleBounds.Height + 2 * marginWorld);
            return visibleBounds;
        }
    }
}