// ----------  UI/Slider.cs  ----------

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace cmetro25.UI;

/// <summary>Einen sehr einfachen 0-bis-3-Slider zeichnen und bedienen.</summary>
public sealed class Slider
{
    public string Label { get; }
    public float Min { get; }
    public float Max { get; }
    public float Value { get; private set; }

    private readonly Rectangle _bar;
    private readonly Texture2D _px;
    private bool _isDragging;

    public Slider(string label, float min, float max,
                  float initial, Rectangle bar, Texture2D px)
    {
        Label = label; Min = min; Max = max;
        Value = MathHelper.Clamp(initial, Min, Max);
        _bar = bar; _px = px;
    }

    public bool Update(MouseState ms, MouseState prev)
    {
        var knobX = _bar.X + (_bar.Width - 10) * ((Value - Min) / (Max - Min));
        var knobRect = new Rectangle((int)knobX, _bar.Y - 4, 10, _bar.Height + 8);

        var mPos = new Point(ms.X, ms.Y);
        bool overKnob = knobRect.Contains(mPos);
        bool overBar = _bar.Contains(mPos);

        if (ms.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released && (overKnob || overBar))
            _isDragging = true;

        if (ms.LeftButton == ButtonState.Released)
            _isDragging = false;

        float old = Value;
        if (_isDragging)
        {
            float t = MathHelper.Clamp((mPos.X - _bar.X) / (float)_bar.Width, 0f, 1f);
            Value = Min + t * (Max - Min);
        }
        return Math.Abs(Value - old) > 0.0001f;   //  ◄◄   true = geändert
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Color colText, Color colBar, Color colKnob)
    {
        // Bar
        sb.Draw(_px, _bar, colBar * 0.4f);
        // Knob
        float kx = _bar.X + (_bar.Width - 10) * ((Value - Min) / (Max - Min));
        sb.Draw(_px, new Rectangle((int)kx, _bar.Y - 4, 10, _bar.Height + 8), colKnob);

        // Label + Wert
        sb.DrawString(font, $"{Label}: {Value:0.00}",
                      new Vector2(_bar.X, _bar.Y - 22), colText);
    }
}
