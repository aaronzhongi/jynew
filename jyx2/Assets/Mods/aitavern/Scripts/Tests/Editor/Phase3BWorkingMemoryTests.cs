// T3B.4 (Phase 3B) — working-memory editor tests: RuntimeMindState +
// ContextAssembler §5 short-term + boot-seeding SEMANTICS.
//
// Plan refs: §2.4 (RuntimeMindState in AITavernManager.Minds, never
// serialized), §4.1/§4.2 (Situation/Task/Surroundings — designer/test set,
// template-rendered, ZERO Grok), §6/§6.1 (assembler ownership, empty-omission,
// the §5.1-5.3 short-term block is Full-profile ONLY — the lean Leave profile
// carries §2 only in 3B), §9 (test conventions: NUnit, black-box on the
// public ContextAssembler.Build / ConversationPrompts.BuildStart /
// AITavernManager API; ContextAssembler is sync — plain [Test]).
//
// AITavernBoot is a MonoBehaviour with Unity-runtime deps (LevelMaster) so
// BootAsync is NOT invoked here. The boot seeding semantics are tested at the
// unit level: directly populating mgr.Minds via GetOrCreateMind + setting
// fields is EXACTLY what SpawnNpc / SeedSurroundings do, and the observable
// "Plans fallback" contract is asserted through the assembler (a seeded mind
// renders §5; an empty / absent mind omits it), not Boot internals.
//
// Black-box: every assertion is on the public ContextAssembler.Build output
// (or ConversationPrompts.BuildStart for the coexistence test). BuildShortTerm
// is private — tested through Build, exactly as the T3A.4 tests do. No runtime
// code is modified.
using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class Phase3BWorkingMemoryTests
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

        WorldCodex MakeWorld()
        {
            var w = NewSO<WorldCodex>();
            w.Era = "南宋宁宗庆元年间";
            w.Polities.Add(new PolityEntry { Name = "大宋", Brief = "中原正统" });
            w.Factions.Add(new FactionEntry { Name = "桃花岛", HomeRegion = "东海", Brief = "黄药师所居" });
            w.NotableFigures.Add(new NotableFigure { Name = "黄药师", Faction = "桃花岛", OneLine = "东邪" });
            return w;
        }

        static int Idx(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.Ordinal);

        // The region of `s` starting at header `from` up to the next section
        // header (or end of string). Scopes a budget/length assertion to §5.
        // Mirrors T3A.4's SectionRegion but includes the §5 header.
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

        // ---------- Test 1: schema / default-init of the 3B working-memory POCOs ----------

        [Test]
        public void Schema_RuntimeMindState_DefaultInit()
        {
            var mind = new RuntimeMindState();
            Assert.IsNotNull(mind.Surroundings,
                "RuntimeMindState.Surroundings default-inits (assembler dereferences mind.Surroundings)");
            Assert.IsNotNull(mind.Surroundings.KnownPresent,
                "SurroundingsModel.KnownPresent default-inits to a non-null list (assembler iterates it)");
            Assert.AreEqual(0, mind.Surroundings.KnownPresent.Count, "fresh KnownPresent is empty");
            Assert.IsNull(mind.Situation, "fresh Situation is null (no scene seeded yet)");
            Assert.IsNull(mind.Task, "fresh Task is null (no scene seeded yet)");

            // CharacterBio's new 3B seed fields default empty; existing fields intact.
            var bio = NewBio("x", "某人");
            Assert.IsTrue(string.IsNullOrEmpty(bio.DefaultSituation), "DefaultSituation default empty");
            Assert.IsTrue(string.IsNullOrEmpty(bio.DefaultTask), "DefaultTask default empty");
            Assert.AreEqual("x", bio.AgentId, "existing AgentId unaffected by additive 3B fields");
            Assert.AreEqual("某人", bio.BioName, "existing BioName unaffected");
            Assert.IsTrue(string.IsNullOrEmpty(bio.Plans), "existing Plans default empty");
            Assert.IsNotNull(bio.Relationships, "existing Relationships list non-null");

            // A bare SurroundingsModel also default-inits its list.
            var sur = new SurroundingsModel();
            Assert.IsNotNull(sur.KnownPresent, "new SurroundingsModel().KnownPresent non-null");
            Assert.AreEqual(0, sur.KnownPresent.Count, "new SurroundingsModel().KnownPresent empty");
        }

        // ---------- Test 2: Minds dict + GetOrCreateMind idempotency/guard ----------

        [Test]
        public void Manager_GetOrCreateMind_IdempotentAndGuarded()
        {
            // MakeManager() already called EnsureInitialized → Minds exists.
            Assert.IsNotNull(_mgr.Minds, "Minds dict non-null after EnsureInitialized");

            var id = new GameId("huangrong");
            var m1 = _mgr.GetOrCreateMind(id);
            var m2 = _mgr.GetOrCreateMind(id);
            Assert.IsNotNull(m1, "GetOrCreateMind returns a mind");
            Assert.AreSame(m1, m2, "GetOrCreateMind is idempotent — same id → SAME instance");
            Assert.AreEqual(id, m1.Owner, "Owner set to the requested GameId");
            Assert.IsTrue(_mgr.Minds.ContainsKey(id), "mind registered in the Minds dict");

            var other = _mgr.GetOrCreateMind(new GameId("ouyangke"));
            Assert.AreNotSame(m1, other, "distinct ids → distinct minds");
            Assert.AreEqual(2, _mgr.Minds.Count, "two distinct minds registered");
        }

        // ---------- Test 3: §5 renders all three sub-lines (Full) ----------

        [Test]
        public void Assembler_ShortTerm_RendersAllThree()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            // Seed exactly as SpawnNpc/SeedSurroundings would: GetOrCreateMind
            // on talker.PlayerId (the assembler's §5 lookup key) + set fields.
            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            mind.Task = "辨明谁可信";
            mind.Surroundings.Summary = "客栈之中；在场可交谈者：欧阳克";

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            StringAssert.Contains(H_SHORTTERM, s, "§5 header present when mind seeded");
            StringAssert.Contains("\n处境：避追兵于客栈", s, "§5.1 处境 line rendered verbatim");
            StringAssert.Contains("\n目标：辨明谁可信", s, "§5.2 目标 line rendered verbatim");
            StringAssert.Contains("\n环境：客栈之中；在场可交谈者：欧阳克", s,
                "§5.3 环境 line uses Surroundings.Summary verbatim");
        }

        // ---------- Test 4: empty sub-lines omitted, block still present ----------

        [Test]
        public void Assembler_ShortTerm_OmitsEmptySubLines()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Task = "辨明谁可信";
            // Situation left null, Surroundings untouched (Summary/PlaceText empty).

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);

            StringAssert.Contains(H_SHORTTERM, s, "§5 block present (Task non-empty)");
            StringAssert.Contains(L_TASK, s, "目标 sub-line emitted");
            Assert.IsFalse(s.Contains(L_SITUATION), "处境 sub-line omitted when Situation empty");
            Assert.IsFalse(s.Contains(L_SURROUNDINGS), "环境 sub-line omitted when Surroundings empty");
        }

        // ---------- Test 5: whole block omitted when all empty; read-only ----------

        [Test]
        public void Assembler_ShortTerm_WholeBlockOmittedWhenAllEmpty()
        {
            // 5a: mind EXISTS but Situation/Task/Surroundings all empty →
            // the whole §5 block (header included) is omitted (no bare header).
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            _mgr.GetOrCreateMind(talker.PlayerId); // empty mind
            Assert.IsTrue(_mgr.Minds.ContainsKey(talker.PlayerId), "mind seeded but empty");

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);
            Assert.IsFalse(s.Contains(H_SHORTTERM),
                "empty mind → §5 omitted entirely, NO bare header");
            StringAssert.Contains(H_SELF, s, "§2 still present (sanity — prompt is otherwise built)");

            // 5b: NO mind for the talker → §5 absent AND Build is read-only
            // (the assembler must NOT GetOrCreateMind / mutate process state).
            var bio2 = NewBio("guojing", "郭靖");
            bio2.Personality = "憨厚";
            var talker2 = TestBuilders.MakeAgent("guojing", bio: bio2);
            Assert.IsFalse(_mgr.Minds.ContainsKey(talker2.PlayerId),
                "precondition: no mind for talker2");

            string s2 = ContextAssembler.Build(talker2, null, _mgr, 0, ContextProfile.Full);
            Assert.IsFalse(s2.Contains(H_SHORTTERM), "no mind → §5 absent");
            Assert.IsFalse(_mgr.Minds.ContainsKey(talker2.PlayerId),
                "Build is READ-ONLY: it must NOT create a mind for an absent talker");
        }

        // ---------- Test 6: 环境 falls back to PlaceText (+ KnownPresent) ----------

        [Test]
        public void Assembler_ShortTerm_SurroundingsFallbackToPlaceText()
        {
            // 6a: Summary empty, PlaceText set, KnownPresent non-empty →
            // 环境 = "<PlaceText>；在场可交谈者：<、-joined>".
            var bioA = NewBio("huangrong", "黄蓉");
            bioA.Personality = "灵动机敏";
            var talkerA = TestBuilders.MakeAgent("huangrong", bio: bioA);

            var mA = _mgr.GetOrCreateMind(talkerA.PlayerId);
            mA.Surroundings.Summary = "";
            mA.Surroundings.PlaceText = "低矮客栈";
            mA.Surroundings.KnownPresent.Add("欧阳克");
            mA.Surroundings.KnownPresent.Add("江湖客");

            string a = ContextAssembler.Build(talkerA, null, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("\n环境：低矮客栈；在场可交谈者：欧阳克、江湖客", a,
                "Summary empty → fall back to PlaceText + 、-joined KnownPresent");

            // 6b: Summary empty, PlaceText set, KnownPresent empty →
            // 环境 = just "<PlaceText>" (no "；在场可交谈者：" suffix).
            var bioB = NewBio("guojing", "郭靖");
            bioB.Personality = "憨厚";
            var talkerB = TestBuilders.MakeAgent("guojing", bio: bioB);

            var mB = _mgr.GetOrCreateMind(talkerB.PlayerId);
            mB.Surroundings.Summary = "";
            mB.Surroundings.PlaceText = "低矮客栈";
            // KnownPresent stays empty.

            string b = ContextAssembler.Build(talkerB, null, _mgr, 0, ContextProfile.Full);
            StringAssert.Contains("\n环境：低矮客栈", b, "PlaceText-only 环境 rendered");
            string sect = SectionRegion(b, H_SHORTTERM);
            Assert.IsFalse(sect.Contains("在场可交谈者："),
                "no KnownPresent → no '；在场可交谈者：' suffix");
        }

        // ---------- Test 7: Leave profile excludes §5 (Plan §6.1 lean) ----------

        [Test]
        public void Assembler_LeaveProfile_ExcludesShortTerm()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            mind.Task = "辨明谁可信";
            mind.Surroundings.Summary = "客栈之中";

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Leave);

            StringAssert.Contains(H_SELF, s, "Leave still emits §2 (stay in voice)");
            Assert.IsFalse(s.Contains(H_SHORTTERM),
                "Leave omits the §5.1-5.3 short-term block (Plan §6.1 — Full-only)");
            Assert.IsFalse(s.Contains(L_SITUATION), "Leave omits 处境");
            Assert.IsFalse(s.Contains(L_TASK), "Leave omits 目标");
            Assert.IsFalse(s.Contains(L_SURROUNDINGS), "Leave omits 环境");
        }

        // ---------- Test 8: section order — §5 after §4 (Plan §1 fixed order) ----------

        [Test]
        public void Assembler_SectionOrder_ShortTermAfterLongTerm()
        {
            _mgr.World = MakeWorld();

            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talkeeBio = NewBio("ouyangke", "欧阳克");
            talkeeBio.Appearance = "锦衣华服";
            talkeeBio.SurfaceManner = "举止温文";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: talkeeBio);

            var dossier = NewSO<CharacterDossier>();
            dossier.AgentId = "huangrong";
            dossier.PolityKnowledge.Add(new KnowledgeLine { Subject = "大宋", Text = "临安繁华" });
            dossier.People.Add(new PersonView { Target = "ouyangke", Impression = "心术不正" });
            _mgr.Dossiers["huangrong"] = dossier;

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";

            string s = ContextAssembler.Build(talker, talkee, _mgr, 0, ContextProfile.Full);

            int lt = Idx(s, H_LONGTERM);
            int st = Idx(s, H_SHORTTERM);
            Assert.Greater(lt, -1, "§4 long-term present");
            Assert.Greater(st, -1, "§5 short-term present");
            Assert.Less(lt, st, "§4 before §5 (Plan §1 fixed order: §4 long-term → §5 short-term)");
        }

        // ---------- Test 9: 3B coexistence at the ConversationPrompts seam ----------

        [Test]
        public void Coexistence_ShortTerm_Phase2Intact()
        {
            _mgr.World = MakeWorld();

            var selfBio = NewBio("huangrong", "黄蓉");
            selfBio.Personality = "灵动机敏";
            selfBio.Identity = "桃花岛黄药师之女";
            var otherBio = NewBio("ouyangke", "欧阳克");
            otherBio.Identity = "白驼山少主";
            otherBio.Appearance = "锦衣华服";
            otherBio.SurfaceManner = "举止温文";

            var talker = TestBuilders.MakeAgent("huangrong", bio: selfBio);
            var talkee = TestBuilders.MakeAgent("ouyangke", bio: otherBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            mind.Situation = "避追兵于客栈";
            mind.Task = "辨明谁可信";

            const string priorMem = "PRIOR_MEM_SENTINEL";

            var start = ConversationPrompts.BuildStart(
                selfBio, otherBio, talker, talkee, _mgr, now: 1_000_000, priorMemory: priorMem);
            string sp = start.SystemPrompt;

            StringAssert.Contains(H_SHORTTERM, sp,
                "assembler §5 short-term block present in the Start prompt");
            StringAssert.Contains(priorMem, sp,
                "Phase 2 prior-memory path STILL intact (3B coexistence, Plan §8)");
            int asmIdx = Idx(sp, H_SELF);   // assembler block start (§2 always present)
            int memIdx = Idx(sp, priorMem);
            Assert.Greater(asmIdx, -1, "assembler block present");
            Assert.Greater(memIdx, -1, "Phase 2 prior-memory block present");
            Assert.Less(asmIdx, memIdx,
                "assembler static block (incl. §5) is PREPENDED before the Phase 2 content");
        }

        // ---------- Test 10: §5 truncated to its char budget ----------

        [Test]
        public void Assembler_ShortTerm_BudgetTruncates()
        {
            var talkerBio = NewBio("huangrong", "黄蓉");
            talkerBio.Personality = "灵动机敏";
            var talker = TestBuilders.MakeAgent("huangrong", bio: talkerBio);

            var mind = _mgr.GetOrCreateMind(talker.PlayerId);
            // > SECT_SHORTTERM_BUDGET (4000) of content for §5.
            mind.Surroundings.Summary = new string('景', 6000);

            string s = ContextAssembler.Build(talker, null, _mgr, 0, ContextProfile.Full);

            string sect5 = SectionRegion(s, H_SHORTTERM);
            Assert.IsNotEmpty(sect5, "§5 must be present");
            Assert.LessOrEqual(sect5.Length, AITavernConstants.SECT_SHORTTERM_BUDGET,
                "§5 must be hard-capped to SECT_SHORTTERM_BUDGET chars");
            StringAssert.EndsWith("…", sect5,
                "an over-budget §5 ends with the ellipsis truncation marker");
        }
    }
}
