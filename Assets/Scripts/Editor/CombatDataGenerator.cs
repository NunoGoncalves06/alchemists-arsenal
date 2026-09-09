#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// Generates a working default set of combat data + utility-AI considerations so
    /// the system can be dropped on an adventurer prefab and exercised immediately.
    /// Menu: <c>Alchemist ▸ Generate Combat Data</c>.
    /// </summary>
    public static class CombatDataGenerator
    {
        private const string Root = "Assets/ScriptableObjects/Combat";

        [MenuItem("Alchemist/Generate Combat Data")]
        public static void Generate()
        {
            EnsureFolder(Root);

            ElementalMatrix matrix = BuildMatrix();

            BombData fire   = BuildBomb("Bomb_Fire",   "Firebloom Flask",  ElementType.Fire,   26, 2.6f, 6f, 1.4f);
            BombData water  = BuildBomb("Bomb_Water",  "Tidevial",         ElementType.Water,  22, 2.4f, 6f, 1.4f);
            BombData nature  = BuildBomb("Bomb_Nature", "Thornburst",       ElementType.Nature, 20, 3.0f, 5f, 1.6f);
            BombData poison = BuildBomb("Bomb_Poison", "Miremist Phial",   ElementType.Poison, 16, 3.4f, 7f, 2.0f);

            BuildMonster("Monster_Treant",    "Bark Treant",   ElementType.Nature, 80, 1.4f);
            BuildMonster("Monster_Emberling", "Emberling",     ElementType.Fire,   45, 2.6f);
            BuildMonster("Monster_Frostkin",  "Frostkin",      ElementType.Water,  55, 2.0f);

            BuildConsiderations();

            AdventurerLoadout loadout = BuildLoadout(new[] { fire, water, nature, poison });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = loadout;
            EditorGUIUtility.PingObject(loadout);
            Debug.Log($"[CombatDataGenerator] Wrote combat data + considerations to '{Root}'.");
        }

        // --------------------------------------------------------------- builders

        private static ElementalMatrix BuildMatrix()
        {
            var matrix = LoadOrCreate<ElementalMatrix>($"{Root}/ElementalMatrix.asset");
            matrix.EnsureInitialised();

            // Elemental wheel: each element is strong (x2) against the next, weak (x0.5) against its predator.
            SetPair(matrix, ElementType.Fire,   ElementType.Nature, 2f, 0.5f);
            SetPair(matrix, ElementType.Nature, ElementType.Water,  2f, 0.5f);
            SetPair(matrix, ElementType.Water,  ElementType.Fire,   2f, 0.5f);
            SetPair(matrix, ElementType.Poison, ElementType.Nature, 2f, 0.5f);
            SetPair(matrix, ElementType.Arcane, ElementType.Poison, 2f, 0.5f);

            EditorUtility.SetDirty(matrix);
            return matrix;
        }

        private static void SetPair(ElementalMatrix m, ElementType strongVs, ElementType target, float strong, float weakBack)
        {
            m.SetMultiplier(strongVs, target, strong);
            m.SetMultiplier(target, strongVs, weakBack);
        }

        private static BombData BuildBomb(string file, string display, ElementType element,
            int damage, float blast, float idealRange, float cooldown)
        {
            var bomb = LoadOrCreate<BombData>($"{Root}/{file}.asset");
            var so = new SerializedObject(bomb);
            so.FindProperty("displayName").stringValue = display;
            so.FindProperty("element").enumValueIndex = (int)element;
            so.FindProperty("baseDamage").intValue = damage;
            so.FindProperty("blastRadius").floatValue = blast;
            so.FindProperty("idealRange").floatValue = idealRange;
            so.FindProperty("minSafeRange").floatValue = 2f;
            so.FindProperty("maxRange").floatValue = idealRange + 6f;
            so.FindProperty("cooldownSeconds").floatValue = cooldown;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bomb);
            return bomb;
        }

        private static void BuildMonster(string file, string display, ElementType element, int hp, float speed)
        {
            var monster = LoadOrCreate<MonsterData>($"{Root}/{file}.asset");
            var so = new SerializedObject(monster);
            so.FindProperty("displayName").stringValue = display;
            so.FindProperty("element").enumValueIndex = (int)element;
            so.FindProperty("maxHealth").intValue = hp;
            so.FindProperty("moveSpeed").floatValue = speed;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(monster);
        }

        private static void BuildConsiderations()
        {
            var elemental = LoadOrCreate<ElementalVulnerabilityConsideration>(
                $"{Root}/Consideration_ElementalVulnerability.asset");
            WriteConsideration(elemental,
                "Favour bombs the elemental matrix rewards. Neutralised below 60% potion quality.",
                AnimationCurve.Linear(0f, 0f, 1f, 1f), weight: 1.1f);

            var distance = LoadOrCreate<DistanceConsideration>($"{Root}/Consideration_Distance.asset");
            WriteConsideration(distance,
                "Prefer the bomb's ideal band; avoid point-blank and out-of-range throws.",
                new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.15f)),
                weight: 1f);

            var health = LoadOrCreate<SelfHealthConsideration>($"{Root}/Consideration_SelfHealth.asset");
            WriteConsideration(health,
                "Raw input is the adventurer's HP fraction. This gentle rising curve slightly " +
                "prefers acting while healthy; give a panic bomb a falling curve instead.",
                AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f),
                weight: 0.6f);
        }

        private static void WriteConsideration(UtilityConsideration c, string description, AnimationCurve curve, float weight)
        {
            var so = new SerializedObject(c);
            so.FindProperty("description").stringValue = description;
            so.FindProperty("responseCurve").animationCurveValue = curve;
            so.FindProperty("weight").floatValue = weight;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(c);
        }

        private static AdventurerLoadout BuildLoadout(BombData[] bombs)
        {
            var loadout = LoadOrCreate<AdventurerLoadout>($"{Root}/AdventurerLoadout_Default.asset");
            var so = new SerializedObject(loadout);
            SerializedProperty slots = so.FindProperty("slots");
            slots.arraySize = bombs.Length;
            for (int i = 0; i < bombs.Length; i++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("bomb").objectReferenceValue = bombs[i];
                slot.FindPropertyRelative("count").intValue = 3;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loadout);
            return loadout;
        }

        // --------------------------------------------------------------- helpers

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
