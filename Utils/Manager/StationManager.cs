using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cmetro25.Core;
using cmetro25.Models;
using cmetro25.Models.Enums;
using cmetro25.Services;
using cmetro25.UI;
using cmetro25.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.Graphics;

namespace cmetro25.Utils.Manager
{
    /// <summary>
    /// High‑Level‑Zustandsmaschine für das Setzen von Stationen.
    /// </summary>
    public sealed class StationManager
    {
        private readonly MapCamera _cam;
    private RoadService? _roads;
    private readonly SpriteFont _font;
    private readonly SpriteBatch _sbUI;

    private readonly IconButton _btnMain;
    private readonly IconButton _btnBus, _btnTram, _btnMetro;
    private Texture2D? _miniIcon;               // kleine Vorschau (16×16)
    private StationType _currentType = StationType.None;

    private Texture2D _pixelTexture;
    private GraphicsDevice _gd;

    private readonly List<Station> _stations = new();

    /* ---------- textbox ---------- */
    private string _nameBuffer = string.Empty;
    private Vector2 _pendingPos;    // Weltpos der zu setzenden Station

    private enum State { Idle, ChoosingType, AwaitMapClick, AwaitName }
    private State _state = State.Idle;

    public StationManager(MapCamera cam, RoadService? roads,
                          Texture2D mainOff,  Texture2D mainOn,
                          Texture2D busOff,   Texture2D busOn,
                          Texture2D tramOff,  Texture2D tramOn,
                          Texture2D metroOff, Texture2D metroOn,
                          SpriteFont font, SpriteBatch sb, Texture2D pixelTexture, GraphicsDevice gd) {
        _cam   = cam;  _roads = roads; _font = font;
        _sbUI  = sb;

        _pixelTexture = pixelTexture;
        _gd = gd;

            // simple absolute layout (links oben)
            var rMain  = new Rectangle(20, 20, mainOff.Width,  mainOff.Height);
        var rBus   = new Rectangle(20, 80, busOff.Width,   busOff.Height);
        var rTram  = new Rectangle(20, 80 + busOff.Height + 10, tramOff.Width, tramOff.Height);
        var rMetro = new Rectangle(20, 80 + busOff.Height + tramOff.Height + 20,
                                    metroOff.Width, metroOff.Height);

        _btnMain  = new IconButton(mainOff,  mainOn,  rMain);
        _btnBus   = new IconButton(busOff,   busOn,   rBus)   { Visible = false };
        _btnTram  = new IconButton(tramOff,  tramOn,  rTram)  { Visible = false };
        _btnMetro = new IconButton(metroOff, metroOn, rMetro) { Visible = false };
    }

    public void SetRoadService(RoadService r) => _roads = r;

    /* ========================== UPDATE ==================================== */
    private MouseState _prevMs;
    public void Update(GameTime gt) {
        var ms = Mouse.GetState();

        // UI‑Buttons immer updaten
        _btnMain.Update(ms, _prevMs);
        _btnBus.Update(ms, _prevMs);
        _btnTram.Update(ms, _prevMs);
        _btnMetro.Update(ms, _prevMs);

        switch (_state) {
            case State.Idle:
                if (_btnMain.ClickedThisFrame) EnterChoosingType();
                break;

            case State.ChoosingType:
                if      (_btnBus.ClickedThisFrame)   SelectType(StationType.Bus,   _btnBus);
                else if (_btnTram.ClickedThisFrame)  SelectType(StationType.Tram,  _btnTram);
                else if (_btnMetro.ClickedThisFrame) SelectType(StationType.Subway, _btnMetro);
                break;

            case State.AwaitMapClick:
                if (ms.LeftButton == ButtonState.Released && _prevMs.LeftButton == ButtonState.Pressed) {
                    // Prüfen: Klick darf nicht auf UI liegen
                    if (!IsMouseOnUI(ms.Position)) {
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
    private void EnterChoosingType() {
        _state = State.ChoosingType;
        _btnMain.Active = true;
        _btnBus.Visible = _btnTram.Visible = _btnMetro.Visible = true;
    }

    private void SelectType(StationType st, IconButton srcBtn) {
        _currentType = st;
        _miniIcon = srcBtn.Active ? srcBtn.GetActiveTexture : srcBtn.GetIncativeTexture; // have off/on same size

        // UI zurückfahren
        _btnBus.Visible = _btnTram.Visible = _btnMetro.Visible = false;

        _state = State.AwaitMapClick;
    }

    private void ExitToIdle() {
        _state = State.Idle;
        _btnMain.Active = false;
        _miniIcon = null;
        _currentType = StationType.None;
    }

    /* ========================== INPUT UTILS =============================== */
    private void HandleTextboxInput() {
        var ks = Keyboard.GetState();
        foreach (var key in ks.GetPressedKeys()) {
            if (!_prevKs.IsKeyDown(key)) {
                if (key == Keys.Enter) {
                    CommitStation();
                    ExitToIdle();
                    break;
                }
                else if (key == Keys.Back && _nameBuffer.Length > 0) _nameBuffer = _nameBuffer[..^1];
                else {
                    char ch = KeysToChar(key, ks); if (ch != '\0') _nameBuffer += ch;
                }
            }
        }
        _prevKs = ks;
    }
    private KeyboardState _prevKs;

    private static char KeysToChar(Keys k, KeyboardState ks) {
        bool shift = ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift);
        return k switch {
            >= Keys.A and <= Keys.Z => (char)('a' + (k - Keys.A) + (shift ? -32 : 0)),
            >= Keys.D0 and <= Keys.D9 => "0123456789"[k - Keys.D0],
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            _ => '\0'
        };
    }

    private bool IsMouseOnUI(Point p) =>
        _btnMain.Bounds.Contains(p) ||
        (_btnBus.Visible   && _btnBus.Bounds.Contains(p)) ||
        (_btnTram.Visible  && _btnTram.Bounds.Contains(p)) ||
        (_btnMetro.Visible && _btnMetro.Bounds.Contains(p));

    /* ========================== CORE ACTIONS ============================== */
    private void CommitStation() {
        _stations.Add(new Station(_pendingPos, _nameBuffer, _currentType));
    }

    private string GuessStreetName(Vector2 wPos) {
        if (_roads == null) return "Station";
        var qt = _roads.GetQuadtree();
        if (qt == null) return "Station";
        float rad = 30f / _cam.Zoom;
        var near = qt.Query(new RectangleF(wPos.X - rad, wPos.Y - rad, rad * 2, rad * 2));
        string? best = null; float bestDist = float.MaxValue;
        foreach (var rd in near) {
            if (string.IsNullOrWhiteSpace(rd.Name)) continue;
            foreach (var seg in rd.Lines) foreach (var pt in seg) {
                float d = Vector2.DistanceSquared(pt, wPos);
                if (d < bestDist) { bestDist = d; best = rd.Name; }
            }
        }
        return best ?? "Station";
    }

    /* ========================== DRAW ===================================== */
    public void Draw() {
        _sbUI.Begin();
        _btnMain.Draw(_sbUI);
        _btnBus.Draw(_sbUI);
        _btnTram.Draw(_sbUI);
        _btnMetro.Draw(_sbUI);

        // mini‑icon overlay --------------------------------------------------
        if (_miniIcon != null) {
            var r = _btnMain.Bounds;
            var dest = new Rectangle(r.Right - 18, r.Bottom - 18, 16, 16);
            _sbUI.Draw(_miniIcon, dest, Color.White);
        }

        if (_state == State.AwaitName) {
            // simple textbox
            var txt = _nameBuffer + "|";   // caret
            var sz = _font.MeasureString(txt) + new Vector2(10, 6);
            var screen = _cam.WorldToScreen(_pendingPos) + new Vector2(0, -30);
            var rect = new Rectangle((int)screen.X, (int)screen.Y, (int)sz.X, (int)sz.Y);
            var pxtx = new Texture2D(_gd, 1, 1, false, SurfaceFormat.Color) { Name = "1px"};
            pxtx.SetData([Color.White]);
            _sbUI.Draw(pxtx, rect, Color.Black * 0.7f);
            _sbUI.Draw(pxtx, new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), Color.White);
            _sbUI.DrawString(_font, txt, new Vector2(rect.X + 5, rect.Y + 3), Color.White);
        }
        _sbUI.End();
    }
    }

}
