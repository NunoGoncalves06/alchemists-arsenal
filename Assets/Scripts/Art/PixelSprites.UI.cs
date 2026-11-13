using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>Interface iconography: logo, pointer, stars, locks, element badges, the 9-slice frame.</summary>
    public static partial class PixelSprites
    {
        // 16 x 16 witch-hat + moon crest for the boot splash
        private static readonly string[] LOGO =
        {
            "................",
            ".......pp.......",
            "......pMMp......",
            "......pMMp......",
            ".....pMMMMp.....",
            ".....pMMMMp.....",
            "....pMMMMMMp....",
            "...pMMMMMMMMp...",
            "..pMMMMMMMMMMp..",
            ".ppppppppppppp..",
            ".pcccccccccccp..",
            ".ppppppppppppp..",
            "......pKKp......",
            ".....pKKKKp.....",
            "......pKKp......",
            "................",
        };

        // 9 x 5 — downward-pointing tutorial arrow (wide base at top, tip at bottom)
        private static readonly string[] POINTER_ARROW =
        {
            ".KKKKKKK.",
            ".KyyyyyK.",
            "..KyyyK..",
            "...KyK...",
            "....K....",
        };

        // 9 x 9 — result star
        private static readonly string[] STAR =
        {
            "....K....",
            "...KYK...",
            "...KYK...",
            "KKKKYKKKK",
            "KYYYYYYYK",
            ".KYYYYYK.",
            "..KYKYK..",
            ".KYK.KYK.",
            ".K.....K.",
        };

        // 10 x 11 — padlock, for a station that isn't open yet
        private static readonly string[] LOCK =
        {
            "...KKKK...",
            "..KllllK..",
            "..Kl..lK..",
            "..Kl..lK..",
            ".KKKKKKKK.",
            ".KyYYYYyK.",
            ".KyYKKYyK.",
            ".KyYKKYyK.",
            ".KyYYYYyK.",
            ".KKKKKKKK.",
            "..........",
        };

        // 16 x 16 — 9-slice UI panel (wood border, ink fill, thin inner bevel)
        private static readonly string[] PANEL9 =
        {
            "WWWWWWWWWWWWWWWW",
            "WwwwwwwwwwwwwwwW",
            "Ww############wW",
            "Ww#++++++++++#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#++++++++++#wW",
            "Ww############wW",
            "WwwwwwwwwwwwwwwW",
            "WWWWWWWWWWWWWWWW",
            "WWWWWWWWWWWWWWWW",
        };

        // --- element icons: colour + distinct silhouette (a11y) ------------
        private static readonly string[] EI_FIRE =
        {
            "....K....",
            "...KRK...",
            "..KRRRK..",
            "..KRYRK..",
            ".KRRYRRK.",
            ".KRYYYRK.",
            "KRRYYYRRK",
            "KRRRRRRRK",
            ".KKKKKKK.",
        };

        private static readonly string[] EI_WATER =
        {
            "....K....",
            "....K....",
            "...KBK...",
            "..KBBBK..",
            "..KBCBK..",
            ".KBBCBBK.",
            ".KBCCBBK.",
            ".KBBBBBK.",
            "..KKKKK..",
        };

        private static readonly string[] EI_NATURE =
        {
            "..KKKKK..",
            ".KGGGGGK.",
            "KGHGHGHGK",
            "KGGGHGGGK",
            "KGHGHGHGK",
            "KGGGHGGGK",
            "KGHGHGHGK",
            ".KGGGGGK.",
            "..KKKKK..",
        };

        private static readonly string[] EI_POISON =
        {
            "..KKKKK..",
            ".KMMMMMK.",
            "KMMHKHMMK",
            "KMHMMMHMK",
            "KMKMHMKMK",
            "KMHMMMHMK",
            "KMMHKHMMK",
            ".KMMMMMK.",
            "..KKKKK..",
        };

        private static readonly string[] EI_ARCANE =
        {
            "....K....",
            "...KMK...",
            "K..KMK..K",
            ".KKMMMKK.",
            "KMMMMMMMK",
            ".KKMMMKK.",
            "K..KMK..K",
            "...KMK...",
            "....K....",
        };
    }
}
