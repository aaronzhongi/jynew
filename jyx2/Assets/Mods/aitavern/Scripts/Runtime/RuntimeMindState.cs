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

        // 3C: public Affect Emotion;                       // §5.4 decaying mood
        // 3D: public string GlobalReflection;              // §5.0 cross-person fold
        // 3D: public Dictionary<GameId, TargetState> Targets;  // §5.5 per-talkee
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
}
