// T3D.6 (Plan §5.3 / §5.4 / §9) — REWRITTEN from the Phase 2 4-facet
// MemoryCompactor.MaybeCompactForOwner tests. The Phase 2 body
// (FacetSlotPrefixes 关系/共同经历/未解矛盾/关键事实, the 20k budget gate,
// BuildPriorMemoryBlock) was REPLACED by the §5.3 4-slot reflection-
// consolidation op (往来印象/情绪变化/好恶变化/违背设定). Per Plan §5.4
// these Phase 2 prompt/parse tests are REWRITTEN to the 4-slot reality —
// expected and scheduled, not a regression.
//
// MemoryCompactor.ConsolidateForOwner is `public static async UniTask<bool>`
// so it is driven directly (black-box on the op contract); MockGrokClient
// scripts the 4-slot block and captures the prompt so the re-fold-from-raw
// (prior summary NOT in Grok input) discipline is asserted exactly as the
// Phase 2 test 3 did, against the new prompt.
//
// Invariants under test (Plan §5.3):
//   - re-fold from RAW: prior ReflectionSummary NOT in Grok prompt; the raw
//     turn text IS.
//   - on success: Targets[other].ReflectionSummary == 往来印象; consumed raw
//     IsFolded=true; ts.Ring cleared; CompactedSummary written (one per pair).
//   - salience gate (|好恶变化|<AFFECT_DELTA_DEADBAND ∧ neutral 情绪变化):
//     summary STILL folds but Emotion/Affection.Value + LastSetMs UNCHANGED.
//   - non-trivial slots → deltas applied FROM RAW (not Affect.Current decay):
//     new Affection.Value == clamp(old Value + δ); Baseline/HalfLifeMs intact.
//   - failure stance (Grok throws / empty / missing 往来印象) → false, NOTHING
//     consumed (Ring intact, no IsFolded, no SetSummary, no delta).
//   - ImpressionDelta appended (bounded); the CharacterDossier asset is
//     NEVER mutated; 违背设定 non-empty does not throw (log only).
using NUnit.Framework;
using System.Collections.Generic;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class MemoryCompactorTests
    {
        AITavernManager _mgr;
        MockGrokClient _mock;
        GameId _huangrong;
        GameId _ouyangke;
        readonly List<CharacterBio> _bios = new List<CharacterBio>();
        readonly List<UnityEngine.ScriptableObject> _sos = new List<UnityEngine.ScriptableObject>();

        // §5.3 4-slot prefixes — matched byte-for-byte against MemoryCompactor.
        const string SLOT_IMPRESSION    = "往来印象：";
        const string SLOT_EMOTION       = "情绪变化：";
        const string SLOT_AFFECTION     = "好恶变化：";
        const string SLOT_CONTRADICTION = "违背设定：";

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _mock = new MockGrokClient();
            _mgr.Grok = _mock;
            _huangrong = new GameId("huangrong");
            _ouyangke = new GameId("ouyangke");
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

        // A raw, un-folded Conversation entry for (owner,other) — the spill the
        // §5.3 re-fold reads. Returns the entry so a test can flip IsFolded etc.
        MemoryEntry AppendRaw(GameId owner, GameId other, string desc, long endedAt)
        {
            _mgr.Memory.AppendConversationMemory(owner, other, desc, endedAt);
            return _mgr.Memory.Entries[_mgr.Memory.Entries.Count - 1];
        }

        // Build a scripted 4-slot block exactly as the §5.3 system prompt asks.
        static string Slots(string impression, string emotion, string affection,
                            string contradiction = "空")
        {
            return SLOT_IMPRESSION + impression + "\n"
                 + SLOT_EMOTION + emotion + "\n"
                 + SLOT_AFFECTION + affection + "\n"
                 + SLOT_CONTRADICTION + contradiction;
        }

        TargetState TargetOf(GameId owner, GameId other)
        {
            var mind = _mgr.GetOrCreateMind(owner);
            if (!mind.Targets.TryGetValue(other, out var ts) || ts == null)
            {
                ts = new TargetState();
                mind.Targets[other] = ts;
            }
            return ts;
        }

        bool Consolidate(long now) =>
            MemoryCompactor.ConsolidateForOwner(_mgr, _huangrong, _ouyangke, now)
                .GetAwaiter().GetResult();

        // ---------- Test 1: re-fold from RAW — prior summary NOT in Grok input ----------

        [Test]
        public void ConsolidateForOwner_refoldsFromRaw_priorSummaryNotInPrompt()
        {
            const string priorSummary = "PRIOR_SUMMARY_SENTINEL 旧的往来印象";
            const string rawTurn = "RAW_TURN_SENTINEL 欧阳克声称无意争斗";

            // A prior per-pair ReflectionSummary exists (must NOT be fed back).
            var ts = TargetOf(_huangrong, _ouyangke);
            ts.ReflectionSummary = priorSummary;

            // Raw, un-folded conversation entry — the only legitimate Grok input.
            AppendRaw(_huangrong, _ouyangke, rawTurn, 1_000_500);

            _mock.Responses.Enqueue(Slots("他声称无意争斗", "平静", "0"));

            bool ok = Consolidate(1_002_000);
            Assert.IsTrue(ok, "a fold over non-empty raw must succeed");
            Assert.AreEqual(1, _mock.CompletionCallCount, "exactly one Grok call");

            string grokInput = _mock.LastSystemPrompt + "\n" + _mock.LastUserContent;
            Assert.IsFalse(grokInput.Contains("PRIOR_SUMMARY_SENTINEL"),
                "the prior ReflectionSummary must NOT appear in the Grok prompt (re-fold from raw, Plan §5.3 anti-degradation)");
            StringAssert.Contains("RAW_TURN_SENTINEL", grokInput,
                "the raw turn text MUST appear in the Grok prompt");
        }

        // ---------- Test 2: success path — summary written, raw folded, ring cleared ----------

        [Test]
        public void ConsolidateForOwner_success_writesSummary_foldsRaw_clearsRing()
        {
            var ts = TargetOf(_huangrong, _ouyangke);
            ts.Ring.Add(new TurnRecord { Speaker = _huangrong, Text = "你到底想做什么", Ms = 1_000_900 });
            var rawEntry = AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 我只是路过", 1_000_500);

            const string setImpression = "他自称路过，但屡屡试探木箱，言辞含蜜而不可尽信";
            _mock.Responses.Enqueue(Slots(setImpression, "警惕 0.6", "-0.3"));

            long now = 1_003_000;
            bool ok = Consolidate(now);

            Assert.IsTrue(ok, "fold over raw+ring succeeds");
            Assert.AreEqual(setImpression, ts.ReflectionSummary,
                "Targets[other].ReflectionSummary == 往来印象 slot text");

            var cs = _mgr.Memory.GetSummary(_huangrong, _ouyangke);
            Assert.IsNotNull(cs, "a CompactedSummary is written for the pair");
            Assert.AreEqual(setImpression, cs.SummaryText, "CompactedSummary.SummaryText == 往来印象");
            Assert.AreEqual(now, cs.CreatedAt, "CreatedAt == now passed in");
            Assert.AreEqual(1, _mgr.Memory.Summaries.Count, "exactly one summary per pair");

            Assert.IsTrue(rawEntry.IsFolded, "consumed raw spill entry marked IsFolded on success");
            Assert.AreEqual(0, ts.Ring.Count, "ts.Ring cleared on success (next conv-end gets a fresh anchor)");
        }

        // ---------- Test 3: salience gate — trivial chit-chat folds summary, no affect move ----------

        [Test]
        public void ConsolidateForOwner_salienceGate_trivialFoldsSummaryButNoAffectMove()
        {
            const long oldSetMs = 0;
            var ts = TargetOf(_huangrong, _ouyangke);
            ts.Affection = new Affect
            {
                Label = "中性", Value = 0.10f, Baseline = 0.0f,
                LastSetMs = oldSetMs, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
            };
            AppendRaw(_huangrong, _ouyangke, "[spill] huangrong: 今日天气不错", 1_000_500);

            // |好恶变化| (0.02) < AFFECT_DELTA_DEADBAND (0.08) AND 情绪变化 neutral
            // → salience gate skips delta application but STILL folds the summary.
            _mock.Responses.Enqueue(Slots("闲谈数语，无关紧要", "平静", "0.02"));

            long now = 5_000_000;
            bool ok = Consolidate(now);

            Assert.IsTrue(ok, "trivial exchange still folds the durable summary");
            Assert.AreEqual("闲谈数语，无关紧要", ts.ReflectionSummary, "summary IS written");

            Assert.AreEqual(0.10f, ts.Affection.Value, 1e-4f,
                "salience gate: trivial → Affection.Value UNCHANGED");
            Assert.AreEqual(oldSetMs, ts.Affection.LastSetMs,
                "salience gate: LastSetMs NOT reset (so baseline-reversion decay still fires, Plan §4.4)");
            Assert.IsNull(_mgr.GetOrCreateMind(_huangrong).Emotion,
                "salience gate: neutral 情绪变化 → Emotion left unset");
        }

        // ---------- Test 4: deltas computed FROM RAW, not from decayed Affect.Current ----------

        [Test]
        public void ConsolidateForOwner_nonTrivial_appliesDeltaFromRaw_notDecayedValue()
        {
            // Affection set with a VERY OLD LastSetMs so Current(now) has decayed
            // almost fully back to Baseline. The §5.3 delta must be applied to
            // the stored Value (the model's read of the RAW turns), NOT to the
            // near-baseline Current(now) — Plan §5.3 must-fix / §9 regression.
            const float oldValue = 0.50f;
            const float baseline = -0.70f;
            const long oldSetMs = 0;
            var ts = TargetOf(_huangrong, _ouyangke);
            ts.Affection = new Affect
            {
                Label = "戒备", Value = oldValue, Baseline = baseline,
                LastSetMs = oldSetMs, HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
            };

            long now = 1_000_000_000; // ≫ many half-lives after oldSetMs
            float decayed = ts.Affection.Current(now);
            Assert.That(decayed, Is.EqualTo(baseline).Within(0.02f),
                "precondition: Current(now) has decayed ~to Baseline (so a from-decayed delta would differ)");

            AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 我助你脱困，断无恶意", 1_000_500);

            const float delta = -0.30f;
            _mock.Responses.Enqueue(Slots("他自陈相助，难辨真伪", "愤怒 0.7", delta.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture)));

            bool ok = Consolidate(now);
            Assert.IsTrue(ok);

            float expected = UnityEngine.Mathf.Clamp(oldValue + delta, -1f, 1f); // 0.50 + (-0.30) = 0.20
            Assert.AreEqual(expected, ts.Affection.Value, 1e-4f,
                "delta applied to STORED Value (from raw), NOT to the decayed Current(now)");
            Assert.AreNotEqual(decayed, ts.Affection.Value,
                "if it had applied delta to the decayed value the result would be near baseline — it must not");
            Assert.AreEqual(baseline, ts.Affection.Baseline, 1e-6f,
                "Affection.Baseline UNCHANGED (canon RelationType seed preserved)");
            Assert.AreEqual(AITavernConstants.AFFECTION_HALFLIFE_MS, ts.Affection.HalfLifeMs, 1e-3f,
                "Affection.HalfLifeMs UNCHANGED");
            Assert.AreEqual(now, ts.Affection.LastSetMs, "non-trivial → LastSetMs reset to now");

            var emo = _mgr.GetOrCreateMind(_huangrong).Emotion;
            Assert.IsNotNull(emo, "non-neutral 情绪变化 sets Emotion");
            Assert.AreEqual(0f, emo.Baseline, 1e-6f, "Emotion.Baseline == 0 (reverts to 平静)");
            Assert.That(emo.Value, Is.EqualTo(0.7f).Within(1e-4f), "Emotion.Value == parsed intensity");
        }

        // ---------- Test 5: failure stances — nothing consumed ----------

        [Test]
        public void ConsolidateForOwner_grokThrows_returnsFalse_nothingConsumed()
        {
            var ts = TargetOf(_huangrong, _ouyangke);
            ts.Ring.Add(new TurnRecord { Speaker = _huangrong, Text = "原始未沉淀的话", Ms = 1_000_900 });
            var raw = AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 原始溢出", 1_000_500);
            _mock.ThrowOnCall = true;

            bool ok = Consolidate(1_003_000);

            Assert.IsFalse(ok, "Grok throw → returns false");
            Assert.IsFalse(raw.IsFolded, "nothing consumed: raw NOT folded on failure");
            Assert.AreEqual(1, ts.Ring.Count, "Ring intact on failure (next conv-end retries)");
            Assert.IsNull(ts.ReflectionSummary, "no ReflectionSummary written on failure");
            Assert.AreEqual(0, _mgr.Memory.Summaries.Count, "no CompactedSummary written on failure");
        }

        [Test]
        public void ConsolidateForOwner_emptyResponse_returnsFalse_nothingConsumed()
        {
            var ts = TargetOf(_huangrong, _ouyangke);
            var raw = AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 原始溢出", 1_000_500);
            _mock.Responses.Enqueue("   ");

            bool ok = Consolidate(1_003_000);

            Assert.IsFalse(ok, "empty/whitespace Grok response → false");
            Assert.IsFalse(raw.IsFolded, "nothing consumed on empty response");
            Assert.IsNull(ts.ReflectionSummary);
            Assert.AreEqual(0, _mgr.Memory.Summaries.Count);
        }

        [Test]
        public void ConsolidateForOwner_missingImpressionSlot_returnsFalse_nothingConsumed()
        {
            var ts = TargetOf(_huangrong, _ouyangke);
            var raw = AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 原始溢出", 1_000_500);
            // 往来印象 is REQUIRED — a response missing it is treated as failure.
            _mock.Responses.Enqueue(SLOT_EMOTION + "平静\n" + SLOT_AFFECTION + "0\n" + SLOT_CONTRADICTION + "空");

            bool ok = Consolidate(1_003_000);

            Assert.IsFalse(ok, "missing 往来印象 slot → false");
            Assert.IsFalse(raw.IsFolded, "nothing consumed when required slot missing");
            Assert.IsNull(ts.ReflectionSummary);
            Assert.AreEqual(0, _mgr.Memory.Summaries.Count);
        }

        [Test]
        public void ConsolidateForOwner_noRaw_returnsFalse_noGrokCall()
        {
            // Nothing happened for this pair — no spill, no ring.
            bool ok = Consolidate(1_003_000);

            Assert.IsFalse(ok, "nothing to consolidate → false");
            Assert.AreEqual(0, _mock.CompletionCallCount, "no Grok call when there is no raw");
        }

        // ---------- Test 6: ImpressionDelta appended; Dossier asset NEVER mutated ----------

        [Test]
        public void ConsolidateForOwner_appendsImpressionDelta_neverMutatesDossierAsset()
        {
            // Canon dossier for the talker with a PersonView the assembler reads.
            var dossier = NewSO<CharacterDossier>();
            dossier.AgentId = "huangrong";
            const string canonImpression = "白驼山少主，心术不正（canon）";
            var pv = new PersonView { Target = "ouyangke", Impression = canonImpression };
            dossier.People.Add(pv);
            _mgr.Dossiers["huangrong"] = dossier;

            var ts = TargetOf(_huangrong, _ouyangke);
            AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 我此行别有目的", 1_000_500);

            const string setImpression = "他承认此行别有目的，本局所见与传闻相合";
            _mock.Responses.Enqueue(Slots(setImpression, "警惕 0.5", "-0.2"));

            bool ok = Consolidate(1_003_000);
            Assert.IsTrue(ok);

            Assert.IsFalse(string.IsNullOrWhiteSpace(ts.ImpressionDelta),
                "ImpressionDelta runtime overlay appended");
            StringAssert.Contains(setImpression, ts.ImpressionDelta,
                "ImpressionDelta derived from the 往来印象 slot text");
            Assert.LessOrEqual(ts.ImpressionDelta.Length, 600,
                "ImpressionDelta is bounded (IMPRESSION_DELTA_MAX_CHARS)");

            // The immutable CharacterDossier ASSET is NEVER written.
            Assert.AreEqual(canonImpression, pv.Impression,
                "the Dossier PersonView.Impression asset is NEVER mutated by consolidation (Plan §5.3 / §4)");
            Assert.AreSame(dossier, _mgr.Dossiers["huangrong"], "Dossiers entry unchanged");
        }

        // ---------- Test 7: 违背设定 non-empty → does not throw (log-only tripwire) ----------

        [Test]
        public void ConsolidateForOwner_contradictionSlotNonEmpty_doesNotThrow()
        {
            var ts = TargetOf(_huangrong, _ouyangke);
            AppendRaw(_huangrong, _ouyangke, "[spill] ouyangke: 我乃丐帮帮主", 1_000_500);

            _mock.Responses.Enqueue(Slots("他自称丐帮帮主，与其设定不符",
                "警惕 0.4", "-0.1", contradiction: "他自称丐帮帮主，与白驼山少主设定矛盾"));

            // Plan §5.3: 违背设定 non-empty logs [Reflect] canon-contradiction
            // and is NON-FATAL — the consolidation must still succeed.
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"\[Reflect\] canon-contradiction"));

            bool ok = Consolidate(1_003_000);

            Assert.IsTrue(ok, "a canon-contradiction is logged but does NOT fail the fold");
            Assert.AreEqual("他自称丐帮帮主，与其设定不符", ts.ReflectionSummary,
                "summary still written despite the contradiction tripwire");
        }

        // ---------- helper ----------

        T NewSO<T>() where T : UnityEngine.ScriptableObject
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<T>();
            _sos.Add(so);
            return so;
        }
    }
}
