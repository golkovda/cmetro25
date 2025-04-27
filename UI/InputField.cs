using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace cmetro25.UI
{
    /* =========================================================================
 *  Minimales Text‑Input‑Feld (UI)
 * =========================================================================*/
    internal sealed class InputField
    {
        public string Text { get; private set; } = string.Empty;
        private readonly Rectangle _box;
        private readonly SpriteFont _font;
        private readonly Texture2D _px;
        private bool _focused;

        public InputField(Rectangle box, SpriteFont font, Texture2D px)
        { _box = box; _font = font; _px = px; }

        public void Clear() { Text = string.Empty; _focused = false; }
        public void SetText(string t) { Text = t; _focused = true; }

        /// <summary>
        /// Liefert true wenn Enter gedrückt wurde → Eingabe fertig.
        /// </summary>
        public bool Update(MouseState ms, KeyboardState ks)
        {
            if (ms.LeftButton == ButtonState.Pressed && !_focused && _box.Contains(ms.Position))
                _focused = true;

            if (!_focused) return false;

            foreach (var key in ks.GetPressedKeys())
            {
                if (key == Keys.Enter) { _focused = false; return true; }
                if (key == Keys.Back && Text.Length > 0) Text = Text[..^1];
                char c = KeyToChar(key, ks);
                if (c != '\0') Text += c;
            }
            return false;
        }

        public void Draw(SpriteBatch sb)
        {
            sb.Draw(_px, _box, Color.Black * 0.6f);
            sb.DrawString(_font, Text + (_focused ? "|" : string.Empty),
                          new Vector2(_box.X + 4, _box.Y + 4), Color.White);
        }

        /* -- naive Tastatur → char Umwandlung ------------------- */
        private static char KeyToChar(Keys k, KeyboardState ks)
        {
            bool shift = ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift);
            return k switch
            {
                >= Keys.A and <= Keys.Z => (char)('a' + (k - Keys.A) + (shift ? -32 : 0)),
                >= Keys.D0 and <= Keys.D9 => (char)('0' + (k - Keys.D0)),
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPeriod => '.',
                _ => '\0'
            };
        }
    }
}
