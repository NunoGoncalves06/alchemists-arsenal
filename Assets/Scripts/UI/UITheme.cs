using System;
using System.Collections.Generic;
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
        public static readonly Color Ink900     = Hex(0x150f19);
        public static readonly Color Ink800     = Hex(0x1f1726);
        public static readonly Color Ink700     = Hex(0x2c2135);
        public static readonly Color Parchment  = Hex(0xefe2c4);
        public static readonly Color ParchmentDim = Hex(0xc9b892);
        public static readonly Color Wood       = Hex(0x6b4a2f);
        public static readonly Color WoodDark   = Hex(0x3f2c1c);
        public static readonly Color Candle     = Hex(0xe8b64c);
        public static readonly Color CandleHot  = Hex(0xf6d873);
        public static readonly Color Witch      = Hex(0x7b4d9e);
        public static readonly Color Danger     = Hex(0xd64550);
        public static readonly Color Ok         = Hex(0x4fae5a);

        // --- surface tokens (UI redesign) -----------------------------------
        // One ramp for every panel so depth reads consistently: the app ground is
        // darkest, cards sit one step up, and anything interactive sits one more.
        public static readonly Color Ground     = Hex(0x120d16); // behind everything
        public static readonly Color Surface    = Hex(0x1f1726); // cards / docks
        public static readonly Color SurfaceHi  = Hex(0x2c2135); // rows, inputs, inactive tabs
        public static readonly Color SurfaceTop = Hex(0x3a2c46); // hover / selected row
        public static readonly Color Line       = Hex(0x4b3a59); // hairline borders
        public static readonly Color LineSoft   = Hex(0x332640);

        // text roles — never pick a raw palette colour for text
        public static readonly Color TextHi     = Hex(0xf3e8cf);
        public static readonly Color TextMid    = Hex(0xbfae8e);
        public static readonly Color TextLow    = Hex(0x8a7c6b);
        public static readonly Color TextOnGold = Hex(0x1b1118);

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

        /// <summary>Fade a token without hand-writing a Color constructor at the call site.</summary>
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ================================================================
        //  TYPE
        // ================================================================
        //
        // Three roles, three real typefaces — the whole UI used to render in one
        // sans face at whatever size each call site felt like, which is most of
        // why it read as a debug overlay rather than a game.
        //
        //   Display — a serif, for screen titles, the shop sign, big numbers.
        //   Body    — a humanist sans, for everything you actually read.
        //   Mono    — for ledgers, the deduction log and aligned figures.
        //
        // Each is built at runtime from the first FAMILY in its list that the
        // player's OS actually has, and falls back to TMP's own LiberationSans asset
        // — which is also chained as a glyph fallback, so a character the chosen face
        // lacks still renders instead of showing tofu. No font binary is committed;
        // dropping a licensed .ttf into Assets/Fonts and assigning it here is the
        // only change the art pass needs.

        public const int SizeDisplay = 46;
        public const int SizeTitle   = 30;
        public const int SizeHeading = 21;
        public const int SizeBody    = 17;
        public const int SizeSmall   = 14;
        public const int SizeTiny    = 12;

        /// <summary>Tracking (TMP font units) for uppercase headings — caps need air.</summary>
        public const float HeadingTracking = 6f;

        private static readonly string[] DisplayFamilies =
        {
            "Georgia", "Constantia", "Book Antiqua", "Palatino Linotype", "Cambria",
            "Garamond", "Times New Roman", "Serif",
        };

        private static readonly string[] BodyFamilies =
        {
            "Segoe UI", "Tahoma", "Verdana", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Arial",
        };

        private static readonly string[] MonoFamilies =
        {
            "Consolas", "Cascadia Mono", "Lucida Console", "DejaVu Sans Mono", "Courier New",
        };

        private static TMP_FontAsset _display, _body, _mono, _fallback;

        /// <summary>Serif display face — titles, the shop sign, hero numbers.</summary>
        public static TMP_FontAsset Display => _display != null
            ? _display
            : _display = BuildFont("DisplayFont", DisplayFamilies);

        /// <summary>The reading face — every label, button and paragraph.</summary>
        public static TMP_FontAsset Body => _body != null
            ? _body
            : _body = BuildFont("BodyFont", BodyFamilies);

        /// <summary>Fixed-pitch — ledgers, the deduction log, aligned figures.</summary>
        public static TMP_FontAsset Mono => _mono != null
            ? _mono
            : _mono = BuildFont("MonoFont", MonoFamilies);

        /// <summary>Back-compat alias for the pre-redesign single font.</summary>
        public static TMP_FontAsset Font => Body;

        /// <summary>
        /// TMP's own imported asset. Also chained onto every built face as a glyph
        /// fallback so an unusual character renders rather than showing tofu (the
        /// "boxes on the SEND button" playtest bug, from the other direction).
        /// </summary>
        private static TMP_FontAsset Fallback
        {
            get
            {
                if (_fallback != null) return _fallback;
                _fallback = TMP_Settings.defaultFontAsset;
                if (_fallback == null)
                {
                    // TMP Essentials was never imported. Last resort: a plain system
                    // face, so the game still has readable text.
                    foreach (string family in new[] { "Segoe UI", "Arial", "Liberation Sans" })
                    {
                        try { _fallback = TMP_FontAsset.CreateFontAsset(family, "Regular", 90); }
                        catch { /* next */ }
                        if (_fallback != null) break;
                    }
                    if (_fallback == null)
                        Debug.LogWarning("[UITheme] No TMP font available at all — run " +
                                         "Window > TextMeshPro > Import TMP Essential Resources.");
                }
                return _fallback;
            }
        }

        /// <summary>
        /// Build a face from the first family in <paramref name="families"/> that the
        /// player's OS actually has.
        ///
        /// It has to be the family-name overload. The obvious route —
        /// <c>Font.CreateDynamicFontFromOSFont</c> then
        /// <c>TMP_FontAsset.CreateFontAsset(Font)</c> — compiles, runs, returns
        /// non-null, and silently gives you TMP's default face instead: a dynamic OS
        /// Font carries no font data for TMP to load a face from, so it logs "Unable
        /// to load font face for [Georgia]" and hands back a fallback. Every title in
        /// the game rendered in the same sans as the body text (caught by comparing a
        /// headless-playtest screenshot against what the serif should look like).
        /// </summary>
        private static TMP_FontAsset BuildFont(string name, string[] families)
        {
            foreach (string family in families)
            {
                TMP_FontAsset asset = null;
                try { asset = TMP_FontAsset.CreateFontAsset(family, "Regular", 90); }
                catch (Exception e) { Debug.LogWarning($"[UITheme] {name}: '{family}' failed — {e.Message}"); }
                if (asset == null) continue;   // not installed on this machine

                asset.name = $"{name} ({family})";
                TMP_FontAsset fb = Fallback;
                if (fb != null && fb != asset)
                    asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fb };
                return asset;
            }

            Debug.LogWarning($"[UITheme] None of the {name} families are installed — using TMP's default face.");
            return Fallback;
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
