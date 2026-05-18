"""All tunables + the auditable curated sets for the lore pipeline.

Everything a human might want to review or override lives here. The
curated sets (ROSTER / NOTABLE_SET / WORLD_SPEC) are the novel-lore
reviewer's Round-1 corrected, cutoff-bounded universe (Plan §3.3 / §10
Q4 / §14). Do not widen them without a novel-lore review.

All Chinese names in this file are 繁體 (as they appear in shediao.txt)
so the prompts can string-match the raw novel. The MODEL is instructed
to OUTPUT 简体中文 (the game/bios are Simplified) — see extract.py.
"""

import os

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

# Default novel location. The user may move it; override with --novel or the
# JYNEW_NOVEL_PATH env var. The file is 繁體, ~2.7 MB, 4 卷 40 回, NOT
# pre-converted to Simplified (Plan §10 Q7 — 繁→简 is done per-call by Grok).
NOVEL_PATH = os.environ.get(
    "JYNEW_NOVEL_PATH",
    r"C:\Users\aaronzhong\Downloads\shediao.txt",
)

# Where the 3 JSON files are written. The Unity importer points here:
# AI Tavern ▸ Import Lore JSON… ▸ tools/lore_pipeline/out/
OUT_DIR = os.environ.get(
    "JYNEW_LORE_OUT",
    os.path.join(os.path.dirname(__file__), "out"),
)

# ---------------------------------------------------------------------------
# Model / sampling
# ---------------------------------------------------------------------------

# Grok chat model id. Phase 1/2 used this id; VERIFY against current xAI
# docs (https://docs.x.ai/) before a real run — model ids change. Override
# with the JYNEW_GROK_MODEL env var without editing this file.
MODEL = os.environ.get("JYNEW_GROK_MODEL", "grok-4.20-non-reasoning")

# Low temperature: this is factual extraction from a fixed text, not
# creative writing. Determinism > variety (Plan §3.2). Idempotent re-runs.
TEMPERATURE = 0.2

# Per-call output ceiling. Extraction blurbs are short; reduce calls are
# the longest (a whole world / dossier).
MAX_TOKENS_EXTRACT = 1500
MAX_TOKENS_REDUCE = 6000

# Transient-error retry/backoff (xai_client.py).
MAX_RETRIES = 4
RETRY_BASE_SECONDS = 2.0  # exponential: 2,4,8,16

# ---------------------------------------------------------------------------
# Story cutoff (Plan §3.3 — LOAD-BEARING, novel-lore reviewer must-fix #1)
# ---------------------------------------------------------------------------

# 回10 「冤家聚頭」 (赵王府, 燕京) is the first & defining 黃蓉×歐陽克 contact
# and the latest point consistent with "they barely know each other; he
# covets her". Per-character extraction is fed ONLY 回1..CUTOFF_HUI and the
# chapter list is PHYSICALLY SLICED (chunker) — not merely prompt-asked.
CUTOFF_HUI = 10

# The §3.3 spoiler-exclusion list. Injected verbatim into every extraction
# / reduce prompt as a hard "these facts do NOT exist yet" fence. Even
# though the text is sliced to 回≤10, a model can still hallucinate famous
# canon it knows out-of-band — this list forbids it explicitly.
SPOILER_EXCLUSIONS = [
    "歐陽鋒逆練九陰真經發瘋（回40 華山論劍）—— 回10 尚未發生，禁止提及。",
    "楊康（完顏康）身世作為「既成定論」—— 回10 僅有疑點/暗示，"
    "黃蓉對此認知薄弱、不確定，禁止寫成已知事實。",
    "歐陽克之死（桃花島巨岩壓死，回21-22）—— 回10 尚未發生，禁止提及。",
    "黃蓉任丐幫幫主 / 學打狗棒法（回12之後）—— 回10 她尚未見過洪七公，禁止提及。",
    "九陰真經情節：周伯通、桃花島三道試題、經文竄改（回16-20）—— "
    "回10 黃蓉只當真經是其父輩傳說，不知內容，禁止提及。",
    "武功名稱 降龍十八掌 / 打狗棒法 / 九陰白骨爪 / 一陽指 / 蛤蟆功 —— "
    "黃蓉在回10 尚不知這些武功的名字（皆為回10之後 洪七公/歐陽鋒 所授/所見）。"
    "MartialNote 只可記錄各人在回10之前 親眼所見 的招式路數"
    "（如 黃蓉 在回10比武中看出 歐陽克 借力打力、白駝山內功；"
    "歐陽克 見黃蓉 桃花島路數 + 軟蝟甲護身），不可寫武功正式名稱。",

    # --- DISTINCT IDENTITIES (anti-hallucination, not a spoiler but a
    #     fabrication fence — lore-audit found the model inventing a
    #     歐陽克=完顏康 merge and a 黃蓉×楊康 betrothal). These are
    #     HARD identity assertions, not "facts that haven't happened yet". ---
    "【人物身分不可混淆】歐陽克 與 完顏康（楊康）是兩個完全不同的人物，"
    "彼此毫無血緣、師承或化名關係。歐陽克 從未化名/假扮 完顏康，"
    "完顏康 也從未假扮 歐陽克。嚴禁把兩人寫成同一人、互為化名、"
    "或一人假扮另一人。在任何 relationship / sharedHistory / theyDoNotKnow "
    "欄位都不得出現「歐陽克即完顏康」「化名」「即為同一人」之類描述。",
    "【指腹為婚的真正當事人】回1/回6 正文確立：郭嘯天 與 楊鐵心 約定，"
    "兩家若一男一女即結為夫妻——此婚約只繫於 郭靖（郭嘯天之子）與 "
    "楊康（楊鐵心親子），且『楊家槍法傳子不傳女』。穆念慈 是 楊鐵心"
    "（回10 化名穆易）的『養女/義女』，並非親生，亦非此婚約當事人；"
    "黃蓉 與此婚約亦完全無關。嚴禁把『指腹為婚/未婚妻/兒女親家』套到 "
    "穆念慈 或 黃蓉 身上，也不得寫成 穆念慈 是 完顏康/郭靖 的指腹未婚妻。",
    "【黃蓉對楊康一無所知】回10 黃蓉與 完顏康（楊康）僅在趙王府擦身/旁觀，"
    "並不相識。嚴禁賦予 黃蓉 與 楊康/完顏康 任何未婚夫/婚約/舊識關係；"
    "黃蓉 對楊康的認知應為「不認識/僅遠遠見過」。",
    "【穆念慈・回10封頂】回10 穆念慈 與 完顏康 的關係僅為『比武招親』"
    "之約（康勝、奪銀梭、立約）與初萌情愫；回10 她並無『丈夫』。嚴禁"
    "出現『丈夫』『楊康之死』『殉夫/絕食殉夫』等情節——楊康之死在回35，"
    "遠在封頂之後，屬未發生情節，禁止提及。",
    "【周伯通・回10封頂】回10 之前 黃蓉、郭靖 與 周伯通 素未謀面，"
    "周伯通 至多為他人口中傳聞；雙白鵰（小白鵰）源自回5 漠北、與 郭靖 "
    "少年相關，與 周伯通 毫無關係。嚴禁編造 周伯通 交付小白鵰、上崖、"
    "或與 黃蓉/郭靖 任何親身互動的 relationship/sharedHistory/theyDoNotKnow。",
]

# ---------------------------------------------------------------------------
# ROSTER — the mod's playable characters (Plan §2.3 / §10 Q4)
# ---------------------------------------------------------------------------

# These get their Bio_*.asset PATCHED (5 fields) AND a Dossier_<agentId>
# .asset written. agentId MUST equal the existing CharacterBio.AgentId in
# jyx2/Assets/Mods/aitavern/Resources/AITavern/Bios/. novelName is 繁體
# (matches shediao.txt) so the presence-gate prompt can string-match it.
ROSTER = [
    {"agentId": "huangrong", "novelName": "黃蓉"},
    {"agentId": "ouyangke", "novelName": "歐陽克"},
    {"agentId": "guojing", "novelName": "郭靖"},
    {"agentId": "munianci", "novelName": "穆念慈"},
]

# ---------------------------------------------------------------------------
# NOTABLE_SET — curated, cutoff-bounded notable people (Plan §10 Q4 / §14)
# ---------------------------------------------------------------------------

# This is BOTH world.notableFigures AND the universe of
# dossier.people[].target. Novel-lore reviewer's Round-1 corrected set:
# every figure 黃蓉/歐陽克 can canonically know by 回10. The importer/runtime
# match by AgentId or BioName, so emit the canonical (繁體) name and let
# Trad→Simp happen in the prompt output.
#
# Each entry: name (繁體), polity, faction, oneLine hint (the model refines
# the oneLine into Simplified, cutoff-bounded "everyone knows" prose).
NOTABLE_SET = [
    {"name": "黃藥師", "polity": "", "faction": "桃花島",
     "hint": "東邪，桃花島島主，黃蓉之父，奇門遁甲、武功詭奇。"},
    {"name": "歐陽鋒", "polity": "", "faction": "白駝山",
     "hint": "西毒，白駝山主，歐陽克之叔（名義上），用毒與蛤蟆功冠絕（武功名僅供你判斷，輸出受 SPOILER 規則約束）。"},
    {"name": "郭靖", "polity": "大宋", "faction": "",
     "hint": "江南長大、漠北蒙古養大的少年，憨厚朴拙，黃蓉在回7-10結識的少年。"},
    {"name": "周伯通", "polity": "", "faction": "全真教",
     "hint": "老頑童，全真教王重陽師弟，性情天真（回10之前以聽聞/側面為主）。"},
    {"name": "梅超風", "polity": "", "faction": "桃花島",
     "hint": "黑風雙煞之一，桃花島叛徒，黃藥師逐出門牆的女弟子。"},
    {"name": "完顏洪烈", "polity": "金", "faction": "",
     "hint": "金國趙王，野心勃勃，趙王府之主（回10 冤家聚頭場景的東道）。"},
    {"name": "完顏康（楊康）", "polity": "金", "faction": "",
     "hint": "趙王府小王爺，武藝出眾、心術可疑（身世在回10僅為疑點，受 SPOILER 規則約束）。"},
    {"name": "穆念慈", "polity": "大宋", "faction": "",
     "hint": "穆易（楊鐵心化名）養女，比武招親的剛烈少女，與完顏康有糾葛"
             "（回10：僅比武之約與初萌情愫；非指腹為婚當事人，回10無丈夫）。"},
    {"name": "丘處機", "polity": "大宋", "faction": "全真教",
     "hint": "長春子，全真七子之一，剛烈任俠，與江南七怪有約。"},
    {"name": "王處一", "polity": "大宋", "faction": "全真教",
     "hint": "玉陽子，全真七子之一，回10之前在燕京一帶與郭靖等有交。"},
    {"name": "沙通天", "polity": "金", "faction": "",
     "hint": "黃河四鬼之首/鬼門龍王，受完顏洪烈延攬的江湖好手。"},
    {"name": "彭連虎", "polity": "金", "faction": "",
     "hint": "千手人屠，兇悍狠辣，受完顏洪烈延攬之一。"},
    {"name": "梁子翁", "polity": "金", "faction": "",
     "hint": "參仙老怪，受完顏洪烈延攬之一，曾與郭靖結怨。"},
    {"name": "靈智上人", "polity": "金", "faction": "",
     "hint": "藏邊密宗高僧，受完顏洪烈延攬之一，武功剛猛。"},
    {"name": "黃蓉", "polity": "", "faction": "桃花島",
     "hint": "桃花島黃藥師之女，靈動機敏（roster 自身；其視角下對自己僅作外貌/氣度）。"},
    {"name": "歐陽克", "polity": "", "faction": "白駝山",
     "hint": "白駝山少主，錦衣風流，回10 趙王府初見黃蓉即神魂飄蕩。"},
]

# Explicitly EXCLUDED (post-回10 — novel-lore reviewer). Documented here
# so a future maintainer does not "helpfully" re-add them. NOT emitted.
EXCLUDED_FROM_NOTABLE = [
    "洪七公 — 黃蓉回12才遇，回10 不識。",
    "一燈大師 / 段智興 — 回30 才出場，回10 不識。",
    "江南七怪（作為已會面之人）— 黃蓉至回10最多『聽聞』，未真正結識。",
    "全真七子完整七人 — 回10 僅 丘處機/王處一 有接觸，其餘不入。",
]

# ---------------------------------------------------------------------------
# WORLD_SPEC — world.json shaping hints (Plan §1 / §14, novel-lore must-fix)
# ---------------------------------------------------------------------------

WORLD_SPEC = {
    # 时代 prose hint. The model writes the final Simplified prose; this
    # pins the era so it doesn't drift to a later point in the saga.
    "era_hint": "南宋寧宗慶元年間（約1199年起），蒙古於漠北崛起。"
                "宋偏安江南、與金為世仇，蒙古勢力漸盛而宋蒙關係未明。",

    # Polities — EXACTLY these three. NO 西夏 (Plan §1/§14: 西夏 在射雕中
    # 無足輕重，不入勢力表). The model must not add a 4th polity.
    "polities": ["大宋", "金", "蒙古"],
    "polities_rule": "勢力表恰為 大宋 / 金 / 蒙古 三者，"
                     "不得加入西夏或其他政權（西夏在射雕中無足輕重，不入表）。",

    # Factions — EXACTLY these five.
    "factions": ["全真教", "桃花島", "白駝山", "丐幫", "大理段氏"],
    "factions_rule": "門派表恰為 全真教 / 桃花島 / 白駝山 / 丐幫 / 大理段氏 五者。",

    # 五絕 rendering rule for notableFigures oneLine — novel-lore must-fix:
    # 段智興 must read 「一燈大師（已退隱為僧）」 NOT 「南帝」; 王重陽/中神通
    # must be 已故・僅追述. (These two are NOT in NOTABLE_SET as dossier
    # targets — they appear, if at all, only as world-codex background
    # one-liners. Stated here so the world reduce renders them correctly
    # if it mentions the 五絕 in era/faction prose.)
    "wujue_rule": "若提及五絕：段智興 須寫作「一燈大師（已退隱為僧）」"
                  "而非「南帝」；王重陽（中神通）須註明「已故・僅追述」。"
                  "東邪黃藥師（桃花島）、西毒歐陽鋒（白駝山）、"
                  "北丐洪七公（丐幫）照常。",
}

# ---------------------------------------------------------------------------
# Chunker
# ---------------------------------------------------------------------------

# The novel uses headers like `第1回 風雪驚變` at line start. chunker.py
# builds the regex from this; kept here so it is auditable / tweakable if
# a different edition uses 第十回 (Chinese numerals) instead of 第10回.
EXPECTED_HUI_COUNT = 40

# Marker that precedes the body in this edition ("----------小說正文開始
# ----------"). Everything before the first 回 header is preamble and is
# discarded regardless; this is informational.
BODY_START_MARKER = "小說正文開始"
