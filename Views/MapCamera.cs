using cmetro25.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

namespace cmetro25.Views
{
    /// <summary>
    /// Repräsentiert eine Kamera für die Kartendarstellung, die Zoom- und Pan-Funktionen unterstützt.
    /// </summary>
    public class MapCamera
    {
        private Vector2 _position;
        private float _zoom;
        private bool _isDragging;
        private Vector2 _lastMousePosition;
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;
        private int _previousScrollWheelValue;

        /// <summary>
        /// Gibt das sichtbare Rechteck in Weltkoordinaten zurück.
        /// </summary>
        public RectangleF BoundingRectangle
        {
            get
            {
                Vector2 topLeft = ScreenToWorld(Vector2.Zero);
                Vector2 bottomRight = ScreenToWorld(new Vector2(_viewportWidth, _viewportHeight));
                return new RectangleF(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
            }
        }

        /// <summary>
        /// Die Position der Kamera in Weltkoordinaten.
        /// </summary>
        public Vector2 Position
        {
            get => _position;
            set { _position = value; UpdateTransformMatrix(); }
        }

        /// <summary>
        /// Der Zoomfaktor der Kamera.
        /// </summary>
        public float Zoom
        {
            get => _zoom;
            set { _zoom = MathHelper.Clamp(value, GameSettings.CameraZoomMin, GameSettings.CameraZoomMax); UpdateTransformMatrix(); }
        }

        /// <summary>
        /// Die Transformationsmatrix der Kamera.
        /// </summary>
        public Matrix TransformMatrix { get; private set; }

        /// <summary>
        /// Initialisiert eine neue Instanz der <see cref="MapCamera"/> Klasse.
        /// </summary>
        /// <param name="viewportWidth">Die Breite des Viewports.</param>
        /// <param name="viewportHeight">Die Höhe des Viewports.</param>
        public MapCamera(int viewportWidth, int viewportHeight)
        {
            _position = Vector2.Zero;
            _zoom = 1.0f;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            UpdateTransformMatrix();
            _previousScrollWheelValue = Mouse.GetState().ScrollWheelValue;
        }

        /// <summary>
        /// Aktualisiert den Zustand der Kamera basierend auf Benutzereingaben.
        /// </summary>
        /// <param name="gameTime">Die verstrichene Zeit seit dem letzten Update.</param>
        public void Update(GameTime gameTime)
        {
            HandleInput();
        }

        /// <summary>
        /// Verarbeitet die Benutzereingaben zur Steuerung der Kamera.
        /// </summary>
        private void HandleInput()
        {
            MouseState mouseState = Mouse.GetState();
            KeyboardState keyboardState = Keyboard.GetState();

            int scrollDelta = mouseState.ScrollWheelValue - _previousScrollWheelValue;
            if (scrollDelta > 0)
                Zoom *= GameSettings.CameraZoomStepFactor;
            else if (scrollDelta < 0)
                Zoom /= GameSettings.CameraZoomStepFactor;
            _previousScrollWheelValue = mouseState.ScrollWheelValue;

            if (mouseState.MiddleButton == ButtonState.Pressed || (mouseState.LeftButton == ButtonState.Pressed && keyboardState.IsKeyDown(Keys.LeftShift)))
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                }
                else
                {
                    var mouseDelta = new Vector2(mouseState.X, mouseState.Y) - _lastMousePosition;
                    Position -= mouseDelta / Zoom;
                }

                _lastMousePosition = new Vector2(mouseState.X, mouseState.Y);
            }
            else
            {
                _isDragging = false;
            }
        }

        /// <summary>
        /// Aktualisiert die Transformationsmatrix der Kamera basierend auf Position und Zoom.
        /// </summary>
        private void UpdateTransformMatrix()
        {
            TransformMatrix = Matrix.CreateTranslation(new Vector3(-_position.X, -_position.Y, 0)) *
                               Matrix.CreateScale(_zoom) *
                               Matrix.CreateTranslation(new Vector3(_viewportWidth / 2f, _viewportHeight / 2f, 0));
        }

        /// <summary>
        /// Setzt die Kamera auf die Standardposition und den Standardzoom zurück.
        /// </summary>
        public void Reset()
        {
            _position = Vector2.Zero;
            _zoom = 1.0f;
            UpdateTransformMatrix();
        }

        /// <summary>
        /// Zentriert die Kamera auf einen bestimmten Punkt in Weltkoordinaten.
        /// </summary>
        /// <param name="point">Der Punkt, auf den die Kamera zentriert werden soll.</param>
        public void CenterOn(Vector2 point)
        {
            _position = point;
            UpdateTransformMatrix();
        }

        /// <summary>
        /// Transformiert eine Position von Weltkoordinaten in Bildschirmkoordinaten.
        /// </summary>
        /// <param name="worldPosition">Die Position in Weltkoordinaten.</param>
        /// <returns>Die transformierte Position in Bildschirmkoordinaten.</returns>
        public Vector2 WorldToScreen(Vector2 worldPosition) => Vector2.Transform(worldPosition, TransformMatrix);

        /// <summary>
        /// Transformiert eine Position von Bildschirmkoordinaten in Weltkoordinaten.
        /// </summary>
        /// <param name="screenPosition">Die Position in Bildschirmkoordinaten.</param>
        /// <returns>Die transformierte Position in Weltkoordinaten.</returns>
        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            var inverseMatrix = Matrix.Invert(TransformMatrix);
            return Vector2.Transform(screenPosition, inverseMatrix);
        }
    }
}
