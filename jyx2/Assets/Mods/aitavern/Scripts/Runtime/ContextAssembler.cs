// Phase 3 (Plan §6) — ContextAssembler: the layered human-like memory
// context builder, and (post-3D coexistence flip, Plan §8) the SOLE
// continuity/transcript source — the Phase 2 BuildPriorMemoryBlock path
// has been removed. Emits, in order: §1 World Codex, §2 Talker Bio,
// §3 Talkee first-impression surface, §4 long-term knowledge (the
// talker's Dossier view of the talkee), §5 short-term (situation / task /
// surroundings / emotion), §5.0 global reflection, and §5.5 per-target
// block (current affect + settled impression + the episodic ring, where
// the just-ended turn surfaces as ［刚刚结束的对话］). Deterministic,
// synchronous, ZERO Grok calls (all Grok work is offline in the lore
// pipeline — T3A.6 — or in the 3D reflection op).
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

                // §5.0 跨人反思 — the talker's cross-person global
                // reflection. Plan §5.3.1 / §1 mock: rendered ABOVE the
                // §5.5 per-target area and AFTER §5.1-5.4 short-term.
                // Full-profile ONLY: Plan §6.1's lean Leave list is
                // "§2+§5.4+§5.5.3" — §5.0 is explicitly excluded there.
                // Read-only; omitted entirely when no mind or
                // GlobalReflection is blank (Plan §1 empty-omission).
                AppendSection(sb, GlobalReflectionBlock(talker, mgr), AITavernConstants.SECT_SHORTTERM_BUDGET);

                // §5.5 per-target area: §5.5.1 当下好恶 + §5.5.2 往来印象
                // （已沉淀）+ §5.5.3 最近交谈 ring, ALL under ONE `·对 X·`
                // header. Full-profile ONLY for §5.5.1/§5.5.2 (Plan §6.1's
                // lean Leave list is "§2+§5.4+§5.5.3" — Leave gets just the
                // §5.5.3 ring, slotted in the Leave branch below). Emitted
                // under its own `·对 X·` header; omitted entirely (no bare
                // header) only when ALL THREE (affection + summary + ring)
                // are empty. Read-only (TryGetValue, never GetOrCreateMind).
                AppendSection(sb, PerTargetBlock(talker, talkee, mgr, now), AITavernConstants.SECT_SHORTTERM_BUDGET);

                // 3D done (§5.0 global reflection rendered above, Full-only;
                //     §5.5.1+§5.5.2+§5.5.3 folded into PerTargetBlock under
                //     one ·对 X· header; §4 ［本局所历］ overlay in
                //     BuildLongTerm). The §5.5.3 ring is now the SOLE
                //     transcript source — ConversationPrompts.BuildContinue/
                //     BuildLeave's AppendTranscript is DELETED (Plan §6.1).
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

                // §5.5.3 episodic ring (Leave profile) — Plan §6.1's lean
                // farewell list is "§2+§5.4+§5.5.3": Leave gets the ring so
                // the goodbye knows "what was just said", but NOT §5.0 /
                // §5.5.1 affection / §5.5.2 summary. Ring-only sub-block
                // under a minimal `·对 X·` header; omitted when ring empty.
                // This is the SOLE transcript source for Leave (the deleted
                // BuildLeave AppendTranscript). Read-only.
                AppendSection(sb, RingOnlyBlock(talker, talkee, mgr), AITavernConstants.SECT_SHORTTERM_BUDGET);

                // 3D done (§5.4 emotion-only above; §5.5.3 ring-only block
                //     rendered above for Leave — no §5.0/§5.5.1/§5.5.2 per
                //     Plan §6.1 lean farewell list).
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
            // (talker, talkee) are also threaded for the §4 runtime overlay
            // below — the canon dossier read is unchanged, by talkerId.
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

                // 3D done: §4 ［本局所历］ runtime overlay (Plan §4 overlay /
                // §5.3). Sourced from the talker's
                // RuntimeMindState.Targets[talkee].ImpressionDelta — the
                // mutable runtime delta layered OVER the immutable canon
                // PersonView.Impression above. READ-ONLY: TryGetValue on
                // mgr.Minds, NEVER GetOrCreateMind / no writes; the
                // CharacterDossier asset is NEVER read or written here. Key:
                // mind by talker.PlayerId, target by talkee.PlayerId — the
                // SAME keying T3C.2/T3D.2 seed with. Emitted AFTER the canon
                // impression/relationship lines for this person; omitted
                // entirely when blank (Plan §1 empty sub-line omission).
                if (mgr.Minds != null && talker != null && talkee != null)
                {
                    RuntimeMindState mind;
                    if (mgr.Minds.TryGetValue(talker.PlayerId, out mind)
                        && mind != null && mind.Targets != null)
                    {
                        TargetState tts;
                        if (mind.Targets.TryGetValue(talkee.PlayerId, out tts)
                            && tts != null
                            && !string.IsNullOrWhiteSpace(tts.ImpressionDelta))
                        {
                            sb.Append("\n  ［本局所历］：").Append(tts.ImpressionDelta.Trim());
                        }
                    }
                }

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

        // ---- §5.0 global cross-person reflection (SECT_SHORTTERM_BUDGET) ----

        // §5.0 跨人反思: the talker's flat cross-person fold (Plan §5.3.1).
        // Rendered ABOVE the §5.5 per-target area (Build, Full-profile ONLY —
        // Plan §6.1's lean Leave list excludes it). READ-ONLY: TryGetValue on
        // mgr.Minds, NEVER GetOrCreateMind / no writes / no Grok (the fold is
        // produced offline-of-render by the T3D.3 conv-end op). Returns null —
        // section omitted — when no mind or GlobalReflection is blank (Plan §1
        // empty-omission). Matches the §1 mock: "此间众人，我之所察（跨人反
        // 思）：" then the reflection prose on the next line.
        static string GlobalReflectionBlock(Agent talker, AITavernManager mgr)
        {
            if (talker == null || mgr == null || mgr.Minds == null) return null;

            RuntimeMindState mind;
            if (!mgr.Minds.TryGetValue(talker.PlayerId, out mind) || mind == null)
                return null;
            if (string.IsNullOrWhiteSpace(mind.GlobalReflection)) return null;

            return "此间众人，我之所察（跨人反思）：\n" + mind.GlobalReflection.Trim();
        }

        // ---- §5.5 per-target area (SECT_SHORTTERM_BUDGET) ----

        // The §5.5 per-target block under ONE `·对 X·` header, folding:
        //   §5.5.1 当下好恶 — the talker's CURRENT affect toward THIS talkee,
        //          decaying at read-time toward the CANON relationship
        //          baseline (NOT 0) via Affect.Current(now) (Plan §4.4);
        //   §5.5.2 往来印象（已沉淀）— the durable per-pair ReflectionSummary
        //          (Plan §5.5.2; backed by Phase 2 CompactedSummary.SummaryText);
        //   §5.5.3 最近交谈 — the episodic ring (anchor + last-9), oldest→
        //          newest, the LAST line tagged ［刚刚结束的对话］ (Plan
        //          §5.5.3 / §1 mock; mirrors the Phase 2 [刚刚结束的对话]
        //          convention). The ring is the SOLE transcript source after
        //          T3D.5 (BuildContinue/BuildLeave AppendTranscript deleted).
        // Full-profile ONLY (Plan §6.1 lean Leave = §2+§5.4+§5.5.3; Leave
        // gets just the §5.5.3 ring, via RingOnlyBlock). READ-ONLY: TryGetValue
        // on the talker's mind + Targets, NEVER GetOrCreateMind / no writes.
        // Returns null — whole block omitted, NO bare `·对 X·` header — ONLY
        // when ALL THREE of §5.5.1 affection / §5.5.2 summary / §5.5.3 ring
        // are empty (so a brand-new pair with only a ring still renders the
        // ·对 X· block with §5.5.3). Signed affection value to 2dp with
        // InvariantCulture (determinism). Matches the §1 mock shape.
        static string PerTargetBlock(Agent talker, Agent talkee, AITavernManager mgr, long now)
        {
            if (talker == null || talkee == null || mgr == null || mgr.Minds == null) return null;

            RuntimeMindState mind;
            if (!mgr.Minds.TryGetValue(talker.PlayerId, out mind) || mind == null) return null;
            if (mind.Targets == null) return null;

            // Key on talkee.PlayerId — the SAME GameId the FSM (T3C.2) and
            // the ring-append (T3D.2) seed target entries with. Read-only
            // lookup, never create.
            TargetState ts;
            if (!mind.Targets.TryGetValue(talkee.PlayerId, out ts) || ts == null) return null;

            bool hasAffection = ts.Affection != null;
            bool hasSummary = !string.IsNullOrWhiteSpace(ts.ReflectionSummary);
            bool hasRing = ts.Ring != null && ts.Ring.Count > 0;

            // ALL THREE empty → omit the whole block (NO bare ·对 X· header).
            if (!hasAffection && !hasSummary && !hasRing) return null;

            var sb = new StringBuilder();
            sb.Append("·对 ").Append(TalkeeName(talkee)).Append('·');

            // §5.5.1 当下好恶 (read-time decayed; omitted if no Affection).
            if (hasAffection)
            {
                float cur = ts.Affection.Current(now);
                // Signed (e.g. "-0.55"); "+" not prefixed on positives to
                // match the §1 mock's bare/negative form. InvariantCulture.
                string val = cur.ToString("0.00", CultureInfo.InvariantCulture);
                string label = ts.Affection.Label;
                if (string.IsNullOrWhiteSpace(label))
                    sb.Append("\n当下好恶：").Append(val).Append("（向长期基线缓回）");
                else
                    sb.Append("\n当下好恶：").Append(val)
                      .Append('（').Append(label.Trim()).Append("，向长期基线缓回）");
            }

            // §5.5.2 往来印象（已沉淀）(omitted if blank).
            if (hasSummary)
                sb.Append("\n往来印象（已沉淀）：").Append(ts.ReflectionSummary.Trim());

            // §5.5.3 最近交谈 ring (omitted if empty).
            if (hasRing)
                AppendRing(sb, ts.Ring, talker, talkee);

            return sb.ToString();
        }

        // §5.5.3 ring-only block for the Leave profile (Plan §6.1's lean
        // farewell = §2+§5.4+§5.5.3): Leave gets the ring so the goodbye
        // knows "what was just said", but NOT §5.0 / §5.5.1 / §5.5.2. The
        // ring is rendered under the SAME minimal `·对 X·` header so the
        // 最近交谈 sub-block reads identically to the Full profile. READ-ONLY
        // (TryGetValue, never GetOrCreateMind). Returns null — whole block
        // omitted, NO bare header — when the ring is empty. This is the SOLE
        // transcript source for Leave (the deleted BuildLeave AppendTranscript).
        static string RingOnlyBlock(Agent talker, Agent talkee, AITavernManager mgr)
        {
            if (talker == null || talkee == null || mgr == null || mgr.Minds == null) return null;

            RuntimeMindState mind;
            if (!mgr.Minds.TryGetValue(talker.PlayerId, out mind) || mind == null) return null;
            if (mind.Targets == null) return null;

            TargetState ts;
            if (!mind.Targets.TryGetValue(talkee.PlayerId, out ts) || ts == null) return null;
            if (ts.Ring == null || ts.Ring.Count == 0) return null;   // ring empty → omit

            var sb = new StringBuilder();
            sb.Append("·对 ").Append(TalkeeName(talkee)).Append('·');
            AppendRing(sb, ts.Ring, talker, talkee);
            return sb.ToString();
        }

        // Render the §5.5.3 最近交谈 ring onto `sb` (oldest→newest). The LAST
        // line is preceded by ［刚刚结束的对话］ on its own line so the model
        // can tell "just said" from older buffered turns (Plan §1 mock +
        // mirrors the Phase 2 BuildPriorMemoryBlock [刚刚结束的对话]
        // convention). Speaker name resolves via the SAME BioName/id logic
        // PerTargetBlock/BuildLongTerm use: a turn spoken by the talkee uses
        // the talkee's display name; any other speaker (the talker's own
        // turns) uses the talker's. Caller guarantees ring is non-empty.
        static void AppendRing(StringBuilder sb, List<TurnRecord> ring, Agent talker, Agent talkee)
        {
            sb.Append("\n最近交谈（最近").Append(ring.Count).Append("轮）：");
            int last = ring.Count - 1;
            for (int i = 0; i < ring.Count; i++)
            {
                var rec = ring[i];
                if (rec == null) continue;
                if (i == last)
                    sb.Append("\n［刚刚结束的对话］");
                sb.Append('\n').Append(SpeakerName(rec.Speaker, talker, talkee))
                  .Append('：').Append(rec.Text);
            }
        }

        // Talkee display name: prefer the authored Bio name, else the id —
        // the SAME resolution AffectionBlock used pre-3D and BuildLongTerm
        // uses for 此人.
        static string TalkeeName(Agent talkee)
        {
            var bio = talkee != null ? talkee.Bio : null;
            return bio != null && !string.IsNullOrWhiteSpace(bio.BioName)
                ? bio.BioName.Trim()
                : (talkee != null ? talkee.PlayerId.Value : "?");
        }

        // Resolve a TurnRecord.Speaker GameId to a display name. 2-party
        // (Phase 1): the speaker is either the talkee or the talker. A turn
        // whose Speaker == talkee.PlayerId uses the talkee's name; everything
        // else (the talker's own turns) uses the talker's name. Same
        // BioName/id fallback as TalkeeName.
        static string SpeakerName(GameId speaker, Agent talker, Agent talkee)
        {
            if (talkee != null && speaker.Equals(talkee.PlayerId))
                return TalkeeName(talkee);
            var bio = talker != null ? talker.Bio : null;
            if (bio != null && !string.IsNullOrWhiteSpace(bio.BioName))
                return bio.BioName.Trim();
            return talker != null ? talker.PlayerId.Value : (speaker.Value ?? "?");
        }
    }
}
