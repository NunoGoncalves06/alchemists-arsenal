#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// Generates a complete, working boss data graph for Biome 5 —
    /// threat profile, attack patterns, per-phase IAUS considerations, phase data,
    /// and the BossDefinition tying them together. Menu: <c>Alchemist ▸ Generate Boss Data</c>.
    /// It authors the same Coven Matriarch that <c>DefaultExpeditionData.Matriarch()</c>
    /// builds in code (attack shapes, phase pace, rig and title card), so an asset
    /// made here fights and looks like the one the game spawns.
    /// </summary>
    public static class BossDataGenerator
    {
        private const string Root = "Assets/ScriptableObjects/Combat/Boss";

        [MenuItem("Alchemist/Generate Boss Data")]
        public static void Generate()
        {
            EnsureFolder(Root);

            ElementalThreatProfile threat = BuildThreatProfile();

            BossAttackPattern volley = BuildAttack("Attack_HexVolley", "Hex Volley", ElementType.Arcane, 11, 12f, 1.3f, 0.8f, 0.6f, 2.0f, 6f,
                BossAttackShape.Volley, count: 3, spread: 1.8f, speed: 10f, lob: true);
            BossAttackPattern storm = BuildAttack("Attack_HexStorm", "Hex Storm", ElementType.Arcane, 9, 13f, 1.2f, 1.0f, 0.8f, 2.6f, 6f,
                BossAttackShape.Volley, count: 7, spread: 2.4f, speed: 10f, lob: true);
            BossAttackPattern slam  = BuildAttack("Attack_CovenSlam", "Coven Slam", ElementType.Arcane, 18, 7f, 2.4f, 0.8f, 1.0f, 2.4f, 10f);
            BossAttackPattern ring  = BuildAttack("Attack_SunderingRing", "Sundering Ring", ElementType.Arcane, 16, 0f, 5.0f, 1.0f, 1.0f, 2.8f, 14f,
                BossAttackShape.Shockwave);
            BossAttackPattern pulse = BuildAttack("Attack_WardPulse", "Ward Pulse", ElementType.Arcane, 9, 0f, 3.4f, 0.45f, 0.6f, 1.4f, 9f,
                BossAttackShape.Shockwave);

            // Per-phase considerations (same types, different response curves).
            var hpRising      = BuildConsideration<BossHealthConsideration>("BossHealth_Rising",   Rising(),        1.0f, "High HP → stay measured (Neutral).");
            var hpFallSoft    = BuildConsideration<BossHealthConsideration>("BossHealth_FallSoft", Falling(),       1.1f, "Lower HP → escalate (Enraged).");
            var hpFallSteep   = BuildConsideration<BossHealthConsideration>("BossHealth_FallSteep",FallingSteep(),  1.0f, "Very low HP → Recovering / stall.");
            var pressFalling  = BuildConsideration<ElementPressureConsideration>("ElemPressure_Falling", Falling(), 0.8f, "Low elemental pressure favours Neutral.");
            var pressRising   = BuildConsideration<ElementPressureConsideration>("ElemPressure_Rising",  Rising(),  0.6f, "Rising pressure nudges toward aggression before the hard ward.");
            var spikeRising   = BuildConsideration<RecentDamageConsideration>("RecentDamage_Rising",     Rising(),  1.2f, "A fresh burst pushes Recovering.");
            var wardOverride  = BuildConsideration<ElementThreatOverrideConsideration>("Ward_LatchOverride", Rising(), 3.0f, "Dominant: 1 while a ward is latched -> forces the ElementalWard phase.");

            BossPhaseData neutral = BuildPhase("BossPhase_Neutral", BossPhase.Neutral, 2.5f,
                new BossConsideration[] { hpRising, pressFalling }, new[] { volley, slam }, 1f);

            BossPhaseData enraged = BuildPhase("BossPhase_Enraged", BossPhase.Enraged, 3f,
                new BossConsideration[] { hpFallSoft, pressRising }, new[] { storm, ring, slam }, 1.3f);

            BossPhaseData ward = BuildPhase("BossPhase_ElementalWard", BossPhase.ElementalWard, 1f,
                new BossConsideration[] { wardOverride }, new[] { pulse, volley }, 0.7f);

            BossPhaseData recovering = BuildPhase("BossPhase_Recovering", BossPhase.Recovering, 1f,
                new BossConsideration[] { spikeRising, hpFallSteep }, new BossAttackPattern[0], 0.35f);

            BossDefinition def = BuildDefinition(threat, new[] { neutral, enraged, ward, recovering });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
            Debug.Log($"[BossDataGenerator] Boss data graph written to '{Root}'.");
        }

        // ---------------------------------------------------------- builders

        private static ElementalThreatProfile BuildThreatProfile()
        {
            var profile = LoadOrCreate<ElementalThreatProfile>($"{Root}/ElementalThreatProfile.asset");
            profile.SetEntries(new[]
            {
                Entry(ElementType.Fire,   8f, 45f, 100f, ElementType.Water),
                Entry(ElementType.Water,  8f, 45f, 100f, ElementType.Nature),
                Entry(ElementType.Nature, 8f, 45f, 100f, ElementType.Fire),
                Entry(ElementType.Poison, 7f, 40f,  90f, ElementType.Arcane),
                Entry(ElementType.Arcane, 6f, 50f, 110f, ElementType.Poison),
            });
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static ElementalThreatProfile.Entry Entry(ElementType e, float decay, float soft, float hard, ElementType ward) =>
            new ElementalThreatProfile.Entry
            {
                element = e, decayPerSecond = decay, softThreshold = soft, hardThreshold = hard, counterWard = ward
            };

        private static BossAttackPattern BuildAttack(string file, string display, ElementType element,
            int damage, float range, float area, float windup, float recovery, float cooldown, float knockback,
            BossAttackShape shape = BossAttackShape.Strike, int count = 1, float spread = 1.6f, float speed = 11f, bool lob = false)
        {
            var atk = LoadOrCreate<BossAttackPattern>($"{Root}/{file}.asset");
            var so = new SerializedObject(atk);
            so.FindProperty("displayName").stringValue = display;
            so.FindProperty("element").enumValueIndex = (int)element;
            so.FindProperty("damage").intValue = damage;
            so.FindProperty("range").floatValue = range;
            so.FindProperty("areaRadius").floatValue = area;
            so.FindProperty("knockback").floatValue = knockback;
            so.FindProperty("windupSeconds").floatValue = windup;
            so.FindProperty("recoverySeconds").floatValue = recovery;
            so.FindProperty("cooldownSeconds").floatValue = cooldown;
            so.FindProperty("shape").enumValueIndex = (int)shape;
            so.FindProperty("count").intValue = count;
            so.FindProperty("spread").floatValue = spread;
            so.FindProperty("projectileSpeed").floatValue = speed;
            so.FindProperty("lob").boolValue = lob;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(atk);
            return atk;
        }

        private static T BuildConsideration<T>(string file, AnimationCurve curve, float weight, string desc)
            where T : BossConsideration
        {
            var c = LoadOrCreate<T>($"{Root}/{file}.asset");
            var so = new SerializedObject(c);
            so.FindProperty("responseCurve").animationCurveValue = curve;
            so.FindProperty("weight").floatValue = weight;
            so.FindProperty("description").stringValue = desc;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(c);
            return c;
        }

        private static BossPhaseData BuildPhase(string file, BossPhase phase, float dwell,
            BossConsideration[] considerations, BossAttackPattern[] attacks, float moveSpeed)
        {
            var pd = LoadOrCreate<BossPhaseData>($"{Root}/{file}.asset");
            var so = new SerializedObject(pd);
            so.FindProperty("phase").enumValueIndex = (int)phase;
            so.FindProperty("minDwellSeconds").floatValue = dwell;
            so.FindProperty("moveSpeedMultiplier").floatValue = moveSpeed;
            FillObjectArray(so.FindProperty("entryConsiderations"), considerations);
            FillObjectArray(so.FindProperty("attackPatterns"), attacks);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pd);
            return pd;
        }

        private static BossDefinition BuildDefinition(ElementalThreatProfile threat, BossPhaseData[] phases)
        {
            var def = LoadOrCreate<BossDefinition>($"{Root}/BossDefinition_CovenMatriarch.asset");
            var so = new SerializedObject(def);
            so.FindProperty("displayName").stringValue = "The Coven Matriarch";
            so.FindProperty("coreElement").enumValueIndex = (int)ElementType.Arcane;
            so.FindProperty("maxHealth").intValue = 1250;
            so.FindProperty("epithet").stringValue = "Mother of the Peak";
            so.FindProperty("visualId").stringValue = "matriarch";
            so.FindProperty("entranceSeconds").floatValue = 1.8f;
            so.FindProperty("projectileOrigin").vector2Value = new Vector2(0f, 2.7f);
            so.FindProperty("threatProfile").objectReferenceValue = threat;
            so.FindProperty("phaseEvalInterval").floatValue = 0.75f;
            so.FindProperty("phaseSwitchMargin").floatValue = 0.05f;
            so.FindProperty("wardDamageMultiplier").floatValue = 0.3f;
            FillObjectArray(so.FindProperty("phases"), phases);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        // ---------------------------------------------------------- helpers

        private static void FillObjectArray(SerializedProperty prop, IReadOnlyList<Object> items)
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        private static AnimationCurve Rising() => AnimationCurve.EaseInOut(0f, 0.05f, 1f, 1f);
        private static AnimationCurve Falling() => AnimationCurve.EaseInOut(0f, 1f, 1f, 0.05f);
        private static AnimationCurve FallingSteep() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.15f), new Keyframe(1f, 0f));

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
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
