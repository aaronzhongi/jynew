// Phase 3A (Plan §6) — ContextAssembler: the layered human-like memory
// context builder. This sub-phase ships the STATIC spine only: §1 World
// Codex, §2 Talker Bio, §3 Talkee first-impression surface, §4 long-term
// knowledge (the talker's Dossier view of the talkee). Deterministic,
// synchronous, ZERO Grok calls (all Grok work is offline in the lore
// pipeline — T3A.6 — or in the 3D reflection op).
//
// SCOPE FENCE (Plan §8 internal sub-phasing):
//   - 3A = static §1-§4 ONLY. §5 short-term (situation/task/surroundings/
//     emotion), the episodic ring, decay, reflection, ImpressionDelta
//     overlay, the §5.0 global reflection — ALL deferred to 3B-3D. This
//     file emits NO §5 placeholder; a fresh game with no World/Dossier
//     assets yet produces an empty assembler block and the conversation
//     still runs on the retained Phase 2 memory path (coexistence, §8).
//   - The `// 3B:` / `// 3D:` markers below pin exactly where the later
//     layers slot in so the next sub-phase has an unambiguous seam.
//
// COEXISTENCE (Plan §8, CRITICAL): in 3A this static block is PREPENDED
// alongside the retained Phase 2 BuildPriorMemoryBlock memory path — it
// does NOT replace it. The Phase 2 path is removed only in 3D.
//
// ANTI-OMNISCIENCE (Plan §6.2): §3 is built by a private helper whose
// signature physically CANNOT accept the talkee's Personality / Identity /
// Plans / Relationships — it takes only the 4 surface fields a stranger
// can perceive (Sex / AgeText / Appearance / SurfaceManner). The guard is
// structural, not a convention.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jyx2.AITavern
{
    /// <summary>
    /// Selects which assembler sections emit. <see cref="Full"/> is used by
    /// Start/Continue (§1-§4 in 3A; §1-§5 once 3B-3D land). <see cref="Leave"/>
    /// is the lean farewell profile (Plan §6.1): canonically §2+§5.4+§5.5.3.
    /// §5.4 emotion lands in 3C (rendered here when set); §5.5.3 ring is 3D.
    /// Until 3D appraisal first sets Emotion, Leave == §2 only in practice.
    /// </summary>
    public enum ContextProfile { Full, Leave }

    public static class ContextAssembler
    {
        /// <summary>
        /// Build the ordered §1-§4 static context block for (talker, talkee).
        /// Empty sections contribute NOTHING (no header, no blank line) so a
        /// fresh game yields a lean prompt that grows as lore assets land.
        /// Synchronous, deterministic, no Grok call.
        /// </summary>
        /// <param name="talker">the speaking agent (always has a Bio)</param>
        /// <param name="talkee">the agent being spoken to</param>
        /// <param name="mgr">process state — World + Dossiers live here</param>
        /// <param name="now">epoch ms; reserved for 3C decay / 3D overlay</param>
        /// <param name="profile">Full (Start/Continue) or Leave (farewell)</param>
        public static string Build(Agent talker, Agent talkee, AITavernManager mgr, long now, ContextProfile profile)
        {
            var sb = new StringBuilder();
            if (talker == null) return string.Empty;

            var talkerBio = talker.Bio;
            var talkeeBio = talkee != null ? talkee.Bio : null;

            // Section order is FIXED (Plan §1 mock). Profile + 3A scope gate
            // which ones emit. 3A is static-only: §5 does not exist here.
            if (profile == ContextProfile.Full)
            {
                // §1 World Codex — omitted entirely if no asset loaded yet.
                AppendSection(sb, BuildWorld(mgr), AITavernConstants.SECT_WORLD_BUDGET);
                // §2 Talker bio — always present (talker always has a Bio).
                AppendSection(sb, BuildSelfBio(talkerBio), AITavernConstants.SECT_SELFBIO_BUDGET);
                // §3 Talkee first-impression surface — anti-omniscience guarded.
                AppendSection(sb, BuildTalkeeSurface(talkeeBio), AITavernConstants.SECT_TALKEE_BUDGET);
                // §4 Long-term knowledge — the TALKER's Dossier view.
                AppendSection(sb, BuildLongTerm(talker, talkee, mgr), AITavernConstants.SECT_LONGTERM_BUDGET);

                // §5 short-term — 处境/目标/环境 (§5.1-5.3, 3B) + 此刻心绪
                // (§5.4 emotion, 3C done) as ONE cohesive block under one
                // header / one budget. Read-only lookup of the talker's
                // RuntimeMindState; whole block omitted if no mind or all of
                // §5.1+§5.2+§5.3+§5.4 are empty (Plan §6 empty-omission).
                // `now` threaded in for §5.4's read-time exp decay.
                AppendSection(sb, BuildShortTerm(talker, mgr, now), AITavernConstants.SECT_SHORTTERM_BUDGET);

                // §5.5.1 affection — the talker's CURRENT好恶 toward THIS
                // talkee (decays toward the canon relationship baseline,
                // read-time via Affect.Current(now)). Full-profile ONLY:
                // Plan §6.1's lean Leave list is "§2+§5.4+§5.5.3" — §5.5.1
                // is NOT in it (Leave gets the §5.5.3 ring only, which is
                // 3D), so affection stays Full-only for 3C. Starts the §5.5
                // per-target area; emitted under its own `·对 X·` header,
                // omitted entirely (no bare header) when there is no target
                // entry or no Affection (the 3C norm until 3D appraisal).
                AppendSection(sb, AffectionBlock(talker, talkee, mgr, now), AITavernConstants.SECT_SHORTTERM_BUDGET);

                // 3C done (§5.4 emotion folded into BuildShortTerm; §5.5.1
                //     affection rendered above, Full-only).
                // 3D: §5.0 global reflection slots BEFORE the §5.5 per-target
                //     area; §5.5.2 reflection summary + §5.5.3 episodic ring
                //     append under the SAME `·对 X·` header AffectionBlock
                //     opens (see the // 3D seam inside AffectionBlock). The
                //     ring becomes the SOLE transcript source — that is when
                //     ConversationPrompts.BuildContinue's AppendTranscript is
                //     deleted (Plan §6.1). In 3A/3C AppendTranscript STAYS.
            }
            else // ContextProfile.Leave
            {
                // Lean farewell profile (Plan §6.1): canon §2+§5.4+§5.5.3.
                // §5.4/§5.5.3 are 3B-3D, so 3A Leave == §2 ONLY. Explicitly
                // NO §1 World Codex, NO §3, NO §4 long-term knowledge —
                // routing ~15k chars of context into a <50-char goodbye is
                // the exact cost regression Plan §6.1 forbids. NOTE (3B): the
                // §5.1-5.3 short-term block (situation/task/surroundings) is
                // Full-profile ONLY — it is deliberately NOT added here; the
                // lean farewell carries only §2 (+ later §5.4/§5.5.3).
                AppendSection(sb, BuildSelfBio(talkerBio), AITavernConstants.SECT_SELFBIO_BUDGET);

                // §5.4 emotion — Plan §6.1 explicitly includes §5.4 in the
                // lean Leave list ("§2+§5.4+§5.5.3"). A tiny standalone
                // 【眼前局势 — 短期记忆】 block carrying ONLY 此刻心绪 (NOT
                // §5.1/5.2/5.3 — those stay Full-only per T3B.3 / §6.1).
                // Omitted entirely when there is no emotion line (no mind,
                // Emotion unset, or Current(now) below EMOTION_FLOOR) — that
                // is the 3C norm, so Leave stays §2-only in practice until
                // 3D appraisal first sets Emotion.
                AppendSection(sb, EmotionOnlyBlock(talker, mgr, now), AITavernConstants.SECT_SHORTTERM_BUDGET);

                // 3C done (§5.4 emotion-only block rendered above for Leave).
                // 3D: §5.5.3 episodic ring slots here (Leave profile).
            }

            return sb.ToString();
        }

        // ---- Section assembly plumbing ----

        // A section with no content after its builder runs contributes
        // NOTHING — no header, no blank line (Plan §1: empty sections omitted
        // entirely so a fresh game produces a lean prompt). Each section is
        // hard-capped to its §7 char budget; on overflow it is cut to the
        // budget and an ellipsis appended.
        static void AppendSection(StringBuilder sb, string section, int budget)
        {
            if (string.IsNullOrWhiteSpace(section)) return;
            section = Truncate(section, budget);
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(section);
        }

        // Hard char cap (Plan §6/§7): if over budget, cut to budget and
        // append "…". The ellipsis is included WITHIN the cap so the section
        // never exceeds `budget` chars total.
        static string Truncate(string s, int budget)
        {
            if (s == null) return null;
            if (budget <= 0) return string.Empty;
            if (s.Length <= budget) return s;
            if (budget == 1) return "…";
            return s.Substring(0, budget - 1) + "…";
        }

        static string SexText(Sex sex)
        {
            switch (sex)
            {
                case Sex.Male:   return "男";
                case Sex.Female: return "女";
                default:         return "其他";
            }
        }

        // ---- §1 World Codex (static, SECT_WORLD_BUDGET) ----

        static string BuildWorld(AITavernManager mgr)
        {
            var w = mgr != null ? mgr.World : null;
            if (w == null) return null;   // no asset loaded → omit §1 entirely

            var sb = new StringBuilder();
            sb.Append("【世界背景】");

            bool any = false;

            if (!string.IsNullOrWhiteSpace(w.Era))
            {
                sb.Append("\n时代：").Append(w.Era.Trim());
                any = true;
            }

            // 势力 + relations. Compact prose, not raw field dumps.
            if (w.Polities != null && w.Polities.Count > 0)
            {
                var names = new List<string>();
                var rels = new List<string>();
                foreach (var p in w.Polities)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                    names.Add(string.IsNullOrWhiteSpace(p.Brief)
                        ? p.Name.Trim()
                        : p.Name.Trim() + " — " + p.Brief.Trim());
                    CollectRelations(p.Relations, rels);
                }
                if (names.Count > 0)
                {
                    sb.Append("\n势力：").Append(string.Join("；", names));
                    if (rels.Count > 0) sb.Append(" 关系：").Append(string.Join("；", rels));
                    any = true;
                }
            }

            // 门派 + relations.
            if (w.Factions != null && w.Factions.Count > 0)
            {
                var names = new List<string>();
                var rels = new List<string>();
                foreach (var f in w.Factions)
                {
                    if (f == null || string.IsNullOrWhiteSpace(f.Name)) continue;
                    var head = f.Name.Trim();
                    if (!string.IsNullOrWhiteSpace(f.HomeRegion))
                        head += "(" + f.HomeRegion.Trim() + ")";
                    if (!string.IsNullOrWhiteSpace(f.Brief))
                        head += " — " + f.Brief.Trim();
                    names.Add(head);
                    CollectRelations(f.Relations, rels);
                }
                if (names.Count > 0)
                {
                    sb.Append("\n门派：").Append(string.Join("；", names));
                    if (rels.Count > 0) sb.Append(" 关系：").Append(string.Join("；", rels));
                    any = true;
                }
            }

            // 天下知名人物.
            if (w.NotableFigures != null && w.NotableFigures.Count > 0)
            {
                var figures = new List<string>();
                foreach (var n in w.NotableFigures)
                {
                    if (n == null || string.IsNullOrWhiteSpace(n.Name)) continue;
                    var tag = !string.IsNullOrWhiteSpace(n.Faction) ? n.Faction.Trim()
                            : !string.IsNullOrWhiteSpace(n.Polity) ? n.Polity.Trim()
                            : null;
                    var entry = n.Name.Trim();
                    if (tag != null) entry += "(" + tag + ")";
                    if (!string.IsNullOrWhiteSpace(n.OneLine)) entry += " — " + n.OneLine.Trim();
                    figures.Add(entry);
                }
                if (figures.Count > 0)
                {
                    sb.Append("\n天下知名人物：").Append(string.Join("；", figures));
                    any = true;
                }
            }

            return any ? sb.ToString() : null;
        }

        static void CollectRelations(List<RelationLine> relations, List<string> sink)
        {
            if (relations == null) return;
            foreach (var r in relations)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Text)) continue;
                sink.Add(r.Text.Trim());
            }
        }

        // ---- §2 Talker bio (static, SECT_SELFBIO_BUDGET) ----

        static string BuildSelfBio(CharacterBio self)
        {
            if (self == null) return null;

            var sb = new StringBuilder();
            sb.Append("【我是谁】");

            // 姓名 / 性别 / 年龄 header line. AgeText empty → omit the 年龄
            // token entirely (Plan §2: "omit is also fine — pick omit").
            sb.Append("\n姓名：").Append(string.IsNullOrWhiteSpace(self.BioName) ? "?" : self.BioName.Trim());
            sb.Append("  性别：").Append(SexText(self.Sex));
            if (!string.IsNullOrWhiteSpace(self.AgeText))
                sb.Append("  年龄：").Append(self.AgeText.Trim());

            // 性情 — Personality, FALLBACK to Identity when Personality empty
            // (Plan §11: the ONLY Phase 1/2 compat concern — old Bio assets
            // have Identity but no Personality, so they degrade to Identity
            // for 性情 rather than emitting nothing).
            bool hasPersonality = !string.IsNullOrWhiteSpace(self.Personality);
            string trait = hasPersonality
                ? self.Personality.Trim()
                : (!string.IsNullOrWhiteSpace(self.Identity) ? self.Identity.Trim() : null);
            if (trait != null)
                sb.Append("\n性情：").Append(trait);

            // 出身 — Identity's first sentence, ONLY when it is DISTINCT from
            // 性情. If Personality was empty we already used Identity AS the
            // 性情 (the fallback) — emitting 出身 then would print Identity
            // twice, so skip. When Personality IS present, 性情=Personality
            // and 出身=Identity-first-sentence are genuinely different slices.
            if (hasPersonality && !string.IsNullOrWhiteSpace(self.Identity))
            {
                var firstSentence = FirstSentence(self.Identity.Trim());
                if (!string.IsNullOrWhiteSpace(firstSentence)
                    && trait.IndexOf(firstSentence, System.StringComparison.Ordinal) < 0)
                {
                    sb.Append("\n出身：").Append(firstSentence);
                }
            }

            return sb.ToString();
        }

        // First sentence = up to and including the first CJK/ASCII sentence
        // terminator, else the whole string. Cheap, deterministic.
        static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '。' || c == '！' || c == '？' || c == '.' || c == '!' || c == '?' || c == '\n')
                    return s.Substring(0, i + 1).Trim();
            }
            return s;
        }

        // ---- §3 Talkee first-impression surface (static, SECT_TALKEE_BUDGET) ----

        // ANTI-OMNISCIENCE GUARD (Plan §6.2). This is the public seam for §3;
        // it pulls the 4 surface fields off the talkee bio and hands them to
        // the structurally-sealed helper below. It NEVER reads — and the
        // helper CANNOT read — talkee Personality / Identity / Plans /
        // Relationships, because a stranger cannot perceive those.
        static string BuildTalkeeSurface(CharacterBio talkee)
        {
            if (talkee == null) return null;
            return BuildTalkeeSurfaceSealed(
                talkee.BioName,
                talkee.Sex,
                talkee.AgeText,
                talkee.Appearance,
                talkee.SurfaceManner);
        }

        // STRUCTURAL anti-omniscience guard: this method's signature
        // physically cannot access the talkee's Personality / Identity /
        // Plans / Relationships — it only receives the 4 first-impression
        // fields. Do NOT widen this signature to take a CharacterBio; the
        // narrow argument list IS the guard (Plan §6.2 / §9 test).
        static string BuildTalkeeSurfaceSealed(
            string bioName, Sex sex, string ageText, string appearance, string surfaceManner)
        {
            bool hasAppearance = !string.IsNullOrWhiteSpace(appearance);
            bool hasManner = !string.IsNullOrWhiteSpace(surfaceManner);

            // If BOTH Appearance AND SurfaceManner are empty → omit §3
            // entirely. No Phase-3 surface data authored yet (pipeline not
            // run); a lone 性别 line would be misleading, not informative.
            if (!hasAppearance && !hasManner) return null;

            var sb = new StringBuilder();
            sb.Append("【对面是谁 — 初见印象】");
            sb.Append("\n姓名：").Append(string.IsNullOrWhiteSpace(bioName) ? "?" : bioName.Trim());
            sb.Append("  性别：").Append(SexText(sex));
            if (!string.IsNullOrWhiteSpace(ageText))
                sb.Append("  年龄：").Append(ageText.Trim());
            if (hasAppearance)
                sb.Append("\n外貌：").Append(appearance.Trim());
            if (hasManner)
                sb.Append("\n气度：").Append(surfaceManner.Trim());
            return sb.ToString();
        }

        // ---- §4 Long-term knowledge (static + 3D overlay, SECT_LONGTERM_BUDGET) ----

        static string BuildLongTerm(Agent talker, Agent talkee, AITavernManager mgr)
        {
            if (mgr == null || mgr.Dossiers == null) return null;
            string talkerId = talker != null ? talker.AgentId.Value : null;
            if (string.IsNullOrEmpty(talkerId)) return null;

            CharacterDossier d;
            if (!mgr.Dossiers.TryGetValue(talkerId, out d) || d == null)
                return null;   // no dossier for this talker → omit §4 entirely

            var sb = new StringBuilder();
            sb.Append("【我所知 — 长期记忆】");
            bool any = false;

            // ·关于势力·
            string polity = JoinKnowledge(d.PolityKnowledge);
            if (polity != null)
            {
                sb.Append("\n·关于势力·  ").Append(polity);
                any = true;
            }

            // ·关于门派·
            string faction = JoinKnowledge(d.FactionKnowledge);
            if (faction != null)
            {
                sb.Append("\n·关于门派·  ").Append(faction);
                any = true;
            }

            // ·关于此人（<talkee>）· — the talkee's PersonView. Match by
            // Target == talkee AgentId, fallback Target == talkee BioName.
            // If no matching PersonView, still emit the 势力/门派 knowledge
            // above but skip this block.
            var talkeeBio = talkee != null ? talkee.Bio : null;
            string talkeeId = talkee != null ? talkee.AgentId.Value : null;
            string talkeeName = talkeeBio != null && !string.IsNullOrWhiteSpace(talkeeBio.BioName)
                ? talkeeBio.BioName.Trim()
                : (talkeeId ?? "?");

            var pv = FindPersonView(d.People, talkeeId, talkeeBio != null ? talkeeBio.BioName : null);
            if (pv != null)
            {
                sb.Append("\n·关于此人（").Append(talkeeName).Append("）·");
                if (!string.IsNullOrWhiteSpace(pv.Relationship))
                    sb.Append("\n  关系：").Append(pv.Relationship.Trim());
                if (!string.IsNullOrWhiteSpace(pv.Impression))
                    sb.Append("\n  印象：").Append(pv.Impression.Trim());
                if (!string.IsNullOrWhiteSpace(pv.MartialNote))
                    sb.Append("\n  武功：").Append(pv.MartialNote.Trim());
                if (!string.IsNullOrWhiteSpace(pv.SharedHistory))
                    sb.Append("\n  旧事：").Append(pv.SharedHistory.Trim());

                // 3D: ImpressionDelta overlay goes here — the runtime
                // ［本局所历］ line is sourced from
                // RuntimeMindState.Targets[talkee].ImpressionDelta (Plan
                // §4 overlay / §5.3). In 3A there is no RuntimeMindState,
                // so the overlay is ALWAYS empty → the ［本局所历］ line is
                // omitted entirely (Plan §1: empty sub-lines contribute
                // nothing). The // 3D marker pins the exact seam.

                any = true;
            }

            return any ? sb.ToString() : null;
        }

        static string JoinKnowledge(List<KnowledgeLine> lines)
        {
            if (lines == null || lines.Count == 0) return null;
            var parts = new List<string>();
            foreach (var k in lines)
            {
                if (k == null || string.IsNullOrWhiteSpace(k.Text)) continue;
                parts.Add(string.IsNullOrWhiteSpace(k.Subject)
                    ? k.Text.Trim()
                    : k.Subject.Trim() + "：" + k.Text.Trim());
            }
            return parts.Count > 0 ? string.Join("  ", parts) : null;
        }

        static PersonView FindPersonView(List<PersonView> people, string targetId, string targetName)
        {
            if (people == null) return null;
            // Primary: Target == talkee AgentId.
            if (!string.IsNullOrEmpty(targetId))
            {
                foreach (var p in people)
                {
                    if (p != null && p.Target == targetId) return p;
                }
            }
            // Fallback: Target == talkee BioName.
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                var tn = targetName.Trim();
                foreach (var p in people)
                {
                    if (p != null && p.Target != null && p.Target.Trim() == tn) return p;
                }
            }
            return null;
        }

        // ---- §5.1-5.4 short-term (volatile, SECT_SHORTTERM_BUDGET) ----

        // The talker's runtime working memory: 处境 (§5.1 Situation) / 目标
        // (§5.2 Task) / 环境 (§5.3 Surroundings) / 此刻心绪 (§5.4 Emotion,
        // 3C). READ-ONLY: the assembler is a pure synchronous renderer (Plan
        // §6) — it looks the mind up with TryGetValue and NEVER calls
        // GetOrCreateMind or writes any field, so rendering a prompt cannot
        // mutate process state. Mirrors the empty-omission contract of
        // BuildSelfBio/BuildLongTerm: every sub-line is emitted only if its
        // source is non-empty, and if §5.1+§5.2+§5.3+§5.4 are ALL empty the
        // whole §5 block (header included) is omitted. §5.4 decays at
        // read-time via Affect.Current(now) (`now` threaded from Build); no
        // mutation, no Grok (Plan §8).
        static string BuildShortTerm(Agent talker, AITavernManager mgr, long now)
        {
            if (talker == null || mgr == null || mgr.Minds == null) return null;

            // Key on talker.PlayerId — the SAME GameId T3B.2 seeds with
            // (GetOrCreateMind(agent.PlayerId)). No create here: an absent
            // key (player has no mind, or pre-3B-seed) → §5 omitted.
            RuntimeMindState mind;
            if (!mgr.Minds.TryGetValue(talker.PlayerId, out mind) || mind == null)
                return null;

            bool hasSituation = !string.IsNullOrWhiteSpace(mind.Situation);
            bool hasTask = !string.IsNullOrWhiteSpace(mind.Task);

            // 环境: prefer T3B.2's template-rendered Summary; else fall back
            // to PlaceText (+ in-scene talkables when KnownPresent non-empty).
            string surroundings = null;
            var sur = mind.Surroundings;
            if (sur != null)
            {
                if (!string.IsNullOrWhiteSpace(sur.Summary))
                {
                    surroundings = sur.Summary.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(sur.PlaceText))
                {
                    surroundings = sur.PlaceText.Trim();
                    if (sur.KnownPresent != null && sur.KnownPresent.Count > 0)
                    {
                        var present = new List<string>();
                        foreach (var k in sur.KnownPresent)
                        {
                            if (!string.IsNullOrWhiteSpace(k)) present.Add(k.Trim());
                        }
                        if (present.Count > 0)
                            surroundings += "；在场可交谈者：" + string.Join("、", present);
                    }
                }
            }
            bool hasSurroundings = !string.IsNullOrWhiteSpace(surroundings);

            // §5.4 此刻心绪 — read-time decayed emotion line (null when
            // Emotion unset OR Current(now) < EMOTION_FLOOR). Folded into
            // THIS block so 处境/目标/环境/此刻心绪 share one header & one
            // budget; it sorts LAST per the §1 mock + the consumed 3C seam
            // ("§5.4 emotion appends AFTER the §5.1-5.3 short-term block").
            string emotion = EmotionLine(mind, now);
            bool hasEmotion = emotion != null;

            // §5.1+§5.2+§5.3+§5.4 ALL empty → omit the whole §5 block (no
            // bare header) — same `any`/null-return contract as BuildLongTerm.
            if (!hasSituation && !hasTask && !hasSurroundings && !hasEmotion) return null;

            var sb = new StringBuilder();
            sb.Append("【眼前局势 — 短期记忆】");
            if (hasSituation) sb.Append("\n处境：").Append(mind.Situation.Trim());
            if (hasTask)      sb.Append("\n目标：").Append(mind.Task.Trim());
            if (hasSurroundings) sb.Append("\n环境：").Append(surroundings);
            if (hasEmotion)   sb.Append('\n').Append(emotion);
            return sb.ToString();
        }

        // ---- §5.4 emotion (decaying, SECT_SHORTTERM_BUDGET) ----

        // §5.4 此刻心绪: the talker's current mood. READ-ONLY, read-time exp
        // decay via Affect.Current(now) (nothing ticks it; Plan §4.3
        // decay-secondary). Returns null — so the line is OMITTED — when:
        //   • no mind / Emotion unset (the normal 3C state: nothing sets
        //     Emotion until the 3D appraisal op), OR
        //   • the decayed intensity has fallen below EMOTION_FLOOR ("mood
        //     has passed", Plan §4.3 omit-below-floor).
        // One short line matching the §1 mock's spirit ("此刻心绪：警惕（强度
        // 0.6，…正缓缓平复）"); the trigger clause is 3D appraisal context
        // we don't have in 3C, so we emit the stable "正缓缓平复" tail. Float
        // formatted with InvariantCulture (determinism — no locale comma).
        static string EmotionLine(RuntimeMindState mind, long now)
        {
            if (mind == null || mind.Emotion == null) return null;   // unset → omit
            float cur = mind.Emotion.Current(now);
            if (cur < AITavernConstants.EMOTION_FLOOR) return null;   // mood has passed → omit

            string label = mind.Emotion.Label;
            string intensity = cur.ToString("0.0", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(label))
                return "此刻心绪：（强度 " + intensity + "，正缓缓平复）";
            return "此刻心绪：" + label.Trim() + "（强度 " + intensity + "，正缓缓平复）";
        }

        // §5.4 emotion as a STANDALONE 【眼前局势 — 短期记忆】 block for the
        // Leave profile (Plan §6.1 puts §5.4 in the lean farewell list). It
        // carries ONLY 此刻心绪 — deliberately NO §5.1/5.2/5.3 (Full-only per
        // T3B.3 / §6.1). Returns null (whole block omitted, no bare header)
        // when there is no emotion line — the 3C norm, so Leave stays
        // §2-only until 3D appraisal first sets Emotion. Read-only.
        static string EmotionOnlyBlock(Agent talker, AITavernManager mgr, long now)
        {
            if (talker == null || mgr == null || mgr.Minds == null) return null;
            RuntimeMindState mind;
            if (!mgr.Minds.TryGetValue(talker.PlayerId, out mind) || mind == null)
                return null;
            string emotion = EmotionLine(mind, now);
            if (emotion == null) return null;   // no mood → omit the whole block
            return "【眼前局势 — 短期记忆】\n" + emotion;
        }

        // ---- §5.5.1 affection (decaying, SECT_SHORTTERM_BUDGET) ----

        // §5.5 per-target area, opening with §5.5.1 当下好恶: the talker's
        // CURRENT affect toward THIS talkee, decaying at read-time toward the
        // CANON relationship baseline (NOT 0) via Affect.Current(now) (Plan
        // §4.4). Full-profile ONLY (Plan §6.1 lean Leave = §2+§5.4+§5.5.3;
        // §5.5.1 is not in that list). READ-ONLY: TryGetValue on the talker's
        // mind + Targets, NEVER GetOrCreateMind / no writes. Returns null —
        // whole block omitted, NO bare `·对 X·` header — when there is no
        // mind / no Targets / no entry for this talkee / no Affection (the
        // 3C norm until 3D appraisal seeds affection). Signed value to 2dp
        // with InvariantCulture (determinism). Matches the §1 mock shape
        // ("·对 欧阳克·" / "当下好恶：…（…，向长期基线缓回）").
        static string AffectionBlock(Agent talker, Agent talkee, AITavernManager mgr, long now)
        {
            if (talker == null || talkee == null || mgr == null || mgr.Minds == null) return null;

            RuntimeMindState mind;
            if (!mgr.Minds.TryGetValue(talker.PlayerId, out mind) || mind == null) return null;
            if (mind.Targets == null) return null;

            // Key on talkee.PlayerId — the SAME GameId the FSM (T3C.2) seeds
            // target entries with. Read-only lookup, never create.
            TargetState ts;
            if (!mind.Targets.TryGetValue(talkee.PlayerId, out ts) || ts == null) return null;
            if (ts.Affection == null) return null;   // no affect for this pair → omit

            float cur = ts.Affection.Current(now);
            // Signed (e.g. "-0.55"); "+" not prefixed on positives to match
            // the §1 mock's bare/negative form. InvariantCulture → "."/"-".
            string val = cur.ToString("0.00", CultureInfo.InvariantCulture);

            // Talkee display name: prefer the authored Bio name, else the id.
            var talkeeBio = talkee.Bio;
            string name = talkeeBio != null && !string.IsNullOrWhiteSpace(talkeeBio.BioName)
                ? talkeeBio.BioName.Trim()
                : talkee.PlayerId.Value;

            var sb = new StringBuilder();
            sb.Append("·对 ").Append(name).Append('·');
            string label = ts.Affection.Label;
            if (string.IsNullOrWhiteSpace(label))
                sb.Append("\n当下好恶：").Append(val).Append("（向长期基线缓回）");
            else
                sb.Append("\n当下好恶：").Append(val)
                  .Append('（').Append(label.Trim()).Append("，向长期基线缓回）");

            // 3D: §5.5.2 reflection summary + §5.5.3 episodic ring append
            //     under THIS ·对 X· header (TargetState.ReflectionSummary /
            //     Ring — declared in 3D; need TurnRecord + MEMORY_RING_CAP).
            //     §5.0 global reflection slots BEFORE this block in Build.

            return sb.ToString();
        }
    }
}
