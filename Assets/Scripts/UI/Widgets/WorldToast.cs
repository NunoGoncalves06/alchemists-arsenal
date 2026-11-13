using TMPro;
using UnityEngine;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// One line of text in the arena, over a fighter's head, that follows them: the
    /// HUD's "Nature bomb — 34 dmg to 2" for that fighter's own last bomb. It is not
    /// parented to the fighter (their art flips and scales), it just tracks them in
    /// LateUpdate, and hides itself once they are gone (withdrawn, fallen, destroyed).
    /// </summary>
    public sealed class WorldToast : MonoBehaviour
    {
        /// <summary>Above the health bar (which sits at +1.25).</summary>
        public const float Lift = 1.75f;

        private Transform _follow;
        private Combat.CombatantBody _body;
        private TextMeshPro _text;
        private Material _ownMaterial;

        /// <summary>The line on show, or null while hidden.</summary>
        public string Text => _text != null && _text.enabled ? _text.text : null;

        /// <summary>Where the toast is, in the world.</summary>
        public Vector3 Position => transform.position;

        /// <summary>A hidden toast for <paramref name="follow"/>, a sibling of it in the arena.</summary>
        public static WorldToast For(Transform follow)
        {
            var go = new GameObject("WorldToast");
            if (follow != null && follow.parent != null) go.transform.SetParent(follow.parent, false);
            var toast = go.AddComponent<WorldToast>();
            toast._follow = follow;
            toast._body = follow != null ? follow.GetComponent<Combat.CombatantBody>() : null;
            var t = toast._text = go.AddComponent<TextMeshPro>();
            t.font = UITheme.Mono;
            t.fontSize = 2.4f;
            t.alignment = TextAlignmentOptions.Bottom;
            t.color = UITheme.TextHi;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(6f, 0.6f);
            t.rectTransform.pivot = new Vector2(0.5f, 0f);
            t.sortingOrder = 60;   // over the fighters, monsters and their bars
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(21, 15, 25, 255);
            // The outline needs this text's own material instance; keep it to free it with the toast.
            toast._ownMaterial = t.fontMaterial;
            if (t.font != null && toast._ownMaterial == t.font.material) toast._ownMaterial = null;
            t.enabled = false;
            toast.Follow();
            return toast;
        }

        /// <summary>Shows <paramref name="richText"/> until the next line or until the fighter is gone.</summary>
        public void Set(string richText)
        {
            if (_text == null || !Alive()) return;
            _text.text = richText;
            _text.enabled = true;
            Follow();
        }

        private bool Alive()
        {
            if (_follow == null || !_follow.gameObject.activeInHierarchy) return false;
            return (object)_body == null || (_body != null && _body.IsAlive && !_body.IsWithdrawn);
        }

        private void Follow()
        {
            if (_follow != null) transform.position = _follow.position + new Vector3(0f, Lift, 0f);
        }

        // The outline gives this text its own material instance: it goes with the toast.
        private void OnDestroy()
        {
            if (_ownMaterial != null) Destroy(_ownMaterial);
        }

        private void LateUpdate()
        {
            if (!Alive())
            {
                if (_text != null && _text.enabled) _text.enabled = false;
                if (_follow == null) Destroy(gameObject);
                return;
            }
            Follow();
        }
    }
}
