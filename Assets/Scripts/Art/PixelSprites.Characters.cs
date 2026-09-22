using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>People: the starting hero and every customer, as a bust and as a fighter.</summary>
    public static partial class PixelSprites
    {
        // 16 x 18 — Rookie adventurer (red tunic, little sword, round head)
        private static readonly string[] ROOKIE =
        {
            "................",
            ".....KKKK.......",
            "....KSSSSK......",
            "...KSSSSSSK.....",
            "...KSssssSK.....",
            "...KSsKsKsK.....",
            "...KSssssSK.....",
            "....KSssSK......",
            "...KKttttKK...L.",
            "..KtTTTTTTtK..L.",
            "..KtTtTTtTtK.LL.",
            "..sKtTTTTtKs.L..",
            "..sKttttttKs.L..",
            "...KttttttK..K..",
            "...KwwKKwwK.....",
            "...Kww.KwwK.....",
            "..Kww..KwwK.....",
            "..KK....KK......",
        };

        // --- customer busts (16 x 16) ---------------------------------------
        // Each buyer reads by silhouette first: helm crest, hood, cap, witch hat.

        private static readonly string[] BUYER_KNIGHT =
        {
            "................",
            "......tttt......",
            ".....KttttK.....",
            "....KLLLLLLK....",
            "...KLLLLLLLLK...",
            "...KLKKLLKKLK...",
            "...KLLLLLLLLK...",
            "...KLKLLLLKLK...",
            "...KLLKKKKLLK...",
            "....KLLLLLLK....",
            "...KlLLLLLLlK...",
            "..KllLLLLLLllK..",
            "..KlLLLLLLLLlK..",
            ".KllLLLLLLLLllK.",
            ".KlLLLLLLLLLLlK.",
            ".KKKKKKKKKKKKKK.",
        };

        private static readonly string[] BUYER_HERBALIST =
        {
            "................",
            "......KKKK......",
            ".....KnNNnK.....",
            "....KnNNNNnK....",
            "....KnSSSSnK....",
            "....KnSKSKnK....",
            "....KnSSSSnK....",
            ".....KnSSnK.....",
            "....KnnnnnnK....",
            "...KnNNNNNNnK...",
            "..KnNNGGGGNNnK..",
            "..KnNNGHHGNNnK..",
            "..KnNNGGGGNNnK..",
            ".KnNNNNNNNNNNnK.",
            ".KnNNNNNNNNNNnK.",
            ".KKKKKKKKKKKKKK.",
        };

        private static readonly string[] BUYER_MERCHANT =
        {
            "................",
            "....KKKKKKKK....",
            "...KWwwwwwwWK...",
            "...KWWWWWWWWK...",
            "....KSSSSSSK....",
            "....KSKSSKSK....",
            "....KSSSSSSK....",
            "....KSKKKKSK....",
            ".....KSSSSK.....",
            "...KyyyyyyyyK...",
            "..KyYYYYYYYYyK..",
            "..KyYYccccYYyK..",
            "..KyYYccccYYyK..",
            ".KyYYYYYYYYYYyK.",
            ".KyYYYYYYYYYYyK.",
            ".KKKKKKKKKKKKKK.",
        };

        private static readonly string[] BUYER_ENVOY =
        {
            "................",
            ".......pp.......",
            "......pMMp......",
            ".....pMMMMp.....",
            "....pMMMMMMp....",
            "...ppppppppp....",
            "....KsssssK.....",
            "....KsKsKsK.....",
            "....KsssssK.....",
            ".....KsssK......",
            "....KpPPPPpK....",
            "...KpPPPPPPpK...",
            "..KpPPPMMPPPpK..",
            ".KpPPPPMMPPPPpK.",
            ".KpPPPPPPPPPPpK.",
            ".KKKKKKKKKKKKKK.",
        };

        // --- full-body fight views (16 x 18, same build as ROOKIE) -----------

        private static readonly string[] FIGHTER_KNIGHT =
        {
            "................",
            ".....tttt.......",
            "....KttttK......",
            "....KLLLLK......",
            "...KLLLLLLK.....",
            "...KLKLLKLK.....",
            "...KLLLLLLK.....",
            "....KLLLLK......",
            "...KKllllKK...l.",
            "..KlLLLLLLlK..l.",
            "..KlLlLLlLlK..l.",
            "..LKlLLLLlKL..l.",
            "..LKllllllKL..l.",
            "...KllllllK...l.",
            "...KllKKllK...l.",
            "...Kll.KllK.....",
            "..Kll..KllK.....",
            "..KK....KK......",
        };

        private static readonly string[] FIGHTER_HERBALIST =
        {
            "................",
            "......KKKK......",
            ".....KnNNnK.....",
            "....KnNNNNnK....",
            "....KnSSSSnK....",
            "....KnSKSKnK....",
            "....KnSSSSnK....",
            ".....KnSSnK.....",
            "....KnnnnnnK....",
            "...KnNNNNNNnK...",
            "..KnNNGGGGNNnK..",
            "..KnNNGHHGNNnK..",
            "..KnNNGGGGNNnK..",
            "...KnNNNNNNnK...",
            "...KnNNNNNNnK...",
            "...KnnKKKKnnK...",
            "...KwwK..KwwK...",
            "...KK......KK...",
        };

        private static readonly string[] FIGHTER_MERCHANT =
        {
            "................",
            ".....KKKKKK.....",
            "....KWwwwwWK....",
            "....KWWWWWWK....",
            ".....KSSSSK.....",
            ".....KSKSKK.....",
            ".....KSSSSK.....",
            ".....KSKKSK.....",
            "....KyyyyyyK....",
            "...KyYYYYYYyK...",
            "..KyYYccccYYyK..",
            "..KyYYccccYYyK..",
            "..KyYYYYYYYYyK..",
            "...KyYYYYYYyK...",
            "...KyYYYYYYyK...",
            "...KyyKKKKyyK...",
            "...KwwK..KwwK...",
            "...KK......KK...",
        };

        private static readonly string[] FIGHTER_ENVOY =
        {
            "................",
            ".......pp.......",
            "......pMMp......",
            ".....pMMMMp.....",
            "....pMMMMMMp....",
            "...ppppppppp....",
            "....KsssssK.....",
            "....KsKsKsK.....",
            "....KsssssK.....",
            ".....KsssK......",
            "....KpPPPPpK....",
            "...KpPPPPPPpK...",
            "..KpPPPMMPPPpK..",
            "..KpPPPMMPPPpK..",
            "..KpPPPPPPPPpK..",
            "...KpPPPPPPpK...",
            "...KppKKKKppK...",
            "...KK......KK...",
        };
    }
}
