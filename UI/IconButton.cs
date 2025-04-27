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
    public sealed class IconButton
    {
        public Rectangle Bounds;          // im Screen‑Space
        private readonly Texture2D _off, _on;
        public bool Active { get; set; }
        public bool Visible { get; set; } = true;
        public bool ClickedThisFrame { get; private set; }

        public Texture2D GetIncativeTexture => _off;
        public Texture2D GetActiveTexture => _on;
        public IconButton(Texture2D off, Texture2D on, Rectangle rect)
        {
            _off = off; _on = on; Bounds = rect;
        }
        public void Update(MouseState ms, MouseState prev)
        {
            ClickedThisFrame = false;
            if (!Visible) return;
            bool over = Bounds.Contains(ms.Position);
            bool press = ms.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
            if (over && press) { Active = !Active; ClickedThisFrame = true; }
        }
        public void Draw(SpriteBatch sb)
        {
            if (!Visible) return;
            var tex = Active ? _on : _off;
            sb.Draw(tex, Bounds, Color.White);
        }
    }
}
