// T3A.4 (Phase 3A) — ContextAssembler static-spine editor tests.
//
// Plan refs: §1 (prompt mock — the four section headers + fixed order),
// §6/§6.1/§6.2 (assembler ownership, lean Leave profile, anti-omniscience
// guard), §7 (per-section char budgets), §8 (3A coexistence: the assembler
// block is PREPENDED alongside the retained Phase 2 BuildPriorMemoryBlock /
// AppendTranscript — nothing Phase 2 is removed until 3D), §11 (Personality
// → Identity fallback, the only Phase 1/2 compat concern).
//
// Black-box: every assertion is on the public ContextAssembler.Build output
// (or the public ConversationPrompts.BuildStart/Continue for §8 coexistence).
// No runtime code is modified. ContextAssembler is sync — plain [Test], no
// .GetAwaiter().GetResult().
//
// The four section headers under test:
//   §1 【世界背景】              §3 【对面是谁 — 初见印象】
//   §2 【我是谁】                §4 【我所知 — 长期记忆】
using NUnit.Framework;
using System;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class ContextAssemblerTests
    {
        AITavernManager _mgr;
        readonly System.Collections.Generic.List<CharacterBio> _bios =
            new System.Collections.Generic.List<CharacterBio>();
        readonly System.Collections.Generic.List<ScriptableObject> _sos =
            new System.Collections.Generic.List<ScriptableObject>();

        const string H_WORLD    = "【世界背景】";
        const string H_SELF     = "【我是谁】";
        const string H_TALKEE   = "【对面是谁";          // prefix — header has a " — 初见印象】" tail
        const string H_LONGTERM = "【我所知";            // prefix — header has " — 长期记忆】" tail

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

        T NewSO<T>() where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance<T>();
            _sos.Add(so);
            return so;
        }

        // A minimally-populated WorldCodex so §1 emits.
        WorldCodex MakeWorld()
        {
            var w = NewSO<WorldCodex>();
            w.Era = "南宋宁宗庆元年间，蒙古崛起于漠北";
            w.Polities.Add(new PolityEntry { Name = "大宋", Brief = "中原正统" });
            w.Factions.Add(new FactionEntry { Name = "桃花岛", HomeRegion = "东海", Brief = "黄药师所居" });
            w.NotableFigures.Add(new NotableFigure { Name = "黄药师", Faction = "桃花岛", OneLine = "东邪" });
            return w;
        }

        static int Idx(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.Ordinal);

        // The region of `s` starting at header `from` up to the next section
        // header (or end of string). Used to scope an assertion to one §.
        static string SectionRegion(string s, string from)
        {
            int start = Idx(s, from);
            if (start < 0) return string.Empty;
            int end = s.Length;
            foreach (var h in new[] { H_WORLD, H_SELF, H_TALKEE, H_LONGTERM })
            {
                int hi = s.IndexOf(h, start + from.Length, StringComparison.Ordinal);
                if (hi >= 0 && hi < end) end = hi;
            }
            return s.Substring(start, end - start);
        }

        // ---------- Test 2: no lore assets → §1 and §4 (and empty §3) omitted ----------

        [Test]
        public void Assembler_NoLoreAssets_OmitsWorldAndLongTerm()
        {
            // World==null, Dossiers empty (the fresh-game / pre-pipeline state).
            Assert.IsNull(_mgr.World);
            Assert.AreEqual(0, _mgr.Dossiers.Count);

            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏、心思缜密";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            // Both surface fields empty → §3 must be omitted.
            talkeeBio.Appearance = "";
            talkeeBio.SurfaceManner = "";

            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            StringAssert.Contains(H_SELF, s);                       // §2 always present
            Assert.IsFalse(s.Contains(H_WORLD), "§1 omitted when World==null");
            Assert.IsFalse(s.Contains(H_LONGTERM), "§4 omitted when no Dossier");
            Assert.IsFalse(s.Contains(H_TALKEE), "§3 omitted when both surface fields empty");
            StringAssert.Contains("灵动机敏", s, "talker Personality drives 性情");
        }

        // ---------- Test 3: all four sections emit in the FIXED order ----------

        [Test]
        public void Assembler_SectionOrderFixed()
        {
            _mgr.World = MakeWorld();

            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            talkeeBio.Appearance = "锦衣华服";
            talkeeBio.SurfaceManner = "举止温文";

            var dossier = NewSO<CharacterDossier>();
            dossier.AgentId = "huangrong";
            dossier.PolityKnowledge.Add(new KnowledgeLine { Subject = "大宋", Text = "临安繁华" });
            dossier.People.Add(new PersonView
            {
                Target = "ouyangke",
                Relationship = "觊觎者",
                Impression = "白驼山少主，心术不正",
            });
            _mgr.Dossiers["huangrong"] = dossier;

            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            int w = Idx(s, H_WORLD);
            int self = Idx(s, H_SELF);
            int tk = Idx(s, H_TALKEE);
            int lt = Idx(s, H_LONGTERM);

            Assert.Greater(w, -1, "§1 present");
            Assert.Greater(self, -1, "§2 present");
            Assert.Greater(tk, -1, "§3 present");
            Assert.Greater(lt, -1, "§4 present");

            Assert.Less(w, self, "§1 before §2");
            Assert.Less(self, tk, "§2 before §3");
            Assert.Less(tk, lt, "§3 before §4 (Plan §1 fixed order)");
        }

        // ---------- Test 4: anti-omniscience — talkee private fields NEVER leak ----------

        [Test]
        public void Assembler_AntiOmniscience_TalkeeSurfaceNeverLeaksPrivate()
        {
            _mgr.World = MakeWorld();

            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";

            // Talkee bio carries DISTINCTIVE sentinels in every private slot.
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            talkeeBio.Personality = "PERSONA_SECRET";
            talkeeBio.Identity = "IDENTITY_SECRET";
            talkeeBio.Plans = "PLANS_SECRET";
            talkeeBio.Relationships.Add(new RelationshipEntry
            {
                // RelationshipEntry has no free-text field; the sentinel rides
                // TargetAgentId (the only string on it). The structural guard
                // means BuildTalkeeSurfaceSealed can't see Relationships at all.
                TargetAgentId = "RELATION_SECRET",
                Relation = RelationType.Enemy,
            });
            // The two fields a stranger CAN perceive.
            talkeeBio.Appearance = "锦衣华服";
            talkeeBio.SurfaceManner = "举止温文";

            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            // §3 region DOES contain the two surface fields.
            string sect3 = SectionRegion(s, H_TALKEE);
            Assert.IsNotEmpty(sect3, "§3 must be present (surface fields set)");
            StringAssert.Contains("锦衣华服", sect3, "§3 shows Appearance");
            StringAssert.Contains("举止温文", sect3, "§3 shows SurfaceManner");

            // The WHOLE assembled prompt contains NONE of the private sentinels
            // (Plan §6.2 structural guard — the load-bearing assertion).
            Assert.IsFalse(s.Contains("PERSONA_SECRET"),
                "talkee Personality must NEVER appear anywhere in the assembled context");
            Assert.IsFalse(s.Contains("IDENTITY_SECRET"),
                "talkee Identity must NEVER appear anywhere in the assembled context");
            Assert.IsFalse(s.Contains("PLANS_SECRET"),
                "talkee Plans must NEVER appear anywhere in the assembled context");
            Assert.IsFalse(s.Contains("RELATION_SECRET"),
                "talkee Relationships must NEVER appear anywhere in the assembled context");
        }

        // ---------- Test 5: §2 性情 — Personality with fallback to Identity ----------

        [Test]
        public void Assembler_Personality_FallsBackToIdentity()
        {
            // Case A: Personality empty → 性情 falls back to Identity (Plan §11).
            var bioA = NewBio("huangrong", "黄蓉");
            bioA.Personality = "";
            bioA.Identity = "桃花岛黄药师之女";
            var talkerA = TestBuilders.MakeAgent("huangrong", bio: bioA);

            string a = ContextAssembler.Build(talkerA, null, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("性情：", a, "性情 line present even with empty Personality");
            StringAssert.Contains("桃花岛黄药师之女", a, "性情 falls back to Identity text");

            // Case B: Personality set → 性情 = Personality, 出身 = Identity's
            // first sentence (distinct slices, no duplicate of Identity).
            var bioB = NewBio("huangrong2", "黄蓉");
            bioB.Personality = "灵动机敏、心思缜密";
            bioB.Identity = "桃花岛黄药师之女。自幼随父习艺。";
            var talkerB = TestBuilders.MakeAgent("huangrong2", bio: bioB);

            string b = ContextAssembler.Build(talkerB, null, _mgr, 0, ContextProfile.Full);
            int trait = Idx(b, "性情：");
            int origin = Idx(b, "出身：");
            Assert.Greater(trait, -1, "性情 line present");
            Assert.Greater(origin, -1, "出身 line present when Personality distinct from Identity");

            string traitLine = b.Substring(trait, origin - trait);
            StringAssert.Contains("灵动机敏", traitLine, "性情 uses Personality");
            Assert.IsFalse(traitLine.Contains("桃花岛黄药师之女"),
                "性情 line must NOT also carry the Identity (no duplicate)");

            string originLine = b.Substring(origin);
            StringAssert.Contains("桃花岛黄药师之女", originLine, "出身 uses Identity first sentence");
            Assert.IsFalse(originLine.Contains("灵动机敏"),
                "出身 line must NOT carry Personality (distinct slice)");
        }

        // ---------- Test 6: Leave profile is lean (§2 only in 3A) ----------

        [Test]
        public void Assembler_LeaveProfile_IsLean()
        {
            _mgr.World = MakeWorld();

            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            talkeeBio.Appearance = "锦衣华服";
            talkeeBio.SurfaceManner = "举止温文";

            var dossier = NewSO<CharacterDossier>();
            dossier.AgentId = "huangrong";
            dossier.PolityKnowledge.Add(new KnowledgeLine { Subject = "大宋", Text = "临安繁华" });
            dossier.People.Add(new PersonView { Target = "ouyangke", Impression = "心术不正" });
            _mgr.Dossiers["huangrong"] = dossier;

            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Leave);

            StringAssert.Contains(H_SELF, s, "Leave still emits §2 (stay in voice)");
            Assert.IsFalse(s.Contains(H_WORLD), "Leave omits §1 World Codex (Plan §6.1 lean)");
            Assert.IsFalse(s.Contains(H_LONGTERM), "Leave omits §4 long-term knowledge");
            Assert.IsFalse(s.Contains(H_TALKEE), "Leave omits §3 (3A Leave == §2 only)");
        }

        // ---------- Test 7: §4 section truncated to its char budget ----------

        [Test]
        public void Assembler_SectionBudget_Truncates()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";

            var dossier = NewSO<CharacterDossier>();
            dossier.AgentId = "huangrong";
            // > SECT_LONGTERM_BUDGET (3000) of content for §4.
            dossier.PolityKnowledge.Add(new KnowledgeLine
            {
                Subject = "大宋",
                Text = new string('字', 5000),
            });
            _mgr.Dossiers["huangrong"] = dossier;

            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);

            string sect4 = SectionRegion(s, H_LONGTERM);
            Assert.IsNotEmpty(sect4, "§4 must be present");
            Assert.LessOrEqual(sect4.Length, AITavernConstants.SECT_LONGTERM_BUDGET,
                "§4 must be hard-capped to SECT_LONGTERM_BUDGET chars");
            StringAssert.EndsWith("…", sect4,
                "an over-budget §4 ends with the ellipsis truncation marker");
        }

        // ---------- Test 8: null talker → empty (PrependAssembler early-returns) ----------

        [Test]
        public void Assembler_NullTalker_ReturnsEmpty()
        {
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            talkeeBio.Appearance = "锦衣华服";
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            string s = ContextAssembler.Build(null, talkee, _mgr, 0, ContextProfile.Full);

            Assert.IsTrue(string.IsNullOrWhiteSpace(s),
                "null talker → empty/whitespace so PrependAssembler skips and the prompt is Phase-2-only");
        }

        // ---------- Test 9: 3A coexistence at the ConversationPrompts seam ----------

        [Test]
        public void Coexistence_AssemblerPrependedPhase2Intact()
        {
            _mgr.World = MakeWorld();

            var selfBio = NewBio("huangrong", "黄蓉");
            selfBio.Personality = "灵动机敏";
            selfBio.Identity = "桃花岛黄药师之女";
            var otherBio = NewBio("ouyangke", "欧阳克");
            otherBio.Identity = "白驼山少主";
            otherBio.Appearance = "锦衣华服";
            otherBio.SurfaceManner = "举止温文";

            var dossier = NewSO<CharacterDossier>();
            dossier.AgentId = "huangrong";
            dossier.People.Add(new PersonView { Target = "ouyangke", Impression = "心术不正" });
            _mgr.Dossiers["huangrong"] = dossier;

            var talker = TestBuilders.MakeAgent("huangrong", bio: selfBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: otherBio);

            const string priorMem = "PRIOR_MEM_SENTINEL";

            // --- BuildStart ---
            var start = ConversationPrompts.BuildStart(
                selfBio, otherBio, talker, talkee, _mgr, now: 1_000_000, priorMemory: priorMem);
            string sp = start.SystemPrompt;

            StringAssert.Contains(H_SELF, sp, "assembler §2 header present in Start prompt");
            StringAssert.Contains(priorMem, sp,
                "Phase 2 prior-memory path STILL intact (3A coexistence, Plan §8)");
            int asmIdxS = Idx(sp, H_SELF);
            int memIdxS = Idx(sp, priorMem);
            Assert.Greater(asmIdxS, -1);
            Assert.Greater(memIdxS, -1);
            Assert.Less(asmIdxS, memIdxS,
                "assembler static block is PREPENDED before the Phase 2 content");

            // --- BuildContinue (with a live transcript) ---
            var conv = new Conversation();
            const string liveTurn = "蓉儿妹妹，别来无恙LIVE_TURN_SENTINEL";
            conv.Transcript.Add(new Message
            {
                Author = new GameId("ouyangke"),
                Text = liveTurn,
                Timestamp = 1_000_500,
            });

            var cont = ConversationPrompts.BuildContinue(
                selfBio, otherBio, conv, talker, talkee, _mgr, now: 1_001_000, priorMemory: priorMem);
            string cp = cont.SystemPrompt;

            StringAssert.Contains(H_SELF, cp, "assembler §2 header present in Continue prompt");
            StringAssert.Contains(priorMem, cp,
                "Phase 2 prior-memory path STILL intact in Continue (Plan §8)");
            StringAssert.Contains(liveTurn, cp,
                "Phase 2 AppendTranscript STILL renders the live transcript in 3A (Plan §8 — "
                + "the §6.1 AppendTranscript deletion is a 3D change, NOT 3A)");
            int asmIdxC = Idx(cp, H_SELF);
            int memIdxC = Idx(cp, priorMem);
            Assert.Less(asmIdxC, memIdxC,
                "assembler block precedes the Phase 2 identity / prior-memory content in Continue");
        }

        // ---------- Test 10: PersonView match by AgentId, fallback by BioName ----------

        [Test]
        public void Dossier_PersonViewMatch_ByAgentIdThenName()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            // Case A: PersonView.Target == talkee AgentId ("ouyangke").
            var dossierA = NewSO<CharacterDossier>();
            dossierA.AgentId = "huangrong";
            dossierA.People.Add(new PersonView
            {
                Target = "ouyangke",
                Impression = "白驼山少主，心术不正IMPRESSION_A",
            });
            _mgr.Dossiers["huangrong"] = dossierA;

            var talkeeBioA = NewBio("ouyangke", "欧阳克");
            var talkeeA = TestBuilders.MakeAgent("ouyangke", bio: talkeeBioA);

            string a = ContextAssembler.Build(talker, talkeeA, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("·关于此人（", a, "§4 per-person block emitted on AgentId match");
            StringAssert.Contains("IMPRESSION_A", a, "matched PersonView Impression rendered");

            // Case B: Target == BioName ("欧阳克"); talkee AgentId differs
            // ("oyk_alt") but its BioName is "欧阳克" → name fallback matches.
            var dossierB = NewSO<CharacterDossier>();
            dossierB.AgentId = "huangrong";
            dossierB.People.Add(new PersonView
            {
                Target = "欧阳克",
                Impression = "心术不正IMPRESSION_B",
            });
            _mgr.Dossiers["huangrong"] = dossierB;

            var talkeeBioB = NewBio("oyk_alt", "欧阳克");
            var talkeeB = TestBuilders.MakeAgent("oyk_alt", bio: talkeeBioB);

            string b = ContextAssembler.Build(talker, talkeeB, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("·关于此人（", b, "§4 per-person block emitted on BioName fallback match");
            StringAssert.Contains("IMPRESSION_B", b, "name-fallback-matched PersonView Impression rendered");
        }
    }
}
