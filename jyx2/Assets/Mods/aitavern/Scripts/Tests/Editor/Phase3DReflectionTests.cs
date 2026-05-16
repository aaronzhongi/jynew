// T3D.6 (Plan §5.1 / §5.2 / §5.3.1 / §5.5 / §6.1 / §9) — NEW Phase 3D tests:
// the episodic ring, the §5.0/§5.5.2/§5.5.3 assembler render + §4 overlay +
// lean Leave profile, and the §9 no-double / no-under render regression
// guarding the T3D.5 AppendTranscript deletion.
//
// Black-box where possible: ring turns are seeded via the REAL path
// EpisodicRing.Record (the same call the turn-writers make — NOT
// conv.Transcript.Add); render is asserted on the public
// ContextAssembler.Build / ConversationPrompts.BuildContinue/BuildLeave
// output; ring/mind state is inspected via the public mgr.Minds/mgr.Memory.
//
// Emitted strings matched byte-for-byte against ContextAssembler.AppendRing:
//   header "\n最近交谈（最近<N>轮）："  recency tag "\n［刚刚结束的对话］"
//   (FULL-WIDTH corner brackets ［ ］ — distinct from the deleted Phase 2
//   ASCII "[刚刚结束的对话]") then "\n<SpeakerName>：<Text>".
using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class Phase3DReflectionTests
    {
        AITavernManager _mgr;
        readonly List<CharacterBio> _bios = new List<CharacterBio>();

        GameId _hr;   // huangrong (NPC talker)
        GameId _ok;   // ouyangke  (NPC talkee)
        GameId _pl;   // player    (human — no mind, valid talkee key)

        const string H_SELF       = "【我是谁】";
        const string H_WORLD      = "【世界背景】";
        const string H_LONGTERM   = "【我所知";
        const string H_SHORTTERM  = "【眼前局势 — 短期记忆】";
        const string L_GLOBAL     = "此间众人，我之所察（跨人反思）：";
        const string L_SUMMARY    = "往来印象（已沉淀）：";
        const string L_RINGHEAD   = "最近交谈（最近";
        const string TAG_JUSTNOW  = "［刚刚结束的对话］";   // full-width ［ ］
        const string L_OVERLAY    = "［本局所历］：";

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _hr = new GameId("huangrong");
            _ok = new GameId("ouyangke");
            _pl = new GameId("player");
            _bios.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in _bios) TestBuilders.DestroyBio(b);
            _bios.Clear();
            TestBuilders.DestroyManager(_mgr);
        }

        // ---------- helpers ----------

        CharacterBio NewBio(string id, string name)
        {
            var bio = TestBuilders.MakeBio(id);
            bio.BioName = name;
            _bios.Add(bio);
            return bio;
        }

        Agent RegisterNpc(string id, string name)
        {
            var agent = TestBuilders.MakeAgent(id, isHuman: false, bio: NewBio(id, name));
            _mgr.NPCs.Register(agent);
            return agent;
        }

        Agent RegisterHuman(string id, string name)
        {
            var agent = TestBuilders.MakeAgent(id, isHuman: true, bio: NewBio(id, name));
            _mgr.NPCs.Register(agent);
            return agent;
        }

        // A 2-party conversation POCO with both ids as participants (the shape
        // EpisodicRing.Record iterates). No FSM state needed — Record only
        // reads conv.Participants.
        static Conversation Conv(GameId a, GameId b)
        {
            var c = new Conversation();
            c.Participants[a] = new ConversationMember { Status = MemberStatusKind.Participating };
            c.Participants[b] = new ConversationMember { Status = MemberStatusKind.Participating };
            return c;
        }

        static int Idx(string hay, string needle) => hay.IndexOf(needle, StringComparison.Ordinal);

        static int CountOccurrences(string hay, string needle)
        {
            int n = 0, i = 0;
            while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        TargetState Ring(GameId owner, GameId talkee)
        {
            return _mgr.GetOrCreateMind(owner).Targets.TryGetValue(talkee, out var ts) ? ts : null;
        }

        // ============================================================
        //  PART 1 — episodic ring (Plan §5.1 / §5.2)
        // ============================================================

        // ---------- Test 1: Record puts the turn in BOTH participants' rings ----------

        [Test]
        public void Ring_Record_AppendsToBothParticipantsRings()
        {
            RegisterNpc("huangrong", "黄蓉");
            RegisterNpc("ouyangke", "欧阳克");
            var conv = Conv(_hr, _ok);

            EpisodicRing.Record(_mgr, conv, _hr, "蓉儿先开口", 1_000_100);

            var hrRing = Ring(_hr, _ok);
            var okRing = Ring(_ok, _hr);
            Assert.IsNotNull(hrRing, "talker's mind has a Targets[talkee] entry");
            Assert.IsNotNull(okRing, "talkee NPC also records the exchange from its own side");
            Assert.AreEqual(1, hrRing.Ring.Count, "huangrong→ouyangke ring has the turn");
            Assert.AreEqual(1, okRing.Ring.Count, "ouyangke→huangrong ring has the turn (both POVs)");
            Assert.AreEqual("蓉儿先开口", hrRing.Ring[0].Text);
            Assert.AreEqual(_hr, hrRing.Ring[0].Speaker, "Speaker recorded verbatim");
        }

        // ---------- Test 2: player skipped as OWNER but is a valid talkee key ----------

        [Test]
        public void Ring_Record_PlayerSkippedAsOwnerButValidTalkee()
        {
            RegisterNpc("huangrong", "黄蓉");
            RegisterHuman("player", "玩家");
            var conv = Conv(_hr, _pl);

            EpisodicRing.Record(_mgr, conv, _pl, "玩家说的一句", 1_000_100);

            // The NPC records it keyed by the player as talkee.
            var hrRing = Ring(_hr, _pl);
            Assert.IsNotNull(hrRing, "NPC owner records the turn keyed by the player talkee");
            Assert.AreEqual(1, hrRing.Ring.Count);

            // The player has NO mind (skipped as owner — mirrors SeedAffection).
            Assert.IsFalse(_mgr.Minds.ContainsKey(_pl),
                "player has no RuntimeMindState — skipped as a ring OWNER (Plan §5.1)");
        }

        // ---------- Test 3: 11th turn evicts Ring[1] (not anchor [0]); count stays 10;
        //                    evicted turn spills to MemoryStash for the canonical pair ----------

        [Test]
        public void Ring_EleventhTurn_EvictsRingOne_NotAnchor_SpillsToStash()
        {
            RegisterNpc("huangrong", "黄蓉");
            RegisterNpc("ouyangke", "欧阳克");
            var conv = Conv(_hr, _ok);

            // 10 turns fill the ring exactly (cap = MEMORY_RING_CAP).
            for (int i = 0; i < AITavernConstants.MEMORY_RING_CAP; i++)
                EpisodicRing.Record(_mgr, conv, _hr, "turn-" + i, 1_000_100 + i);

            var ts = Ring(_hr, _ok);
            Assert.AreEqual(10, ts.Ring.Count, "ring full at cap");
            Assert.AreEqual("turn-0", ts.Ring[0].Text, "Ring[0] is the anchor (first turn)");
            string ring1Before = ts.Ring[1].Text;        // "turn-1" — oldest non-anchor
            Assert.AreEqual("turn-1", ring1Before);

            int stashBefore = _mgr.Memory.Entries.Count;

            // 11th turn → evict Ring[1], spill it; anchor survives; count stays 10.
            EpisodicRing.Record(_mgr, conv, _hr, "turn-10", 1_000_111);

            Assert.AreEqual(10, ts.Ring.Count, "Count stays at MEMORY_RING_CAP after eviction");
            Assert.AreEqual("turn-0", ts.Ring[0].Text,
                "anchor Ring[0] SURVIVES eviction (NEVER evicted — Plan §5.1)");
            Assert.AreEqual("turn-2", ts.Ring[1].Text,
                "the OLDEST NON-anchor (old Ring[1]=turn-1) was evicted; turn-2 shifts into Ring[1]");
            Assert.AreEqual("turn-10", ts.Ring[ts.Ring.Count - 1].Text, "newest turn appended last");

            Assert.AreEqual(stashBefore + 1, _mgr.Memory.Entries.Count,
                "exactly one evicted turn spilled to the raw MemoryStash list (mid-conv, no Grok)");
            var spill = _mgr.Memory.Entries[_mgr.Memory.Entries.Count - 1];
            Assert.AreEqual(MemoryType.Conversation, spill.Type);
            StringAssert.Contains("[spill]", spill.Description, "evicted turn spilled with the [spill] marker");
            StringAssert.Contains(ring1Before, spill.Description, "the evicted turn-1 text is what got spilled");
            // Canonical pair-keyed (same string.CompareOrdinal MemoryStash uses).
            string expectedKey = string.CompareOrdinal("huangrong", "ouyangke") <= 0
                ? "huangrong|ouyangke" : "ouyangke|huangrong";
            Assert.AreEqual(expectedKey, spill.PairKey, "spill keyed under the canonical pair");
        }

        // ---------- Test 4: blank-text turns are NOT ringed (guarded) ----------

        [Test]
        public void Ring_Record_BlankTurnIgnored()
        {
            RegisterNpc("huangrong", "黄蓉");
            RegisterNpc("ouyangke", "欧阳克");
            var conv = Conv(_hr, _ok);

            EpisodicRing.Record(_mgr, conv, _hr, "   ", 1_000_100);
            EpisodicRing.Record(_mgr, conv, _hr, null, 1_000_101);

            var ts = Ring(_hr, _ok);
            // Targets may be created defensively, but no blank turn is appended.
            Assert.IsTrue(ts == null || ts.Ring.Count == 0,
                "blank/whitespace/null turn text is NOT ringed (guarded return, Plan §5.1)");
        }

        // ============================================================
        //  PART 2 — assembler 3D render (Plan §5.0 / §5.5 / §4 overlay / §6.1)
        // ============================================================

        // ---------- Test 5: §5.0 GlobalReflection — Full only, omit when blank ----------

        [Test]
        public void Assembler_GlobalReflection_FullOnly_OmitWhenBlank()
        {
            var talker = RegisterNpc("huangrong", "黄蓉");
            var talkee = RegisterNpc("ouyangke", "欧阳克");
            talker.Bio.Personality = "灵动机敏";

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);

            // Blank → §5.0 omitted.
            string blank = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);
            Assert.IsFalse(blank.Contains(L_GLOBAL), "§5.0 omitted when GlobalReflection blank");

            // Set → §5.0 rendered (Full).
            mind.GlobalReflection = "在场众人皆回避木箱话题，各怀心事";
            string full = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains(L_GLOBAL, full, "§5.0 header rendered when set (Full profile)");
            StringAssert.Contains("在场众人皆回避木箱话题", full, "§5.0 reflection prose rendered");

            // Leave profile EXCLUDES §5.0 even when set (Plan §6.1 lean list).
            string leave = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Leave);
            Assert.IsFalse(leave.Contains(L_GLOBAL),
                "§5.0 excluded from the lean Leave profile (Plan §6.1: §2+§5.4+§5.5.3 only)");
        }

        // ---------- Test 6: PerTargetBlock — §5.5.1+§5.5.2+§5.5.3 under one ·对 X· ----------

        [Test]
        public void Assembler_PerTargetBlock_RendersAllThreeUnderOneHeader()
        {
            var talker = RegisterNpc("huangrong", "黄蓉");
            var talkee = RegisterNpc("ouyangke", "欧阳克");
            talker.Bio.Personality = "灵动机敏";

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            var ts = new TargetState
            {
                Affection = new Affect
                {
                    Label = "厌恶", Value = -0.55f, Baseline = -0.4f,
                    LastSetMs = 0, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
                },
                ReflectionSummary = "他屡屡试探木箱、回避正面问话",
            };
            ts.Ring.Add(new TurnRecord { Speaker = talkee.PlayerId, Text = "蓉儿妹妹别来无恙", Ms = 1 });
            ts.Ring.Add(new TurnRecord { Speaker = talker.PlayerId, Text = "你到底想做什么", Ms = 2 });
            mind.Targets[talkee.PlayerId] = ts;

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            int header = Idx(s, "·对 欧阳克·");
            int aff = Idx(s, "当下好恶：");
            int sum = Idx(s, L_SUMMARY);
            int ring = Idx(s, L_RINGHEAD);
            Assert.Greater(header, -1, "single ·对 X· header present");
            Assert.Greater(aff, -1, "§5.5.1 当下好恶 present");
            Assert.Greater(sum, -1, "§5.5.2 往来印象（已沉淀）present");
            Assert.Greater(ring, -1, "§5.5.3 ring header present");
            Assert.Less(header, aff, "header precedes §5.5.1");
            Assert.Less(aff, sum, "§5.5.1 precedes §5.5.2");
            Assert.Less(sum, ring, "§5.5.2 precedes §5.5.3 (one cohesive block)");

            // Only ONE ·对 欧阳克· header (the three sub-blocks share it).
            Assert.AreEqual(1, CountOccurrences(s, "·对 欧阳克·"),
                "all three §5.5 sub-blocks fold under exactly ONE ·对 X· header");

            // Ring oldest→newest, recency tag before the LAST line.
            int t1 = Idx(s, "蓉儿妹妹别来无恙");
            int tag = Idx(s, TAG_JUSTNOW);
            int t2 = Idx(s, "你到底想做什么");
            Assert.Less(t1, tag, "older turn rendered before the ［刚刚结束的对话］ tag");
            Assert.Less(tag, t2, "tag precedes the LAST (newest) turn (Plan §1 mock / §5.5.3)");
        }

        // ---------- Test 7: PerTargetBlock null (no bare header) when ALL empty;
        //                    renders if ANY present ----------

        [Test]
        public void Assembler_PerTargetBlock_OmittedWhenAllEmpty_RendersIfRingOnly()
        {
            var talker = RegisterNpc("huangrong", "黄蓉");
            var talkee = RegisterNpc("ouyangke", "欧阳克");
            talker.Bio.Personality = "灵动机敏";

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Targets[talkee.PlayerId] = new TargetState();   // Affection null, summary null, ring empty

            string empty = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);
            Assert.IsFalse(empty.Contains("·对 欧阳克·"),
                "no bare ·对 X· header when affection+summary+ring ALL empty");

            // Ring-only (brand-new pair) → block renders with §5.5.3.
            mind.Targets[talkee.PlayerId].Ring.Add(
                new TurnRecord { Speaker = talkee.PlayerId, Text = "唯一一句", Ms = 1 });
            string ringOnly = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("·对 欧阳克·", ringOnly, "block renders if ANY of the three present (ring-only)");
            StringAssert.Contains("唯一一句", ringOnly);
        }

        // ---------- Test 8: §4 ［本局所历］ from mind ImpressionDelta only; Dossier untouched ----------

        [Test]
        public void Assembler_LongTermOverlay_FromImpressionDeltaOnly_DossierUntouched()
        {
            var talker = RegisterNpc("huangrong", "黄蓉");
            var talkee = RegisterNpc("ouyangke", "欧阳克");
            talker.Bio.Personality = "灵动机敏";

            var dossier = ScriptableObject.CreateInstance<CharacterDossier>();
            dossier.AgentId = "huangrong";
            const string canon = "白驼山少主，心术不正（canon）";
            var pv = new PersonView { Target = "ouyangke", Impression = canon };
            dossier.People.Add(pv);
            _mgr.Dossiers["huangrong"] = dossier;
            try
            {
                var mind = _mgr.GetOrCreateMind(talker.PlayerId);
                mind.Targets[talkee.PlayerId] = new TargetState
                {
                    ImpressionDelta = "[1003000] 本局他承认别有目的",
                };

                string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

                StringAssert.Contains(canon, s, "canon PersonView.Impression still rendered");
                StringAssert.Contains(L_OVERLAY, s, "§4 ［本局所历］ overlay line emitted");
                StringAssert.Contains("本局他承认别有目的", s, "overlay sourced from runtime ImpressionDelta");
                Assert.AreEqual(canon, pv.Impression,
                    "the Dossier asset PersonView.Impression is NEVER mutated by the assembler render");
            }
            finally { ScriptableObject.DestroyImmediate(dossier); }
        }

        // ---------- Test 9: Leave = §2 + §5.4 + §5.5.3-ring only (no §1/§3/§4/§5.0/§5.5.1/§5.5.2) ----------

        [Test]
        public void Assembler_LeaveProfile_RingOnlyPlusSelfBioPlusEmotion()
        {
            _mgr.World = ScriptableObject.CreateInstance<WorldCodex>();
            _mgr.World.Era = "南宋宁宗庆元年间";
            try
            {
                var talker = RegisterNpc("huangrong", "黄蓉");
                var talkee = RegisterNpc("ouyangke", "欧阳克");
                talker.Bio.Personality = "灵动机敏";

                var dossier = ScriptableObject.CreateInstance<CharacterDossier>();
                dossier.AgentId = "huangrong";
                dossier.People.Add(new PersonView { Target = "ouyangke", Impression = "心术不正LONGTERM" });
                _mgr.Dossiers["huangrong"] = dossier;
                try
                {
                    var mind = _mgr.GetOrCreateMind(talker.PlayerId);
                    mind.GlobalReflection = "GLOBAL_REFLECTION_SENTINEL";
                    var ts = new TargetState
                    {
                        Affection = new Affect
                        {
                            Label = "厌恶", Value = -0.55f, Baseline = -0.4f,
                            LastSetMs = 0, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
                        },
                        ReflectionSummary = "SUMMARY_SENTINEL 他屡屡试探",
                    };
                    ts.Ring.Add(new TurnRecord { Speaker = talkee.PlayerId, Text = "最后一句RING", Ms = 1 });
                    mind.Targets[talkee.PlayerId] = ts;

                    string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Leave);

                    // Present: §2 self-bio + §5.5.3 ring.
                    StringAssert.Contains(H_SELF, s, "Leave keeps §2 (stay in voice)");
                    StringAssert.Contains(L_RINGHEAD, s, "Leave keeps the §5.5.3 ring (knows what just happened)");
                    StringAssert.Contains("最后一句RING", s, "Leave renders the ring turn");
                    StringAssert.Contains(TAG_JUSTNOW, s, "Leave ring still tags the newest turn");

                    // Excluded by the lean Leave list (Plan §6.1).
                    Assert.IsFalse(s.Contains(H_WORLD), "Leave excludes §1 World Codex");
                    Assert.IsFalse(s.Contains(H_LONGTERM), "Leave excludes §4 long-term knowledge");
                    Assert.IsFalse(s.Contains("心术不正LONGTERM"), "no §4 dossier content in Leave");
                    Assert.IsFalse(s.Contains(L_GLOBAL), "Leave excludes §5.0 global reflection");
                    Assert.IsFalse(s.Contains("GLOBAL_REFLECTION_SENTINEL"), "no §5.0 content in Leave");
                    Assert.IsFalse(s.Contains("当下好恶："), "Leave excludes §5.5.1 affection");
                    Assert.IsFalse(s.Contains(L_SUMMARY), "Leave excludes §5.5.2 reflection summary");
                    Assert.IsFalse(s.Contains("SUMMARY_SENTINEL"), "no §5.5.2 content in Leave");
                }
                finally { ScriptableObject.DestroyImmediate(dossier); }
            }
            finally { var w = _mgr.World; _mgr.World = null; ScriptableObject.DestroyImmediate(w); }
        }

        // ============================================================
        //  PART 3 — §9 no-double / no-under render (T3D.5 deletion guard)
        // ============================================================

        // ---------- Test 10: a turn driven through the REAL ring path appears
        //                     EXACTLY ONCE in BuildContinue, last-tagged, and
        //                     no "Conversation so far:" string survives ----------

        [Test]
        public void Continue_LiveTurnViaRing_AppearsExactlyOnce_NoDoubleNoUnderRender()
        {
            var talker = RegisterNpc("huangrong", "黄蓉");
            var talkee = RegisterNpc("ouyangke", "欧阳克");
            talker.Bio.Personality = "灵动机敏";
            talker.Bio.Identity = "桃花岛黄药师之女";
            talkee.Bio.Identity = "白驼山少主";
            var conv = Conv(talker.PlayerId, talkee.PlayerId);

            // Drive turns through the REAL co-located ring path (the same call
            // AgentGenerateMessageOp / the player-submit site make) — NOT
            // conv.Transcript.Add. This is the §5.1 invariant that makes the
            // T3D.5 AppendTranscript deletion safe.
            const string oldTurn  = "OLD_TURN_SENTINEL 蓉儿妹妹别来无恙";
            const string liveTurn = "LIVE_TURN_SENTINEL 你到底想做什么";
            EpisodicRing.Record(_mgr, conv, talkee.PlayerId, oldTurn, 1_000_100);
            EpisodicRing.Record(_mgr, conv, talker.PlayerId, liveTurn, 1_000_200);

            var cont = ConversationPrompts.BuildContinue(
                talker.Bio, talkee.Bio, conv, talker, talkee, _mgr, now: 1_001_000);
            string sp = cont.SystemPrompt;

            // NO under-render: the live (most-recent) turn IS present.
            StringAssert.Contains(liveTurn, sp,
                "the in-flight turn reaches the Continue prompt via the §5.5.3 ring (no under-render)");

            // NO double-render: it appears EXACTLY ONCE (only via §5.5.3, never
            // a second AppendTranscript path).
            Assert.AreEqual(1, CountOccurrences(sp, liveTurn),
                "the live turn appears EXACTLY ONCE (ring is the sole transcript source — T3D.5)");
            Assert.AreEqual(1, CountOccurrences(sp, oldTurn),
                "the older ring turn also appears exactly once");

            // It is inside the §5.5.3 ring block, last-tagged ［刚刚结束的对话］.
            int ringHead = Idx(sp, L_RINGHEAD);
            int tag = Idx(sp, TAG_JUSTNOW);
            int live = Idx(sp, liveTurn);
            Assert.Greater(ringHead, -1, "§5.5.3 ring header present in Continue");
            Assert.Less(ringHead, tag, "ring header precedes the recency tag");
            Assert.Less(tag, live, "the ［刚刚结束的对话］ tag precedes the live (newest) turn");

            // The deleted Phase 2 paths leave NO trace.
            Assert.IsFalse(sp.Contains("Conversation so far:"),
                "the deleted Phase 2 live-transcript header must NOT appear (AppendTranscript removed)");
            Assert.IsFalse(sp.Contains("你与 欧阳克 的过往："),
                "the deleted Phase 2 prior-memory header must NOT appear (BuildPriorMemoryBlock removed)");

            // PrependAssembler output + the Chinese anti-repeat tail survive.
            StringAssert.Contains(H_SELF, sp, "assembler block still prepended (PrependAssembler intact)");
            StringAssert.Contains("不要复述上面已有的对话内容", sp,
                "the Phase 2 Chinese anti-repeat tail is KEPT (Plan §6 / T3D.5)");
        }

        // ---------- Test 11: N ringed turns → all N appear once, only newest tagged ----------

        [Test]
        public void Continue_AllRingedTurnsShownOnce_OnlyNewestTagged()
        {
            var talker = RegisterNpc("huangrong", "黄蓉");
            var talkee = RegisterNpc("ouyangke", "欧阳克");
            talker.Bio.Personality = "灵动机敏";
            talker.Bio.Identity = "桃花岛黄药师之女";
            talkee.Bio.Identity = "白驼山少主";
            var conv = Conv(talker.PlayerId, talkee.PlayerId);

            const int N = 5;
            for (int i = 0; i < N; i++)
            {
                var who = (i % 2 == 0) ? talkee.PlayerId : talker.PlayerId;
                EpisodicRing.Record(_mgr, conv, who, "RTURN_" + i + "_SENTINEL", 1_000_100 + i);
            }

            var cont = ConversationPrompts.BuildContinue(
                talker.Bio, talkee.Bio, conv, talker, talkee, _mgr, now: 1_001_000);
            string sp = cont.SystemPrompt;

            for (int i = 0; i < N; i++)
                Assert.AreEqual(1, CountOccurrences(sp, "RTURN_" + i + "_SENTINEL"),
                    "ringed turn " + i + " appears exactly once (no double-render)");

            // Exactly ONE recency tag, on the newest turn.
            Assert.AreEqual(1, CountOccurrences(sp, TAG_JUSTNOW),
                "exactly ONE ［刚刚结束的对话］ tag in the prompt");
            int tag = Idx(sp, TAG_JUSTNOW);
            int lastTurn = Idx(sp, "RTURN_" + (N - 1) + "_SENTINEL");
            int prevTurn = Idx(sp, "RTURN_" + (N - 2) + "_SENTINEL");
            Assert.Less(prevTurn, tag, "the tag is NOT before the second-newest turn");
            Assert.Less(tag, lastTurn, "the tag immediately precedes only the NEWEST turn");
            Assert.IsFalse(sp.Contains("Conversation so far:"),
                "no residual Phase 2 transcript path");
        }
    }
}
