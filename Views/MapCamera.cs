using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

namespace cmetro25.Views
{
    public class MapCamera
    {
        private Vector2 _position;
        private float _zoom;
        private bool _isDragging;
        private Vector2 _lastMousePosition;
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;
        private int _previousScrollWheelValue;

        public RectangleF BoundingRectangle
        {
            get
            {
                Vector2 topLeft = ScreenToWorld(Vector2.Zero);
                Vector2 bottomRight = ScreenToWorld(new Vector2(_viewportWidth, _viewportHeight));
                return new RectangleF(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
            }
        }

        public Vector2 Position
        {
            get => _position;
            set { _position = value; UpdateTransformMatrix(); }
        }

        public float Zoom
        {
            get => _zoom;
            set { _zoom = MathHelper.Clamp(value, 0.3f, 30f); UpdateTransformMatrix(); }
        }

        public Matrix TransformMatrix { get; private set; }

        public MapCamera(int viewportWidth, int viewportHeight)
        {
            _position = Vector2.Zero;
            _zoom = 1.0f;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            UpdateTransformMatrix();
            _previousScrollWheelValue = Mouse.GetState().ScrollWheelValue;
        }

        public void Update(GameTime gameTime)
        {
            HandleInput();
        }

        private void HandleInput()
        {
            MouseState mouseState = Mouse.GetState();
            KeyboardState keyboardState = Keyboard.GetState();

            int scrollDelta = mouseState.ScrollWheelValue - _previousScrollWheelValue;
            if (scrollDelta > 0)
                Zoom *= 1.1f;
            else if (scrollDelta < 0)
                Zoom *= 0.9f;
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

        private void UpdateTransformMatrix()
        {
            TransformMatrix = Matrix.CreateTranslation(new Vector3(-_position.X, -_position.Y, 0)) *
                               Matrix.CreateScale(_zoom) *
                               Matrix.CreateTranslation(new Vector3(_viewportWidth / 2f, _viewportHeight / 2f, 0));
        }

        public void Reset()
        {
            _position = Vector2.Zero;
            _zoom = 1.0f;
            UpdateTransformMatrix();
        }

        public void CenterOn(Vector2 point)
        {
            _position = point;
            UpdateTransformMatrix();
        }

        public Vector2 WorldToScreen(Vector2 worldPosition) => Vector2.Transform(worldPosition, TransformMatrix);

        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            var inverseMatrix = Matrix.Invert(TransformMatrix);
            return Vector2.Transform(screenPosition, inverseMatrix);
        }
    }
}
