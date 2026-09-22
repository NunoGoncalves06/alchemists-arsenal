using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// Every material the code-built art uses, from one cache.
    ///
    /// <b>One material per texture, never one shared material for every sprite.</b>
    /// That was a real, shipped bug: with a single shared material the batcher groups
    /// renderers and draws the whole batch with one bound texture, so the adventurer
    /// rendered with the monsters' texture ("changing skin" the instant a wave walked
    /// in). This used to be enforced by two parallel caches (PixelSprites and
    /// PlaceholderArt); it is one rule in one place now.
    ///
    /// Particles and trails use two tiny shaders kept in <c>Resources/Shaders</c>, so
    /// player builds always include them (a shader reached only through
    /// <c>Shader.Find</c> can be stripped and render pink in a build but not in the
    /// Editor). They multiply texture by vertex colour, which is exactly what particle
    /// and trail renderers feed them.
    /// </summary>
    public static class SpriteMaterials
    {
        public enum ParticleBlend { Alpha, Additive }

        private static readonly Dictionary<Texture, Material> _sprites = new Dictionary<Texture, Material>();
        private static readonly Dictionary<(Texture, ParticleBlend), Material> _particles =
            new Dictionary<(Texture, ParticleBlend), Material>();
        private static Shader _spriteShader;
        private static Texture2D _white;

        /// <summary>The unlit sprite material for this sprite's texture (2D URP needs an
        /// unlit shader or sprites render black with no Light2D in the scene).</summary>
        public static Material For(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            Texture tex = sprite.texture;
            if (_sprites.TryGetValue(tex, out Material cached) && cached != null) return cached;

            if (_spriteShader == null)
                _spriteShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                                ?? Shader.Find("Sprites/Default");
            if (_spriteShader == null) return null;

            var material = new Material(_spriteShader) { name = "PixelUnlit_" + tex.name, mainTexture = tex };
            _sprites[tex] = material;
            return material;
        }

        /// <summary>A particle / trail material over <paramref name="texture"/> (a flat white pixel if null).</summary>
        public static Material Particle(ParticleBlend blend, Texture texture = null)
        {
            Texture tex = texture != null ? texture : White;
            if (_particles.TryGetValue((tex, blend), out Material cached) && cached != null) return cached;

            Shader shader = Resources.Load<Shader>(blend == ParticleBlend.Additive
                                ? "Shaders/PixelAdditive" : "Shaders/PixelAlpha")
                            ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                            ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = $"Particle{blend}_{tex.name}", mainTexture = tex };
            _particles[(tex, blend)] = material;
            return material;
        }

        /// <summary>A 2x2 opaque white texture: square pixel particles, trails.</summary>
        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px_particle" };
                var px = new Color32[4];
                for (int i = 0; i < 4; i++) px[i] = new Color32(255, 255, 255, 255);
                _white.SetPixels32(px);
                _white.Apply(false, true);
                return _white;
            }
        }
    }
}
