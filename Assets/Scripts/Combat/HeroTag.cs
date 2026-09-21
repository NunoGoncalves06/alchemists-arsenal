using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Ties a spawned adventurer back to the <see cref="Data.HeroRecord"/> that
    /// produced it, so events that only have a <see cref="CombatantBody"/> — chiefly
    /// <c>ExpeditionTelemetry.OnDied</c> — can say <i>who</i> went down rather than
    /// just how many.
    ///
    /// Read while the body is still alive in the death callback: bodies linger for
    /// <c>deathLingerSeconds</c> after <c>OnAnyDied</c> fires, so the tag is still
    /// there when it is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public class HeroTag : MonoBehaviour
    {
        public string heroId;
    }
}
