#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// One-click generator for the station navigation data assets. Creates (or
    /// updates in place) 1 <see cref="StationSet"/> + 4 <see cref="StationDefinition"/>
    /// assets under <c>Assets/ScriptableObjects/Stations/</c> with sensible defaults,
    /// so the decoupled data layer isn't hand-authored click-by-click.
    /// </summary>
    public static class StationDataGenerator
    {
        private const string FolderPath = "Assets/ScriptableObjects/Stations";

        [MenuItem("Alchemist/Generate Station Data")]
        public static void GenerateStationData()
        {
            EnsureFolder(FolderPath);

            StationDefinition counter = WriteDefinition(
                CraftingStation.Counter, "Counter", unlockedByDefault: true,
                "Take the customer's order. Read the ticket, then head to Prep.");

            StationDefinition prep = WriteDefinition(
                CraftingStation.Prep, "Prep", unlockedByDefault: true,
                "Chop and grind the herbs the recipe calls for before they reach the cauldron.");

            StationDefinition cauldron = WriteDefinition(
                CraftingStation.Cauldron, "Cauldron", unlockedByDefault: true,
                "Stir with the mouse to build heat. Keep the brew inside the green optimal band.");

            StationDefinition bottling = WriteDefinition(
                CraftingStation.Bottling, "Bottling", unlockedByDefault: true,
                "Seal the finished potion and hand it to the afternoon adventurer.");

            StationSet set = WriteSet(new[] { counter, prep, cauldron, bottling });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = set;
            EditorGUIUtility.PingObject(set);
            Debug.Log($"[StationDataGenerator] Generated 1 StationSet + 4 StationDefinition assets in '{FolderPath}'.");
        }

        private static StationDefinition WriteDefinition(
            CraftingStation station, string displayName, bool unlockedByDefault, string tutorialHint)
        {
            string path = $"{FolderPath}/Station_{station}.asset";

            StationDefinition def = AssetDatabase.LoadAssetAtPath<StationDefinition>(path);
            bool isNew = def == null;
            if (isNew) def = ScriptableObject.CreateInstance<StationDefinition>();

            var so = new SerializedObject(def);
            so.FindProperty("station").enumValueIndex = (int)station;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("tutorialHint").stringValue = tutorialHint;
            so.FindProperty("unlockedByDefault").boolValue = unlockedByDefault;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (isNew) AssetDatabase.CreateAsset(def, path);
            else EditorUtility.SetDirty(def);

            return def;
        }

        private static StationSet WriteSet(StationDefinition[] definitions)
        {
            string path = $"{FolderPath}/StationSet.asset";

            StationSet set = AssetDatabase.LoadAssetAtPath<StationSet>(path);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<StationSet>();

            var so = new SerializedObject(set);
            SerializedProperty stations = so.FindProperty("stations");
            stations.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
                stations.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            if (isNew) AssetDatabase.CreateAsset(set, path);
            else EditorUtility.SetDirty(set);

            return set;
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
