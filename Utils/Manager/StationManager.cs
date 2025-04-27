using System.Collections.Generic;
using cmetro25.Models;
using cmetro25.Models.Enums;
using cmetro25.Services;
using cmetro25.UI;
using cmetro25.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

namespace cmetro25.Utils.Manager;

/// <summary>
///     High‑Level‑Zustandsmaschine für das Setzen von Stationen.
/// </summary>
public sealed class StationManager
{
    private readonly MapCamera _cam;
    private RoadService? _roads;
    private readonly SpriteFont _font;
    private readonly SpriteBatch _sbUI;

    private readonly IconButton _btnMain;
    private readonly IconButton _btnBus, _btnTram, _btnMetro;
    private Texture2D? _miniIcon; // kleine Vorschau (16×16)
    private StationType _currentType = StationType.None;

    private Texture2D _pixelTexture;
    private readonly GraphicsDevice _gd;

    private readonly List<Station> _stations = new();

    /* ---------- textbox ---------- */
    private string _nameBuffer = string.Empty;
    private Vector2 _pendingPos; // Weltpos der zu setzenden Station

    private const int BTN = 48; // Ziel-Kantenlänge aller Icons
    private const int GAP = 8; // vertikaler Abstand

    private enum State
    {
        Idle,
        ChoosingType,
        AwaitMapClick,
        AwaitName
    }

    private State _state = State.Idle;

    public StationManager(MapCamera cam, RoadService? roads,
        Texture2D mainOff, Texture2D mainOn,
        Texture2D busOff, Texture2D busOn,
        Texture2D tramOff, Texture2D tramOn,
        Texture2D metroOff, Texture2D metroOn,
        SpriteFont font, SpriteBatch sb, Texture2D pixelTexture, GraphicsDevice gd)
    {
        _cam = cam;
        _roads = roads;
        _font = font;
        _sbUI = sb;

        _pixelTexture = pixelTexture;
        _gd = gd;

        // linke obere Ecke
        var rMain = new Rectangle(20, 20, BTN, BTN);
        var rBus = new Rectangle(20, rMain.Bottom + GAP, BTN, BTN);
        var rTram = new Rectangle(20, rBus.Bottom + GAP, BTN, BTN);
        var rMetro = new Rectangle(20, rTram.Bottom + GAP, BTN, BTN);

        _btnMain = new IconButton(mainOff, mainOn, rMain);
        _btnBus = new IconButton(busOff, busOn, rBus) { Visible = false };
        _btnTram = new IconButton(tramOff, tramOn, rTram) { Visible = false };
        _btnMetro = new IconButton(metroOff, metroOn, rMetro) { Visible = false };
    }

    public void SetRoadService(RoadService r)
    {
        _roads = r;
    }

    /* ========================== UPDATE ==================================== */
    private MouseState _prevMs;

    public void Update(GameTime gt)
    {
        var ms = Mouse.GetState();

        // UI‑Buttons immer updaten
        _btnMain.Update(ms, _prevMs);
        _btnBus.Update(ms, _prevMs);
        _btnTram.Update(ms, _prevMs);
        _btnMetro.Update(ms, _prevMs);

        switch (_state)
        {
            case State.Idle:
                if (_btnMain.ClickedThisFrame) EnterChoosingType();
                break;

            case State.ChoosingType:
                if (_btnBus.ClickedThisFrame) SelectType(StationType.Bus, _btnBus);
                else if (_btnTram.ClickedThisFrame) SelectType(StationType.Tram, _btnTram);
                else if (_btnMetro.ClickedThisFrame) SelectType(StationType.Subway, _btnMetro);
                break;

            case State.AwaitMapClick:
                if (ms.LeftButton == ButtonState.Pressed && _prevMs.LeftButton == ButtonState.Released)
                {
                    if (!IsMouseOnUI(ms.Position))
                    {
                        _pendingPos = _cam.ScreenToWorld(ms.Position.ToVector2());
                        _nameBuffer = GuessStreetName(_pendingPos);
                        _state = State.AwaitName;
                    }
                }
                break;

            case State.AwaitName:
                HandleTextboxInput();
                break;
        }

        _prevMs = ms;
    }

    /* ========================== STATE HELPERS ============================= */
    private void EnterChoosingType()
    {
        _state = State.ChoosingType;
        _btnMain.Active = true;
        _btnBus.Visible = _btnTram.Visible = _btnMetro.Visible = true;
    }

    private void SelectType(StationType st, IconButton srcBtn)
    {
        _currentType = st;
        _miniIcon = srcBtn.Active ? srcBtn.GetActiveTexture : srcBtn.GetIncativeTexture; // have off/on same size

        // UI zurückfahren
        _btnBus.Visible = _btnTram.Visible = _btnMetro.Visible = false;

        _state = State.AwaitMapClick;
    }

    private void ExitToIdle()
    {
        _state = State.Idle;
        _btnMain.Active = false;
        _miniIcon = null;
        _currentType = StationType.None;
    }

    /* ========================== INPUT UTILS =============================== */
    private void HandleTextboxInput()
    {
        var ks = Keyboard.GetState();
        foreach (var key in ks.GetPressedKeys())
            if (!_prevKs.IsKeyDown(key))
            {
                if (key == Keys.Enter)
                {
                    CommitStation();
                    ExitToIdle();
                    break;
                }

                if (key == Keys.Back && _nameBuffer.Length > 0)
                {
                    _nameBuffer = _nameBuffer[..^1];
                }
                else
                {
                    var ch = KeysToChar(key, ks);
                    if (ch != '\0') _nameBuffer += ch;
                }
            }

        _prevKs = ks;
    }

    private KeyboardState _prevKs;

    private static char KeysToChar(Keys k, KeyboardState ks)
    {
        var shift = ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift);
        return k switch
        {
            >= Keys.A and <= Keys.Z => (char)('a' + (k - Keys.A) + (shift ? -32 : 0)),
            >= Keys.D0 and <= Keys.D9 => "0123456789"[k - Keys.D0],
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            _ => '\0'
        };
    }

    private bool IsMouseOnUI(Point p)
    {
        return _btnMain.Bounds.Contains(p) ||
               (_btnBus.Visible && _btnBus.Bounds.Contains(p)) ||
               (_btnTram.Visible && _btnTram.Bounds.Contains(p)) ||
               (_btnMetro.Visible && _btnMetro.Bounds.Contains(p));
    }

    /* ========================== CORE ACTIONS ============================== */
    private void CommitStation()
    {
        _stations.Add(new Station(_pendingPos, _nameBuffer, _currentType));
    }

    private string GuessStreetName(Vector2 wPos)
    {
        if (_roads == null) return "Station";
        var qt = _roads.GetQuadtree();
        if (qt == null) return "Station";
        var rad = 30f / _cam.Zoom;
        var near = qt.Query(new RectangleF(wPos.X - rad, wPos.Y - rad, rad * 2, rad * 2));
        string? best = null;
        var bestDist = float.MaxValue;
        foreach (var rd in near)
        {
            if (string.IsNullOrWhiteSpace(rd.Name)) continue;
            foreach (var seg in rd.Lines)
            foreach (var pt in seg)
            {
                var d = Vector2.DistanceSquared(pt, wPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = rd.Name;
                }
            }
        }

        return best ?? "Station";
    }

    /* ========================== DRAW ===================================== */
    public void Draw()
    {

        /* 1) Stationen in Weltkoordinaten  */
        _sbUI.Begin(transformMatrix: _cam.TransformMatrix,
                     samplerState: SamplerState.AnisotropicClamp);

        foreach (var st in _stations)
        {
            float r = 6f / _cam.Zoom;                    // ~6 px unabhängig vom Zoom
            Color col = st.Modes switch                  // einfache Farb-Mappung
            {
                StationType.Bus => Color.Gold,
                StationType.Tram => Color.Orange,
                StationType.Subway => Color.DeepSkyBlue,
                StationType.Bus | StationType.Tram => Color.LightGreen,
                StationType.Bus | StationType.Subway => Color.LightSkyBlue,
                StationType.Tram | StationType.Subway => Color.Violet,
                _ => Color.White
            };

            _sbUI.DrawCircle(new CircleF(st.Position, r), 12, col, r);
        }
        _sbUI.End();

        _sbUI.Begin();
        _btnMain.Draw(_sbUI);
        _btnBus.Draw(_sbUI);
        _btnTram.Draw(_sbUI);
        _btnMetro.Draw(_sbUI);

        // mini‑icon overlay --------------------------------------------------
        if (_miniIcon != null)
        {
            var r = _btnMain.Bounds;
            var dest = new Rectangle(r.Right - 18, r.Bottom - 18, 16, 16);
            _sbUI.Draw(_miniIcon, dest, Color.White);
        }

        if (_state == State.AwaitName)
        {
            // simple textbox
            var txt = _nameBuffer + "|"; // caret
            var sz = _font.MeasureString(txt) + new Vector2(10, 6);
            var screen = _cam.WorldToScreen(_pendingPos) + new Vector2(0, -30);
            var rect = new Rectangle((int)screen.X, (int)screen.Y, (int)sz.X, (int)sz.Y);
            _sbUI.Draw(_pixelTexture,
                new Rectangle(rect.X - 2, rect.Y - 2,
                    rect.Width + 4, rect.Height + 4),
                Color.White);
            _sbUI.Draw(_pixelTexture, rect, Color.Black * 0.7f);
            _sbUI.DrawString(_font, txt, new Vector2(rect.X + 5, rect.Y + 3), Color.White);
        }

        _sbUI.End();
    }
}