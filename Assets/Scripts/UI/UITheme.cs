using UnityEngine;
using TMPro;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The one palette + type source (DESIGN.md §5.2 tokens). A code stand-in for
    /// the <c>UITheme</c> ScriptableObject until the art pass — no hard-coded hex
    /// anywhere else in the UI.
    /// </summary>
    public static class UITheme
    {
        // chrome
        public static readonly Color Ink900     = Hex(0x1b141f);
        public static readonly Color Ink800     = Hex(0x241a2b);
        public static readonly Color Ink700     = Hex(0x33263c);
        public static readonly Color Parchment  = Hex(0xefe2c4);
        public static readonly Color ParchmentDim = Hex(0xc9b892);
        public static readonly Color Wood       = Hex(0x6b4a2f);
        public static readonly Color WoodDark   = Hex(0x3f2c1c);
        public static readonly Color Candle     = Hex(0xe8b64c);
        public static readonly Color CandleHot  = Hex(0xf6d873);
        public static readonly Color Witch      = Hex(0x7b4d9e);
        public static readonly Color Danger     = Hex(0xd64550);
        public static readonly Color Ok         = Hex(0x4fae5a);

        // elements
        public static readonly Color Nature = Hex(0x5ea637);
        public static readonly Color Fire   = Hex(0xe2683a);
        public static readonly Color Water  = Hex(0x3f8fd0);
        public static readonly Color Poison = Hex(0xb6c33f);
        public static readonly Color Arcane = Hex(0xc451a8);

        public static Color Element(Combat.ElementType e) => e switch
        {
            Combat.ElementType.Fire => Fire,
            Combat.ElementType.Water => Water,
            Combat.ElementType.Nature => Nature,
            Combat.ElementType.Poison => Poison,
            Combat.ElementType.Arcane => Arcane,
            _ => ParchmentDim,
        };

        public static Color GradeColor(Combat.PotionGrade g) => g switch
        {
            Combat.PotionGrade.Perfect => Ok,
            Combat.PotionGrade.Great => Candle,
            Combat.PotionGrade.Okay => Hex(0xd9902e),
            _ => Danger,
        };

        private static TMP_FontAsset _font;
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font == null) _font = TMP_Settings.defaultFontAsset;
                return _font;
            }
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
