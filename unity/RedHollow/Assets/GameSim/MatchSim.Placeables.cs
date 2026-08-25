using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 006 (T-06) owns this half of <see cref="MatchSim"/>: placeable combat effects.
    /// Requirement R-23, plus the R-16 / B-002 half of it that makes a barricade a monster's target
    /// "until destroyed"; graded by fixtures G-027, G-028, G-029.
    ///
    /// Four operations, one per way a placeable touches combat: a trap fires
    /// (<see cref="TriggerPlaceable"/>), a turret fires (<see cref="TurretTick"/>), a placeable is
    /// fired *at* (<see cref="ApplyPlaceableDamage"/>), and a med station heals
    /// (<see cref="TickMedStations"/>).
    ///
    /// R-26 / DEC-019 runs through all of them as an allowlist rather than a denylist, exactly as
    /// <see cref="ResolveHeroAttack"/> enforces it: the only thing a placeable may damage is a
    /// living <see cref="Monster"/>. Heroes, shelters and other placeables are never scanned, so a
    /// blast beside the team's own barricade cannot demolish it and a turret cannot reuse
    /// hero-targeting rules that would let it fire into a shelter (G-028's `defends_against`).
    ///
    /// Sad-path convention for this file, chosen once because the repo is inconsistent
    /// (<see cref="ApplyHotspotAttack"/> throws KeyNotFoundException,
    /// <see cref="ApplyHeroDamage"/> throws ArgumentException, <see cref="SelectTarget"/> and
    /// <see cref="SellPlacement"/> refuse): **these commands refuse, never throw.** Every one of
    /// them is driven by the host's own combat loop off physics events — a trap crossing, a firing
    /// tick, a monster's swing — where a stale id is ordinary (the entity died, was sold, or broke
    /// between the trigger and the tick) rather than exceptional. A refusal writes no state, emits
    /// no event and answers with a result that names the id and carries zeros, so it can never be
    /// mistaken for an effect that happened. The one exception is a null request object, which is
    /// not a game state but a broken call and throws as it does elsewhere.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-23 / G-027, G-029 — a trap that has spent its last trigger leaves the world.</summary>
        private const string PlaceableBroken = "placeable_broken";

        /// <summary>
        /// R-23 / R-16 — a placeable destroyed by damage rather than spent by use. Deliberately a
        /// second name alongside <see cref="PlaceableBroken"/>, which G-027 and G-029 lock for a
        /// spent trap: the shell plays a collapsing wall here and a snapped trap there, and a
        /// client cannot tell them apart from an event that says only "gone".
        /// </summary>
        private const string PlaceableDestroyed = "placeable_destroyed";

        /// <summary>
        /// R-23 / R-35. Sim time the last med-station tick was accounted for, the companion to
        /// <see cref="_lastRegenTickAt"/>: healing accrues over the window since the previous tick
        /// rather than being recomputed from match start every time.
        /// </summary>
        private double _lastMedStationTickAt;

        /// <summary>
        /// R-23 / B-018. A monster crossed a trap.
        ///
        /// Returns <see cref="ISimResult"/> because the two trap rows answer in different shapes: a
        /// spike trap reports one victim and a countdown (<see cref="TrapTriggerResult"/>, G-027)
        /// while dynamite reports a list and one damage figure
        /// (<see cref="BlastTriggerResult"/>, G-029). Collapsing them into one wide result would
        /// make every caller read fields that are meaningless for the trap it triggered.
        ///
        /// Only the two trap rows are triggerable. A turret fires on its own tick and a barricade
        /// and a med station have nothing to fire at all, so naming one of those here is a refusal
        /// rather than a no-op with an event — an announced trigger the shell would render.
        /// </summary>
        public ISimResult TriggerPlaceable(string placeableId, string monsterId)
        {
            BeginCommand();

            // A trap that is no longer in the world never fires again — the same Placeable.Exists
            // predicate R-22's sell and R-16's blocker check read.
            if (placeableId == null
                || !State.Placeables.TryGetValue(placeableId, out var placeable)
                || !placeable.Exists)
            {
                return RefuseTrigger(placeableId);
            }

            // R-16 / R-18: a corpse does not walk onto anything. Guarded here rather than inside
            // each branch so a trap can never spend a trigger on a monster that is already dead.
            if (monsterId == null
                || !State.Monsters.TryGetValue(monsterId, out var monster)
                || !monster.Alive)
            {
                return RefuseTrigger(placeableId);
            }

            if (placeable.Type == PlaceableType.SpikeTrap)
            {
                return TriggerSpikeTrap(placeable, monster);
            }

            if (placeable.Type == PlaceableType.DynamiteTrap)
            {
                return TriggerDynamite(placeable, monster);
            }

            return RefuseTrigger(placeableId);
        }

        /// <summary>
        /// R-23 / B-018. A turret's firing tick: the nearest living monster inside
        /// <see cref="Placeable.Range"/> takes <see cref="Placeable.Damage"/>.
        ///
        /// Only <see cref="MatchState.Monsters"/> is scanned (R-26 / G-028's `defends_against`):
        /// heroes and hotspots are not filtered out of a shared candidate list, they are never in
        /// one, so no future edit to monster targeting can leak a shelter into a turret's sights.
        ///
        /// A tick with nothing valid in range is a defined no-op — that is most frames of a real
        /// match — and replicates nothing rather than firing at whatever else stands nearby.
        /// </summary>
        public TurretTickResult TurretTick(string turretId)
        {
            BeginCommand();

            var result = new TurretTickResult
            {
                TurretId = turretId,
                TargetId = null,
                Distance = 0.0,
                DamageDealt = 0.0,
                TargetHpAfter = 0.0,
            };

            if (turretId == null
                || !State.Placeables.TryGetValue(turretId, out var turret)
                || !turret.Exists
                || turret.Type != PlaceableType.Turret)
            {
                return Finish(result);
            }

            var target = NearestLivingMonster(turret.Pos, turret.Range, out var distance);
            if (target == null)
            {
                return Finish(result);
            }

            var hpAfter = DamageMonster(target, turret.Damage);

            Emit("turret_fired", new Dictionary<string, object>
            {
                { "turret_id", turret.Id },
                { "target_id", target.Id },
                { "damage", turret.Damage },
            });

            result.TargetId = target.Id;
            result.Distance = distance;
            result.DamageDealt = turret.Damage;
            result.TargetHpAfter = hpAfter;

            return Finish(result);
        }

        /// <summary>
        /// R-23 / R-16. Something hit a placeable — today the only such attacker is a monster
        /// whose path a barricade blocked, which R-16 makes "the target until destroyed".
        ///
        /// A command taking a request, like <see cref="ApplyHotspotAttack"/> and
        /// <see cref="ApplyHeroDamage"/>, rather than a tick or a bare id pair: this is the third
        /// member of the same family (an attacker, an amount, a victim) and it is the operation
        /// that makes R-16's "until destroyed" clause reachable at all. Without it a barricade is
        /// immortal, and a 100-scrip wall blocks its lane for the whole match.
        ///
        /// **Only a barricade is damageable.** R-23 gives an HP column to that row and to no other,
        /// so the other four ship with <see cref="PlaceableStats.MaxHp"/> = 0 and
        /// <see cref="PurchasePlacement"/> copies that 0 onto every turret, trap and med station it
        /// places. Treating those as "at 0 HP, therefore destroyed" would delete every non-barricade
        /// placeable the first time anything brushed it, so the rule is written the other way round:
        /// hitting a non-barricade is a defined no-op that credits no damage and destroys nothing.
        /// Giving the other rows invented HP numbers would have been the alternative, and R-23 has
        /// no such numbers to give.
        /// </summary>
        public PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            BeginCommand();

            var result = new PlaceableDamageResult
            {
                PlaceableId = request.TargetId,
                DamageTaken = 0.0,
                HpAfter = 0.0,
                Destroyed = false,
            };

            if (request.TargetId == null || !State.Placeables.TryGetValue(request.TargetId, out var placeable))
            {
                return Finish(result);
            }

            // Rubble absorbs nothing. Checked before any HP moves, so a second swing that lands on
            // the same frame as the killing one cannot announce a second collapse.
            if (!placeable.Exists || !placeable.IsBarricade || request.Damage <= 0.0)
            {
                result.HpAfter = placeable.Hp;
                return Finish(result);
            }

            var hpBefore = placeable.Hp;

            // Floors at 0 rather than going negative: a negative wall renders as a negative health
            // bar, and any rule written as `Hp != 0` would keep treating it as standing.
            var hpAfter = Math.Max(0.0, hpBefore - request.Damage);
            placeable.Hp = hpAfter;

            RecordChange(placeable.Id, "hp", hpBefore, hpAfter);
            Emit("placeable_damaged", new Dictionary<string, object>
            {
                { "placeable_id", placeable.Id },
                { "amount", request.Damage },
                { "by", request.AttackerId },
            });

            // The full incoming hit is credited, not the portion the remaining HP could absorb —
            // the same convention HeroDamageResult.DamageTaken follows (G-020).
            result.DamageTaken = request.Damage;
            result.HpAfter = hpAfter;

            if (hpAfter <= 0.0)
            {
                // R-16 "until destroyed": Exists is the predicate SelectTarget reads when it decides
                // whether a declared blocker still redirects, so this line is what releases the lane.
                placeable.Exists = false;
                RecordChange(placeable.Id, "exists", true, false);
                Emit(PlaceableDestroyed, new Dictionary<string, object>
                {
                    { "placeable_id", placeable.Id },
                    { "by", request.AttackerId },
                });

                result.Destroyed = true;
            }

            return Finish(result);
        }

        /// <summary>
        /// R-23 / R-35. The med station healing tick: every living hero inside a standing med
        /// station's radius regains HP for the time elapsed since the last tick.
        ///
        /// No arguments, elapsed time read off the injected clock, void return — the same seam
        /// shape as <see cref="TickHeroRegen"/>, <see cref="TickHeroRespawns"/> and
        /// <see cref="TickStatusEffects"/>. No fixture grades med stations, so there is no result
        /// shape to honour and the healing replicates through LastObservation's state changes like
        /// any other delta.
        ///
        /// R-35's "Med Station stacks" is implemented by this being a *separate* source rather than
        /// a competing one: it runs on its own accrual window and neither reads nor resets
        /// <see cref="Hero.LastDamagedAt"/>, so a hero standing in the radius while out of combat
        /// collects both payouts and a hero still in combat collects this one alone. The PRD gives
        /// the word no arithmetic; addition of two independent sources is the reading that makes
        /// the station worth its 200 scrip without multiplying anything.
        ///
        /// Both numbers come from the R-23 catalog row rather than from the entity, because
        /// <see cref="Placeable"/> carries no heal-rate column — one source for the pair keeps the
        /// rate and the radius from being tuned in two different places.
        /// </summary>
        public void TickMedStations()
        {
            BeginCommand();

            var now = _clock.ElapsedSeconds;
            var elapsedSeconds = now - _lastMedStationTickAt;
            if (elapsedSeconds <= 0.0)
            {
                _lastMedStationTickAt = now;
                return;
            }

            var stats = _config.Placeables.TryGet(PlaceableType.MedStation);
            if (stats == null || stats.HealPerSecond <= 0.0)
            {
                // No configured row means no aura. TryGet rather than StatsFor: a tick runs every
                // frame, and a missing row must not take the host down mid-combat.
                _lastMedStationTickAt = now;
                return;
            }

            var healed = stats.HealPerSecond * elapsedSeconds;

            foreach (var placeable in State.Placeables.Values)
            {
                // A station that was destroyed or sold (R-22) is off the map, and an aura that
                // outlived its emitter would keep a refunded 200-scrip station healing.
                if (!placeable.Exists || placeable.Type != PlaceableType.MedStation)
                {
                    continue;
                }

                foreach (var hero in State.Heroes.Values)
                {
                    // R-33: a corpse does not heal, consistent with TickHeroRegen. Coming back is
                    // respawn, at full HP — a station must never quietly resurrect the hero lying
                    // in its footprint.
                    if (!hero.Alive || hero.Hp >= hero.MaxHp)
                    {
                        continue;
                    }

                    // Inclusive, the convention G-019 set for every boundary in this sim: a hero
                    // standing exactly on the edge of the radius is inside it.
                    if (placeable.Pos.DistanceTo(hero.Pos) > stats.Range)
                    {
                        continue;
                    }

                    var hpBefore = hero.Hp;

                    // This hero's own cap — the three classes do not share one — so a top-up lands
                    // exactly on MaxHp however long the station has been standing.
                    var hpAfter = Math.Min(hero.MaxHp, hpBefore + healed);
                    if (hpAfter <= hpBefore)
                    {
                        continue;
                    }

                    hero.Hp = hpAfter;
                    RecordChange(hero.Id, "hp", hpBefore, hpAfter);
                }
            }

            _lastMedStationTickAt = now;
        }

        // ---- trap branches -------------------------------------------------------------------------

        /// <summary>
        /// R-23 / G-027. One crossing of a spike trap: full damage to the crossing monster, then one
        /// trigger off the counter, and the trap breaks on the crossing that reaches 0.
        ///
        /// Damage first, break second, in that order: G-027's `defends_against` names both halves —
        /// "trigger counter never decremented (infinite trap)" and "trap removed before dealing its
        /// final hit" — so the last crossing must both hurt and be the last.
        /// </summary>
        private ISimResult TriggerSpikeTrap(Placeable trap, Monster monster)
        {
            // Spikes with no triggers left are scenery. Exists already guards the normal case; this
            // keeps the counter from going negative if the two ever disagree.
            if (trap.TriggersRemaining <= 0)
            {
                return RefuseTrigger(trap.Id);
            }

            var hpAfter = DamageMonster(monster, trap.Damage);

            var triggersBefore = trap.TriggersRemaining;
            var triggersAfter = triggersBefore - 1;
            trap.TriggersRemaining = triggersAfter;
            RecordChange(trap.Id, "triggers_remaining", triggersBefore, triggersAfter);

            EmitTriggered(trap, monster);

            var broke = triggersAfter <= 0;
            if (broke)
            {
                RemoveSpentTrap(trap);
            }

            return Finish(new TrapTriggerResult
            {
                PlaceableId = trap.Id,
                DamageDealt = trap.Damage,
                MonsterHpAfter = hpAfter,
                TriggersRemaining = triggersAfter,
                Broke = broke,
            });
        }

        /// <summary>
        /// R-23 / G-029. Dynamite: one detonation, full damage to every living monster inside
        /// <see cref="Placeable.BlastRadius"/> of the charge, then the trap is gone.
        ///
        /// `monsters_hit` is ordered nearest-first (ties by ordinal id) and that order is contract:
        /// the golden manifest canonicalizes `state_changes` and `emitted_events` but never sorts
        /// inside a result value, so G-029's `["m5", "m6"]` is compared element by element.
        ///
        /// The counter is spent rather than decremented blindly — R-23 calls dynamite single use,
        /// and a charge that has already gone off must never report a negative countdown.
        /// </summary>
        private ISimResult TriggerDynamite(Placeable charge, Monster trigger)
        {
            var result = new BlastTriggerResult
            {
                PlaceableId = charge.Id,
                DamageEach = charge.Damage,
            };

            // R-26 / DEC-019: monsters only. Heroes, shelters and the team's own placeables are
            // never candidates, so a charge laid beside a friendly barricade cannot demolish it.
            foreach (var caught in LivingMonstersWithin(charge.Pos, charge.BlastRadius))
            {
                DamageMonster(caught, charge.Damage);
                result.MonstersHit.Add(caught.Id);
            }

            var triggersBefore = charge.TriggersRemaining;
            var triggersAfter = triggersBefore > 0 ? triggersBefore - 1 : 0;
            charge.TriggersRemaining = triggersAfter;
            RecordChange(charge.Id, "triggers_remaining", triggersBefore, triggersAfter);

            // The triggering monster names the event even though the blast hit several — the shell
            // plays the detonation from whoever set it off (G-029 pins `monster_id: "m5"`).
            EmitTriggered(charge, trigger);
            RemoveSpentTrap(charge);

            return Finish(result);
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// A trigger command that resolved to nothing: no state written, no event emitted, and a
        /// zeroed result naming the placeable that was asked for. See the file's sad-path note.
        /// </summary>
        private ISimResult RefuseTrigger(string placeableId)
        {
            return Finish(new TrapTriggerResult
            {
                PlaceableId = placeableId,
                DamageDealt = 0.0,
                MonsterHpAfter = 0.0,
                TriggersRemaining = 0,
                Broke = false,
            });
        }

        /// <summary>R-23 / G-027, G-029 — the shape both traps announce a firing with.</summary>
        private void EmitTriggered(Placeable trap, Monster monster)
        {
            Emit("placeable_triggered", new Dictionary<string, object>
            {
                { "placeable_id", trap.Id },
                { "monster_id", monster.Id },
                { "damage", trap.Damage },
            });
        }

        /// <summary>
        /// R-23 — a trap that has spent its last use leaves the world. <see cref="Placeable.Exists"/>
        /// is the same predicate R-22's sell writes and R-16's blocker check reads, so a spent trap
        /// stops occupying ground and stops being anything's target the moment it breaks.
        /// </summary>
        private void RemoveSpentTrap(Placeable trap)
        {
            trap.Exists = false;
            RecordChange(trap.Id, "exists", true, false);
            Emit(PlaceableBroken, new Dictionary<string, object>
            {
                { "placeable_id", trap.Id },
            });
        }

        /// <summary>
        /// R-23 — applies one placeable's damage to one monster and reports the HP left.
        ///
        /// Unlike <see cref="ResolveHeroAttack"/>, reaching 0 marks the monster dead here: G-029
        /// pins `m5.alive true -> false` on the blast that drops it, so a placeable kill is visible
        /// in the replicated stream. R-40's accounting (bounty, XP, wave progress) still runs
        /// through <see cref="RecordMonsterKill"/> — this only stops a corpse from being hit twice.
        /// </summary>
        private double DamageMonster(Monster monster, double damage)
        {
            var hpBefore = monster.Hp;
            var hpAfter = Math.Max(0.0, hpBefore - damage);
            monster.Hp = hpAfter;

            RecordChange(monster.Id, "hp", hpBefore, hpAfter);

            if (hpAfter <= 0.0 && monster.Alive)
            {
                monster.Alive = false;
                RecordChange(monster.Id, "alive", true, false);
            }

            return hpAfter;
        }

        /// <summary>
        /// R-23 / G-028 — the nearest living monster within <paramref name="range"/> of
        /// <paramref name="from"/>, or null when the sky is empty.
        ///
        /// Ties break to the lowest entity id, compared ordinally. R-16 states that rule for monster
        /// targeting and the PRD never extends it to turrets, but *some* total order is mandatory:
        /// the sim is host-authoritative and replicated (R-51), so an answer that fell out of
        /// Dictionary iteration order would differ between a host and a rebuilt world holding the
        /// same entities. Reusing R-16's tiebreak keeps one rule in the codebase instead of two.
        /// </summary>
        private Monster NearestLivingMonster(Vec2 from, double range, out double distance)
        {
            Monster best = null;
            distance = 0.0;

            foreach (var monster in State.Monsters.Values)
            {
                if (!monster.Alive)
                {
                    continue;
                }

                var candidateDistance = from.DistanceTo(monster.Pos);

                // Inclusive, per G-019's convention: a monster at exactly range 8 is in reach.
                if (candidateDistance > range)
                {
                    continue;
                }

                var better = best == null
                    || candidateDistance < distance
                    || (candidateDistance == distance && string.CompareOrdinal(monster.Id, best.Id) < 0);

                if (better)
                {
                    best = monster;
                    distance = candidateDistance;
                }
            }

            if (best == null)
            {
                distance = 0.0;
            }

            return best;
        }

        /// <summary>
        /// R-23 / G-029 — every living monster inside <paramref name="radius"/> of
        /// <paramref name="centre"/>, nearest first and ties by ordinal id.
        ///
        /// The order is the result's contract, not a convenience: G-029 compares `monsters_hit`
        /// positionally, and Dictionary iteration order would make the same blast report differently
        /// on two hosts holding identical worlds (R-51).
        /// </summary>
        private List<Monster> LivingMonstersWithin(Vec2 centre, double radius)
        {
            var caught = new List<Monster>();

            foreach (var monster in State.Monsters.Values)
            {
                // A corpse is not damaged again: re-damaging the dead would replay kill effects and
                // (once R-40's accounting hangs off this) pay a second bounty.
                if (!monster.Alive)
                {
                    continue;
                }

                // Inclusive, per G-019's convention: a monster standing exactly on the edge of the
                // blast is caught by it.
                if (centre.DistanceTo(monster.Pos) > radius)
                {
                    continue;
                }

                caught.Add(monster);
            }

            caught.Sort((left, right) =>
            {
                var byDistance = centre.DistanceTo(left.Pos).CompareTo(centre.DistanceTo(right.Pos));
                return byDistance != 0 ? byDistance : string.CompareOrdinal(left.Id, right.Id);
            });

            return caught;
        }
    }
}
