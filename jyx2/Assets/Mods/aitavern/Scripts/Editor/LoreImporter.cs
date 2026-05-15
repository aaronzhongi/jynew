// T3A.5 — JSON → ScriptableObject lore importer (AI Tavern Phase 3, Plan §3 step 5 / §10 Q1).
//
// This editor-only tool consumes the offline lore pipeline's JSON output
// (tools/lore_pipeline/out/, produced by T3A.6 — written next) and produces /
// patches the Unity assets the runtime ContextAssembler reads via
// AITavernBoot.LoadWorldAndDossiers:
//
//   Resources.Load<WorldCodex>("AITavern/WorldCodex")
//       -> Assets/Mods/aitavern/Resources/AITavern/WorldCodex.asset
//   Resources.LoadAll<CharacterDossier>("AITavern/Dossiers")
//       -> Assets/Mods/aitavern/Resources/AITavern/Dossiers/Dossier_<agentId>.asset
//   Resources.LoadAll<CharacterBio>("AITavern/Bios")  (existing, hand-authored)
//       -> Assets/Mods/aitavern/Resources/AITavern/Bios/Bio_*.asset  (PATCHED, not clobbered)
//
// Lives in the AITavern.Editor asmdef (includePlatforms:["Editor"]) so
// UnityEditor never leaks into player builds (Plan §3, existing-impl-lens fix).
//
// =====================================================================================
//  CANONICAL JSON CONTRACT  (T3A.5 defines this; T3A.6 Python pipeline MUST conform)
// =====================================================================================
// Three files are read from the chosen import folder. Each is OPTIONAL — a
// missing file is warned-and-skipped, it does NOT abort the whole import.
// All JSON keys are camelCase. Missing optional keys are tolerated (the field
// is left at the ScriptableObject default / empty — the DTO layer never throws
// on a missing optional key because UnityEngine.JsonUtility default-inits
// absent fields). No Newtonsoft dependency — JsonUtility only.
//
// ---- world.json -> WorldCodex.asset (full re-write each import) -----------------------
// {
//   "era": "string",                                  // -> WorldCodex.Era
//   "polities": [                                      // -> WorldCodex.Polities
//     { "name": "string",                              //    -> PolityEntry.Name
//       "brief": "string",                             //    -> PolityEntry.Brief
//       "relations": [ { "target": "string",           //    -> RelationLine.Target
//                        "text": "string" } ] } ],     //    -> RelationLine.Text
//   "factions": [                                      // -> WorldCodex.Factions
//     { "name": "string",                              //    -> FactionEntry.Name
//       "brief": "string",                             //    -> FactionEntry.Brief
//       "homeRegion": "string",                        //    -> FactionEntry.HomeRegion
//       "relations": [ { "target": "string",
//                        "text": "string" } ] } ],
//   "notableFigures": [                                // -> WorldCodex.NotableFigures
//     { "name": "string",                              //    -> NotableFigure.Name
//       "polity": "string",                            //    -> NotableFigure.Polity
//       "faction": "string",                           //    -> NotableFigure.Faction
//       "oneLine": "string" } ]                        //    -> NotableFigure.OneLine
// }
//
// ---- bios.json -> PATCH existing Bio_*.asset (5 Phase 3 fields ONLY) -----------------
// {
//   "bios": [
//     { "agentId": "string",        // match key against existing CharacterBio.AgentId
//       "sex": "Male|Female|Other", // -> CharacterBio.Sex enum, case-insensitive;
//                                    //    unknown/empty -> Sex.Other + warning
//       "ageText": "string",        // -> CharacterBio.AgeText
//       "personality": "string",    // -> CharacterBio.Personality
//       "appearance": "string",     // -> CharacterBio.Appearance
//       "surfaceManner": "string" } // -> CharacterBio.SurfaceManner
//   ]
// }
// The importer sets ONLY those 5 fields. It MUST NOT touch AgentId/RoleId/
// HeadId/BioName/Identity/Plans/Relationships/Interests/StartingItems/
// SpawnMarkerName (hand-authored Phase 1/2 data). If no Bio_*.asset exists
// for a given agentId, log a WARNING and skip — the importer never CREATES a
// bio (Phase 1/2 bios are hand-authored; the importer only enriches).
//
// ---- dossiers.json -> Dossier_<agentId>.asset (one per entry, full re-write) ---------
// {
//   "dossiers": [
//     { "agentId": "string",                           // -> CharacterDossier.AgentId
//       "polityKnowledge": [ { "subject": "string",    // -> KnowledgeLine.Subject
//                               "text": "string" } ],  //    -> KnowledgeLine.Text
//       "factionKnowledge": [ { "subject": "string",
//                                "text": "string" } ],
//       "people": [
//         { "target": "string",                        // -> PersonView.Target
//           "relationship": "string",                  // -> PersonView.Relationship
//           "impression": "string",                    // -> PersonView.Impression
//           "martialNote": "string",                   // -> PersonView.MartialNote
//           "sharedHistory": "string",                 // -> PersonView.SharedHistory
//           "theyDoNotKnow": "string" } ] } ]          // -> PersonView.TheyDoNotKnow
//   ]
// }
//
// (A standalone copy of this contract is also written to
//  tools/lore_pipeline/JSON_CONTRACT.md so T3A.6 can target it without
//  reading C# — keep the two in sync if the schema ever changes.)
// =====================================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Jyx2.AITavern;

namespace Jyx2.AITavern.EditorTools
{
    public static class LoreImporter
    {
        // --- Asset destination paths (MUST match AITavernBoot.LoadWorldAndDossiers) ---
        // AITavernBoot:  Resources.Load<WorldCodex>("AITavern/WorldCodex")
        // AITavernBoot:  Resources.LoadAll<CharacterDossier>("AITavern/Dossiers")
        // AITavernBoot:  Resources.LoadAll<CharacterBio>("AITavern/Bios")
        const string ResourcesRoot = "Assets/Mods/aitavern/Resources/AITavern";
        const string WorldCodexAssetPath = ResourcesRoot + "/WorldCodex.asset";
        const string DossiersDir = ResourcesRoot + "/Dossiers";
        const string BiosDir = ResourcesRoot + "/Bios";

        const string DefaultPipelineOut = "tools/lore_pipeline/out";

        [MenuItem("AI Tavern/Import Lore JSON…")]
        public static void ImportLoreMenu()
        {
            // Default the folder picker to the repo's tools/lore_pipeline/out/
            // when it exists; otherwise the project root.
            string projectRoot = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                                  ?? Directory.GetParent(Application.dataPath)?.FullName
                                  ?? Application.dataPath;
            string defaultDir = Path.Combine(projectRoot, DefaultPipelineOut);
            if (!Directory.Exists(defaultDir)) defaultDir = projectRoot;

            string folder = EditorUtility.OpenFolderPanel(
                "Select lore pipeline JSON folder", defaultDir, "");
            if (string.IsNullOrEmpty(folder))
            {
                Debug.Log("[LoreImporter] Import cancelled (no folder selected).");
                return;
            }

            Import(folder);
        }

        /// <summary>
        /// Reads world.json / bios.json / dossiers.json from <paramref name="folder"/>
        /// and writes/patches the Unity assets. Robust to a partially-present
        /// folder: each missing file is warned-and-skipped, never aborts.
        /// Idempotent: WorldCodex + Dossiers are fully re-written; the 5 Phase 3
        /// bio fields are re-patched with no duplication.
        /// </summary>
        public static void Import(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Debug.LogError($"[LoreImporter] Folder does not exist: '{folder}'. Import aborted.");
                return;
            }

            int dossiersWritten = 0;
            int biosPatched = 0;
            bool worldUpdated = false;
            var missingBioAgents = new List<string>();

            try
            {
                AssetDatabase.StartAssetEditing();

                worldUpdated = ImportWorld(Path.Combine(folder, "world.json"));
                biosPatched = ImportBios(Path.Combine(folder, "bios.json"), missingBioAgents);
                dossiersWritten = ImportDossiers(Path.Combine(folder, "dossiers.json"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string missingNote = missingBioAgents.Count == 0
                ? "none"
                : string.Join(", ", missingBioAgents);
            Debug.Log(
                $"[LoreImporter] Import complete from '{folder}'.\n" +
                $"  WorldCodex updated: {(worldUpdated ? "yes" : "no (world.json missing/empty)")}\n" +
                $"  Dossiers written:   {dossiersWritten}\n" +
                $"  Bios patched:       {biosPatched}\n" +
                $"  JSON agentIds with NO matching Bio_*.asset (skipped, not created): {missingNote}");
        }

        // ---------------------------- world.json ----------------------------

        static bool ImportWorld(string jsonPath)
        {
            string json = TryReadJson(jsonPath, "world.json");
            if (json == null) return false;

            WorldJson dto;
            try { dto = JsonUtility.FromJson<WorldJson>(json); }
            catch (Exception e)
            {
                Debug.LogError($"[LoreImporter] Failed to parse world.json: {e.Message}. Skipping world.");
                return false;
            }
            if (dto == null)
            {
                Debug.LogWarning("[LoreImporter] world.json parsed to null. Skipping world.");
                return false;
            }

            // Greenfield: no hand-authoring to preserve — full (re)write.
            var world = AssetDatabase.LoadAssetAtPath<WorldCodex>(WorldCodexAssetPath);
            bool created = world == null;
            if (created) world = ScriptableObject.CreateInstance<WorldCodex>();

            world.Era = dto.era ?? "";

            world.Polities = new List<PolityEntry>();
            if (dto.polities != null)
            {
                foreach (var p in dto.polities)
                {
                    if (p == null) continue;
                    world.Polities.Add(new PolityEntry
                    {
                        Name = p.name ?? "",
                        Brief = p.brief ?? "",
                        Relations = MapRelations(p.relations),
                    });
                }
            }

            world.Factions = new List<FactionEntry>();
            if (dto.factions != null)
            {
                foreach (var f in dto.factions)
                {
                    if (f == null) continue;
                    world.Factions.Add(new FactionEntry
                    {
                        Name = f.name ?? "",
                        Brief = f.brief ?? "",
                        HomeRegion = f.homeRegion ?? "",
                        Relations = MapRelations(f.relations),
                    });
                }
            }

            world.NotableFigures = new List<NotableFigure>();
            if (dto.notableFigures != null)
            {
                foreach (var n in dto.notableFigures)
                {
                    if (n == null) continue;
                    world.NotableFigures.Add(new NotableFigure
                    {
                        Name = n.name ?? "",
                        Polity = n.polity ?? "",
                        Faction = n.faction ?? "",
                        OneLine = n.oneLine ?? "",
                    });
                }
            }

            if (created) AssetDatabase.CreateAsset(world, WorldCodexAssetPath);
            EditorUtility.SetDirty(world);
            Debug.Log($"[LoreImporter] WorldCodex {(created ? "created" : "updated")} at {WorldCodexAssetPath} " +
                      $"(polities={world.Polities.Count}, factions={world.Factions.Count}, notable={world.NotableFigures.Count}).");
            return true;
        }

        static List<RelationLine> MapRelations(List<RelationJson> rels)
        {
            var list = new List<RelationLine>();
            if (rels == null) return list;
            foreach (var r in rels)
            {
                if (r == null) continue;
                list.Add(new RelationLine { Target = r.target ?? "", Text = r.text ?? "" });
            }
            return list;
        }

        // ---------------------------- bios.json ----------------------------
        // PATCH ONLY: Sex, AgeText, Personality, Appearance, SurfaceManner.
        // Never touches AgentId/RoleId/HeadId/BioName/Identity/Plans/
        // Relationships/Interests/StartingItems/SpawnMarkerName.

        static int ImportBios(string jsonPath, List<string> missingBioAgents)
        {
            string json = TryReadJson(jsonPath, "bios.json");
            if (json == null) return 0;

            BiosJson dto;
            try { dto = JsonUtility.FromJson<BiosJson>(json); }
            catch (Exception e)
            {
                Debug.LogError($"[LoreImporter] Failed to parse bios.json: {e.Message}. Skipping bios.");
                return 0;
            }
            if (dto == null || dto.bios == null)
            {
                Debug.LogWarning("[LoreImporter] bios.json parsed to null/empty. Skipping bios.");
                return 0;
            }

            // Index existing hand-authored Bio_*.asset by AgentId.
            var byAgent = new Dictionary<string, CharacterBio>();
            string[] bioGuids = AssetDatabase.FindAssets("t:CharacterBio", new[] { BiosDir });
            foreach (var g in bioGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var bio = AssetDatabase.LoadAssetAtPath<CharacterBio>(p);
                if (bio != null && !string.IsNullOrEmpty(bio.AgentId))
                    byAgent[bio.AgentId] = bio;
            }

            int patched = 0;
            foreach (var bj in dto.bios)
            {
                if (bj == null || string.IsNullOrEmpty(bj.agentId)) continue;

                if (!byAgent.TryGetValue(bj.agentId, out var bio) || bio == null)
                {
                    // Do NOT create — Phase 1/2 bios are hand-authored.
                    Debug.LogWarning($"[LoreImporter] No Bio_*.asset found for agentId '{bj.agentId}' " +
                                     $"under {BiosDir}. Skipping (importer never creates bios — only enriches).");
                    missingBioAgents.Add(bj.agentId);
                    continue;
                }

                // Set ONLY the 5 Phase 3 fields on the loaded instance. Direct
                // field assignment on the existing ScriptableObject leaves every
                // other serialized field exactly as authored; SetDirty + SaveAssets
                // persists just the mutated object (no clobber of Phase 1/2 data).
                bio.Sex = ParseSex(bj.sex, bj.agentId);
                if (bj.ageText != null) bio.AgeText = bj.ageText;
                if (bj.personality != null) bio.Personality = bj.personality;
                if (bj.appearance != null) bio.Appearance = bj.appearance;
                if (bj.surfaceManner != null) bio.SurfaceManner = bj.surfaceManner;

                EditorUtility.SetDirty(bio);
                patched++;
                Debug.Log($"[LoreImporter] Patched Phase 3 fields on Bio for agentId '{bj.agentId}' " +
                          $"(Sex/AgeText/Personality/Appearance/SurfaceManner only).");
            }
            return patched;
        }

        static Sex ParseSex(string raw, string agentId)
        {
            if (string.IsNullOrEmpty(raw))
            {
                Debug.LogWarning($"[LoreImporter] bios.json entry '{agentId}' has empty 'sex'; defaulting to Sex.Other.");
                return Sex.Other;
            }
            switch (raw.Trim().ToLowerInvariant())
            {
                case "male": return Sex.Male;
                case "female": return Sex.Female;
                case "other": return Sex.Other;
                default:
                    Debug.LogWarning($"[LoreImporter] bios.json entry '{agentId}' has unknown sex '{raw}'; defaulting to Sex.Other.");
                    return Sex.Other;
            }
        }

        // -------------------------- dossiers.json --------------------------

        static int ImportDossiers(string jsonPath)
        {
            string json = TryReadJson(jsonPath, "dossiers.json");
            if (json == null) return 0;

            DossiersJson dto;
            try { dto = JsonUtility.FromJson<DossiersJson>(json); }
            catch (Exception e)
            {
                Debug.LogError($"[LoreImporter] Failed to parse dossiers.json: {e.Message}. Skipping dossiers.");
                return 0;
            }
            if (dto == null || dto.dossiers == null)
            {
                Debug.LogWarning("[LoreImporter] dossiers.json parsed to null/empty. Skipping dossiers.");
                return 0;
            }

            // Create the Dossiers directory if missing.
            if (!AssetDatabase.IsValidFolder(DossiersDir))
            {
                Directory.CreateDirectory(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName, DossiersDir));
                AssetDatabase.Refresh();
            }

            int written = 0;
            foreach (var dj in dto.dossiers)
            {
                if (dj == null || string.IsNullOrEmpty(dj.agentId))
                {
                    Debug.LogWarning("[LoreImporter] dossiers.json entry missing agentId; skipped.");
                    continue;
                }

                string assetPath = $"{DossiersDir}/Dossier_{dj.agentId}.asset";
                var dossier = AssetDatabase.LoadAssetAtPath<CharacterDossier>(assetPath);
                bool created = dossier == null;
                if (created) dossier = ScriptableObject.CreateInstance<CharacterDossier>();

                // Full (re)write — greenfield, no hand-authoring to preserve.
                dossier.AgentId = dj.agentId;
                dossier.PolityKnowledge = MapKnowledge(dj.polityKnowledge);
                dossier.FactionKnowledge = MapKnowledge(dj.factionKnowledge);
                dossier.People = new List<PersonView>();
                if (dj.people != null)
                {
                    foreach (var pv in dj.people)
                    {
                        if (pv == null) continue;
                        dossier.People.Add(new PersonView
                        {
                            Target = pv.target ?? "",
                            Relationship = pv.relationship ?? "",
                            Impression = pv.impression ?? "",
                            MartialNote = pv.martialNote ?? "",
                            SharedHistory = pv.sharedHistory ?? "",
                            TheyDoNotKnow = pv.theyDoNotKnow ?? "",
                        });
                    }
                }

                if (created) AssetDatabase.CreateAsset(dossier, assetPath);
                EditorUtility.SetDirty(dossier);
                written++;
                Debug.Log($"[LoreImporter] Dossier {(created ? "created" : "updated")} at {assetPath} " +
                          $"(polityKnowledge={dossier.PolityKnowledge.Count}, " +
                          $"factionKnowledge={dossier.FactionKnowledge.Count}, people={dossier.People.Count}).");
            }
            return written;
        }

        static List<KnowledgeLine> MapKnowledge(List<KnowledgeJson> src)
        {
            var list = new List<KnowledgeLine>();
            if (src == null) return list;
            foreach (var k in src)
            {
                if (k == null) continue;
                list.Add(new KnowledgeLine { Subject = k.subject ?? "", Text = k.text ?? "" });
            }
            return list;
        }

        // ---------------------------- helpers ----------------------------

        static string TryReadJson(string path, string label)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[LoreImporter] {label} not found at '{path}'. Skipping (partial-folder OK).");
                return null;
            }
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Debug.LogWarning($"[LoreImporter] {label} is empty at '{path}'. Skipping.");
                    return null;
                }
                return text;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LoreImporter] Failed to read {label} at '{path}': {e.Message}. Skipping.");
                return null;
            }
        }

        // =================================================================
        //  Serializable DTOs — the canonical JSON contract (camelCase keys).
        //  UnityEngine.JsonUtility deserializes [System.Serializable] classes
        //  + List<T>; absent optional keys are default-init'd (no throw).
        // =================================================================

        [Serializable]
        public class WorldJson
        {
            public string era;
            public List<PolityJson> polities;
            public List<FactionJson> factions;
            public List<NotableFigureJson> notableFigures;
        }

        [Serializable]
        public class PolityJson
        {
            public string name;
            public string brief;
            public List<RelationJson> relations;
        }

        [Serializable]
        public class FactionJson
        {
            public string name;
            public string brief;
            public string homeRegion;
            public List<RelationJson> relations;
        }

        [Serializable]
        public class RelationJson
        {
            public string target;
            public string text;
        }

        [Serializable]
        public class NotableFigureJson
        {
            public string name;
            public string polity;
            public string faction;
            public string oneLine;
        }

        [Serializable]
        public class BiosJson
        {
            public List<BioJson> bios;
        }

        [Serializable]
        public class BioJson
        {
            public string agentId;
            public string sex;
            public string ageText;
            public string personality;
            public string appearance;
            public string surfaceManner;
        }

        [Serializable]
        public class DossiersJson
        {
            public List<DossierJson> dossiers;
        }

        [Serializable]
        public class DossierJson
        {
            public string agentId;
            public List<KnowledgeJson> polityKnowledge;
            public List<KnowledgeJson> factionKnowledge;
            public List<PersonViewJson> people;
        }

        [Serializable]
        public class KnowledgeJson
        {
            public string subject;
            public string text;
        }

        [Serializable]
        public class PersonViewJson
        {
            public string target;
            public string relationship;
            public string impression;
            public string martialNote;
            public string sharedHistory;
            public string theyDoNotKnow;
        }
    }
}
#endif
