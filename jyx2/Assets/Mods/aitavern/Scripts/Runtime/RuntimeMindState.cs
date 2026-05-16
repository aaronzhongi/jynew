using System.Collections.Generic;

namespace Jyx2.AITavern
{
    // Phase 3B (Plan §2.4): per-live-agent volatile short-term memory.
    // Lives in AITavernManager.Minds (in-process only, never serialized —
    // same boundary as Phase 2 MemoryStash). 3B ships Situation/Task/
    // Surroundings; Emotion (3C) / GlobalReflection + Targets (3D) are
    // added by their sub-phases at the marked seams.
    public class RuntimeMindState
    {
        public GameId Owner;
        public string Situation;            // §5.1 designer/test set; static for a scene run
        public string Task;                 // §5.2 designer/test set; static for a scene run
        public SurroundingsModel Surroundings = new SurroundingsModel();  // §5.3

        public Affect Emotion;                                  // §5.4 decaying mood (set by 3D appraisal; until then unset → §5.4 omitted)
        public Dictionary<GameId, TargetState> Targets = new Dictionary<GameId, TargetState>();  // §5.5 per-talkee (3C: Affection only)
        // 3D: public string GlobalReflection;                  // §5.0 cross-person fold
    }

    // §5.3 surroundings — structural model + a cheap template/Grok-summarized
    // prose blob. 3B/frozen-NPC: structural set once at spawn, Summary is
    // template-rendered, ZERO Grok calls (Plan §4.2 / §10 Q5). The Grok-
    // summarize-on-structural-change path is a 3B+ enhancement that never
    // fires while NPCs are frozen.
    public class SurroundingsModel
    {
        public string PlaceText;                              // e.g. "低矮客栈，七八桌客人"
        public List<string> KnownPresent = new List<string>(); // talkable actors here (display names/ids)
        public string Summary;                                // rendered prose for the prompt
        public long LastUpdatedMs;                            // structural-change timestamp
    }

    // Phase 3C (Plan §2.4 / §4.3 / §4.4): a decaying scalar+label used for
    // BOTH emotion (§5.4, decays toward 平静/0) and per-target affection
    // (§5.5.1, decays toward the CANON relationship baseline — NOT 0).
    // Pure POCO: System.Math.Exp (NOT UnityEngine.Mathf) so it has no engine
    // dependency and matches the long-epoch-ms precision IClock/FakeClock use
    // (existing-impl-lens fix). Decay is LAZY — computed on read via
    // Current(now); nothing ticks it. 3C builds this container; the appraisal
    // that actually MOVES Value on events is 3D (§5.3 reflection deltas).
    public class Affect
    {
        public string Label;       // 平静/警惕/愤怒/喜悦/恐惧 … or like/hate label
        public float  Value;       // emotion: 0..1 intensity; affection: -1..1
        public float  Baseline;    // emotion → 0 ; affection → long-term relation baseline
        public long   LastSetMs;
        public float  HalfLifeMs;  // emotion short, affection long (constants §7)

        public float Current(long now)
        {
            if (HalfLifeMs <= 0f) return Value;          // guard: no decay configured
            double dt = now - LastSetMs;                  // long-long = long → widened to double
            return (float)(Baseline + (Value - Baseline) * System.Math.Exp(-dt / HalfLifeMs));
        }
    }

    // Phase 3C ships ONLY Affection (§5.5.1). ReflectionSummary / Ring /
    // ImpressionDelta (§5.5.2/3/overlay) are 3D — declared then, at the
    // marked seams, because Ring needs TurnRecord + MEMORY_RING_CAP which
    // do not exist yet.
    public class TargetState
    {
        public Affect Affection;                 // §5.5.1 decays toward canon baseline
        // 3D: public string ReflectionSummary;  // §5.5.2 per-pair fold
        // 3D: public System.Collections.Generic.List<TurnRecord> Ring;  // §5.5.3 last-N turns
        // 3D: public string ImpressionDelta;    // §4 runtime overlay on Dossier.PersonView
    }
}
