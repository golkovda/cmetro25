// ----------  UI/Toggle.cs  ----------
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace cmetro25.UI;

/// <summary>Sehr einfache Checkbox (Label links vom Kästchen).</summary>
public sealed class Toggle
{
    public string Label { get; }
    public bool Value { get; private set; }

    private readonly Rectangle _box;
    private readonly Texture2D _px;
    private bool _pressedLastFrame;

    public Toggle(string label, bool initial, Rectangle box, Texture2D px)
    {
        Label = label; Value = initial; _box = box; _px = px;
    }

    /// <returns>true, wenn in diesem Frame umgeschaltet wurde</returns>
    public bool Update(MouseState ms)
    {
        bool within = _box.Contains(ms.X, ms.Y);
        bool clicked = within &&
                       ms.LeftButton == ButtonState.Pressed &&
                       !_pressedLastFrame;
        _pressedLastFrame = ms.LeftButton == ButtonState.Pressed;

        if (clicked) Value = !Value;
        return clicked;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Color colFg, Color colBox)
    {
        sb.Draw(_px, _box, colBox * 0.6f);
        if (Value)
            sb.Draw(_px, new Rectangle(_box.X + 4, _box.Y + 4,
                                       _box.Width - 8, _box.Height - 8),
                    colFg);

        sb.DrawString(font, Label,
                      new Vector2(_box.Right + 6, _box.Y - 2), colFg);
    }
}
