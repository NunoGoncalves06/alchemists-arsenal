using System;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// A leaf in mid-air on its way into the pot: a gravity body until it reaches the
    /// surface, then handed to the liquid. Its sprite is added by the pot's view.
    /// </summary>
    public class FallingIngredient : MonoBehaviour
    {
        public ElementType Element { get; private set; }
        private PhysicsCauldronManager _pot;
        private float _surfaceY;
        private Rigidbody2D _rb;

        public static event Action<FallingIngredient> OnSpawned;

        public void Init(PhysicsCauldronManager pot, ElementType element, float surfaceY)
        {
            _pot = pot;
            Element = element;
            _surfaceY = surfaceY;
            _rb = GetComponent<Rigidbody2D>();
            OnSpawned?.Invoke(this);
        }

        private void FixedUpdate()
        {
            if (_rb == null || _rb.linearVelocity.y > 0f || _rb.position.y > _surfaceY) return;
            if (_pot != null) _pot.Land(Element, _rb.position, _rb.angularVelocity * 0.3f);
            Destroy(gameObject);
        }
    }
}
