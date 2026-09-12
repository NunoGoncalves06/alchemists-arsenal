using UnityEngine;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The visible spoon. Follows the cursor while it is over the pot, tilts into the
    /// direction of the stir, and lifts away when the cursor leaves — so "the spoon
    /// only stirs when the mouse is on the pot" is something the player can see
    /// rather than a rule they have to be told.
    /// </summary>
    public class CauldronSpoon : MonoBehaviour
    {
        [SerializeField] private PhysicsCauldronManager pot;
        [SerializeField] private SpriteRenderer art;
        [SerializeField] private float restHeight = 2.6f;

        private float _tilt;

        public void Configure(PhysicsCauldronManager cauldron, SpriteRenderer renderer)
        {
            pot = cauldron;
            art = renderer;
        }

        private void LateUpdate()
        {
            if (pot == null || art == null) return;

            bool over = pot.MouseOverCauldron;
            art.enabled = true;

            Vector3 target = over ? MouseWorld() : pot.transform.position + Vector3.up * restHeight;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 18f);

            // Lean into the stir — sign of Spin01 says which way it is being turned.
            float wanted = over ? -pot.Spin01 * 28f : 0f;
            _tilt = Mathf.Lerp(_tilt, wanted, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Euler(0f, 0f, _tilt);

            var c = art.color;
            c.a = over ? 1f : 0.55f;
            art.color = c;
        }

        private static Vector3 MouseWorld()
        {
            if (Camera.main == null) return Vector3.zero;
            Vector3 p = Input.mousePosition;
            p.z = Mathf.Abs(Camera.main.transform.position.z);
            Vector3 w = Camera.main.ScreenToWorldPoint(p);
            w.z = 0f;
            return w;
        }
    }
}
