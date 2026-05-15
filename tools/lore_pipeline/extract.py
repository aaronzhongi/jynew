"""The map-reduce that produces world.json / bios.json / dossiers.json.

Shape (Plan §3.1):

  World reduce      : per-回 mini-summary (回1..CUTOFF) -> 1 consolidation
                      call -> world.json
  Per-roster map    : for each ROSTER char, for each 回 in 1..CUTOFF:
                      cheap presence gate -> if present, structured extract
  Per-roster reduce : consolidate that char's per-回 extracts -> its
                      bios.json entry + its dossiers.json entry

ANTI-OMNISCIENCE (Plan §3.3, CRITICAL):
  * The chapter list is PHYSICALLY sliced to 回<=CUTOFF_HUI by
    chunker.chapters_up_to_cutoff() BEFORE any per-character call — the
    structural guard. Post-cutoff text is never in any prompt.
  * Every prompt ALSO carries the §3.3 SPOILER_EXCLUSIONS fence as a
    belt-and-braces guard against out-of-band model knowledge.

Output is 简体中文 (the novel is 繁體; the game/bios are 简体) — every
prompt explicitly instructs Simplified output. We do NOT pre-convert the
2.7 MB file (Plan §10 Q7).

Every JSON field in the contract (JSON_CONTRACT.md) is emitted, with ""
or [] when unknown, for auditability. `sex` is ALWAYS emitted for every
ROSTER bio (the importer overwrites Sex unconditionally — an absent value
would wrongly reset a correct hand-authored value).
"""

import json
import re
from typing import Dict, List

from . import chunker, config
from .xai_client import XaiClient

# ---------------------------------------------------------------------------
# Shared prompt fragments
# ---------------------------------------------------------------------------

_OUTPUT_LANG = (
    "【输出语言】无论原文是繁体，你的全部输出必须是简体中文。"
    "人名、门派名、地名一律转简体（例：黃蓉→黄蓉、歐陽鋒→欧阳锋、"
    "歐陽克→欧阳克、黃藥師→黄药师、桃花島→桃花岛、軟蝟甲→软猬甲）。"
)


def _cutoff_fence(cutoff: int) -> str:
    bullets = "\n".join(f"  ✗ {x}" for x in config.SPOILER_EXCLUSIONS)
    return (
        f"【时间封顶 — 极重要】当前认知严格封顶在《射雕英雄传》第{cutoff}回"
        f"（冤家聚头）结束为止（含第{cutoff}回）。只能采用第1回至第{cutoff}回"
        f"正文已确立的事实。任何第{cutoff}回之后才发生/才确立的情节，"
        f"即使你从别处知道，也必须当作【尚未发生、当前无人知晓】。"
        f"以下为明确禁止出现的内容（违反即为错误）：\n{bullets}\n"
        f"若某事实在第{cutoff}回时仅为传闻/疑点而非定论，"
        f"必须如实标注为不确定，不得写成既成事实。"
    )


def _json_only(schema_hint: str) -> str:
    return (
        "【输出格式】只输出一个 JSON 对象，不要任何解释、前后缀或代码块标记。"
        f"JSON 结构如下（所有字段都要出现，未知用空字符串或空数组）：\n{schema_hint}"
    )


def _parse_json(raw: str, *, fallback: dict) -> dict:
    """Robust JSON extraction (model may wrap in ```json or add prose).
    On total failure returns the fallback so the pipeline still emits a
    contract-shaped file (empty fields) rather than crashing."""
    if not raw:
        return dict(fallback)
    txt = raw.strip()
    if txt.startswith("```"):
        txt = re.sub(r"^```[a-zA-Z]*\n?", "", txt)
        txt = re.sub(r"\n?```$", "", txt).strip()
    try:
        return json.loads(txt)
    except json.JSONDecodeError:
        m = re.search(r"\{.*\}", txt, re.DOTALL)
        if m:
            try:
                return json.loads(m.group(0))
            except json.JSONDecodeError:
                pass
    print(f"[extract] WARNING: could not parse model JSON; emitting empty "
          f"contract-shaped fallback. Raw head: {txt[:160]!r}")
    return dict(fallback)


# ---------------------------------------------------------------------------
# 1. WORLD reduce  ->  world.json
# ---------------------------------------------------------------------------

def _world_chapter_digest(client: XaiClient, ch: chunker.Chapter) -> str:
    system = (
        "你是《射雕英雄传》考据助手。只做客观抽取，不演绎、不剧透。"
        + _OUTPUT_LANG
    )
    user = (
        f"以下是第{ch.hui}回〈{ch.title}〉原文（繁体）。\n"
        f"{_cutoff_fence(config.CUTOFF_HUI)}\n"
        "请用简体中文，列出本回中与【世界背景】相关的客观事实，"
        "仅限：时代/年代线索；政权（大宋、金、蒙古）的处境与彼此关系；"
        "门派（全真教、桃花岛、白驼山、丐帮、大理段氏）的处境与关系；"
        "天下知名人物的公开身份。每条一行，≤30字，无则写「无」。"
        "不要写情节梗概。\n\n"
        f"———原文———\n{ch.text}"
    )
    return client.complete(system, user,
                           max_tokens=config.MAX_TOKENS_EXTRACT,
                           label=f"world-digest 回{ch.hui}")


def build_world(client: XaiClient, chapters: List[chunker.Chapter]) -> dict:
    """World pass: digest 回1..CUTOFF, then ONE consolidation call."""
    in_scope = chunker.chapters_up_to_cutoff(chapters)  # structural cutoff
    digests = []
    for ch in in_scope:
        d = _world_chapter_digest(client, ch)
        digests.append(f"【第{ch.hui}回 {ch.title}】\n{d if d else '(dry-run)'}")
    joined = "\n\n".join(digests)

    polities = "、".join(config.WORLD_SPEC["polities"])
    factions = "、".join(config.WORLD_SPEC["factions"])
    notable_names = "、".join(n["name"] for n in config.NOTABLE_SET)
    notable_hints = "\n".join(
        f"  - {n['name']}（政权:{n['polity'] or '—'} / "
        f"门派:{n['faction'] or '—'}）：{n['hint']}"
        for n in config.NOTABLE_SET
    )

    schema = (
        '{\n'
        '  "era": "时代背景的简体中文散文(2-4句)",\n'
        '  "polities": [ { "name":"", "brief":"", '
        '"relations":[ {"target":"","text":""} ] } ],\n'
        '  "factions":  [ { "name":"", "brief":"", "homeRegion":"", '
        '"relations":[ {"target":"","text":""} ] } ],\n'
        '  "notableFigures": [ { "name":"", "polity":"", "faction":"", '
        '"oneLine":"" } ]\n'
        '}'
    )
    system = (
        "你是《射雕英雄传》世界观考据员。基于给定的逐回摘要，"
        "归并出一份截至第10回为止的『世界背景』。客观、不剧透、不演绎。"
        + _OUTPUT_LANG
    )
    user = (
        f"{_cutoff_fence(config.CUTOFF_HUI)}\n\n"
        f"【时代约束】{config.WORLD_SPEC['era_hint']}\n"
        f"【政权约束】{config.WORLD_SPEC['polities_rule']} 即恰为：{polities}。\n"
        f"【门派约束】{config.WORLD_SPEC['factions_rule']} 即恰为：{factions}。\n"
        f"【五绝写法】{config.WORLD_SPEC['wujue_rule']}\n"
        f"【知名人物集合】notableFigures 恰为以下这些人（不增不减），"
        f"用其简体名，按给定政权/门派归类，oneLine 写一句"
        f"『天下皆知』式的、截至第10回成立的简介：\n{notable_names}\n"
        f"{notable_hints}\n\n"
        f"{_json_only(schema)}\n\n"
        f"———逐回世界摘要———\n{joined}"
    )
    raw = client.complete(system, user,
                          max_tokens=config.MAX_TOKENS_REDUCE,
                          label="world-reduce")
    world = _parse_json(raw, fallback={
        "era": config.WORLD_SPEC["era_hint"],
        "polities": [{"name": p, "brief": "", "relations": []}
                     for p in config.WORLD_SPEC["polities"]],
        "factions": [{"name": f, "brief": "", "homeRegion": "",
                      "relations": []}
                     for f in config.WORLD_SPEC["factions"]],
        "notableFigures": [{"name": n["name"], "polity": n["polity"],
                            "faction": n["faction"], "oneLine": ""}
                           for n in config.NOTABLE_SET],
    })
    return _normalize_world(world)


def _normalize_world(w: dict) -> dict:
    """Coerce to the exact contract shape (camelCase keys, full nesting)."""
    def rel(r):
        return {"target": str(r.get("target", "")),
                "text": str(r.get("text", ""))}
    return {
        "era": str(w.get("era", "")),
        "polities": [{
            "name": str(p.get("name", "")),
            "brief": str(p.get("brief", "")),
            "relations": [rel(r) for r in p.get("relations", []) or []],
        } for p in w.get("polities", []) or []],
        "factions": [{
            "name": str(f.get("name", "")),
            "brief": str(f.get("brief", "")),
            "homeRegion": str(f.get("homeRegion", "")),
            "relations": [rel(r) for r in f.get("relations", []) or []],
        } for f in w.get("factions", []) or []],
        "notableFigures": [{
            "name": str(n.get("name", "")),
            "polity": str(n.get("polity", "")),
            "faction": str(n.get("faction", "")),
            "oneLine": str(n.get("oneLine", "")),
        } for n in w.get("notableFigures", []) or []],
    }


# ---------------------------------------------------------------------------
# 2. PER-ROSTER map  (presence gate -> structured per-回 extract)
# ---------------------------------------------------------------------------

def _present_in_chapter(client: XaiClient, novel_name: str,
                        ch: chunker.Chapter) -> bool:
    """Cheap gate first (Plan §3.1 step 3): skip 回 where the char is
    absent so the expensive structured extract isn't wasted. A literal
    name hit short-circuits without an API call. In dry-run, assume
    present (so the preview shows the full prompt set / max cost)."""
    if novel_name in ch.text:
        return True
    if client.dry:
        return True
    system = "你是文本检索助手。只回答 yes 或 no。"
    user = (f"下面第{ch.hui}回原文中，是否出现了人物「{novel_name}」"
            f"（本人登场或在场，不含纯粹他人提及的传闻）？"
            f"只回 yes 或 no。\n\n{ch.text}")
    ans = client.complete(system, user, max_tokens=4,
                          label=f"gate {novel_name} 回{ch.hui}")
    return ans.strip().lower().startswith("y")


def _extract_char_chapter(client: XaiClient, novel_name: str,
                          ch: chunker.Chapter) -> dict:
    notable_names = "、".join(n["name"] for n in config.NOTABLE_SET
                             if n["name"] != novel_name)
    schema = (
        '{\n'
        '  "appearance": "陌生人初见此人时的外貌(衣着/相貌/身形)",\n'
        '  "surface_manner": "仅凭初见可知的气度举止",\n'
        '  "personality_signals": ["本回显露的性情线索", "..."],\n'
        '  "age_signals": "年龄线索(如『少女』『二十余』)或空",\n'
        '  "sex": "Male | Female | Other",\n'
        '  "events": ["此人在本回的客观行动/经历", "..."],\n'
        '  "relationship_touchpoints": [\n'
        '     {"target":"知名人物名","contact":"本回与其互动/见闻(客观)",\n'
        '      "martial_witnessed":"本回亲眼所见其招式路数(不写武功正式名)"} ]\n'
        '}'
    )
    system = (
        f"你是《射雕英雄传》人物考据员，专注人物「{novel_name}」。"
        "只抽取本回正文客观确立的内容，不演绎、不剧透、不跨回推断。"
        + _OUTPUT_LANG
    )
    user = (
        f"{_cutoff_fence(config.CUTOFF_HUI)}\n\n"
        f"针对人物「{novel_name}」，从第{ch.hui}回〈{ch.title}〉原文抽取信息。\n"
        f"relationship_touchpoints 的 target 只能取自这些知名人物"
        f"（出现谁记谁，没有则空数组）：{notable_names}。\n"
        "martial_witnessed：只写本回此人『被亲眼看到』的招式风格/路数，"
        "且【绝不可写降龙十八掌/打狗棒法/九阴白骨爪/一阳指/蛤蟆功等武功正式名】"
        "（第10回时黄蓉尚不知这些名字）；可写如『借力打力』『白驼山内功』"
        "『桃花岛路数』『软猬甲护身』等所见路数。\n\n"
        f"{_json_only(schema)}\n\n"
        f"———第{ch.hui}回原文———\n{ch.text}"
    )
    raw = client.complete(system, user,
                          max_tokens=config.MAX_TOKENS_EXTRACT,
                          label=f"extract {novel_name} 回{ch.hui}")
    return _parse_json(raw, fallback={
        "appearance": "", "surface_manner": "", "personality_signals": [],
        "age_signals": "", "sex": "", "events": [],
        "relationship_touchpoints": [],
    })


# ---------------------------------------------------------------------------
# 3. PER-ROSTER reduce  ->  bios.json entry + dossiers.json entry
# ---------------------------------------------------------------------------

# Canon-obvious sex (still derived from text below, but never left empty —
# the contract/importer overwrites Sex unconditionally).
_CANON_SEX = {"黃蓉": "Female", "歐陽克": "Male"}


def _coerce_sex(value: str, novel_name: str) -> str:
    v = (value or "").strip().lower()
    if v in ("male", "男", "m"):
        return "Male"
    if v in ("female", "女", "f"):
        return "Female"
    if v in ("other", "其他"):
        return "Other"
    # Never leave empty: fall back to canon-obvious, else Other.
    return _CANON_SEX.get(novel_name, "Other")


def reduce_character(client: XaiClient, roster_entry: dict,
                     per_chapter: List[dict]) -> Dict[str, dict]:
    """Consolidate one ROSTER char's per-回 extracts into its bio entry
    + dossier entry. Returns {"bio": {...}, "dossier": {...}} both in the
    exact JSON_CONTRACT shape."""
    agent_id = roster_entry["agentId"]
    novel_name = roster_entry["novelName"]
    notable_names = [n["name"] for n in config.NOTABLE_SET
                     if n["name"] != novel_name]
    notable_list = "、".join(notable_names)
    polities = "、".join(config.WORLD_SPEC["polities"])
    factions = "、".join(config.WORLD_SPEC["factions"])
    evidence = json.dumps(per_chapter, ensure_ascii=False, indent=1)

    schema = (
        '{\n'
        '  "bio": {\n'
        '    "sex": "Male | Female | Other",\n'
        '    "ageText": "如『约十五』的散文,非数字",\n'
        '    "personality": "§2 性情:稳定特质,有别于身份/职业",\n'
        '    "appearance": "§3 外貌:陌生人初见所见",\n'
        '    "surfaceManner": "§3 气度:仅初见印象的举止"\n'
        '  },\n'
        '  "dossier": {\n'
        '    "polityKnowledge": [ {"subject":"大宋/金/蒙古","text":"此人额外所知,超出世界背景"} ],\n'
        '    "factionKnowledge": [ {"subject":"门派名","text":"此人额外所知,超出世界背景"} ],\n'
        '    "people": [ {\n'
        '      "target":"知名人物名(简体)",\n'
        '      "relationship":"§4.2.1 关系",\n'
        '      "impression":"§4.2.2 印象",\n'
        '      "martialNote":"§4.2.3 截至第10回亲历所见的武功路数(不写武功正式名)",\n'
        '      "sharedHistory":"§4.2.4 共历桥段",\n'
        '      "theyDoNotKnow":"§5.5.4 此人截至第10回 并不知道 关于本传主的什么"\n'
        '    } ]\n'
        '  }\n'
        '}'
    )
    system = (
        f"你是《射雕英雄传》人物档案归并员，归并人物「{novel_name}」"
        f"截至第{config.CUTOFF_HUI}回为止的人物志与认知档案。"
        "客观、忠于原文、不剧透、不演绎。"
        + _OUTPUT_LANG
    )
    user = (
        f"{_cutoff_fence(config.CUTOFF_HUI)}\n\n"
        f"传主：「{novel_name}」（agentId={agent_id}）。\n"
        "依据下方逐回抽取证据，归并为一份 bio + dossier。\n"
        "规则：\n"
        f"1. bio.sex 必填，不得为空。\n"
        f"2. bio 的 personality/appearance/surfaceManner 区分开："
        f"appearance+surfaceManner 只含『陌生人初见可知』，"
        f"不得混入性情、身世、计划。\n"
        f"3. dossier.people：只为传主在第{config.CUTOFF_HUI}回前『确实认得』"
        f"的知名人物各写一条 PersonView，target 取自：{notable_list}。"
        f"若传主截至第{config.CUTOFF_HUI}回根本未识某人（如仅听闻、"
        f"或该人尚未登场），不要为其写条目。\n"
        f"4. martialNote 只记第{config.CUTOFF_HUI}回前亲眼所见的路数，"
        f"严禁写降龙十八掌/打狗棒法/九阴白骨爪/一阳指/蛤蟆功等武功正式名。\n"
        f"5. polityKnowledge.subject 取自 {polities}；"
        f"factionKnowledge.subject 取自 {factions}；"
        f"只写『超出公共世界背景之外、此人额外所知』的内容，无则空数组。\n"
        f"6. theyDoNotKnow：站在该 target 的角度，写其截至第"
        f"{config.CUTOFF_HUI}回 对传主【并不知情】之事（支撑心智理论）。\n"
        f"7. 【归属句式・强制】relationship/impression/sharedHistory/"
        f"theyDoNotKnow 一律用『据传主所见』『传主认为』『他声称』"
        f"『对方表示』等归属/主观句式，写成传主的主观认知，"
        f"不得用全知旁白把任一方说法当成既成事实。\n"
        f"8. 【简体・强制】所有字段（尤其 people[].target）必须输出简体中文，"
        f"逐字转换：黃→黄、歐→欧、陽→阳、藥→药、靈→灵、彭連虎→彭连虎、"
        f"梅超風→梅超风、完顏→完颜、丘處機→丘处机 等，"
        f"严禁任何繁体字残留。\n"
        f"9. 【身分不可混淆・强制】严禁把 欧阳克 与 完颜康(杨康) 写成同一人/"
        f"互为化名/一人假扮另一人；严禁赋予 黄蓉 与 杨康/完颜康 任何"
        f"婚约/未婚夫/旧识关系（黄蓉 回10 对杨康一无所知）。\n\n"
        f"{_json_only(schema)}\n\n"
        f"———「{novel_name}」逐回抽取证据(JSON)———\n{evidence}"
    )
    raw = client.complete(system, user,
                          max_tokens=config.MAX_TOKENS_REDUCE,
                          label=f"reduce {novel_name}")
    parsed = _parse_json(raw, fallback={"bio": {}, "dossier": {}})

    bio_in = parsed.get("bio", {}) or {}
    dos_in = parsed.get("dossier", {}) or {}

    # --- bios.json entry (every key emitted; sex ALWAYS non-empty) -------
    bio = {
        "agentId": agent_id,
        "sex": _coerce_sex(bio_in.get("sex", ""), novel_name),
        "ageText": str(bio_in.get("ageText", "")),
        "personality": str(bio_in.get("personality", "")),
        "appearance": str(bio_in.get("appearance", "")),
        "surfaceManner": str(bio_in.get("surfaceManner", "")),
    }

    # --- dossiers.json entry (exact contract shape) ---------------------
    def kline(k):
        return {"subject": str(k.get("subject", "")),
                "text": str(k.get("text", ""))}

    def pview(p):
        return {
            "target": str(p.get("target", "")),
            "relationship": str(p.get("relationship", "")),
            "impression": str(p.get("impression", "")),
            "martialNote": str(p.get("martialNote", "")),
            "sharedHistory": str(p.get("sharedHistory", "")),
            "theyDoNotKnow": str(p.get("theyDoNotKnow", "")),
        }

    dossier = {
        "agentId": agent_id,
        "polityKnowledge": [kline(k)
                            for k in dos_in.get("polityKnowledge", []) or []],
        "factionKnowledge": [kline(k)
                             for k in dos_in.get("factionKnowledge", []) or []],
        "people": [pview(p) for p in dos_in.get("people", []) or []],
    }
    return {"bio": bio, "dossier": dossier}


# ---------------------------------------------------------------------------
# Orchestration (called by run.py). `only` selects a subset.
# ---------------------------------------------------------------------------

def run_pipeline(client: XaiClient, chapters: List[chunker.Chapter],
                 only: str = None) -> Dict[str, dict]:
    """Returns {"world": {...}, "bios": {...}, "dossiers": {...}} for the
    selected subset. CUTOFF is enforced structurally here: the per-roster
    map pulls EXCLUSIVELY from chapters_up_to_cutoff()."""
    results: Dict[str, dict] = {}
    in_scope = chunker.chapters_up_to_cutoff(chapters)  # <-- structural slice
    print(f"[extract] structural cutoff: feeding 回"
          f"{[c.hui for c in in_scope]} only "
          f"(CUTOFF_HUI={config.CUTOFF_HUI}); "
          f"{len(chapters) - len(in_scope)} later 回 are NEVER sent.")

    if only in (None, "world"):
        results["world"] = build_world(client, chapters)

    if only in (None, "bios", "dossiers"):
        bios, dossiers = [], []
        for entry in config.ROSTER:
            nm = entry["novelName"]
            per_chapter = []
            for ch in in_scope:                      # cutoff-bounded only
                if not _present_in_chapter(client, nm, ch):
                    continue
                ex = _extract_char_chapter(client, nm, ch)
                ex["_hui"] = ch.hui
                per_chapter.append(ex)
            reduced = reduce_character(client, entry, per_chapter)
            bios.append(reduced["bio"])
            dossiers.append(reduced["dossier"])
        if only in (None, "bios"):
            results["bios"] = {"bios": bios}
        if only in (None, "dossiers"):
            results["dossiers"] = {"dossiers": dossiers}

    return results
