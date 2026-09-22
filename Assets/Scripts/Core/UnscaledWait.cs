using System.Collections;
using UnityEngine;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Waits in unscaled game time, i.e. summed <see cref="Time.unscaledDeltaTime"/>.
    ///
    /// Use this instead of <see cref="WaitForSecondsRealtime"/> for anything the
    /// headless playtest steps through. WaitForSecondsRealtime reads the wall clock,
    /// which <see cref="Time.captureDeltaTime"/> does not touch, so under the
    /// fixed-step harness a screen's timer and the harness's own timeouts drifted
    /// apart, and the result depended on how fast the machine rendered frames.
    /// It is still unaffected by pause and by the fight's speed-up, exactly like
    /// WaitForSecondsRealtime was.
    /// </summary>
    public static class UnscaledWait
    {
        public static IEnumerator Seconds(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                yield return null;
        }
    }
}
