# Lore Pipeline JSON Contract (T3A.5)

This file is the **canonical contract** between the offline lore pipeline
(T3A.6, Python `xai-sdk`) and the Unity-side importer
(`jyx2/Assets/Mods/aitavern/Scripts/Editor/LoreImporter.cs`, T3A.5).

T3A.6 **MUST** emit exactly these three files into its output folder
(default `tools/lore_pipeline/out/`). The Unity menu item
**`AI Tavern ▸ Import Lore JSON…`** reads them and writes/patches the
ScriptableObjects that the runtime `ContextAssembler` consumes (loaded by
`AITavernBoot.LoadWorldAndDossiers`).

## General rules

- **Encoding:** UTF-8, no BOM. Chinese content is **Simplified** (the novel
  is Traditional; the pipeline converts per Plan §3.2 / §10 Q7).
- **Keys are camelCase.** The importer maps them to the SO's PascalCase fields.
- **All keys are optional.** A missing key leaves the corresponding SO field
  at its default/empty value (`UnityEngine.JsonUtility` default-inits absent
  fields — the importer never throws on a missing optional key). Still,
  emitting every key (empty string / empty array when unknown) is preferred
  for auditability.
- **Each of the three files is independently optional.** A missing file is
  warned-and-skipped; it does not abort the import of the others. Partial
  re-runs are supported.
- **Idempotent:** `WorldCodex` and every `Dossier_<agentId>.asset` are fully
  re-written each import. Bios are **patched** (5 fields only) each import.
  Re-running with the same JSON yields no duplication and no drift.

## Asset destinations (informational — the importer owns these paths)

| JSON file       | Unity asset written/patched                                                    |
|-----------------|--------------------------------------------------------------------------------|
| `world.json`    | `Assets/Mods/aitavern/Resources/AITavern/WorldCodex.asset` (full re-write)      |
| `dossiers.json` | `Assets/Mods/aitavern/Resources/AITavern/Dossiers/Dossier_<agentId>.asset` (1 per entry, full re-write) |
| `bios.json`     | `Assets/Mods/aitavern/Resources/AITavern/Bios/Bio_*.asset` (**PATCH** existing only — 5 Phase 3 fields) |

These match `AITavernBoot.LoadWorldAndDossiers`' `Resources.Load<WorldCodex>("AITavern/WorldCodex")`,
`Resources.LoadAll<CharacterDossier>("AITavern/Dossiers")`, and the Phase 1
`Resources.LoadAll<CharacterBio>("AITavern/Bios")`.

---

## 1. `world.json` → `WorldCodex.asset`

Full (re)write each import — greenfield, no hand-authoring to preserve.

```json
{
  "era": "string — §1 时代 prose (TextArea WorldCodex.Era)",
  "polities": [
    {
      "name": "string",
      "brief": "string",
      "relations": [
        { "target": "string (canonical name of related polity)", "text": "string" }
      ]
    }
  ],
  "factions": [
    {
      "name": "string",
      "brief": "string",
      "homeRegion": "string",
      "relations": [
        { "target": "string (canonical name of related faction)", "text": "string" }
      ]
    }
  ],
  "notableFigures": [
    {
      "name": "string",
      "polity": "string",
      "faction": "string",
      "oneLine": "string ('everyone knows' one-liner)"
    }
  ]
}
```

| JSON                       | WorldCodex field                  |
|----------------------------|-----------------------------------|
| `era`                      | `Era`                             |
| `polities[].name`          | `Polities[].Name`                 |
| `polities[].brief`         | `Polities[].Brief`                |
| `polities[].relations[].target` | `Polities[].Relations[].Target` |
| `polities[].relations[].text`   | `Polities[].Relations[].Text`   |
| `factions[].name`          | `Factions[].Name`                 |
| `factions[].brief`         | `Factions[].Brief`                |
| `factions[].homeRegion`    | `Factions[].HomeRegion`           |
| `factions[].relations[].target` | `Factions[].Relations[].Target` |
| `factions[].relations[].text`   | `Factions[].Relations[].Text`   |
| `notableFigures[].name`    | `NotableFigures[].Name`           |
| `notableFigures[].polity`  | `NotableFigures[].Polity`         |
| `notableFigures[].faction` | `NotableFigures[].Faction`        |
| `notableFigures[].oneLine` | `NotableFigures[].OneLine`        |

---

## 2. `bios.json` → PATCH existing `Bio_*.asset`

The importer **patches only the 5 Phase 3 fields** of an existing
hand-authored `CharacterBio` asset, matched by `agentId`. It **does NOT**
touch `AgentId / RoleId / HeadId / BioName / Identity / Plans /
Relationships / Interests / StartingItems / SpawnMarkerName` (Phase 1/2
hand-authored data — clobbering it is a hard failure). If no `Bio_*.asset`
exists for a given `agentId`, the importer logs a **warning and skips it**
(it never creates a bio — Phase 1/2 bios are hand-authored; the importer
only enriches).

```json
{
  "bios": [
    {
      "agentId": "string — must equal an existing CharacterBio.AgentId, e.g. \"huangrong\"",
      "sex": "Male | Female | Other  (case-insensitive; unknown/empty → Other + warning)",
      "ageText": "string — e.g. \"约十五\" (prose, not int)",
      "personality": "string — §2 性情 (durable trait; distinct from Identity)",
      "appearance": "string — §3 外貌 (what a STRANGER sees first)",
      "surfaceManner": "string — §3 气度 (first-impression demeanor only)"
    }
  ]
}
```

| JSON               | CharacterBio field |
|--------------------|--------------------|
| `bios[].agentId`   | (match key — `AgentId`, not overwritten) |
| `bios[].sex`       | `Sex` (enum `Male`/`Female`/`Other`) |
| `bios[].ageText`   | `AgeText`          |
| `bios[].personality` | `Personality`    |
| `bios[].appearance`  | `Appearance`     |
| `bios[].surfaceManner` | `SurfaceManner` |

`sex` mapping: case-insensitive match on `"male"`/`"female"`/`"other"`.
Any unknown or empty value → `Sex.Other` and a logged warning.

---

## 3. `dossiers.json` → `Dossier_<agentId>.asset`

One asset per entry, full (re)write each import. Bounded by the pipeline to
`CUTOFF_HUI = 10` (Plan §3.3 — informational here; enforced offline in T3A.6).

```json
{
  "dossiers": [
    {
      "agentId": "string — links to CharacterBio.AgentId; also names the asset file",
      "polityKnowledge": [
        { "subject": "string", "text": "string (§4.1.1 beyond World Codex)" }
      ],
      "factionKnowledge": [
        { "subject": "string", "text": "string (§4.1.2 beyond World Codex)" }
      ],
      "people": [
        {
          "target": "string — AgentId or canonical name",
          "relationship": "string — §4.2.1",
          "impression": "string — §4.2.2",
          "martialNote": "string — §4.2.3 武功认知 (canon at cutoff 回10)",
          "sharedHistory": "string — §4.2.4 共历桥段",
          "theyDoNotKnow": "string — §5.5.4: what THIS person canonically does NOT know about the dossier owner at cutoff"
        }
      ]
    }
  ]
}
```

| JSON                              | CharacterDossier field          |
|-----------------------------------|---------------------------------|
| `dossiers[].agentId`              | `AgentId` (also `Dossier_<agentId>.asset`) |
| `dossiers[].polityKnowledge[].subject`  | `PolityKnowledge[].Subject` |
| `dossiers[].polityKnowledge[].text`     | `PolityKnowledge[].Text`    |
| `dossiers[].factionKnowledge[].subject` | `FactionKnowledge[].Subject`|
| `dossiers[].factionKnowledge[].text`    | `FactionKnowledge[].Text`   |
| `dossiers[].people[].target`        | `People[].Target`            |
| `dossiers[].people[].relationship`  | `People[].Relationship`      |
| `dossiers[].people[].impression`    | `People[].Impression`        |
| `dossiers[].people[].martialNote`   | `People[].MartialNote`       |
| `dossiers[].people[].sharedHistory` | `People[].SharedHistory`     |
| `dossiers[].people[].theyDoNotKnow` | `People[].TheyDoNotKnow`     |

---

## Example minimal valid set

`world.json`
```json
{ "era": "南宋宁宗庆元年间，蒙古崛起于漠北。",
  "polities": [ { "name": "大宋", "brief": "偏安江南，抗金。",
                  "relations": [ { "target": "金", "text": "对金：世仇。" } ] } ],
  "factions": [ { "name": "桃花岛", "brief": "黄药师所居，奇门遁甲。",
                  "homeRegion": "东海桃花岛", "relations": [] } ],
  "notableFigures": [ { "name": "黄药师", "polity": "", "faction": "桃花岛",
                        "oneLine": "东邪，桃花岛主。" } ] }
```

`bios.json`
```json
{ "bios": [ { "agentId": "huangrong", "sex": "Female", "ageText": "约十五",
              "personality": "灵动机敏、心思缜密。", "appearance": "常作乞丐少年装扮。",
              "surfaceManner": "言语机锋，神色狡黠。" } ] }
```

`dossiers.json`
```json
{ "dossiers": [ { "agentId": "huangrong",
    "polityKnowledge": [ { "subject": "大宋", "text": "父亲不问世事，但曾言及朝局。" } ],
    "factionKnowledge": [ { "subject": "白驼山", "text": "地处西域，欧阳锋为主，毒功冠绝。" } ],
    "people": [ { "target": "ouyangke", "relationship": "觊觎者（canon）",
                  "impression": "白驼山少主，武功不弱，心术不正。",
                  "martialNote": "回10比武见其借力打力、白驼山内功。",
                  "sharedHistory": "回10 赵王府冤家聚头。",
                  "theyDoNotKnow": "他不知我桃花岛底细、软猬甲护身。" } ] } ] }
```
