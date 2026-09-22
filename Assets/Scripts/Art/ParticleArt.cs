using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// Textures for particles, in the game's pixel style. A particle quad shows its
    /// whole texture, so the shape of a glow or a puff is the shape of its texture:
    /// on a flat white square every flash and every wisp of smoke was a square.
    /// </summary>
    public static class ParticleArt
    {
        /// <summary>A round glow in three hard steps of brightness (white; the particle tints it).</summary>
        public static Texture GlowDot => PixelCanvas.Glow("fx_glow", 16, new Color32(255, 255, 255, 255), 16f).texture;

        /// <summary>A round puff: solid in the middle, a dithered rim so its edge breaks up like smoke.</summary>
        public static Texture Puff
        {
            get
            {
                const string key = "fx_puff";
                if (PixelCanvas.TryGet(key, out Sprite s)) return s.texture;
                var c = new PixelCanvas(12, 12);
                c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 6f, 6f, 6f, 6f), (x, y) =>
                {
                    bool rim = !PixelCanvas.InEllipse(x, y, 6f, 6f, 4.6f, 4.6f);
                    if (rim && ((x + y) & 1) == 0) return PixelCanvas.Clear;
                    return new Color32(255, 255, 255, (byte)(rim ? 170 : 255));
                });
                return c.Bake(key, 12f, new Vector2(0.5f, 0.5f)).texture;
            }
        }
    }
}
