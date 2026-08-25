using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 007 (T-07) owns this half of <see cref="MatchSim"/>: hero damage, death and
    /// respawn, and the no-friendly-fire rule. Requirements R-26, R-33, R-34, R-35, R-36;
    /// graded by fixtures G-020, G-021, G-030.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    ///
    /// R-34 is honoured by omission: nothing here spends a resource pool, because heroes have
    /// none. Cooldowns (ticket 008) are the only cast limit.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// DEC-RUN-2. `floor(damage * 0.7)` is exact arithmetic, but IEEE doubles are not:
        /// `90 * 0.7 == 62.99999999999999`, so a bare Math.Floor would report 62 where the rule
        /// means 63 (sixteen integer damage values under 1000 straddle this way). Nudging by an
        /// epsilon far smaller than any meaningful HP quantum restores agreement with exact
        /// arithmetic while leaving genuinely fractional results alone — 15 * 0.7 = 10.5 still
        /// floors to 10, which is what G-020 pins. The sim is host-authoritative and replicated
        /// (R-51), so an off-by-one here would propagate to every client's HP bar.
        /// </summary>
        private const double FloorEpsilon = 1e-9;

        /// <summary>
        /// R-26 / R-36. The one <see cref="LineEntity.Kind"/> a hero attack is allowed to damage.
        /// The shell labels every entity its raycast crossed; this is the whole allowlist.
        /// </summary>
        private const string MonsterLineKind = "monster";

        /// <summary>
        /// R-35. Sim time the last regen tick was accounted for, so healing accrues once per
        /// elapsed second rather than being recomputed from scratch on every tick.
        /// </summary>
        private double _lastRegenTickAt;

        /// <summary>
        /// R-31, R-33 / B-012, B-013. Something hit a hero.
        ///
        /// Order is contract (G-020's `defends_against`): reduce first, *then* check death. A
        /// Sawbones on 5 HP hit for 100 reports 70 damage taken, not 100 and not the 5 HP the hit
        /// actually consumed — HP clamps at 0 while the reported amount stays the full incoming
        /// (post-reduction) hit.
        /// </summary>
        public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!State.Heroes.TryGetValue(request.TargetId, out var hero))
            {
                throw new ArgumentException(
                    "no hero '" + request.TargetId + "' in the match state", nameof(request));
            }

            var damageTaken = IncomingDamageFor(hero, request.Damage);

            var hpBefore = hero.Hp;
            var hpAfter = Math.Max(0.0, hpBefore - damageTaken);
            hero.Hp = hpAfter;

            // R-35: any hit restarts the out-of-combat countdown. Not a replicated delta — clients
            // derive "in combat" from the damage event, not from a timestamp field.
            hero.LastDamagedAt = _clock.ElapsedSeconds;

            RecordChange(hero.Id, "hp", hpBefore, hpAfter);
            Emit("hero_damaged", new Dictionary<string, object>
            {
                { "hero_id", hero.Id },
                { "amount", damageTaken },
            });

            var result = new HeroDamageResult
            {
                HeroId = hero.Id,
                DamageTaken = damageTaken,
                HpAfter = hpAfter,
                Downed = false,
            };

            // R-33 / DEC-010: 0 HP kills instantly — there is no downed-but-revivable state.
            if (hpAfter <= 0.0 && hero.Alive)
            {
                // Read off the injected clock, never a wall clock: this is what makes G-021
                // reproducible and what lets the host replicate a respawn deadline (R-51).
                var respawnAt = _clock.ElapsedSeconds + _config.RespawnDelaySeconds;

                hero.Alive = false;
                hero.RespawnAt = respawnAt;

                // `Alive` is the untargetable predicate R-16 reads, so it is a replicated delta.
                // The respawn deadline rides on the death event instead (G-021 pins both shapes).
                RecordChange(hero.Id, "alive", true, false);
                Emit("hero_died", new Dictionary<string, object>
                {
                    { "hero_id", hero.Id },
                    { "respawn_at", respawnAt },
                });

                result.Downed = true;
                result.RespawnAt = respawnAt;
            }

            // R-33: a wiped party is NOT a loss. R-02 is the only defeat rule — civilians reaching
            // zero — so nothing here touches MatchState.Status, however many heroes are down.
            return Finish(result);
        }

        /// <summary>
        /// R-26, R-36 / B-019 / DEC-019. A hero's attack resolves along its aim line.
        ///
        /// <see cref="HeroAttackRequest.EntitiesOnLine"/> arrives nearest-first exactly as the
        /// shell's raycast reported it: physics decides who is *on* the line, the sim decides who
        /// is *hit*. The nearest entity whose kind is monster takes the damage and nothing else
        /// does — an allowlist, not a denylist of things to skip, so a placeable kind nobody
        /// thought to exclude (a turret, a med station) can never be shot by a stray round.
        ///
        /// That allowlist is how <see cref="SimConfig.FriendlyFire"/> being false is enforced;
        /// there is no shipped mode in which it is true, so the PRD defines no other branch.
        /// </summary>
        public HeroAttackResult ResolveHeroAttack(HeroAttackRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new HeroAttackResult
            {
                AttackerId = request.AttackerId,
                HitId = null,
                DamageDealt = 0.0,
                TargetHpAfter = 0.0,
            };

            var target = FirstMonsterOnLine(request.EntitiesOnLine);
            if (target == null)
            {
                // A line that crossed only friendlies (or whose monster has already left the
                // world) is a clean miss: it resolves, damages nothing and replicates nothing.
                return Finish(result);
            }

            var hpBefore = target.Hp;
            var hpAfter = Math.Max(0.0, hpBefore - request.Damage);
            target.Hp = hpAfter;

            RecordChange(target.Id, "hp", hpBefore, hpAfter);
            Emit("monster_damaged", new Dictionary<string, object>
            {
                { "monster_id", target.Id },
                { "amount", request.Damage },
                { "by", request.AttackerId },
            });

            result.HitId = target.Id;
            result.DamageDealt = request.Damage;
            result.TargetHpAfter = hpAfter;

            // Reaching 0 HP does not kill here: R-40's kill accounting (bounty, XP, wave progress)
            // runs through RecordMonsterKill, which is another ticket's operation.
            return Finish(result);
        }

        /// <summary>
        /// R-35. Out-of-combat regen: a hero untouched for <see cref="SimConfig.RegenDelaySeconds"/>
        /// heals <see cref="SimConfig.RegenHpPerSecond"/> per second up to MaxHp. Driven from the
        /// host's fixed-step loop, reading elapsed time off the injected clock the way
        /// <see cref="TickStatusEffects"/> does.
        ///
        /// Returns void rather than an ISimResult: no fixture grades regen, so there is no result
        /// shape to honour, and the healing it does is replicated through LastObservation's state
        /// changes like any other delta.
        /// </summary>
        public void TickHeroRegen()
        {
            BeginCommand();

            var now = _clock.ElapsedSeconds;

            foreach (var hero in State.Heroes.Values)
            {
                // R-33: a corpse does not heal. Coming back is respawn (full HP at respawn_at),
                // and regen must never be the thing that resurrects anyone.
                if (!hero.Alive || hero.Hp >= hero.MaxHp)
                {
                    continue;
                }

                // Healing accrues only over the window that is both past this hero's 5s grace
                // period AND not already paid out by an earlier tick. Taking a hit moves
                // LastDamagedAt forward, which is what restarts the countdown.
                var eligibleFrom = Math.Max(_lastRegenTickAt, hero.LastDamagedAt + _config.RegenDelaySeconds);
                var eligibleSeconds = now - eligibleFrom;
                if (eligibleSeconds <= 0.0)
                {
                    continue;
                }

                var hpBefore = hero.Hp;
                var hpAfter = Math.Min(hero.MaxHp, hpBefore + (_config.RegenHpPerSecond * eligibleSeconds));
                if (hpAfter <= hpBefore)
                {
                    continue;
                }

                hero.Hp = hpAfter;
                RecordChange(hero.Id, "hp", hpBefore, hpAfter);
            }

            _lastRegenTickAt = now;
        }

        /// <summary>
        /// R-33 / DEC-010. Brings back every dead hero whose <see cref="Hero.RespawnAt"/> deadline
        /// the clock has reached: full HP, at <see cref="SimConfig.RespawnPoint"/>, targetable
        /// again. <see cref="ApplyHeroDamage"/> only *schedules* the deadline (G-021); this is the
        /// half that executes it, without which a hero stays dead for the rest of the match.
        ///
        /// No arguments, elapsed time read off the injected clock, void return — the same seam
        /// shape as <see cref="TickHeroRegen"/> and <see cref="TickStatusEffects"/>. No fixture
        /// grades respawn execution, so there is no result shape to honour and the revival
        /// replicates through LastObservation's state changes like any other delta.
        /// </summary>
        public void TickHeroRespawns()
        {
            BeginCommand();

            var now = _clock.ElapsedSeconds;
            var spawn = _config.RespawnPoint;

            foreach (var hero in State.Heroes.Values)
            {
                // A living hero is not this tick's business: respawn is neither a heal (R-35 owns
                // topping the living up) nor a teleport. No deadline means nothing was ever
                // scheduled for this hero, so there is nothing to execute — and it is also what
                // makes the tick idempotent, since a revive spends the deadline below.
                if (hero.Alive || !hero.RespawnAt.HasValue)
                {
                    continue;
                }

                // The comparison is inclusive, matching how this sim already treats deadlines:
                // G-019 expires a status effect at exactly its expires_at and its
                // `defends_against` names strict greater-than as the bug it guards. R-33's
                // "after 10s" therefore means back *at* respawn_at, not one tick later — and
                // since the host replicates this (R-51), a strict `>` here would put every
                // client's revive frame one tick off the host's.
                if (now < hero.RespawnAt.Value)
                {
                    continue;
                }

                var hpBefore = hero.Hp;

                hero.Alive = true;
                hero.Hp = hero.MaxHp;  // this hero's own cap — the three classes do not share one
                hero.Pos = spawn;
                hero.RespawnAt = null; // deadline spent; a later tick must not fire it again

                // `alive` is the untargetable predicate R-16 reads and `hp` drives every client's
                // health bar, so both are replicated deltas. The spawn point rides the event
                // instead, exactly as the death deadline rides hero_died: no fixture replicates a
                // position field, and the shell moves the transform off the event.
                RecordChange(hero.Id, "hp", hpBefore, hero.Hp);
                RecordChange(hero.Id, "alive", false, true);
                Emit("hero_respawned", new Dictionary<string, object>
                {
                    { "hero_id", hero.Id },
                    { "x", spawn.X },
                    { "y", spawn.Y },
                });

                // R-35 bookkeeping deliberately untouched: a hero returns at MaxHp, so TickHeroRegen
                // skips it outright, and the only route back below MaxHp is ApplyHeroDamage, which
                // writes LastDamagedAt itself. The pre-death timestamp is therefore unreachable
                // rather than stale, and resetting it would be a no-op the reader has to verify.
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// R-31 / DEC-009 / B-012. Incoming damage after the hero's class passive.
        ///
        /// Class-conditional: only Sawbones reduces, and the reduction floors so hero HP never
        /// lands on a fraction (fractional HP is exactly the replication desync G-020 defends
        /// against). See <see cref="FloorEpsilon"/> for why the floor is nudged.
        /// </summary>
        private double IncomingDamageFor(Hero hero, double rawDamage)
        {
            if (hero.HeroClass != HeroClass.Sawbones)
            {
                return rawDamage;
            }

            return Math.Floor((rawDamage * (1.0 - _config.SawbonesDamageReduction)) + FloorEpsilon);
        }

        /// <summary>
        /// R-26 / R-36. The nearest monster the aim line crossed, or null when it crossed none.
        /// Kind is an allowlist — heroes, barricades and every other placeable are passed through
        /// untouched no matter where on the line they sit.
        /// </summary>
        private Monster FirstMonsterOnLine(List<LineEntity> entitiesOnLine)
        {
            if (entitiesOnLine == null)
            {
                return null;
            }

            foreach (var entity in entitiesOnLine)
            {
                if (entity == null || entity.Kind != MonsterLineKind)
                {
                    continue;
                }

                if (State.Monsters.TryGetValue(entity.Id, out var monster))
                {
                    return monster;
                }
            }

            return null;
        }
    }
}
