using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>The road's monster families (recoloured per element by Monster()).</summary>
    public static partial class PixelSprites
    {
        // 16 x 16 — Bark Treant (nature)
        private static readonly string[] TREANT =
        {
            "................",
            "....KK...KK.....",
            "...KnnK.KnnK....",
            "...KnNnKnNnK....",
            "..KnNNnnnNNnK...",
            "..KnNnnnnnNnK...",
            "..KnNnKKKnNnK...",
            "..KnNnKGKnNnK...",
            "..KnNnnKnnNnK...",
            "...KnNnnnNnK....",
            "...KwwNNNwwK....",
            "...KwWwnwWwK....",
            "...KwWwwwWwK....",
            "...KwwK.KwwK....",
            "..KwwK...KwwK...",
            "..KKK.....KKK...",
        };

        // 13 x 14 — Emberling (fire imp)
        private static readonly string[] EMBERLING =
        {
            "......YY.....",
            ".....YRRY....",
            "....KRRRRK...",
            "...KRrRRrRK..",
            "..KRRRKRRRK..",
            "..KRRKYKRRK..",
            "..KRRRRRRRK..",
            "..KRrRRRRrK..",
            "...KRRRRRK...",
            "....KRKRK....",
            "....KRK.KRK..",
            "...KRK...KRK.",
            "...KK.....KK.",
            ".............",
        };

        // 13 x 14 — Frostkin (water/ice)
        private static readonly string[] FROSTKIN =
        {
            "......CC.....",
            ".....CBBC....",
            "....KBBBBK...",
            "...KBbBBbBK..",
            "..KBBBKBBBK..",
            "..KBBKCKBBK..",
            "..KBBBBBBBK..",
            "..KBbBBBBbK..",
            "..KBBBBBBBK..",
            "...KBCBCBK...",
            "...KBK.KBK...",
            "..KBK...KBK..",
            "..KK.....KK..",
            ".............",
        };

        // 13 x 14 — Coven Acolyte (arcane)
        private static readonly string[] ACOLYTE =
        {
            "......KK.....",
            ".....KppK....",
            "....KpMMpK...",
            "...KpMMMMpK..",
            "...KpMsMspK..",
            "...KpMMMMpK..",
            "..KppMMMMppK.",
            "..KpPPPPPPpK.",
            "..KpPpPPpPpK.",
            "..KpPPPPPPpK.",
            "...KpPPPPpK..",
            "...KppKKppK..",
            "..KKK...KKK..",
            ".............",
        };

        // 13 x 13 — Miremaw (poison)
        private static readonly string[] MIREMAW =
        {
            ".............",
            "...KK...KK...",
            "..KppK.KppK..",
            "..KpMpKpMpK..",
            ".KppMMpMMppK.",
            ".KpMMMMMMMpK.",
            ".KpMKMKMKMpK.",
            ".KpMMMMMMMpK.",
            ".KpMHKHKHMpK.",
            "..KpMMMMMpK..",
            "..KppKpKppK..",
            "..KK..K..KK..",
            ".............",
        };
    }
}
