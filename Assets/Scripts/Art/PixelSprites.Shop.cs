using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>The shop's props: pot, bench tools, ingredients, flasks, the cat.</summary>
    public static partial class PixelSprites
    {
        // 28 x 28 — the bubbling cauldron: fat tapering body, 3 legs, side handles,
        // lighter rim, green brew, rising bubbles. Redrawn on a strict 28-wide grid —
        // the previous version had rows of two different lengths and handles that
        // landed on a different column each row, so the right-hand rim rendered as
        // loose blocks floating off the pot.
        private static readonly string[] CAULDRON =
        {
            ".............HH.............",
            "............HGGH............",
            "............HGGH............",
            ".......HH....HH....HH.......",
            "......HGGH........HGGH......",
            "......HGGH........HGGH......",
            ".......HH..........HH.......",
            ".KKKKKKKKKKKKKKKKKKKKKKKKKK.",
            ".K444444444444444444444444K.",
            ".K333333333333333333333333K.",
            "KK1GHGGGHGGGHGGGHGGGHGGGH1KK",
            "KK1GHHGGHHGGHHGGHHGGHHGGH1KK",
            "KK1KKKKKKKKKKKKKKKKKKKKKK1KK",
            "K23333333333333333333333332K",
            "K23333334433333333333333332K",
            "K23333344443333333333333332K",
            "K23333344433333333333333332K",
            "K23333334333333333333333332K",
            "K23333333333333333333333332K",
            "K23333333333333333333333332K",
            "K23333333333333333333333332K",
            ".K233333333333333333333332K.",
            "..K2222222222222222222222K..",
            "...K22222222222222222222K...",
            ".....K2222222222222222K.....",
            "......K22222222222222K......",
            ".......K2K..K2K...K2K.......",
            ".......K2K..K2K...K2K.......",
        };

        // 10 x 10 coin
        private static readonly string[] COIN =
        {
            "...KKKK...",
            ".KKyyyyKK.",
            ".KyYYYYyK.",
            "KyYYccYYyK",
            "KyYcccYYyK",
            "KyYccYYYyK",
            "KyYYYYYYyK",
            ".KyYYYYyK.",
            ".KKyyyyKK.",
            "...KKKK...",
        };

        // 12 x 12 — sleeping cat for the menu counter
        private static readonly string[] CAT =
        {
            "............",
            "..K......K..",
            ".KKK....KKK.",
            ".K1KKKKKK1K.",
            "K1111111111K",
            "K1111111111K",
            "K11K1111K11K",
            "K1111111111K",
            ".K11111111K.",
            "..KKKKKKKK.K",
            "........KKKK",
            "............",
        };

        // 12 x 12 — counter service bell (the Counter station's icon)
        private static readonly string[] BELL =
        {
            ".....KK.....",
            "....KyyK....",
            "...KyyyyK...",
            "...KyYYyK...",
            "..KyYYYYyK..",
            "..KyYYYYyK..",
            ".KyYYYYYYyK.",
            ".KyYYYYYYyK.",
            "KKKKKKKKKKKK",
            "....KyyK....",
            "....KKKK....",
            "............",
        };

        // 14 x 11 — mortar and pestle (the Prep station's icon)
        private static readonly string[] MORTAR =
        {
            "..........KK..",
            ".........KllK.",
            "........KllK..",
            ".......KllK...",
            "..KKKKKKKKKKK.",
            ".KwWWWWWWWWWwK",
            ".KwWWWWWWWWWwK",
            "..KwWWWWWWWwK.",
            "...KwWWWWWwK..",
            "....KwwwwwK...",
            ".....KKKKK....",
        };

        // 10 x 13 — wooden stirring spoon (follows the cursor over the pot)
        private static readonly string[] SPOON =
        {
            "...KKKK...",
            "..KWWWWK..",
            ".KWwwwwWK.",
            ".KWwwwwWK.",
            "..KWWWWK..",
            "...KWWK...",
            "...KWWK...",
            "...KWWK...",
            "...KWWK...",
            "...KWWK...",
            "...KWWK...",
            "...KWWK...",
            "....KK....",
        };

        // 12 x 12 — herb leaf (green by default; tinted per element by ElementSwap)
        private static readonly string[] HERB =
        {
            "......K.....",
            ".....KGK....",
            "....KGHGK...",
            "...KGHHGK...",
            "..KGHHHGK...",
            "..KGHnHGK...",
            "..KGnHnGK...",
            "...KGnGK....",
            "....KwK.....",
            "....KwK.....",
            "...KwK......",
            "...KK.......",
        };

        // 14 x 16 — potion flask (green fill; tinted per element)
        private static readonly string[] FLASK =
        {
            "....KKKK....",
            "....KccK....",
            "....KccK....",
            "...KKccKK...",
            "..KKc..cKK..",
            "..Kc....cK..",
            ".KKc.HH.cKK.",
            ".KcGHHHHGcK.",
            ".KcGHHHHGcK.",
            ".KcGGHHGGcK.",
            ".KcGGGGGGcK.",
            ".KcGGGGGGcK.",
            ".KKcGGGGcKK.",
            "..KKccccKK..",
            "...KKKKKK...",
            "............",
        };
    }
}
