using System;
using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 008 (T-08) owns this half of <see cref="MatchSim"/>: hero abilities and status
    /// effects. Requirements R-31, R-32; graded by fixtures G-018, G-019.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    ///
    /// Two entry points, deliberately: <see cref="CastAbility"/> is the *gate* (is this slot
    /// unlocked, is it off cooldown, which ability does this class bind to it) and
    /// <see cref="ApplyAbility"/> is one ability's *effect*. G-018 calls the effect directly with
    /// a caster that is not in the match state at all, so the gate cannot live inside it — and,
    /// because G-018 pins the effect's deltas exactly, no cooldown bookkeeping may leak into it
    /// either. Cooldowns are therefore written in the gate and nowhere else.
    /// </summary>
    public sealed partial class MatchSim
    {
        // ---- status effect identities ------------------------------------------------------------

        /// <summary>R-31 / G-018 — fixture-locked spelling of the Rancher's slow.</summary>
        private const string LassoSlowStatus = "lasso_slow";

        /// <summary>R-31 — the Sawbones' timed damage reduction, riding on the hero itself.</summary>
        private const string BulwarkStatus = "bulwark";

        // ---- class passives (R-31) ---------------------------------------------------------------

        /// <summary>R-31 Gunslinger passive: every 4th basic crits.</summary>
        private const int GunslingerCritEveryNthBasic = 4;

        /// <summary>R-31 Gunslinger passive: a crit is double damage.</summary>
        private const double GunslingerCritMultiplier = 2.0;

        /// <summary>R-31 Rancher passive: a spread basic carries to a second monster.</summary>
        private const int RancherBasicAttackTargets = 2;

        /// <summary>Every other class's basic stops at the first monster on the line (R-26/R-36).</summary>
        private const int DefaultBasicAttackTargets = 1;

        /// <summary>
        /// R-32 ceiling on a rank-scaled damage reduction. Bulwark at 0.6 climbs with rank like
        /// every other ability number, and without a ceiling a high enough rank or a heavy-handed
        /// config would make a hero immune (or, past 1.0, heal it) — neither is a state the PRD
        /// describes, and both are unrecoverable for the monsters.
        /// </summary>
        private const double MaxTimedDamageReduction = 0.9;

        // ---- rejection reasons (R-31 unlock, R-32 cooldown) ---------------------------------------
        //
        // The client draws "you never learned this" and "it is not ready yet" differently, so they
        // are distinct strings. The exact wording is nobody's contract — only the distinction is.

        private const string RejectionUnknownCaster = "unknown_caster";

        private const string RejectionUnknownSlot = "invalid_slot";

        private const string RejectionCasterDead = "caster_dead";

        private const string RejectionAbilityLocked = "ability_locked";

        private const string RejectionAbilityCooling = "ability_cooling";

        /// <summary>
        /// The profile read match start makes (R-43). Named here rather than reused from T-09's
        /// save constant because the two operations are different verbs on the same service.
        /// </summary>
        private const string LoadOperation = "load";

        /// <summary>
        /// R-31 Gunslinger passive — basics fired, per attacker id.
        ///
        /// Keyed by id rather than held on <see cref="Hero"/> because the attacker need not be in
        /// <see cref="MatchState.Heroes"/> at all: G-030 resolves a shot from "hero_gun", which
        /// that fixture never declares as a hero. Per attacker rather than per sim so two
        /// Gunslingers in one lobby each keep their own rhythm — a shared counter would make the
        /// second player's first shot crit off the first player's three.
        /// </summary>
        private readonly Dictionary<string, int> _basicAttacksFired =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// R-31 / B-011. The Rancher's lasso effect, applied directly.
        ///
        /// This is the raw effect, not a cast: it performs no unlock or cooldown check, because
        /// G-018 hands it a caster that is not in the match state and pins its result and its two
        /// deltas exactly. Anything a gate would add here — a cooldown write, an `accepted` flag —
        /// would be an extra delta this fixture fails on. Pressing Q goes through
        /// <see cref="CastAbility"/>, which routes back into the same effect helper.
        /// </summary>
        public AbilityResult ApplyAbility(AbilityCastRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Ability != AbilityName.Lasso)
            {
                throw new ArgumentException(
                    "ApplyAbility resolves the lasso effect only; '" + request.Ability
                    + "' is a gated cast and goes through CastAbility (R-31)", nameof(request));
            }

            var result = new AbilityResult { TargetId = request.TargetId };

            // R-16 / R-33: a corpse, or a monster that has already left the world, is not a valid
            // target. A whiff resolves cleanly and replicates nothing rather than throwing.
            var target = LivingMonster(request.TargetId);
            if (target == null)
            {
                return Finish(result);
            }

            result.SlowExpiresAt = ApplyLassoSlow(target);
            result.SpeedAfter = target.CurrentSpeed;
            return Finish(result);
        }

        /// <summary>
        /// R-31 / B-011. Expire every status effect whose time is up, on monsters and heroes alike.
        ///
        /// The deadline is inclusive: an effect ends AT its `expires_at`, which is what G-019 pins
        /// and what its `defends_against` names strict greater-than as the bug for. R-33's respawn
        /// tick reads its deadline the same way, so the whole sim agrees on one convention — and
        /// since the host replicates this (R-51), a strict comparison here would put every client
        /// one tick out of step with the host.
        /// </summary>
        public StatusTickResult TickStatusEffects()
        {
            BeginCommand();

            var now = _clock.ElapsedSeconds;
            var result = new StatusTickResult();

            foreach (var monster in State.Monsters.Values)
            {
                foreach (var expired in ExpireDueEffects(monster.Id, monster.StatusEffects, now))
                {
                    if (expired == LassoSlowStatus)
                    {
                        // G-019: expiry restores BASE speed, never "un-multiplies" the current
                        // one — that is what keeps a slow from leaving a permanent residue.
                        var speedBefore = monster.CurrentSpeed;
                        monster.CurrentSpeed = monster.BaseSpeed;
                        RecordChange(monster.Id, "current_speed", speedBefore, monster.CurrentSpeed);
                    }

                    result.Expired.Add(new ExpiredStatus { TargetId = monster.Id, Status = expired });
                }
            }

            foreach (var hero in State.Heroes.Values)
            {
                // Nothing to restore on a hero: Bulwark is read off the effect list at damage time
                // (see AfterTimedDamageReduction), so removing the effect *is* the expiry.
                foreach (var expired in ExpireDueEffects(hero.Id, hero.StatusEffects, now))
                {
                    result.Expired.Add(new ExpiredStatus { TargetId = hero.Id, Status = expired });
                }
            }

            return Finish(result);
        }

        /// <summary>
        /// R-31, R-32 / R-34. A hero pressed Q or E: resolve the ability its class binds to that
        /// slot, at the rank the hero carries, if the slot is unlocked and off cooldown.
        /// Cooldowns are the only cast limit — heroes have no mana (R-34).
        ///
        /// A refusal is inert by construction: every rejection path returns before anything is
        /// written, so a refused cast damages nothing, applies no status, replicates no delta and —
        /// critically for R-32 — does not restart the timer it was refused by. Spamming the key
        /// brings the ability back no later than waiting quietly would.
        /// </summary>
        public AbilityCastOutcome CastAbility(HeroAbilityRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var outcome = new AbilityCastOutcome
            {
                CasterId = request.CasterId,
                Slot = request.Slot,
            };

            if (request.CasterId == null || !State.Heroes.TryGetValue(request.CasterId, out var caster))
            {
                return Refuse(outcome, RejectionUnknownCaster);
            }

            // A slot nobody has is a malformed command, not a free cast: refused before any kit is
            // read, so an unknown key can never resolve as Q by accident.
            if (request.Slot != AbilitySlot.Q && request.Slot != AbilitySlot.E)
            {
                return Refuse(outcome, RejectionUnknownSlot);
            }

            // R-33: dead heroes spectate. Checked before the cooldown is written so a corpse
            // pressing Q does not burn the timer it would come back with.
            if (!caster.Alive)
            {
                return Refuse(outcome, RejectionCasterDead);
            }

            var kit = _config.HeroKits.KitFor(caster.HeroClass);
            var isQ = request.Slot == AbilitySlot.Q;
            var spec = isQ ? kit.Q : kit.E;
            var cooldownSeconds = isQ ? kit.QCooldownSeconds : kit.ECooldownSeconds;

            outcome.Ability = spec.Name;
            outcome.Rank = EffectiveRank(caster, request.Slot);

            // R-31: rank 0 is "never unlocked". A fresh account is basic-attack only (R-44).
            if (outcome.Rank <= 0)
            {
                return Refuse(outcome, RejectionAbilityLocked);
            }

            var now = _clock.ElapsedSeconds;
            if (caster.CooldownReadyAt.TryGetValue(request.Slot, out var readyAt))
            {
                // R-32: a refusal reports the deadline that is already running, never a fresh one.
                outcome.CooldownReadyAt = readyAt;
                if (now < readyAt)
                {
                    return Refuse(outcome, RejectionAbilityCooling);
                }
            }

            outcome.Accepted = true;
            outcome.CooldownReadyAt = now + cooldownSeconds;

            // R-32: per hero and per slot. Not a replicated delta — the client is told the deadline
            // through the cast's own result, and G-018 pins the effect's deltas to exactly the two
            // the world moved.
            caster.CooldownReadyAt[request.Slot] = outcome.CooldownReadyAt;

            ResolveAbilityEffect(caster, spec, RankMultiplier(spec, outcome.Rank), request, outcome);
            return Finish(outcome);
        }

        /// <summary>
        /// R-31 / R-43. At match start every hero adopts the ability allocations saved on its
        /// player's account profile, so a veteran begins with previously unlocked abilities and a
        /// fresh account begins basic-attack-only (R-44 makes an unknown callsign a fresh account
        /// rather than an error).
        ///
        /// Void return and no arguments, the same seam shape as the other match-loop operations
        /// here: no fixture grades match start, so there is no result shape to honour, and the
        /// allocations replicate through LastObservation's state changes like any other delta.
        /// </summary>
        public void ApplySavedAbilityAllocations()
        {
            BeginCommand();

            foreach (var hero in State.Heroes.Values)
            {
                if (hero.AccountId == null)
                {
                    // A hero with no account is a bot or an editor-time placeholder: it keeps the
                    // ranks it was constructed with rather than being reset to a fresh account's.
                    continue;
                }

                var profile = _profileStore.Load(hero.AccountId);
                RecordExternalCall(ProfileStoreService, LoadOperation, new Dictionary<string, object>
                {
                    { "account_id", hero.AccountId },
                });

                foreach (var slot in new[] { AbilitySlot.Q, AbilitySlot.E })
                {
                    var saved = profile.Abilities.TryGetValue(slot, out var rank) ? rank : 0;
                    var before = hero.Abilities.TryGetValue(slot, out var current) ? current : 0;

                    hero.Abilities[slot] = saved;
                    RecordChange(hero.Id, "abilities." + slot, before, saved);
                }
            }
        }

        // ---- the gate's helpers ------------------------------------------------------------------

        /// <summary>
        /// Records the refusal on the outcome and finishes the command. Nothing has been written to
        /// the world by the time any caller reaches here, which is what makes "a refused cast
        /// changes nothing" structural rather than something each branch has to remember.
        /// </summary>
        private AbilityCastOutcome Refuse(AbilityCastOutcome outcome, string reason)
        {
            outcome.Accepted = false;
            outcome.RejectionReason = reason;
            return Finish(outcome);
        }

        /// <summary>
        /// R-32 / DEC-014. The rank a cast actually resolves at, capped at
        /// <see cref="SimConfig.MaxAbilityRank"/>. T-09 refuses the *spend* past the cap; this is
        /// the ceiling in effect terms, so a migrated or tampered profile carrying rank 5 casts as
        /// rank 3 rather than as something no balance pass ever saw.
        /// </summary>
        private int EffectiveRank(Hero hero, string slot)
        {
            var rank = hero.Abilities.TryGetValue(slot, out var saved) ? saved : 0;
            return Math.Min(rank, _config.MaxAbilityRank);
        }

        /// <summary>
        /// R-32 — "rank-ups improve numbers ~+25%/rank". Rank 1 is the unlocked baseline, so the
        /// curve is applied to the ranks *above* it. The per-rank fraction is config
        /// (<see cref="AbilitySpec.RankScalingPerRank"/>), never a constant here: rank scaling is
        /// balance data, and a designer retuning the curve must not need a code change.
        /// </summary>
        private static double RankMultiplier(AbilitySpec spec, int rank)
        {
            return 1.0 + (spec.RankScalingPerRank * Math.Max(0, rank - 1));
        }

        // ---- the six abilities (R-31) --------------------------------------------------------------

        private void ResolveAbilityEffect(
            Hero caster,
            AbilitySpec spec,
            double rankMultiplier,
            HeroAbilityRequest request,
            AbilityCastOutcome outcome)
        {
            switch (spec.Name)
            {
                case AbilityName.FanTheHammer:
                    ResolveBurst(spec, rankMultiplier, request, outcome);
                    break;

                case AbilityName.Deadeye:
                    ResolvePiercingLine(spec, rankMultiplier, request, outcome);
                    break;

                case AbilityName.Lasso:
                    ResolveLasso(request, outcome);
                    break;

                case AbilityName.Stampede:
                    ResolveDash(caster, spec, rankMultiplier, request, outcome);
                    break;

                case AbilityName.Whirl:
                    ResolveAreaSpin(caster, spec, rankMultiplier, outcome);
                    break;

                case AbilityName.Bulwark:
                    ResolveGuard(caster, spec, rankMultiplier, outcome);
                    break;

                default:
                    throw new ArgumentException(
                        "hero class '" + caster.HeroClass + "' binds slot '" + outcome.Slot
                        + "' to ability '" + spec.Name + "', which no effect implements (R-31)",
                        nameof(spec));
            }
        }

        /// <summary>
        /// R-31 Gunslinger Q — Fan the Hammer. "6-shot burst" is a damage rule, not an animation:
        /// one cast lands <see cref="AbilitySpec.Hits"/> shots' worth of damage on one monster.
        /// The burst resolves as a single application because it is one trigger-pull as far as the
        /// world is concerned — six hit flashes are the shell's problem, and six separate deltas
        /// would be six times the replication traffic for one decision.
        /// </summary>
        private void ResolveBurst(
            AbilitySpec spec, double rankMultiplier, HeroAbilityRequest request, AbilityCastOutcome outcome)
        {
            var target = CastTarget(request);
            if (target == null)
            {
                return;
            }

            DamageMonster(target, HitDamage(spec, rankMultiplier) * HitsOf(spec), outcome);
        }

        /// <summary>
        /// R-31 Gunslinger E — Deadeye. A piercing line: every monster the shot crossed takes it,
        /// where a basic stops at the first. R-26/R-36 is enforced the same way it is for basics —
        /// an allowlist on <see cref="LineEntity.Kind"/>, not a denylist of things to skip — which
        /// is what keeps a new piercing ability from being where friendly fire creeps back in.
        /// </summary>
        private void ResolvePiercingLine(
            AbilitySpec spec, double rankMultiplier, HeroAbilityRequest request, AbilityCastOutcome outcome)
        {
            foreach (var monster in LivingMonstersOnLine(request.EntitiesOnLine, int.MaxValue))
            {
                DamageMonster(monster, HitDamage(spec, rankMultiplier) * HitsOf(spec), outcome);
            }
        }

        /// <summary>
        /// R-31 Rancher Q — the lasso, reached through the gate. Routes into the same effect helper
        /// G-018 grades through <see cref="ApplyAbility"/>, so the two entry points can never drift
        /// into two different slows.
        /// </summary>
        private void ResolveLasso(HeroAbilityRequest request, AbilityCastOutcome outcome)
        {
            var target = CastTarget(request);
            if (target == null)
            {
                return;
            }

            outcome.EffectExpiresAt = ApplyLassoSlow(target);
            MarkAffected(outcome, target.Id);
        }

        /// <summary>
        /// R-31 Rancher E — Stampede. The Rancher dashes along its aim and every monster the lane
        /// crossed is shoved the same distance further along it. <see cref="AbilitySpec.Radius"/>
        /// is that distance for both halves: reach is one number for this ability, and splitting it
        /// would be two tunables that always want to move together.
        ///
        /// Only monsters are displaced or damaged (R-26/R-36); an ally standing in the lane is
        /// passed through untouched, exactly as it is by a basic.
        /// </summary>
        private void ResolveDash(
            Hero caster,
            AbilitySpec spec,
            double rankMultiplier,
            HeroAbilityRequest request,
            AbilityCastOutcome outcome)
        {
            var direction = Normalized(request.AimDirection);
            var distance = spec.Radius;

            foreach (var monster in LivingMonstersOnLine(request.EntitiesOnLine, int.MaxValue))
            {
                var posBefore = monster.Pos;
                monster.Pos = Displaced(posBefore, direction, distance);
                RecordChange(monster.Id, "pos", posBefore, monster.Pos);

                MarkAffected(outcome, monster.Id);
                DamageMonster(monster, HitDamage(spec, rankMultiplier) * HitsOf(spec), outcome);
            }

            // The dash itself. Recorded like any other position delta; the shell animates the
            // travel, the sim only owns where the hero ends up.
            var casterBefore = caster.Pos;
            caster.Pos = Displaced(casterBefore, direction, distance);
            RecordChange(caster.Id, "pos", casterBefore, caster.Pos);
        }

        /// <summary>
        /// R-31 Sawbones Q — Whirl. An AoE spin centred on the caster: every monster within
        /// <see cref="AbilitySpec.Radius"/> takes the hit, in every direction, and nothing outside
        /// it does. R-26/R-36 falls out of the shape — the sweep enumerates
        /// <see cref="MatchState.Monsters"/> and never sees a hero or a placeable at all.
        /// </summary>
        private void ResolveAreaSpin(
            Hero caster, AbilitySpec spec, double rankMultiplier, AbilityCastOutcome outcome)
        {
            foreach (var monster in State.Monsters.Values.ToList())
            {
                if (!monster.Alive)
                {
                    continue;
                }

                // Inclusive: a monster exactly on the rim is inside the spin. Same convention as
                // every other reach comparison here (R-16's targeting, the turret's range).
                if (caster.Pos.DistanceTo(monster.Pos) > spec.Radius)
                {
                    continue;
                }

                DamageMonster(monster, HitDamage(spec, rankMultiplier) * HitsOf(spec), outcome);
            }
        }

        /// <summary>
        /// R-31 Sawbones E — Bulwark. A timed damage reduction riding on the caster, expired by
        /// <see cref="TickStatusEffects"/> on the same inclusive deadline as the lasso.
        ///
        /// The resolved magnitude is frozen onto the effect rather than looked up again at damage
        /// time, so a rank-up (or a live config edit) part-way through cannot retune a guard that
        /// is already running. Recasting refreshes rather than stacking: two overlapping copies of
        /// the same reduction is not a mechanic the PRD describes.
        /// </summary>
        private void ResolveGuard(
            Hero caster, AbilitySpec spec, double rankMultiplier, AbilityCastOutcome outcome)
        {
            var expiresAt = _clock.ElapsedSeconds + spec.DurationSeconds;
            var magnitude = Math.Min(MaxTimedDamageReduction, spec.Magnitude * rankMultiplier);
            var before = SnapshotEffects(caster.StatusEffects);

            caster.StatusEffects.RemoveAll(effect => effect.Type == BulwarkStatus);
            caster.StatusEffects.Add(new StatusEffect(BulwarkStatus, expiresAt, magnitude));

            RecordChange(caster.Id, "status_effects", before, SnapshotEffects(caster.StatusEffects));
            Emit("status_applied", new Dictionary<string, object>
            {
                { "status", BulwarkStatus },
                { "target_id", caster.Id },
            });

            outcome.EffectExpiresAt = expiresAt;
        }

        // ---- the lasso effect, shared by both entry points -----------------------------------------

        /// <summary>
        /// R-31 / DEC-008 / G-018. Multiplies the monster's move speed and records when the slow
        /// ends. Returns that deadline.
        ///
        /// A second lasso on an already-slowed monster refreshes the timer and leaves the speed
        /// alone: G-018's `defends_against` names stacking on an already-slowed monster as the bug,
        /// and compounding 5.0 -> 2.5 -> 1.25 would let two Ranchers stop a wave dead. Base speed
        /// is untouched throughout — it is what expiry restores.
        /// </summary>
        private double ApplyLassoSlow(Monster target)
        {
            var expiresAt = _clock.ElapsedSeconds + _config.LassoDurationSeconds;
            var effectsBefore = SnapshotEffects(target.StatusEffects);
            var alreadySlowed = target.StatusEffects.FindIndex(e => e.Type == LassoSlowStatus);

            if (alreadySlowed >= 0)
            {
                target.StatusEffects[alreadySlowed] = new StatusEffect(LassoSlowStatus, expiresAt);
            }
            else
            {
                var speedBefore = target.CurrentSpeed;
                target.CurrentSpeed = speedBefore * _config.LassoSlowMultiplier;
                target.StatusEffects.Add(new StatusEffect(LassoSlowStatus, expiresAt));

                RecordChange(target.Id, "current_speed", speedBefore, target.CurrentSpeed);
            }

            RecordChange(target.Id, "status_effects", effectsBefore, SnapshotEffects(target.StatusEffects));
            Emit("status_applied", new Dictionary<string, object>
            {
                { "status", LassoSlowStatus },
                { "target_id", target.Id },
            });

            return expiresAt;
        }

        // ---- status effect plumbing ----------------------------------------------------------------

        /// <summary>
        /// The replicated shape of an effect list. G-018 and G-019 both carry a `status_effects`
        /// delta whose from/to are arrays of `{type, expires_at}`, so both sides of every such
        /// change are snapshotted through one place.
        /// </summary>
        private static List<IDictionary<string, object>> SnapshotEffects(IEnumerable<StatusEffect> effects)
        {
            return effects.Select(effect => effect.ToFields()).ToList();
        }

        /// <summary>
        /// Removes every effect whose deadline the clock has reached, records the one
        /// `status_effects` delta that covers them and emits one `status_expired` event each.
        /// Returns the types removed, so the caller can undo whatever each one was doing.
        /// </summary>
        private List<string> ExpireDueEffects(string entityId, List<StatusEffect> effects, double now)
        {
            var due = effects.Where(effect => now >= effect.ExpiresAt).ToList();
            if (due.Count == 0)
            {
                return new List<string>();
            }

            var before = SnapshotEffects(effects);
            foreach (var effect in due)
            {
                effects.Remove(effect);
            }

            RecordChange(entityId, "status_effects", before, SnapshotEffects(effects));
            foreach (var effect in due)
            {
                Emit("status_expired", new Dictionary<string, object>
                {
                    { "status", effect.Type },
                    { "target_id", entityId },
                });
            }

            return due.Select(effect => effect.Type).ToList();
        }

        /// <summary>
        /// R-31 / DEC-RUN-7. Incoming damage after every timed reduction the hero is carrying.
        ///
        /// Bulwark stacks MULTIPLICATIVELY with the Sawbones' flat class passive, so a guarded
        /// Sawbones takes 0.7 * 0.4 = 0.28 of the raw hit. DEC-016 models combat on League of
        /// Legends, where damage reduction composes that way, which is the reading most consistent
        /// with the spec's own stated model — and, unlike an additive one, it cannot reach immunity
        /// however many sources are added later.
        ///
        /// Each reduction floors through <see cref="FloorEpsilon"/> for the reason DEC-RUN-2 gives:
        /// hero HP must never land on a fraction, and IEEE doubles would otherwise report 62 where
        /// exact arithmetic says 63.
        /// </summary>
        private double AfterTimedDamageReduction(Hero hero, double damage)
        {
            var reduced = damage;

            foreach (var effect in hero.StatusEffects)
            {
                if (effect.Magnitude <= 0.0)
                {
                    continue;
                }

                var magnitude = Math.Min(MaxTimedDamageReduction, effect.Magnitude);
                reduced = Math.Floor((reduced * (1.0 - magnitude)) + FloorEpsilon);
            }

            return reduced;
        }

        // ---- basic attack passives (R-31), hooked from ResolveHeroAttack ----------------------------

        /// <summary>
        /// R-31 Gunslinger passive — every 4th basic crits for double.
        ///
        /// Counted per trigger-pull rather than per hit: a basic that crossed only friendlies is
        /// still a basic, and a shooter whose rhythm silently reset on a miss would be a passive
        /// the player cannot count on. Class-conditional — nobody else's basics crit.
        /// </summary>
        private double BasicAttackDamage(string attackerId, string heroClass, double rawDamage)
        {
            if (heroClass != HeroClass.Gunslinger)
            {
                return rawDamage;
            }

            var key = attackerId ?? string.Empty;
            var fired = (_basicAttacksFired.TryGetValue(key, out var count) ? count : 0) + 1;
            _basicAttacksFired[key] = fired;

            return fired % GunslingerCritEveryNthBasic == 0
                ? rawDamage * GunslingerCritMultiplier
                : rawDamage;
        }

        /// <summary>
        /// R-31 Rancher passive — a spread basic carries to a second monster on the same line.
        ///
        /// The pair is the first two *monsters*, not the first two entities: an ally standing
        /// second in the line is not one of the two (R-26/R-36). <paramref name="alreadyHit"/> is
        /// the primary target <see cref="ResolveHeroAttack"/> already resolved, which stays the one
        /// the result reports — G-030 pins that result to four fields, so a second target rides the
        /// delta and event streams rather than growing the shape.
        /// </summary>
        private void ResolveSpreadTargets(HeroAttackRequest request, Monster alreadyHit, double damage)
        {
            var remaining = BasicAttackTargetsFor(request.AttackerClass) - 1;
            if (remaining <= 0 || request.EntitiesOnLine == null)
            {
                return;
            }

            foreach (var entity in request.EntitiesOnLine)
            {
                if (remaining <= 0)
                {
                    return;
                }

                if (entity == null || entity.Kind != MonsterLineKind)
                {
                    continue;
                }

                if (!State.Monsters.TryGetValue(entity.Id, out var monster)
                    || ReferenceEquals(monster, alreadyHit))
                {
                    continue;
                }

                var hpBefore = monster.Hp;
                monster.Hp = Math.Max(0.0, hpBefore - damage);

                RecordChange(monster.Id, "hp", hpBefore, monster.Hp);
                Emit("monster_damaged", new Dictionary<string, object>
                {
                    { "monster_id", monster.Id },
                    { "amount", damage },
                    { "by", request.AttackerId },
                });

                remaining -= 1;
            }
        }

        /// <summary>R-31 — how many monsters one basic attack of this class may hit.</summary>
        private static int BasicAttackTargetsFor(string heroClass)
        {
            return heroClass == HeroClass.Rancher
                ? RancherBasicAttackTargets
                : DefaultBasicAttackTargets;
        }

        // ---- targeting and geometry helpers ---------------------------------------------------------

        /// <summary>
        /// The monster a single-target cast resolves on: the named target when it is alive and
        /// present, otherwise the nearest monster the aim line crossed. Returns null for a cast
        /// aimed at a corpse or at nothing, which resolves as a whiff rather than an error — the
        /// PRD says nothing about refusing such a cast, and corrupting the world over it would be
        /// far worse than letting it miss (R-16/R-33).
        /// </summary>
        private Monster CastTarget(HeroAbilityRequest request)
        {
            return LivingMonster(request.TargetId)
                   ?? LivingMonstersOnLine(request.EntitiesOnLine, 1).FirstOrDefault();
        }

        /// <summary>The monster with this id when it exists and is alive; null otherwise.</summary>
        private Monster LivingMonster(string monsterId)
        {
            if (monsterId == null || !State.Monsters.TryGetValue(monsterId, out var monster))
            {
                return null;
            }

            return monster.Alive ? monster : null;
        }

        /// <summary>
        /// R-26 / R-36. The living monsters an aim line crossed, nearest-first, capped at
        /// <paramref name="maxTargets"/>. Kind is an allowlist exactly as it is for basics: heroes,
        /// barricades and every other placeable pass through untouched no matter where they sit.
        /// </summary>
        private List<Monster> LivingMonstersOnLine(List<LineEntity> entitiesOnLine, int maxTargets)
        {
            var monsters = new List<Monster>();
            if (entitiesOnLine == null)
            {
                return monsters;
            }

            foreach (var entity in entitiesOnLine)
            {
                if (monsters.Count >= maxTargets)
                {
                    break;
                }

                if (entity == null || entity.Kind != MonsterLineKind)
                {
                    continue;
                }

                var monster = LivingMonster(entity.Id);
                if (monster != null)
                {
                    monsters.Add(monster);
                }
            }

            return monsters;
        }

        /// <summary>Damage one hit of this ability does at this rank.</summary>
        private static double HitDamage(AbilitySpec spec, double rankMultiplier)
        {
            return spec.Damage * rankMultiplier;
        }

        /// <summary>
        /// Hits one cast resolves. An unset <see cref="AbilitySpec.Hits"/> means one — a single-hit
        /// ability should not have to declare the number 1 to avoid dealing nothing.
        /// </summary>
        private static int HitsOf(AbilitySpec spec)
        {
            return spec.Hits > 0 ? spec.Hits : 1;
        }

        /// <summary>Unit vector along the aim, or zero when the shell reported no direction.</summary>
        private static Vec2 Normalized(Vec2 direction)
        {
            var length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
            return length <= 0.0 ? new Vec2(0, 0) : new Vec2(direction.X / length, direction.Y / length);
        }

        private static Vec2 Displaced(Vec2 from, Vec2 unitDirection, double distance)
        {
            return new Vec2(from.X + (unitDirection.X * distance), from.Y + (unitDirection.Y * distance));
        }

        /// <summary>
        /// Applies ability damage to one monster and books it on the outcome. Mirrors
        /// <see cref="ResolveHeroAttack"/>'s delta and event shapes so a client renders an ability
        /// hit and a basic hit through the same path.
        /// </summary>
        private void DamageMonster(Monster target, double damage, AbilityCastOutcome outcome)
        {
            MarkAffected(outcome, target.Id);
            if (damage <= 0.0)
            {
                return;
            }

            var hpBefore = target.Hp;
            target.Hp = Math.Max(0.0, hpBefore - damage);

            RecordChange(target.Id, "hp", hpBefore, target.Hp);
            Emit("monster_damaged", new Dictionary<string, object>
            {
                { "monster_id", target.Id },
                { "amount", damage },
                { "by", outcome.CasterId },
            });

            outcome.TotalDamage += damage;

            // Reaching 0 HP does not kill here, exactly as with a basic: R-40's kill accounting
            // (bounty, XP, wave progress) runs through RecordMonsterKill.
        }

        /// <summary>
        /// Books a monster as affected. Deduplicated because an ability can both displace and
        /// damage the same target (Stampede), and the outcome names each monster once.
        /// </summary>
        private static void MarkAffected(AbilityCastOutcome outcome, string monsterId)
        {
            if (!outcome.MonstersAffected.Contains(monsterId))
            {
                outcome.MonstersAffected.Add(monsterId);
            }
        }
    }
}
