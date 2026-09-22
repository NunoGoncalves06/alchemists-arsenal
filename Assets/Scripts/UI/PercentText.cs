using UnityEngine;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Cached "0%".."100%" labels. Gauges refresh every frame, and formatting a new
    /// string each time was a steady allocation for a value that only ever takes
    /// 101 distinct shapes.
    /// </summary>
    public static class PercentText
    {
        private static readonly string[] Cache = Build();

        private static string[] Build()
        {
            var a = new string[101];
            for (int i = 0; i <= 100; i++) a[i] = i + "%";
            return a;
        }

        public static string Of(float value01) => Cache[Mathf.Clamp(Mathf.RoundToInt(value01 * 100f), 0, 100)];
    }
}
