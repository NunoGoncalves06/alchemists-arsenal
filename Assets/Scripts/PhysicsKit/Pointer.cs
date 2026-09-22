using UnityEngine;
using UnityEngine.EventSystems;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// The one place world interactions read the pointer from. Everything that is
    /// stirred, dragged or poured asks here instead of reading
    /// <see cref="Input.mousePosition"/> directly, for one reason: the headless
    /// playtest has no mouse. With <see cref="Scripted"/> set, the harness drives
    /// real stirring, dragging and pouring through the same code path a player
    /// uses, instead of skipping the stations with a quality adjustment.
    /// </summary>
    public static class Pointer
    {
        /// <summary>Non-null while something (the headless playtest) is steering the pointer.</summary>
        public static ScriptedPointer Scripted;

        public static bool IsScripted => Scripted != null;

        /// <summary>Pointer position in the world as seen by <paramref name="cam"/>.</summary>
        public static Vector2 World(Camera cam)
        {
            if (Scripted != null) return Scripted.World;
            if (cam == null) return Vector2.zero;
            Vector3 p = Input.mousePosition;
            p.z = Mathf.Abs(cam.transform.position.z);
            return cam.ScreenToWorldPoint(p);
        }

        public static bool Held => Scripted != null ? Scripted.Held : Input.GetMouseButton(0);
        public static bool PressedThisFrame => Scripted != null ? Scripted.PressedThisFrame : Input.GetMouseButtonDown(0);
        public static bool ReleasedThisFrame => Scripted != null ? Scripted.ReleasedThisFrame : Input.GetMouseButtonUp(0);

        /// <summary>
        /// True while the real mouse is over a UI element that takes clicks, so a
        /// click on a button never also grabs whatever world object sits behind it.
        /// A scripted pointer is never "over UI".
        /// </summary>
        public static bool OverUI =>
            Scripted == null && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>A pointer the harness moves in world space and presses by hand.</summary>
    public sealed class ScriptedPointer
    {
        public Vector2 World;
        public bool Held { get; private set; }

        private int _pressedFrame = -1, _releasedFrame = -1;

        // Edges land on the NEXT frame: the harness runs in a coroutine, after this
        // frame's Updates have already read the pointer.
        public bool PressedThisFrame => Time.frameCount == _pressedFrame;
        public bool ReleasedThisFrame => Time.frameCount == _releasedFrame;

        public void Press() { Held = true; _pressedFrame = Time.frameCount + 1; }
        public void Release() { Held = false; _releasedFrame = Time.frameCount + 1; }
    }
}
