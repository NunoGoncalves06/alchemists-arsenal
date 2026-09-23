using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The moment an upgrade is bought: the card flashes white, pops and settles, and
    /// an INSTALLED stamp slams onto it, holds, and lifts off again (the card's own
    /// button says INSTALLED from then on). Runs on unscaled time (the Evening is
    /// paused time) and removes what it added when it is done.
    /// </summary>
    public class UpgradeReveal : MonoBehaviour
    {
        private const float PopSeconds = 0.9f, StampHold = 1.1f, StampFade = 0.4f;

        private Image _flash;
        private TextMeshProUGUI _stamp;
        private float _t;

        public static void Play(RectTransform card)
        {
            if (card == null) return;
            var r = card.gameObject.AddComponent<UpgradeReveal>();
            r.Build(card);
        }

        private void Build(RectTransform card)
        {
            _flash = UIFactory.Panel(card, new Color(1f, 0.95f, 0.8f, 0.9f), "Flash");
            UIFactory.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;

            _stamp = UIFactory.Label(card, "INSTALLED", UITheme.SizeHeading, UITheme.Ok, TextAlignmentOptions.Center, true);
            UIFactory.Place(_stamp.rectTransform, 0.30f, 0.30f, 0.98f, 0.72f);
            _stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            _stamp.raycastTarget = false;
            _stamp.color = new Color(UITheme.Ok.r, UITheme.Ok.g, UITheme.Ok.b, 0f);
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / PopSeconds);
            // A pop that overshoots and settles.
            float pop = 1f + 0.14f * Mathf.Sin(k * Mathf.PI) * (1f - k);
            transform.localScale = new Vector3(pop, pop, 1f);
            if (_flash != null)
            {
                _flash.color = new Color(1f, 0.95f, 0.8f, 0.9f * (1f - k));
                if (k >= 1f) { Destroy(_flash.gameObject); _flash = null; }
            }
            if (_stamp != null)
            {
                float slam = Mathf.Clamp01(_t / (PopSeconds / 3f));
                float lift = Mathf.Clamp01((_t - StampHold) / StampFade);
                _stamp.color = new Color(UITheme.Ok.r, UITheme.Ok.g, UITheme.Ok.b, slam * (1f - lift));
                float s = Mathf.Lerp(1.6f, 1f, slam) + 0.15f * lift;
                _stamp.rectTransform.localScale = new Vector3(s, s, 1f);
                if (lift >= 1f) { Destroy(_stamp.gameObject); _stamp = null; }
            }
            if (k >= 1f && _stamp == null)
            {
                transform.localScale = Vector3.one;
                enabled = false;
            }
        }
    }
}
