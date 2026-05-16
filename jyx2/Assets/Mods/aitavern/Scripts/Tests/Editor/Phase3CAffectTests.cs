// T3C.4 (Phase 3C) — affect-with-decay editor tests: Affect.Current math,
// AffectBaseline.ForRelation map, ContextAssembler §5.4 emotion + §5.5.1
// affection render/omit SEMANTICS.
//
// Plan refs: §2.4 (Affect / TargetState live on RuntimeMindState in
// AITavernManager.Minds, never serialized), §4.3 (emotion decays to 平静/0
// fast, omitted below EMOTION_FLOOR), §4.4 (affection decays toward the CANON
// relationship baseline — NOT 0 — over AFFECTION_HALFLIFE_MS), §6/§6.1
// (assembler ownership, empty-omission, §5.4 is in the lean Leave list but
// §5.5.1 affection is Full-profile ONLY), §9 (test conventions: NUnit,
// black-box on the public ContextAssembler.Build / Affect.Current /
// AffectBaseline.ForRelation / ConversationPrompts.BuildStart /
// AITavernManager API; ContextAssembler is sync — plain [Test]), §10 Q3
// (RATIFIED amendment — affection baseline is sourced from the committed
// RelationType canon via AffectBaseline.ForRelation, not a pipeline scalar).
//
// EmotionLine / EmotionOnlyBlock / AffectionBlock / BuildShortTerm are
// private — tested through ContextAssembler.Build exactly as the T3A.4 /
// T3B.4 tests do. The decay math itself is asserted via the PUBLIC
// Affect.Current(now) and AffectBaseline.ForRelation(RelationType). No
// runtime code is modified. Affect / mind fields are set directly on the
// public objects MakeManager / GetOrCreateMind hand back (it is all public).
//
// String formats matched byte-for-byte against the ContextAssembler helper
// bodies: header "【眼前局势 — 短期记忆】" (spaced em-dash); emotion
// "\n此刻心绪：<Label>（强度 <0.0>，正缓缓平复）" (InvariantCulture "0.0");
// affection "·对 <name>·\n当下好恶：<0.00>（<Label>，向长期基线缓回）"
// (InvariantCulture "0.00", signed, no leading '+').
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class Phase3CAffectTests
    {
        AITavernManager _mgr;
        readonly List<CharacterBio> _bios = new List<CharacterBio>();
        readonly List<ScriptableObject> _sos = new List<ScriptableObject>();

        const string H_WORLD     = "【世界背景】";
        const string H_SELF      = "【我是谁】";
        const string H_TALKEE    = "【对面是谁";   // prefix — tail " — 初见印象】"
        const string H_LONGTERM  = "【我所知";     // prefix — tail " — 长期记忆】"
        const string H_SHORTTERM = "【眼前局势 — 短期记忆】";

        const string L_SITUATION    = "\n处境：";
        const string L_TASK         = "\n目标：";
        const string L_SURROUNDINGS = "\n环境：";
        const string L_EMOTION      = "此刻心绪：";
        const string L_AFFECTION    = "当下好恶：";

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _bios.Clear();
            _sos.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in _bios) TestBuilders.DestroyBio(b);
            foreach (var s in _sos) if (s != null) UnityEngine.Object.DestroyImmediate(s);
            _bios.Clear();
            _sos.Clear();
            TestBuilders.DestroyManager(_mgr);
        }

        // ---------- helpers ----------

        CharacterBio NewBio(string agentId, string bioName = null)
        {
            var bio = TestBuilders.MakeBio(agentId);
            if (bioName != null) bio.BioName = bioName;
            _bios.Add(bio);
            return bio;
        }

        static int Idx(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.Ordinal);

        // The region of `s` starting at header `from` up to the next section
        // header (or end of string). Scopes an assertion to §5.
        static string SectionRegion(string s, string from)
        {
            int start = Idx(s, from);
            if (start < 0) return string.Empty;
            int end = s.Length;
            foreach (var h in new[] { H_WORLD, H_SELF, H_TALKEE, H_LONGTERM, H_SHORTTERM })
            {
                int hi = s.IndexOf(h, start + from.Length, StringComparison.Ordinal);
                if (hi >= 0 && hi < end) end = hi;
            }
            return s.Substring(start, end - start);
        }

        // ---------- Test 1: Affect.Current reverts toward Baseline, not 0 ----------

        [Test]
        public void Affect_Current_RevertsTowardBaselineNotZero()
        {
            var a = new Affect { Value = 0.9f, Baseline = -0.7f, LastSetMs = 0, HalfLifeMs = 1000f };

            // dt = 0 → exp(0) = 1 → Current == Value exactly.
            Assert.That(a.Current(0), Is.EqualTo(0.9f).Within(1e-4f),
                "dt=0 → e^0=1 → Current returns Value verbatim");

            // dt = 10 × half-life → exp(-10) ≈ 4.5e-5 → Current ≈ Baseline,
            // and CRUCIALLY negative (proves it reverts to the NEGATIVE
            // canon baseline, NOT to 0 — Plan §4.4).
            float far = a.Current(10000);
            Assert.That(far, Is.EqualTo(-0.7f).Within(0.01f),
                "dt≫half-life → Current relaxes to Baseline (-0.7), not 0");
            Assert.Less(far, 0f,
                "reverts to the NEGATIVE baseline — affection does NOT decay toward 0 (Plan §4.4)");
        }

        // ---------- Test 2: Current is monotone, never crosses below Baseline ----------

        [Test]
        public void Affect_Current_Monotone()
        {
            var a = new Affect { Value = 1f, Baseline = 0f, LastSetMs = 0, HalfLifeMs = 1000f };

            float t0    = a.Current(0);
            float t500  = a.Current(500);
            float t1000 = a.Current(1000);
            float t5000 = a.Current(5000);

            Assert.That(t0, Is.EqualTo(1f).Within(1e-4f), "t=0 → Value");
            // Strictly decreasing toward the Baseline (0).
            Assert.Less(t500, t0,    "decays: t=500 < t=0");
            Assert.Less(t1000, t500, "decays: t=1000 < t=500");
            Assert.Less(t5000, t1000, "decays: t=5000 < t=1000");

            // Each sample is >= Baseline and never crosses below it.
            foreach (var v in new[] { t0, t500, t1000, t5000 })
                Assert.GreaterOrEqual(v, a.Baseline,
                    "Value > Baseline → Current stays >= Baseline, never crosses below");

            // t=1000 is exactly one half-life: Baseline + (Value-Baseline)/e.
            Assert.That(t1000, Is.EqualTo(1f / (float)Math.E).Within(1e-3f),
                "one half-life → ~1/e of the way from Baseline to Value");
        }

        // ---------- Test 3: HalfLifeMs<=0 guard returns Value, no NaN ----------

        [Test]
        public void Affect_Current_HalfLifeGuard()
        {
            var a = new Affect { Value = 0.42f, Baseline = -0.3f, LastSetMs = 0, HalfLifeMs = 0f };

            // The `HalfLifeMs <= 0f` guard short-circuits to Value for ANY now
            // — no division, no Exp, no NaN/Infinity.
            foreach (long now in new long[] { 0, 1, 1000, long.MaxValue / 2 })
            {
                float v = a.Current(now);
                Assert.That(v, Is.EqualTo(0.42f).Within(1e-6f),
                    "HalfLifeMs<=0 → guard returns Value verbatim (no decay configured)");
                Assert.IsFalse(float.IsNaN(v), "guard must not produce NaN");
                Assert.IsFalse(float.IsInfinity(v), "guard must not produce Infinity");
            }

            // Negative half-life also hits the same guard.
            var b = new Affect { Value = -0.8f, Baseline = 0.5f, LastSetMs = 0, HalfLifeMs = -50f };
            Assert.That(b.Current(9999), Is.EqualTo(-0.8f).Within(1e-6f),
                "negative HalfLifeMs also hits the <=0 guard");
        }

        // ---------- Test 4: AffectBaseline maps EVERY RelationType ----------

        [Test]
        public void AffectBaseline_MapsAllRelationTypes()
        {
            // Documented canon (Plan §4.4 / AffectBaseline.cs).
            Assert.That(AffectBaseline.ForRelation(RelationType.Enemy),   Is.EqualTo(-0.7f).Within(1e-6f), "Enemy → -0.7");
            Assert.That(AffectBaseline.ForRelation(RelationType.Rival),   Is.EqualTo(-0.4f).Within(1e-6f), "Rival → -0.4");
            Assert.That(AffectBaseline.ForRelation(RelationType.Neutral), Is.EqualTo( 0.0f).Within(1e-6f), "Neutral → 0");
            Assert.That(AffectBaseline.ForRelation(RelationType.Ally),    Is.EqualTo( 0.4f).Within(1e-6f), "Ally → +0.4");
            Assert.That(AffectBaseline.ForRelation(RelationType.Friend),  Is.EqualTo( 0.6f).Within(1e-6f), "Friend → +0.6");

            // Exhaustive: EVERY enum member maps into [-1,1] (so a baseline
            // can never push Affect.Current outside the affection range).
            foreach (RelationType r in Enum.GetValues(typeof(RelationType)))
            {
                float b = AffectBaseline.ForRelation(r);
                Assert.GreaterOrEqual(b, -1f, "ForRelation(" + r + ") >= -1");
                Assert.LessOrEqual(b, 1f, "ForRelation(" + r + ") <= 1");
            }

            // The enum has exactly the 5 members the map is exhaustive over
            // (guards against an un-mapped member silently hitting `default`).
            Assert.AreEqual(5, Enum.GetValues(typeof(RelationType)).Length,
                "RelationType is exactly {Neutral,Ally,Friend,Rival,Enemy} — AffectBaseline is exhaustive");
        }

        // ---------- Test 5: §5.4 emotion omitted when Emotion unset ----------

        [Test]
        public void Assembler_Emotion_OmittedWhenUnset()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";   // §5 block exists (Situation non-empty)
            mind.Emotion = null;               // §5.4 unset → must be omitted

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);

            StringAssert.Contains(H_SHORTTERM, s, "§5 block present (Situation seeded)");
            StringAssert.Contains("\n处境：避追兵于客栈", s, "§5.1 处境 still rendered");
            Assert.IsFalse(s.Contains(L_EMOTION),
                "Emotion==null → §5.4 此刻心绪 line omitted entirely");
        }

        // ---------- Test 6: §5.4 omitted below EMOTION_FLOOR, rendered at/above ----------

        [Test]
        public void Assembler_Emotion_OmittedBelowFloor()
        {
            // Case A: Current(0) = 0.05 < EMOTION_FLOOR (0.12) → omitted.
            var bioA = NewBio("huangrong", "黄蓉");
            bioA.Personality = "灵动机敏";
            var talkerA = TestBuilders.MakeAgent("huangrong", bio: bioA);

            var mA = _mgr.GetOrCreateMind(talkerA.PlayerId);
            mA.Situation = "避追兵于客栈";
            mA.Emotion = new Affect
            {
                Label = "警惕", Value = 0.05f, Baseline = 0f,
                LastSetMs = 0, HalfLifeMs = AITavernConstants.EMOTION_HALFLIFE_MS,
            };

            string a = ContextAssembler.Build(talkerA, null, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains(H_SHORTTERM, a, "§5 block still present (Situation seeded)");
            Assert.IsFalse(a.Contains(L_EMOTION),
                "Current 0.05 < EMOTION_FLOOR 0.12 → §5.4 omitted ('mood has passed')");

            // Case B: Current(0) = 0.6 >= floor → rendered with the EXACT
            // prefix incl. InvariantCulture "0.6" and the 正缓缓平复 wrapper.
            var bioB = NewBio("guojing", "郭靖");
            bioB.Personality = "憨厚";
            var talkerB = TestBuilders.MakeAgent("guojing", bio: bioB);

            var mB = _mgr.GetOrCreateMind(talkerB.PlayerId);
            mB.Situation = "避追兵于客栈";
            mB.Emotion = new Affect
            {
                Label = "警惕", Value = 0.6f, Baseline = 0f,
                LastSetMs = 0, HalfLifeMs = AITavernConstants.EMOTION_HALFLIFE_MS,
            };

            string b = ContextAssembler.Build(talkerB, null, _mgr, 0, ContextProfile.Full);
            string expected = "\n此刻心绪：警惕（强度 "
                + (0.6f).ToString("0.0", CultureInfo.InvariantCulture)
                + "，正缓缓平复）";
            StringAssert.Contains(expected, b,
                "Current 0.6 >= floor → §5.4 rendered byte-for-byte (label, InvariantCulture 0.0, 正缓缓平复 tail)");
            // Sanity: the InvariantCulture format really is "0.6" (no locale comma).
            StringAssert.Contains("（强度 0.6，", b, "intensity formatted as InvariantCulture \"0.6\"");
        }

        // ---------- Test 7: §5.4 in BOTH Full and Leave; Leave stays lean ----------

        [Test]
        public void Assembler_Emotion_InBothFullAndLeave()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            mind.Task = "辨明谁可信";
            mind.Surroundings.Summary = "客栈之中";
            mind.Emotion = new Affect
            {
                Label = "愤怒", Value = 0.7f, Baseline = 0f,
                LastSetMs = 0, HalfLifeMs = AITavernConstants.EMOTION_HALFLIFE_MS,
            };

            // (a) Full → §5 block carries 此刻心绪 (+ the §5.1-5.3 lines).
            string full = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains(H_SHORTTERM, full, "Full: §5 header present");
            StringAssert.Contains(L_EMOTION, full, "Full: §5.4 此刻心绪 present");
            StringAssert.Contains(L_SITUATION, full, "Full: §5.1 处境 present");

            // (b) Leave → STILL a §5 header + 此刻心绪, but the §5.1/5.2/5.3
            // lines are Full-only (Plan §6.1 lean Leave = §2+§5.4+§5.5.3).
            string leave = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Leave);
            StringAssert.Contains(H_SHORTTERM, leave, "Leave: standalone §5.4-only short-term header present");
            StringAssert.Contains(L_EMOTION, leave, "Leave: §5.4 此刻心绪 present (Plan §6.1 includes §5.4)");
            Assert.IsFalse(leave.Contains(L_SITUATION), "Leave: §5.1 处境 omitted (Full-only)");
            Assert.IsFalse(leave.Contains(L_TASK), "Leave: §5.2 目标 omitted (Full-only)");
            Assert.IsFalse(leave.Contains(L_SURROUNDINGS), "Leave: §5.3 环境 omitted (Full-only)");

            // (c) Leave with NO emotion → no §5 header at all (3C norm:
            // Leave stays §2-only until 3D appraisal first sets Emotion).
            mind.Emotion = null;
            string leaveNoEmo = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Leave);
            Assert.IsFalse(leaveNoEmo.Contains(H_SHORTTERM),
                "Leave + no emotion → §5 absent entirely (no bare header; Leave == §2-only)");
            StringAssert.Contains(H_SELF, leaveNoEmo, "Leave still emits §2 (stay in voice)");
        }

        // ---------- Test 8: §5.5.1 affection — Full-only render + omit ----------

        [Test]
        public void Assembler_Affection_FullOnly_RendersAndOmits()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";   // ensure §5 region exists regardless
            mind.Targets[talkee.PlayerId] = new TargetState
            {
                Affection = new Affect
                {
                    Label = "厌恶偏强", Value = -0.55f, Baseline = -0.7f,
                    LastSetMs = 0, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
                },
            };

            // Full now=0 → dt=0 → Current == Value (-0.55) → byte-for-byte.
            string full = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("·对 欧阳克·", full, "§5.5 per-target header uses the talkee Bio name");
            string expected = "\n当下好恶："
                + (-0.55f).ToString("0.00", CultureInfo.InvariantCulture)
                + "（厌恶偏强，向长期基线缓回）";
            StringAssert.Contains(expected, full,
                "§5.5.1 当下好恶 rendered byte-for-byte (signed InvariantCulture 0.00, label, 向长期基线缓回 tail)");
            StringAssert.Contains("当下好恶：-0.55（", full, "signed value formatted as InvariantCulture \"-0.55\"");

            // Leave → §5.5.1 affection is Full-only (Plan §6.1).
            string leave = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Leave);
            Assert.IsFalse(leave.Contains(L_AFFECTION),
                "Leave omits §5.5.1 affection (Full-only — not in the lean §2+§5.4+§5.5.3 list)");
            Assert.IsFalse(leave.Contains("·对 欧阳克·"),
                "Leave omits the §5.5 ·对 X· header too (no bare header)");

            // No target entry → Full omits the whole §5.5 block (no bare header).
            var talkee2Bio = NewBio("guojing", "郭靖");
            var talkee2 = TestBuilders.MakeAgent("guojing", bio: talkee2Bio);
            string full2 = ContextAssembler.Build(talker, talkee2, _mgr, 0, ContextProfile.Full);
            Assert.IsFalse(full2.Contains("·对 郭靖·"),
                "no Targets entry for this talkee → §5.5 ·对 X· header omitted");
            Assert.IsFalse(full2.Contains(L_AFFECTION),
                "no Targets entry → no 当下好恶 line for that talkee");
        }

        // ---------- Test 9: AffectionBlock is READ-ONLY (no mind/target seeding) ----------

        [Test]
        public void Assembler_Affection_ReadOnly()
        {
            // 9a: talker mind EXISTS but has NO Targets entry for the talkee.
            // Build must NOT GetOrCreate / seed a target entry.
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            Assert.IsFalse(mind.Targets.ContainsKey(talkee.PlayerId),
                "precondition: no target entry for the talkee");

            ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            Assert.IsFalse(_mgr.Minds[talker.PlayerId].Targets.ContainsKey(talkee.PlayerId),
                "Build is READ-ONLY: it must NOT create / seed a Targets entry");

            // 9b: talker has NO mind at all → Build must not create one
            // (mirror the T3B.4 read-only test).
            var bio2 = NewBio("guojing", "郭靖");
            bio2.Personality = "憨厚";
            var talker2 = TestBuilders.MakeAgent("guojing", bio: bio2);
            Assert.IsFalse(_mgr.Minds.ContainsKey(talker2.PlayerId),
                "precondition: no mind for talker2");

            ContextAssembler.Build(talker2, talkee, _mgr, 0, ContextProfile.Full);

            Assert.IsFalse(_mgr.Minds.ContainsKey(talker2.PlayerId),
                "Build is READ-ONLY: it must NOT create a mind for an absent talker");
        }

        // ---------- Test 10: section order — §5.4 after §5.1-5.3, §5.5.1 last ----------

        [Test]
        public void Assembler_Order_EmotionAfterSurroundings_AffectionAfterShortTerm()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            var dossier = ScriptableObject.CreateInstance<CharacterDossier>();
            _sos.Add(dossier);
            dossier.AgentId = "huangrong";
            dossier.PolityKnowledge.Add(new KnowledgeLine { Subject = "大宋", Text = "临安繁华" });
            _mgr.Dossiers["huangrong"] = dossier;   // §4 present

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            mind.Task = "辨明谁可信";
            mind.Surroundings.Summary = "客栈之中";
            mind.Emotion = new Affect
            {
                Label = "警惕", Value = 0.6f, Baseline = 0f,
                LastSetMs = 0, HalfLifeMs = AITavernConstants.EMOTION_HALFLIFE_MS,
            };
            mind.Targets[talkee.PlayerId] = new TargetState
            {
                Affection = new Affect
                {
                    Label = "提防", Value = -0.3f, Baseline = -0.4f,
                    LastSetMs = 0, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
                },
            };

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            int env = Idx(s, "\n环境：");
            int emo = Idx(s, L_EMOTION);
            int aff = Idx(s, L_AFFECTION);
            int lt  = Idx(s, H_LONGTERM);
            int st  = Idx(s, H_SHORTTERM);

            Assert.Greater(env, -1, "§5.3 环境 present");
            Assert.Greater(emo, -1, "§5.4 此刻心绪 present");
            Assert.Greater(aff, -1, "§5.5.1 当下好恶 present");
            Assert.Greater(lt, -1, "§4 long-term present");
            Assert.Greater(st, -1, "§5 short-term present");

            Assert.Less(env, emo, "§5.4 emotion sorts AFTER §5.3 surroundings");
            Assert.Less(emo, aff, "§5.5.1 affection sorts AFTER §5.4 emotion (§5.5 per-target area is last)");
            Assert.Less(lt, st, "§4 long-term precedes §5 short-term (Plan §1 fixed order)");
        }

        // ---------- Test 11: 3D final state — §5.4 emotion + §5.5.1 affection
        //                    render ALONGSIDE the §5.5.3 ring ----------

        // REWRITTEN from the old 3C-coexistence test (T3D.6, Plan §5.4/§8).
        // T3D.5 removed the Phase 2 prior-memory path (superseded by §5.5.2/
        // §5.5.3). The assembler block is now the SOLE context. This file's
        // domain focus: §5.4 此刻心绪 + §5.5.1 当下好恶 STILL render alongside
        // the live-turn §5.5.3 ring, the live turn seeded through the REAL
        // path EpisodicRing.Record (NOT conv.Transcript).
        [Test]
        public void Coexistence_Affect_RendersAlongsideRing()
        {
            var selfBio = NewBio("huangrong", "黄蓉");
            selfBio.Personality = "灵动机敏";
            selfBio.Identity = "桃花岛黄药师之女";
            var otherBio = NewBio("ouyangke", "欧阳克");
            otherBio.Identity = "白驼山少主";

            var talker = TestBuilders.MakeAgent("huangrong", bio: selfBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: otherBio);
            _mgr.NPCs.Register(talker);
            _mgr.NPCs.Register(talkee);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            mind.Emotion = new Affect
            {
                Label = "喜悦", Value = 0.8f, Baseline = 0f,
                LastSetMs = 0, HalfLifeMs = AITavernConstants.EMOTION_HALFLIFE_MS,
            };
            mind.Targets[talkee.PlayerId] = new TargetState
            {
                Affection = new Affect
                {
                    Label = "亲近", Value = 0.5f, Baseline = 0.4f,
                    LastSetMs = 0, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
                },
            };

            var conv = new Conversation();
            conv.Participants[talker.PlayerId] = new ConversationMember { Status = MemberStatusKind.Participating };
            conv.Participants[talkee.PlayerId] = new ConversationMember { Status = MemberStatusKind.Participating };
            const string liveTurn = "你为何在此LIVE_TURN_SENTINEL";
            EpisodicRing.Record(_mgr, conv, talkee.PlayerId, liveTurn, 1_000_500);

            // now ≈ LastSetMs so §5.4 emotion has NOT decayed below
            // EMOTION_FLOOR (dt≪half-life) — the emotion line must still render.
            var cont = ConversationPrompts.BuildContinue(
                selfBio, otherBio, conv, talker, talkee, _mgr, now: 1_000L);
            string sp = cont.SystemPrompt;

            // §5.4 emotion + §5.5.1 affection STILL render (domain focus) ...
            StringAssert.Contains(L_EMOTION, sp, "§5.4 此刻心绪 still renders");
            StringAssert.Contains(L_AFFECTION, sp, "§5.5.1 当下好恶 still renders");
            // ... ALONGSIDE the §5.5.3 ring carrying the live turn (no
            // under-render), exactly once (no double-render — T3D.5).
            StringAssert.Contains(liveTurn, sp, "live turn via the §5.5.3 ring (no under-render)");
            int occ = 0, idx = 0;
            while ((idx = sp.IndexOf(liveTurn, idx, StringComparison.Ordinal)) >= 0) { occ++; idx += liveTurn.Length; }
            Assert.AreEqual(1, occ, "live turn appears EXACTLY ONCE (ring is sole transcript source)");
            StringAssert.Contains("［刚刚结束的对话］", sp, "ring tags the newest turn");

            // §5.5.1 affection and §5.5.3 ring fold under ONE ·对 欧阳克· header.
            int affIdx = Idx(sp, L_AFFECTION);
            int ringIdx = Idx(sp, "最近交谈（最近");
            Assert.Greater(affIdx, -1);
            Assert.Greater(ringIdx, -1);
            Assert.Less(affIdx, ringIdx, "§5.5.1 affection precedes the §5.5.3 ring (one ·对 X· block)");

            Assert.IsFalse(sp.Contains("PRIOR_MEM_SENTINEL"), "no Phase 2 priorMemory path");
            Assert.IsFalse(sp.Contains("Conversation so far:"), "no Phase 2 live-transcript header");
            int asmIdx = Idx(sp, H_SELF);
            Assert.Greater(asmIdx, -1, "assembler block present (PrependAssembler intact)");
            StringAssert.Contains("不要复述上面已有的对话内容", sp,
                "the Phase 2 Chinese anti-repeat tail is KEPT");
        }

        // ---------- Test 12: whole §5 omitted when §5.1-5.4 ALL empty ----------

        [Test]
        public void Assembler_WholeShortTerm_OmittedWhenAllEmptyInclEmotion()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            // Mind EXISTS but Situation/Task/Surroundings empty AND Emotion null
            // → the whole §5 block (header included) is omitted (extended 3C
            // null-return: §5.1+§5.2+§5.3+§5.4 all empty).
            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            Assert.IsTrue(_mgr.Minds.ContainsKey(talker.PlayerId), "mind seeded but empty");
            Assert.IsNull(mind.Situation, "Situation empty");
            Assert.IsNull(mind.Task, "Task empty");
            Assert.IsNull(mind.Emotion, "Emotion unset");

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);

            Assert.IsFalse(s.Contains(H_SHORTTERM),
                "§5.1-5.4 all empty → §5 omitted entirely, NO bare header (extended 3C null-return)");
            Assert.IsFalse(s.Contains(L_EMOTION), "no 此刻心绪 line");
            StringAssert.Contains(H_SELF, s, "§2 still present (sanity — prompt is otherwise built)");
        }
    }
}
