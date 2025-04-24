// ----------  UI/PerformanceUI.cs  ----------
using System.Collections.Generic;
using System.Text;
using cmetro25.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace cmetro25.UI
{
    public sealed class PerformanceUI
    {
        private readonly SpriteFont _font;
        private readonly Texture2D _px;
        private readonly StringBuilder _sb = new(512);

        /* ----------------- GUI-Elemente ----------------- */
        private readonly List<Slider> _sliders = new();
        private readonly List<Toggle> _toggles = new();

        private readonly Toggle _themeToggle;

        public PerformanceUI(SpriteFont font, GraphicsDevice gd)
        {
            _font = font;
            _px = new Texture2D(gd, 1, 1);
            _px.SetData(new[] { Color.White });

            int m = 18;
            _themeToggle = new Toggle("Dark", GameSettings.IsDarkTheme,
                          new Rectangle(250, 170, m, m), _px);

            /* ---------- Slider-Definitions ---------- */
            int x = 20, y = 230, w = 180, h = 4, gap = 40;
            void AddSlider(string key, string label)
            {
                float start = GameSettings.RoadTargetPx.TryGetValue(key, out var v) ? v : 1f;
                _sliders.Add(new Slider(label, 1f, 5f, start,
                               new Rectangle(x, y + _sliders.Count * gap, w, h), _px));
            }
            AddSlider("motorway", "Motorway");
            AddSlider("primary", "Primary");
            AddSlider("trunk", "Trunk");
            AddSlider("secondary", "Secondary");
            AddSlider("tertiary", "Tertiary");
            AddSlider("residential", "Residential");
            AddSlider("unclassified", "Unclassified");

            /* ---------- Layer-Toggles ---------- */
            int tx = 250, ty = 230, tGap = 30, b = 18;
            void AddToggle(ref bool flag, string label)
            {
                _toggles.Add(new Toggle(label, flag,
                             new Rectangle(tx, ty + _toggles.Count * tGap, b, b), _px));
            }
            AddToggle(ref GameSettings.ShowDistricts, "Boundaries");
            AddToggle(ref GameSettings.ShowWaterBodies, "Lakes");
            AddToggle(ref GameSettings.ShowRails, "Rails");
            AddToggle(ref GameSettings.ShowRivers, "Rivers");
            AddToggle(ref GameSettings.ShowRoads, "Roads");
            AddToggle(ref GameSettings.ShowStations, "Stations");
        }

        private MouseState _prevMouse;

        /// <summary>
        /// Aktualisiert Slider & Toggles.  
        /// <br/>Returns `(sliderChanged, toggleChanged)` Flags.</summary>
        public (bool slider, bool toggle) Update()
        {

            var ms = Mouse.GetState();
            var prev = _prevMouse;
            _prevMouse = ms;

            bool themeChanged = _themeToggle.Update(ms);
            if (themeChanged) GameSettings.ToggleTheme();

            bool anySlider = false, anyToggle = false;

            foreach (var s in _sliders)
                if (s.Update(ms, prev))
                    anySlider = true;

            foreach (var t in _toggles)
                if (t.Update(ms))
                    anyToggle = true;

            if (anySlider)
                foreach (var s in _sliders)
                    GameSettings.RoadTargetPx[s.Label.ToLower()] = s.Value;


            // Toggle-Werte direkt in GameSettings flags zurückschreiben
            GameSettings.ShowDistricts = _toggles[0].Value;
            GameSettings.ShowWaterBodies = _toggles[1].Value;
            GameSettings.ShowRails = _toggles[2].Value;
            GameSettings.ShowRivers = _toggles[3].Value;
            GameSettings.ShowRoads = _toggles[4].Value;
            GameSettings.ShowStations = _toggles[5].Value;

            return (anySlider, anyToggle || themeChanged);
        }

        /* ---------- Text & GUI zeichnen ---------- */
        public void Draw(SpriteBatch sb, int fps, int ups,
                         int visTiles, long memMB)
        {
            _sb.Clear();
            _sb.AppendLine($"FPS: {fps}");
            _sb.AppendLine($"UPS: {ups}");
            _sb.AppendLine($"Tiles: {visTiles}");
            _sb.AppendLine($"Mem MB: {memMB}");


            sb.Begin();
            _themeToggle.Draw(sb, _font, Color.White, Color.Gray);
            sb.DrawString(_font, _sb, new Vector2(11, 11), Color.Black * 0.8f);
            sb.DrawString(_font, _sb, new Vector2(10, 10), Color.Yellow);

            foreach (var s in _sliders)
                s.Draw(sb, _font, Color.White, Color.Gray, Color.Orange);

            foreach (var t in _toggles)
                t.Draw(sb, _font, Color.White, Color.Gray);

            sb.End();
        }
    }
}
