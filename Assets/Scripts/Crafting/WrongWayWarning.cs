using UnityEngine;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The "wrong way" notice over the cauldron: it appears once the spoon has been turned the
    /// wrong way for a full second without a break, blinks while it is up, and is gone the
    /// moment the stir is right again (or stops). Plain state; <see cref="CauldronView"/> draws it.
    /// </summary>
    public sealed class WrongWayWarning
    {
        /// <summary>How long the wrong way must hold, unbroken, before it is shown.</summary>
        public const float Delay = 1f;

        /// <summary>The words on screen: red letters, four words at most.</summary>
        public const string Text = "Wrong way!";

        private const float BlinkHz = 4f;
        private float _held, _shown;

        public bool Visible => _held >= Delay;

        /// <summary>1 while the letters are lit, low while they blink off.</summary>
        public float Blink01 => !Visible ? 0f : Mathf.Repeat(_shown * BlinkHz, 1f) < 0.55f ? 1f : 0.12f;

        public void Tick(bool backwards, float dt)
        {
            if (!backwards) { _held = 0f; _shown = 0f; return; }
            _held += dt;
            if (Visible) _shown += dt;
        }
    }
}
