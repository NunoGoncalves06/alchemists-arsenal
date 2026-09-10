using System;
using UnityEngine;

namespace AlchemistsArsenal.Story
{
    public enum DiaryUnlockKind
    {
        Manual = 0,              // unlocked explicitly (the opening cinematic)
        BiomeCleared = 1,        // param = biome index
        FirstPerfectPotion = 2,
        IngredientDiscovered = 3 // param = herb id
    }

    [Serializable]
    public struct DiaryUnlock
    {
        public DiaryUnlockKind kind;
        public string param;

        public static DiaryUnlock Manual => new DiaryUnlock { kind = DiaryUnlockKind.Manual };
        public static DiaryUnlock Biome(int i) => new DiaryUnlock { kind = DiaryUnlockKind.BiomeCleared, param = i.ToString() };
        public static DiaryUnlock Perfect => new DiaryUnlock { kind = DiaryUnlockKind.FirstPerfectPotion };
        public static DiaryUnlock Ingredient(string id) => new DiaryUnlock { kind = DiaryUnlockKind.IngredientDiscovered, param = id };
    }

    /// <summary>
    /// One visual diary beat (DESIGN.md §7.9.4). Text + optional hand-drawn
    /// cutscene frames; when <see cref="cutsceneFrames"/> is empty the diary screen
    /// renders a procedural illustration for the slice.
    /// </summary>
    [CreateAssetMenu(fileName = "DiaryEntry", menuName = "Alchemist's Arsenal/Story/Diary Entry", order = 40)]
    public class DiaryEntryData : ScriptableObject
    {
        public string id = "diary_00";
        public string entryTitle = "Untitled";
        [TextArea(3, 8)] public string entryText = "";
        public Sprite[] cutsceneFrames = Array.Empty<Sprite>();
        [Min(0.5f)] public float frameRate = 2.5f;
        public DiaryUnlock unlock = DiaryUnlock.Manual;

        public static DiaryEntryData Create(string id, string title, string text, DiaryUnlock unlock)
        {
            var d = CreateInstance<DiaryEntryData>();
            d.id = id;
            d.entryTitle = title;
            d.entryText = text;
            d.unlock = unlock;
            return d;
        }
    }
}
