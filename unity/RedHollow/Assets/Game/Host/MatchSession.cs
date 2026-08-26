using System;
using System.Collections.Generic;
using RedHollow.Game.View;
using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// Ticket 019 (T-19) — the playable bootstrap: the plain C# object that assembles a match and
    /// drives it. Every piece it needs already existed and nothing connected them.
    ///
    /// It owns the wiring the PRD's loop implies but no single class held: opening the match with
    /// a wave (R-19), keeping monsters targeted (R-16) and moving (R-17/R-18), letting the R-18
    /// gate turn contact into damage, driving placeable combat (R-23 turrets at 1 Hz, trap
    /// crossings on footprint entry, R-02/R-40 kill accounting for those deaths), moving the
    /// campaign on when a wave is cleared (R-02/R-03), and keeping the view set level with the
    /// world (R-51).
    ///
    /// Plain C# rather than a MonoBehaviour, and that is the whole architecture of the shell:
    /// <see cref="MatchHostBehaviour"/> stays a two-member pump that holds one of these and calls
    /// <see cref="Step"/>, so no game rule ever enters a component (T-10's IL invariant).
    ///
    /// <b>It decides no rule either.</b> Every line below is either a <see cref="MatchSim"/>
    /// command or the question "does the sim still need to be asked?" — the wave counter moves in
    /// <see cref="IMatchSimHost.BeginPlanningPhase"/>, combat opens in
    /// <see cref="ISimHost.TickPlanningTimer"/>, and defeat fires inside
    /// <see cref="ISimHost.ApplyHotspotAttack"/>. What this class contributes is the *schedule*,
    /// which the PRD deliberately leaves unstated (R-04's interstitial sits somewhere in here) and
    /// which is therefore the one thing here that may be retuned without touching a rule.
    /// </summary>
    public sealed class MatchSession
    {
        private readonly IMatchSimHost _sim;
        private readonly MatchViewBinder _views;
        private readonly HostLoop _loop;

        /// <summary>
        /// R-23 — turret Damage is 20 and the PRD's rate is 20 DPS; <see cref="MatchSim.TurretTick"/>
        /// has no cooldown, so the host is the 1 Hz limiter. First combat step fires immediately
        /// (the same "first swing is free" reading monster attacks use).
        /// </summary>
        private const double TurretFirePeriodSeconds = 1.0;

        /// <summary>
        /// Scratch list for the monsters that need a target this step, so the retarget pass does
        /// not allocate sixty times a second and does not mutate
        /// <see cref="MatchState.Monsters"/> while enumerating it.
        /// </summary>
        private readonly List<string> _needTarget = new List<string>();

        /// <summary>Scratch: standing turret ids this fire window, so a tick cannot mutate the dictionary under the enumerator.</summary>
        private readonly List<string> _turretIds = new List<string>();

        /// <summary>Scratch: standing spike / dynamite ids this step.</summary>
        private readonly List<string> _trapIds = new List<string>();

        /// <summary>Scratch: living monster ids this trap pass.</summary>
        private readonly List<string> _livingScratch = new List<string>();

        /// <summary>Scratch: living-roster copy for the placeable-kill reap.</summary>
        private readonly List<string> _reapScratch = new List<string>();

        /// <summary>
        /// Trap occupancy last combat step, keyed <c>placeableId\0monsterId</c>. A key that is new
        /// this step is a crossing; a key that remains is a monster standing on the trap, which
        /// must not spend another trigger.
        /// </summary>
        private readonly HashSet<string> _trapOccupancy = new HashSet<string>();

        /// <summary>Occupancy assembled this step; swapped into <see cref="_trapOccupancy"/> at the end of the pass.</summary>
        private readonly HashSet<string> _trapOccupancyNow = new HashSet<string>();

        /// <summary>
        /// R-40 — which placeable's owner should be credited if this monster is reaped as a
        /// placeable kill this step. Written when a turret tick or trap trigger drops HP to 0.
        /// </summary>
        private readonly Dictionary<string, string> _placeableKillOwner = new Dictionary<string, string>();

        /// <summary>
        /// Accrued combat time toward the next turret volley. Starts at the period so the first
        /// positive-delta combat step fires; a zero-delta pump (presentation refresh) must not.
        /// </summary>
        private double _turretAccrual = TurretFirePeriodSeconds;

        /// <summary>
        /// R-19 — the wave whose monsters this session has already put in the colony. Zero means
        /// "none yet". It is the only piece of campaign state here, and it exists because
        /// <see cref="IMatchSimHost.SpawnWave"/> is not idempotent: the sim happily spawns wave 2
        /// twice, so *something* has to remember that it already asked, and the wave counter alone
        /// cannot say whether the monsters standing under it are this wave's or the last one's.
        /// </summary>
        private int _waveInTheColony;

        /// <param name="sim">The widened sim seam (R-51). Everything below goes through it.</param>
        /// <param name="heroIntents">R-30 — this client's / the host's resolved hero input, or null.</param>
        /// <param name="views">
        /// R-51 — the view set to keep level with the world, or null for a headless session (a
        /// dedicated host, or a test). A session must run without one: rendering is not a rule.
        /// </param>
        public MatchSession(
            IMatchSimHost sim,
            IHeroIntentSource heroIntents = null,
            MatchViewBinder views = null)
        {
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }

            _sim = sim;
            _views = views;

            // R-18 — contact is geometry and the sim holds none, so the loop is given the shell's
            // answer to "who has arrived?". Owned here rather than injected because a session with
            // no attack source is a match whose monsters walk to the shelters and stand there
            // politely; nothing in the PRD describes that game.
            _loop = new HostLoop(sim, new ContactMonsterAttacks(), heroIntents);
        }

        /// <summary>
        /// R-19 — open the match: the wave the match is on enters the colony. On a fresh match
        /// (<see cref="WaveState.Number"/> = 1) that is wave 1, which is what "starting a match
        /// spawns wave 1" means; a session handed a match already on wave 10 opens wave 10.
        ///
        /// The counter is read, never written: <see cref="IMatchSimHost.BeginPlanningPhase"/> is
        /// the only thing that advances it (G-016), and a bootstrap that opened by advancing would
        /// silently skip wave 1 of every match it ever started.
        /// </summary>
        public void Start()
        {
            _waveInTheColony = _sim.State.Wave.Number;
            _sim.SpawnWave(_waveInTheColony);

            // The wave that just arrived is visible before the first frame is drawn, rather than
            // one step later (R-51).
            SyncViews();
        }

        /// <summary>
        /// One host step of a live match: the loop (R-51), wave progression (R-02/R-03) and the
        /// view set. Bounded by nothing here — the caller owns the cadence, so a fixed-step pump
        /// and a test loop drive exactly the same code.
        ///
        /// Targeting runs before the loop rather than after it, so a monster that was handed a
        /// target this step also walks that way this step: the other order costs every spawn one
        /// wasted tick and, worse, leaves a monster whose shelter was just emptied (R-12) walking
        /// one more step at a target it can no longer hurt.
        ///
        /// Wave progression runs after it, because the loop is what ends a planning phase
        /// (R-03) — asking first would always be reading the previous step's phase.
        /// </summary>
        public void Step(double deltaSeconds)
        {
            RetargetMonstersThatNeedOne();

            _loop.Step(deltaSeconds);

            // R-23 / R-40 — placeable combat after movement, so a monster that walked onto a trap
            // this step triggers this step, and a turret fires at where the wave actually stands.
            DrivePlaceableCombat(deltaSeconds);

            AdvanceTheCampaign();

            SyncViews();
        }

        /// <summary>
        /// R-16 — keep every living monster pointed at something.
        ///
        /// The host has to keep asking, and the two reasons are both ordinary mid-match states
        /// rather than edge cases: <see cref="IMatchSimHost.SpawnWave"/> leaves
        /// <see cref="Monster.TargetId"/> null, so an unasked wave never leaves its breach, and
        /// R-12 invalidates a target the moment its shelter is emptied — which is exactly when the
        /// wave needs to be sent at the next one.
        ///
        /// Asked only for the monsters that need it, not for the whole roster every step. That is
        /// not a micro-optimisation: each command resets
        /// <see cref="ISimHost.LastObservation"/>, which netcode replicates from (R-51), so thirty
        /// re-selections a step would shred every other command's observation for answers that had
        /// not changed.
        /// </summary>
        private void RetargetMonstersThatNeedOne()
        {
            var state = _sim.State;

            // A finished match re-targets nobody: R-02 fires the moment the last civilian dies, and
            // with every shelter empty SelectTarget has no honest answer left to give.
            if (state.IsOver)
            {
                return;
            }

            _needTarget.Clear();

            foreach (var monster in state.Monsters.Values)
            {
                if (monster != null && monster.Alive && !HoldsAnAttackableTarget(state, monster))
                {
                    _needTarget.Add(monster.Id);
                }
            }

            for (var i = 0; i < _needTarget.Count; i++)
            {
                _sim.SelectTarget(_needTarget[i]);
            }
        }

        /// <summary>
        /// Whether this monster's current target is still something R-16 would have picked. The
        /// three readings are the sim's own (MatchSim.Targeting.cs): a dead hero and a destroyed
        /// placeable have left the field, and an emptied hotspot has stopped being a valid target
        /// (R-12) even though the building is still standing.
        /// </summary>
        private static bool HoldsAnAttackableTarget(MatchState state, Monster monster)
        {
            if (string.IsNullOrEmpty(monster.TargetId))
            {
                return false;
            }

            if (state.Heroes.TryGetValue(monster.TargetId, out var hero))
            {
                return hero.Alive;
            }

            if (state.Hotspots.TryGetValue(monster.TargetId, out var hotspot))
            {
                return hotspot.IsValidTarget;
            }

            if (state.Placeables.TryGetValue(monster.TargetId, out var placeable))
            {
                return placeable.Exists;
            }

            return false;
        }

        /// <summary>
        /// R-23 / R-02 / R-40 — the placeable combat the sim exposes but cannot schedule: turrets
        /// fire at 1 Hz, traps fire on footprint <i>entry</i>, and any monster a placeable already
        /// marked dead is reaped through <see cref="IMatchSimHost.RecordMonsterKill"/> so the wave
        /// roster actually shrinks. Combat only, and only on a positive delta — a <c>Pump(0)</c>
        /// refresh must not spend a spike or take a free turret shot.
        /// </summary>
        private void DrivePlaceableCombat(double deltaSeconds)
        {
            var state = _sim.State;

            if (state.IsOver || state.Phase != MatchPhase.Combat)
            {
                _trapOccupancy.Clear();
                _trapOccupancyNow.Clear();
                _placeableKillOwner.Clear();
                _turretAccrual = TurretFirePeriodSeconds;
                return;
            }

            if (!(deltaSeconds > 0.0))
            {
                return;
            }

            _turretAccrual += deltaSeconds;
            while (_turretAccrual >= TurretFirePeriodSeconds)
            {
                FireEveryTurret(state);
                _turretAccrual -= TurretFirePeriodSeconds;
            }

            DetectTrapCrossings(state);
            ReapPlaceableKills(state);
        }

        /// <summary>R-23 / G-028 — every standing turret, once per fire window, nearest in range.</summary>
        private void FireEveryTurret(MatchState state)
        {
            _turretIds.Clear();
            foreach (var placeable in state.Placeables.Values)
            {
                if (placeable != null && placeable.Exists && placeable.Type == PlaceableType.Turret)
                {
                    _turretIds.Add(placeable.Id);
                }
            }

            for (var i = 0; i < _turretIds.Count; i++)
            {
                var turretId = _turretIds[i];
                var result = _sim.TurretTick(turretId);
                if (result == null || string.IsNullOrEmpty(result.TargetId) || result.DamageDealt <= 0.0)
                {
                    continue;
                }

                if (state.Monsters.TryGetValue(result.TargetId, out var victim)
                    && victim != null && !victim.Alive)
                {
                    NotePlaceableKillOwner(victim.Id, OwnerOf(state, turretId));
                }
            }
        }

        /// <summary>
        /// R-23 / G-027 / G-029 — a monster that was outside a trap last step and is inside it now
        /// is a crossing. Occupancy is rebuilt every step; standing still keeps the key and spends
        /// nothing further.
        /// </summary>
        private void DetectTrapCrossings(MatchState state)
        {
            _trapOccupancyNow.Clear();

            _trapIds.Clear();
            foreach (var placeable in state.Placeables.Values)
            {
                if (placeable != null
                    && placeable.Exists
                    && (placeable.Type == PlaceableType.SpikeTrap
                        || placeable.Type == PlaceableType.DynamiteTrap))
                {
                    _trapIds.Add(placeable.Id);
                }
            }

            _livingScratch.Clear();
            foreach (var monster in state.Monsters.Values)
            {
                if (monster != null && monster.Alive)
                {
                    _livingScratch.Add(monster.Id);
                }
            }

            var radius = _sim.PlaceableFootprintRadius;

            for (var t = 0; t < _trapIds.Count; t++)
            {
                var trapId = _trapIds[t];
                if (!state.Placeables.TryGetValue(trapId, out var trap) || trap == null || !trap.Exists)
                {
                    continue;
                }

                for (var m = 0; m < _livingScratch.Count; m++)
                {
                    var monsterId = _livingScratch[m];
                    if (!state.Monsters.TryGetValue(monsterId, out var monster)
                        || monster == null || !monster.Alive)
                    {
                        continue;
                    }

                    if (trap.Pos.DistanceTo(monster.Pos) > radius)
                    {
                        continue;
                    }

                    var key = trapId + "\0" + monsterId;
                    _trapOccupancyNow.Add(key);

                    if (_trapOccupancy.Contains(key))
                    {
                        continue;
                    }

                    var owner = trap.OwnerPlayerId;
                    var result = _sim.TriggerPlaceable(trapId, monsterId);
                    NoteKillsFromTrigger(state, owner, monsterId, result);

                    if (!state.Placeables.TryGetValue(trapId, out trap) || trap == null || !trap.Exists)
                    {
                        // Dynamite (and a spike's last trigger) leaves the world; further monsters
                        // this step cannot cross a trap that is already gone.
                        break;
                    }
                }
            }

            _trapOccupancy.Clear();
            foreach (var key in _trapOccupancyNow)
            {
                _trapOccupancy.Add(key);
            }
        }

        private void NoteKillsFromTrigger(
            MatchState state, string ownerPlayerId, string triggerMonsterId, ISimResult result)
        {
            var blast = result as BlastTriggerResult;
            if (blast != null)
            {
                for (var i = 0; i < blast.MonstersHit.Count; i++)
                {
                    var id = blast.MonstersHit[i];
                    if (state.Monsters.TryGetValue(id, out var caught) && caught != null && !caught.Alive)
                    {
                        NotePlaceableKillOwner(id, ownerPlayerId);
                    }
                }

                return;
            }

            if (state.Monsters.TryGetValue(triggerMonsterId, out var victim)
                && victim != null && !victim.Alive)
            {
                NotePlaceableKillOwner(triggerMonsterId, ownerPlayerId);
            }
        }

        private void NotePlaceableKillOwner(string monsterId, string ownerPlayerId)
        {
            if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(ownerPlayerId))
            {
                return;
            }

            if (!_placeableKillOwner.ContainsKey(monsterId))
            {
                _placeableKillOwner[monsterId] = ownerPlayerId;
            }
        }

        private static string OwnerOf(MatchState state, string placeableId)
        {
            if (state.Placeables.TryGetValue(placeableId, out var placeable) && placeable != null)
            {
                return placeable.OwnerPlayerId;
            }

            return null;
        }

        /// <summary>
        /// R-02 / R-20 / R-40 — placeable <c>DamageMonster</c> flips <c>alive</c> at 0 HP without
        /// shrinking the wave roster. Hero basics leave <c>alive</c> true for the shell to reap;
        /// only the already-flagged corpses are taken here, credited to the placer when we saw
        /// which placeable dropped them.
        /// </summary>
        private void ReapPlaceableKills(MatchState state)
        {
            _reapScratch.Clear();
            _reapScratch.AddRange(state.Wave.LivingMonsterIds);

            for (var i = 0; i < _reapScratch.Count; i++)
            {
                var monsterId = _reapScratch[i];
                if (!state.Monsters.TryGetValue(monsterId, out var monster)
                    || monster == null || monster.Alive)
                {
                    continue;
                }

                var stats = _sim.Config.Monsters.TryGet(monster.Type);
                string ownerPlayerId;
                _placeableKillOwner.TryGetValue(monsterId, out ownerPlayerId);

                var hero = HeroForPlaceableOwner(state, ownerPlayerId);
                var kill = new MonsterKillRequest
                {
                    MonsterId = monsterId,
                    MonsterType = monster.Type,
                    Bounty = stats == null ? 0 : stats.Bounty,
                    KillerHeroId = hero == null ? null : hero.Id,
                };

                _sim.RecordMonsterKill(kill);

                var accountId = AccountForPlayerSlot(state, ownerPlayerId);
                if (string.IsNullOrEmpty(accountId) && hero != null)
                {
                    accountId = hero.AccountId;
                }

                if (!string.IsNullOrEmpty(accountId))
                {
                    _sim.AwardKillXp(kill, accountId);
                }

                _placeableKillOwner.Remove(monsterId);
            }
        }

        private static string AccountForPlayerSlot(MatchState state, string playerSlotId)
        {
            if (string.IsNullOrEmpty(playerSlotId))
            {
                return null;
            }

            var players = state.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null && string.Equals(player.Id, playerSlotId, StringComparison.Ordinal))
                {
                    return player.AccountId;
                }
            }

            return playerSlotId;
        }

        private static Hero HeroForPlaceableOwner(MatchState state, string ownerPlayerId)
        {
            var accountId = AccountForPlayerSlot(state, ownerPlayerId);
            if (string.IsNullOrEmpty(accountId))
            {
                return null;
            }

            foreach (var hero in state.Heroes.Values)
            {
                if (hero != null && string.Equals(hero.AccountId, accountId, StringComparison.Ordinal))
                {
                    return hero;
                }
            }

            return null;
        }

        /// <summary>
        /// R-02 / R-03 / R-19 — the campaign moves on. Three sim commands share the job and the PRD
        /// orders none of the timing between them, so the schedule below is this class's decision
        /// and is stated as such:
        ///
        ///  * <see cref="ISimHost.ApplyHotspotAttack"/> / <c>RecordMonsterKill</c> return the phase
        ///    to planning when the wave is cleared (R-02), leaving the counter alone;
        ///  * <see cref="IMatchSimHost.BeginPlanningPhase"/> advances the counter (G-016) — asked
        ///    here on the first step after the clear, which puts R-04's interstitial at one host
        ///    step rather than at a number nothing in the PRD supports;
        ///  * <see cref="ISimHost.TickPlanningTimer"/> (already driven by the loop) opens combat
        ///    when R-03's 60 seconds elapse, and the wave is spawned into that combat phase.
        ///
        /// A finished match is left entirely alone. Both of the sim's own guards are there — spawn
        /// refuses, planning throws — but reaching either would mean this class had decided a won
        /// match still had a campaign to advance, and the one that throws would take the whole
        /// session down (R-01).
        /// </summary>
        private void AdvanceTheCampaign()
        {
            var state = _sim.State;

            if (state.IsOver)
            {
                return;
            }

            // The wave this session opened has been cleared and the phase has fallen back to
            // planning with the counter still on it. Nothing else advances the counter, so nothing
            // else can start the next wave.
            if (state.Phase == MatchPhase.Planning
                && state.Wave.Number == _waveInTheColony
                && state.Wave.LivingMonsterIds.Count == 0)
            {
                _sim.BeginPlanningPhase();
                return;
            }

            // Planning has ended (R-03) on a wave whose monsters are not in the colony yet.
            if (state.Phase == MatchPhase.Combat && state.Wave.Number != _waveInTheColony)
            {
                _waveInTheColony = state.Wave.Number;
                _sim.SpawnWave(_waveInTheColony);
            }
        }

        /// <summary>
        /// R-51 — the view set follows the world, every step. Null-checked rather than replaced by
        /// a no-op binder: a headless host must not build a <see cref="UnityEngine.GameObject"/>
        /// per monster for nobody to look at.
        /// </summary>
        private void SyncViews()
        {
            if (_views == null)
            {
                return;
            }

            _views.Sync(_sim.State);
        }
    }
}
