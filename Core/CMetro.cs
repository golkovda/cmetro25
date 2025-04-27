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
using System.Collections.Concurrent;
using System.Reflection;
using Newtonsoft.Json; // Sicherstellen, dass dies vorhanden ist

namespace cmetro25.Core
{
    /// <summary>
    /// Hauptklasse für das Spiel CMetro. Verwaltet das Laden und Rendern der Kartendaten, Eingaben und UI.
    /// </summary>
    public class CMetro : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // --- Daten (werden asynchron geladen) ---
        private List<District> _districts;
        private List<Road> _roads;
        private List<WaterBody> _waterBodies;
        private List<PolylineElement> _rails;
        private List<PolylineElement> _rivers;
        private List<PointElement> _stations;

        // --- Services und Renderer (werden nach dem Laden initialisiert) ---
        private MapLoader _mapLoader;
        private RoadService _roadService;
        private TileManager _tileManager;
        private PolygonRenderer _polygonRenderer;

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

        // NEU: Für Kamera-Bewegungserkennung -> Tile-Anforderung
        private Vector2 _lastCameraPositionForTileRequest;
        private float _lastCameraZoomForTileRequest;

        private string _versionText;


        /// <summary>
        /// Initialisiert eine neue Instanz der CMetro-Klasse.
        /// </summary>
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

        /// <summary>
        /// Initialisiert das Spiel und legt grundlegende Komponenten fest.
        /// </summary>
        protected override void Initialize()
        {
            // Initialisiere nur Dinge, die keine geladenen Daten benötigen
            _camera = new MapCamera(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
            _camera.CenterOn(new Vector2(555, 555)); // Startposition (Beispiel)
            _lastZoomForInterpolationUpdate = _camera.Zoom;

            // KEIN Laden der Kartendaten hier!
            base.Initialize();
        }

        /// <summary>
        /// Lädt Inhalte und startet den asynchronen Ladevorgang für die Kartendaten.
        /// </summary>
        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("CMFont");

            // Erstelle die Basistextur
            _pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
            _pixelTexture.SetData(new[] { Color.White });

            // Initialisiere UI-Komponenten, die keine Kartendaten brauchen
            _performanceUI = new PerformanceUI(_font, GraphicsDevice);

            // NEU: Starte den asynchronen Ladevorgang
            StartLoadingMapData();

            // LoadContent()  – am Ende der Methode
            var asm = typeof(CMetro).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var versiontext = info?.InformationalVersion ??
                              asm.GetName().Version?.ToString();
            if (versiontext != null)
                _versionText = "v" + versiontext;
            else
                _versionText = "<versionfile missing>";


        }

        /// <summary>
        /// Startet den asynchronen Ladevorgang der Kartendaten.
        /// </summary>
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

        /// <summary>
        /// Lädt die Kartendaten asynchron im Hintergrund.
        /// </summary>
        /// <returns>Ein Task, der den Ladevorgang repräsentiert.</returns>
        private async Task LoadMapDataAsync()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // 1. MapLoader erstellen (ohne Kamera erstmal)
                // OPTIMIERUNG: Übergebe den Basis-Zoom für die initiale Interpolation
                _mapLoader = new MapLoader(GameSettings.RoadBaseMaxInterpolationDistance, _camera.Zoom); // Nutze initialen Kamera-Zoom

                // 2. Pfade zu den Daten
                string basePath = AppContext.BaseDirectory;
                string districtPath = GameSettings.DistrictGeoJsonPath; // NEU: Verwende GameSettings
                string roadPath = GameSettings.RoadGeoJsonPath; // NEU: Verwende GameSettings
                string waterPath = GameSettings.WaterGeoJsonPath;    // NEU: Verwende GameSettings
                string railPath = GameSettings.RailsGeoJsonPath;    // NEU: Verwende GameSettings
                string riverPath = GameSettings.RiversGeoJsonPath;    // NEU: Verwende GameSettings
                string stationPath = GameSettings.StationsGeoJsonPath;    // NEU: Verwende GameSettings


                // 3. Daten laden und verarbeiten (dies dauert am längsten)
                Debug.WriteLine("Loading districts...");
                _districts = _mapLoader.LoadDistricts(districtPath);
                Debug.WriteLine($"Districts loaded: {_districts?.Count ?? 0}");

                Debug.WriteLine("Loading roads...");
                _roads = _mapLoader.LoadRoads(roadPath);
                Debug.WriteLine($"Roads loaded: {_roads?.Count ?? 0}");

                Debug.WriteLine("Loading water bodies...");
                _waterBodies = _mapLoader.LoadWaterBodies(waterPath);
                Debug.WriteLine($"Water bodies loaded: {_waterBodies?.Count ?? 0}");

                Debug.WriteLine("Loading rails...");
                _rails = _mapLoader.LoadRails(railPath);
                Debug.WriteLine($"Rails loaded: {_rails?.Count ?? 0}");

                Debug.WriteLine("Loading rivers...");
                _rivers = _mapLoader.LoadRivers(riverPath);
                Debug.WriteLine($"Rivers loaded: {_rivers?.Count ?? 0}");

                Debug.WriteLine("Loading stations...");
                _stations = _mapLoader.LoadStations(stationPath);
                Debug.WriteLine($"Stations loaded: {_stations?.Count ?? 0}");

                if (_waterBodies == null)
                {
                    // Optional: Fehler werfen oder nur loggen, wenn Wasser optional ist
                    Debug.WriteLine("[Warning] Failed to load water body data.");
                    _waterBodies = new List<WaterBody>(); // Leere Liste, um NullPointer zu vermeiden
                }

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

        /// <summary>
        /// Initialisiert Komponenten, die geladene Kartendaten benötigen.
        /// Diese Methode wird im Hauptthread aufgerufen.
        /// </summary>
        private void InitializeMapComponents()
        {
            try
            {
                Debug.WriteLine("Initializing unified renderers …");
                var sw = Stopwatch.StartNew();

                // *** neue Renderer ***
                _polygonRenderer = new PolygonRenderer(GraphicsDevice, _pixelTexture, _font);
                var pointRenderer = new PointRenderer(_pixelTexture);

                _mapLoader.SetCamera(_camera);
                _roadService.SetCamera(_camera);

                _tileManager = new TileManager(GraphicsDevice,
                                               _districts, _waterBodies,
                                               _roadService, _mapLoader,
                                               _polygonRenderer, pointRenderer,
                                               GameSettings.TileSize,
                                               _rivers, _rails, _stations, _camera);


                sw.Stop();
                Debug.WriteLine($"Renderers ready in {sw.ElapsedMilliseconds} ms");

                _mapDataReady = true;
                RequestTilesForCurrentView();
                _lastCameraPositionForTileRequest = _camera.Position;
                _lastCameraZoomForTileRequest = _camera.Zoom;
            }
            catch (Exception ex)
            {
                _loadingError = $"Renderer init failed: {ex.Message}";
                Debug.WriteLine(_loadingError);
                _mapDataReady = false;
            }
        }

        /// <summary>
        /// Entlädt die Inhalte und gibt Ressourcen frei.
        /// </summary>
        protected override void UnloadContent()
        {
            _pixelTexture?.Dispose();
            _tileManager?.ClearCache(); // Cache leeren und RenderTargets freigeben
            // Weitere Dispose-Aufrufe für andere Ressourcen hier
            base.UnloadContent();
        }

        /// <summary>
        /// Aktualisiert den Spielzustand.
        /// </summary>
        /// <param name="gameTime">Die Zeit, die seit dem letzten Update vergangen ist.</param>
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
            _tileManager.ProcessBuildResults();

            HandleZoomDebounce(gameTime);
            UpdatePerformanceCounters(gameTime);

            (bool sChanged, bool tChanged) = (false, false);
            if (_showPerformanceMenu)
                (sChanged, tChanged) = _performanceUI.Update();

            if ((sChanged || tChanged) && _mapDataReady && _tileManager != null)
            {
                _tileManager.ClearCache();
                RequestTilesForCurrentView();
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// Führt grundlegende Eingabeverarbeitung aus, wie z.B. das Überprüfen der Escape-Taste.
        /// </summary>
        private void HandleBasicInput()
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
               Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();
            // Hier könnte man z.B. mit F5 das Neuladen triggern, falls _loadingError != null
        }

        /// <summary>
        /// Verarbeitet Spieleingaben, wie das Umschalten des Performance-Menüs.
        /// </summary>
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

        /// <summary>
        /// Verarbeitet die Zoom-Debounce-Logik, um Road-Interpolation nur bei stabilen Zoom-Änderungen auszulösen.
        /// </summary>
        /// <param name="gameTime">Die Zeit seit dem letzten Update.</param>
        private void HandleZoomDebounce(GameTime gameTime)
        {
            float currentZoom = _camera.Zoom;
            // Prüfe auf signifikante Zoom-Änderung
            if (Math.Abs(currentZoom - _lastZoomForInterpolationUpdate) > GameSettings.RoadInterpolationZoomThreshold)
            {
                _timeSinceLastSignificantZoomChange = 0.0; // Reset timer
                _lastZoomForInterpolationUpdate = currentZoom; // Merke diesen Zoom als "letzten bewegten"
            }
            else
            {
                // Zoom ist relativ stabil
                _timeSinceLastSignificantZoomChange += gameTime.ElapsedGameTime.TotalSeconds;

                // Wenn Zoom lange genug stabil war, löse Update aus
                if (_timeSinceLastSignificantZoomChange >= GameSettings.RoadInterpolationUpdateDebounce)
                {
                    // Nur auslösen, wenn sich der Zoom seit dem letzten Update tatsächlich geändert hat
                    if (Math.Abs(currentZoom - _roadService.GetLastInterpolationZoom()) > 0.01f) // GetLastInterpolationZoom() in RoadService hinzufügen
                    {
                        RectangleF visibleBounds = CalculateVisibleBounds(100f); // Größerer Puffer für Interpolation
                        _roadService.UpdateRoadInterpolationsAsync(currentZoom, visibleBounds);
                        // Wichtig: Zeit zurücksetzen, damit nicht sofort wieder ausgelöst wird
                        _timeSinceLastSignificantZoomChange = -GameSettings.RoadInterpolationUpdateDebounce; // Negativ setzen, um erneutes Auslösen zu verzögern
                    }
                }
            }
        }

        /// <summary>
        /// Aktualisiert die Performance-Zähler, wie FPS und Updates pro Sekunde.
        /// </summary>
        /// <param name="gameTime">Die verstrichene Zeit seit dem letzten Update.</param>
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

        /// <summary>
        /// Prüft, ob sich die Kamera signifikant bewegt oder gezoomt hat, und fordert neue Tiles an.
        /// </summary>
        private void CheckCameraMovementAndRequestTiles()
        {
            bool zoomChanged = Math.Abs(_camera.Zoom - _lastCameraZoomForTileRequest) > GameSettings.CameraZoomThresholdForTileRequest;
            // Prüfe Distanz in Weltkoordinaten oder Bildschirmkoordinaten? Bildschirm ist oft intuitiver.
            float cameraMoveDistanceScreen = Vector2.Distance(_camera.WorldToScreen(_lastCameraPositionForTileRequest), _camera.WorldToScreen(_camera.Position));
            bool positionChanged = cameraMoveDistanceScreen > GameSettings.CameraMoveThresholdForTileRequest;

            if (zoomChanged || positionChanged)
            {
                RequestTilesForCurrentView();
                _lastCameraPositionForTileRequest = _camera.Position;
                _lastCameraZoomForTileRequest = _camera.Zoom;
                // Debug.WriteLine("Camera moved significantly, requested new tiles.");
            }
        }

        /// <summary>
        /// Fordert die Generierung von Tiles für die aktuelle Kamerasicht an.
        /// </summary>
        private void RequestTilesForCurrentView()
        {
            if (_tileManager != null && _camera != null)
            {
                int tileZoomLevel = ComputeTileZoomLevel(_camera.Zoom);
                RectangleF viewBounds = CalculateVisibleBounds(GameSettings.TileSize); // Puffer = 1 Tile-Größe in Pixeln
                _tileManager.RequestTileGeneration(viewBounds, tileZoomLevel);
            }
        }

        /// <summary>
        /// Zeichnet den aktuellen Spielzustand.
        /// </summary>
        /// <param name="gameTime">Die verstrichene Zeit seit dem letzten Draw-Aufruf.</param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(GameSettings.MapBackgroundColor);

            // --- Ladebildschirm / Fehler ---
            if (_isLoading || !_mapDataReady)
            {
                // Zeichne einen Lade- oder Fehlerbildschirm
                _spriteBatch.Begin();
                string message = _isLoading ? "Lade Kartendaten..." : (_loadingError ?? "Initialisierung...");
                if (_loadingError != null && !_isLoading)
                    message = $"Fehler beim Laden:\n{_loadingError.Split('\n')[0]}";

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

            _polygonRenderer.DrawDistrictLabels(_spriteBatch,
                                    _districts,
                                    _camera,
                                    Mouse.GetState());

            // --- UI / Performance ---
            if (_showPerformanceMenu)
            {
                _performanceUI.Draw(_spriteBatch,
                        _fps, _updatesPerSecond,
                        _tileManager?.LastDrawnTileCount ?? 0,
                        GC.GetTotalMemory(false) / (1024 * 1024));
            }

            DrawVersion();

            base.Draw(gameTime);
            
        }

        private void DrawVersion()
        {
            _spriteBatch.Begin();
            var size = _font.MeasureString(_versionText);
            _spriteBatch.DrawString(_font, _versionText,
                new Vector2(10, _graphics.PreferredBackBufferHeight - size.Y - 10),
                Color.White * 0.6f);          // 60 % Opazität
            _spriteBatch.End();
        }

        /// <summary>
        /// Berechnet den diskreten Zoomlevel für das Tiling.
        /// </summary>
        /// <param name="zoom">Der aktuelle Zoomfaktor der Kamera.</param>
        /// <returns>Ein Integer, der den diskreten Zoomlevel darstellt.</returns>
        private int ComputeTileZoomLevel(float zoom)
        {
            // Experimentiere mit der Basis (z.B. 1.0 statt 1.2) und dem Logarithmus-Basis (2)
            // Ziel: Sinnvolle Anzahl von Kacheln pro Zoomstufe
            int level = (int)Math.Floor(Math.Log(Math.Max(1.0, zoom), 2)); // Log Basis 2
            // Begrenze den Level, z.B. basierend auf TileSize und erwarteter Kartengröße
            // MaxLevel könnte z.B. 8 sein (2^8 = 256 Tiles pro Dimension)
            int maxLevel = GameSettings.MaxTileZoomLevel;
            return Math.Clamp(level, 0, maxLevel);
        }

        /// <summary>
        /// Berechnet die sichtbaren Weltkoordinaten mit einem optionalen Rand in Pixeln.
        /// </summary>
        /// <param name="pixelMargin">Der zusätzliche Rand in Pixeln.</param>
        /// <returns>Ein RectangleF, das die sichtbaren Weltkoordinaten darstellt.</returns>
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