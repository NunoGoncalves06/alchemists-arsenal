using System;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Reference <see cref="ICombatant"/> + <see cref="IDamageable"/> component for
    /// adventurers and monsters. Reads position/velocity straight off its
    /// Rigidbody2D, registers with the right side's registry, and (if a sibling
    /// <see cref="IElementalWardProvider"/> is present) applies elemental damage
    /// reduction while a ward is active.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class CombatantBody : MonoBehaviour, ICombatant, IDamageable
    {
        [SerializeField] private Team team = Team.Monster;
        [SerializeField] private ElementType element = ElementType.Nature;
        [Min(1)] [SerializeField] private int maxHP = 60;
        [SerializeField] private int currentHP = -1; // -1 => start at maxHP

        private Rigidbody2D _rb;
        private IElementalWardProvider _ward; // optional

        public int CurrentHP => currentHP;
        public int MaxHP => maxHP;
        public ElementType Element => element;
        public Team Team => team;
        public Vector2 Position => _rb != null ? _rb.position : (Vector2)transform.position;
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;
        public bool IsAlive => currentHP > 0;

        public event Action<CombatantBody> OnDied;

        /// <summary>Raised on every applied hit (after mitigation) with the amount actually dealt.</summary>
        public event Action<DamageInfo> OnDamaged;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _ward = GetComponent<IElementalWardProvider>();
            if (currentHP < 0) currentHP = maxHP;
        }

        private void OnEnable()
        {
            if (team == Team.Monster) MonsterRegistry.Register(this);
            else AdventurerRegistry.Register(this);
        }

        private void OnDisable()
        {
            if (team == Team.Monster) MonsterRegistry.Unregister(this);
            else AdventurerRegistry.Unregister(this);
        }

        /// <summary>Copy stats from a <see cref="MonsterData"/> definition (spawn-time).</summary>
        public void InitialiseFrom(MonsterData data)
        {
            if (data == null) return;
            element = data.Element;
            maxHP = data.MaxHealth;
            currentHP = data.MaxHealth;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive) return;

            int amount = Mathf.Max(0, info.Amount);
            if (_ward != null && _ward.HasActiveWard && _ward.WardElement == info.Element)
                amount = Mathf.RoundToInt(amount * Mathf.Clamp01(_ward.WardMultiplier));

            currentHP = Mathf.Max(0, currentHP - amount);

            OnDamaged?.Invoke(new DamageInfo(amount, info.Element, info.SourcePoint, info.Source));

            if (currentHP > 0) return;

            OnDied?.Invoke(this);
            if (team == Team.Monster) MonsterRegistry.Unregister(this);
            else AdventurerRegistry.Unregister(this);
        }
    }
}
