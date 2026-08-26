using System;
using System.Collections.Generic;
using System.Linq;
using RedHollow.Sim;

namespace RedHollow.Tools.BalanceProbe
{
    /// <summary>
    /// Headless solo-balance probe at SHIPPED numbers (R-19 calls them playtest-tunable; this is
    /// the playtest). It drives the REAL <see cref="MatchSim"/> through a faithful mirror of the
    /// Unity shell's host schedule and answers the question the owner will otherwise discover by
    /// hand: can a solo gunslinger clear all ten waves, and if not, where does the campaign die?
    ///
    /// What is REAL: every sim command, the wave table, the catalogs, targeting, movement, the
    /// R-18 attack gate (contact = the sim's own arrival clamp, exactly as
    /// ContactMonsterAttacks derives it), turret 1 Hz / trap edge-trigger / kill reaping exactly
    /// as MatchSession drives them, hero basic cadence 0.25 s at catalog damage along a
    /// nearest-monster aim, purchases through PurchasePlacement's real zone validation.
    ///
    /// What is APPROXIMATE (all of it pessimistic — a human does better):
    ///  * the hero stands at team spawn and never kites, dodges or retreats;
    ///  * the hero casts no Q/E abilities and spends no skill points;
    ///  * the aim line carries only the nearest monster (no incidental second body for pierce).
    ///
    /// So a policy that WINS here should be comfortably winnable by a human, and a policy that
    /// collapses at wave N marks where real play starts depending on movement and abilities.
    /// </summary>
    internal static class Program
    {
        private const double Step = 1.0 / 60.0;

        private static void Main()
        {
            Console.WriteLine("Red Hollow solo balance probe — shipped numbers, 60 Hz host steps");
            Console.WriteLine("hero: stationary gunslinger, basics only, perfect aim, 0.25s cadence");
            Console.WriteLine();

            RunPolicy("hero only (no purchases)", buyTurrets: false, buySpikes: false);
            RunPolicy("hero + turrets", buyTurrets: true, buySpikes: false);
            RunPolicy("hero + turrets + spikes", buyTurrets: true, buySpikes: true);
        }

        private static void RunPolicy(string name, bool buyTurrets, bool buySpikes)
        {
            Console.WriteLine("== policy: " + name + " ==");
            var run = new SoloRun(buyTurrets, buySpikes);
            run.Play();
            Console.WriteLine();
        }
    }

    /// <summary>One solo campaign, played to its end (victory, defeat, or the step bound).</summary>
    internal sealed class SoloRun
    {
        private const double StepSeconds = 1.0 / 60.0;

        /// <summary>The shell's shipped basic-attack cadence (CombatActionConfig).</summary>
        private const double AttackCadenceSeconds = 0.25;

        /// <summary>The shell's shipped aim-line length (CombatActionConfig).</summary>
        private const double AimLineLength = 30.0;

        /// <summary>MatchSession's turret rate limit: Damage 20 at 1 Hz = the catalog's 20 DPS.</summary>
        private const double TurretFirePeriodSeconds = 1.0;

        /// <summary>240 sim-minutes bound; a healthy campaign ends far earlier.</summary>
        private const int MaxSteps = 240 * 60 * 60;

        private readonly bool _buyTurrets;
        private readonly bool _buySpikes;

        private readonly ColonyMap _map = ColonyMap.V1();
        private readonly SimConfig _config = new SimConfig();
        private readonly SimClock _clock = new SimClock();
        private readonly MatchState _state;
        private readonly MatchSim _sim;

        private readonly Hero _hero;
        private readonly string _playerId = "player_1";
        private const string AccountId = "acc_probe";

        // ---- mirrors of the shell's per-step state ----
        private double _attackClock;
        private double _turretAccrual = TurretFirePeriodSeconds;
        private readonly HashSet<string> _trapOccupancy = new HashSet<string>();
        private readonly HashSet<string> _trapOccupancyNow = new HashSet<string>();
        private readonly List<string> _scratch = new List<string>();
        private int _waveInTheColony;
        private int _planningShoppedForWave;

        // ---- per-wave report ----
        private int _heroDowns;

        public SoloRun(bool buyTurrets, bool buySpikes)
        {
            _buyTurrets = buyTurrets;
            _buySpikes = buySpikes;

            _state = _map.CreateMatchState(_config);
            _state.Wave.TotalWaves = _config.TotalWaves;
            _state.Phase = MatchPhase.Combat;
            _state.Status = MatchStatus.InProgress;

            _sim = new MatchSim(_state, _config, null, _clock, null) { ColonyMap = _map };

            var kit = _config.HeroKits.KitFor(HeroClass.Gunslinger);
            _hero = new Hero
            {
                Id = "hero_1",
                HeroClass = HeroClass.Gunslinger,
                AccountId = AccountId,
                Pos = _map.TeamSpawn,
                Hp = kit.MaxHp,
                MaxHp = kit.MaxHp,
                Alive = true,
            };
            _state.Heroes[_hero.Id] = _hero;
            _state.Players.Add(new PlayerSlot
            {
                Id = _playerId,
                AccountId = AccountId,
                HeroClass = HeroClass.Gunslinger,
                Ready = false,
                Connected = true,
            });
        }

        public void Play()
        {
            _waveInTheColony = _state.Wave.Number;
            _sim.SpawnWave(_waveInTheColony);

            var waveStartedAt = 0.0;
            var civiliansAtWaveStart = _state.TotalCivilians;
            var reportedWave = _state.Wave.Number;

            for (var i = 0; i < MaxSteps && !_state.IsOver; i++)
            {
                StepOnce();

                if (_state.Wave.Number != reportedWave)
                {
                    ReportWave(reportedWave, waveStartedAt, civiliansAtWaveStart);
                    reportedWave = _state.Wave.Number;
                    waveStartedAt = _clock.ElapsedSeconds;
                    civiliansAtWaveStart = _state.TotalCivilians;
                }
            }

            ReportWave(reportedWave, waveStartedAt, civiliansAtWaveStart);
            Console.WriteLine("outcome: " + _state.Status
                + "  (wave " + _state.Wave.Number + "/" + _state.Wave.TotalWaves
                + ", civilians " + _state.TotalCivilians + "/20"
                + ", hero downs " + _heroDowns
                + ", scrip " + _state.Team.Scrip
                + ", sim time " + _clock.ElapsedSeconds.ToString("F0") + "s)");
        }

        private void ReportWave(int wave, double startedAt, int civiliansBefore)
        {
            Console.WriteLine("  wave " + wave.ToString().PadLeft(2)
                + ": " + (_clock.ElapsedSeconds - startedAt).ToString("F1").PadLeft(6) + "s"
                + "  civilians " + civiliansBefore + " -> " + _state.TotalCivilians
                + "  scrip " + _state.Team.Scrip
                + "  standing " + _state.PlaceableCount
                + "  hero downs " + _heroDowns);
        }

        /// <summary>One 60 Hz host step, in MatchSession's order.</summary>
        private void StepOnce()
        {
            RetargetMonstersThatNeedOne();

            // ---- HostLoop.Step ----
            _clock.Advance(StepSeconds);
            _sim.TickPlanningTimer();
            _sim.TickStatusEffects();
            _sim.TickHeroRegen();
            TickRespawnsCountingDowns();
            _sim.TickMedStations();
            _sim.TickMonsterMovement(StepSeconds);
            ResolveMonsterContactAttacks();

            // ---- MatchSession.DrivePlaceableCombat ----
            DrivePlaceableCombat();

            // ---- ShellBootstrap.HandleCombatActions (the bot player) ----
            FireHeroBasics();

            // ---- MatchSession.AdvanceTheCampaign ----
            AdvanceTheCampaign();

            // ---- the bot's planning turn (shop, then ready up) ----
            ShopAndReadyDuringPlanning();
        }

        private void TickRespawnsCountingDowns()
        {
            var wasDown = !_hero.Alive;
            _sim.TickHeroRespawns();
            if (wasDown && _hero.Alive)
            {
                // Back at spawn on full HP (R-33); the bot resumes firing next step.
            }
        }

        // ---- mirrors ------------------------------------------------------------------------

        /// <summary>MatchSession.RetargetMonstersThatNeedOne, verbatim policy.</summary>
        private void RetargetMonstersThatNeedOne()
        {
            if (_state.IsOver)
            {
                return;
            }

            _scratch.Clear();
            foreach (var monster in _state.Monsters.Values)
            {
                if (monster != null && monster.Alive && !HoldsAnAttackableTarget(monster))
                {
                    _scratch.Add(monster.Id);
                }
            }

            for (var i = 0; i < _scratch.Count; i++)
            {
                _sim.SelectTarget(_scratch[i]);
            }
        }

        private bool HoldsAnAttackableTarget(Monster monster)
        {
            if (string.IsNullOrEmpty(monster.TargetId))
            {
                return false;
            }

            if (_state.Heroes.TryGetValue(monster.TargetId, out var hero))
            {
                return hero.Alive;
            }

            if (_state.Hotspots.TryGetValue(monster.TargetId, out var hotspot))
            {
                return hotspot.IsValidTarget;
            }

            if (_state.Placeables.TryGetValue(monster.TargetId, out var placeable))
            {
                return placeable.Exists;
            }

            return false;
        }

        /// <summary>ContactMonsterAttacks + HostLoop's gate-then-route, verbatim policy.</summary>
        private void ResolveMonsterContactAttacks()
        {
            _scratch.Clear();
            foreach (var monster in _state.Monsters.Values)
            {
                if (monster == null || !monster.Alive || string.IsNullOrEmpty(monster.TargetId))
                {
                    continue;
                }

                _scratch.Add(monster.Id);
            }

            foreach (var monsterId in _scratch)
            {
                if (!_state.Monsters.TryGetValue(monsterId, out var monster) || !monster.Alive)
                {
                    continue;
                }

                if (!TryTarget(monster.TargetId, out var targetPos, out var kind))
                {
                    continue;
                }

                // The shell's derived reach: arrived, or arrived this tick.
                if (monster.Pos.DistanceTo(targetPos) > monster.CurrentSpeed * StepSeconds)
                {
                    continue;
                }

                var stats = _config.Monsters.TryGet(monster.Type);
                if (stats == null || !_sim.TryMonsterAttack(monster.Id))
                {
                    continue;
                }

                switch (kind)
                {
                    case TargetKind.Hotspot:
                        _sim.ApplyHotspotAttack(new HotspotAttackRequest
                        {
                            AttackerId = monster.Id,
                            AttackerType = monster.Type,
                            Damage = stats.AttackDamage,
                            TargetId = monster.TargetId,
                        });
                        break;

                    case TargetKind.Hero:
                        var wasAlive = _hero.Alive;
                        _sim.ApplyHeroDamage(new HeroDamageRequest
                        {
                            AttackerId = monster.Id,
                            AttackerType = monster.Type,
                            Damage = stats.AttackDamage,
                            TargetId = monster.TargetId,
                        });
                        if (wasAlive && !_hero.Alive)
                        {
                            _heroDowns++;
                        }

                        break;

                    case TargetKind.Barricade:
                        _sim.ApplyPlaceableDamage(new PlaceableDamageRequest
                        {
                            AttackerId = monster.Id,
                            AttackerType = monster.Type,
                            Damage = stats.AttackDamage,
                            TargetId = monster.TargetId,
                        });
                        break;
                }
            }
        }

        private bool TryTarget(string targetId, out Vec2 pos, out TargetKind kind)
        {
            pos = new Vec2(0.0, 0.0);
            kind = TargetKind.Hotspot;

            if (_state.Heroes.TryGetValue(targetId, out var hero))
            {
                pos = hero.Pos;
                kind = TargetKind.Hero;
                return hero.Alive;
            }

            if (_state.Hotspots.TryGetValue(targetId, out var hotspot))
            {
                pos = hotspot.Pos;
                kind = TargetKind.Hotspot;
                return true;
            }

            if (_state.Placeables.TryGetValue(targetId, out var placeable))
            {
                pos = placeable.Pos;
                kind = TargetKind.Barricade;
                return placeable.Exists;
            }

            return false;
        }

        /// <summary>MatchSession.DrivePlaceableCombat, verbatim policy (turrets 1 Hz, trap edges, reap).</summary>
        private void DrivePlaceableCombat()
        {
            if (_state.IsOver || _state.Phase != MatchPhase.Combat)
            {
                _trapOccupancy.Clear();
                _trapOccupancyNow.Clear();
                _turretAccrual = TurretFirePeriodSeconds;
                return;
            }

            _turretAccrual += StepSeconds;
            while (_turretAccrual >= TurretFirePeriodSeconds)
            {
                foreach (var turretId in _state.Placeables.Values
                             .Where(p => p.Exists && p.Type == PlaceableType.Turret)
                             .Select(p => p.Id).ToList())
                {
                    _sim.TurretTick(turretId);
                }

                _turretAccrual -= TurretFirePeriodSeconds;
            }

            DetectTrapCrossings();
            ReapDeadOnRoster(creditPlacer: true);
        }

        private void DetectTrapCrossings()
        {
            _trapOccupancyNow.Clear();
            var radius = _sim.PlaceableFootprintRadius;

            var traps = _state.Placeables.Values
                .Where(p => p.Exists
                            && (p.Type == PlaceableType.SpikeTrap || p.Type == PlaceableType.DynamiteTrap))
                .Select(p => p.Id).ToList();
            var living = _state.Monsters.Values.Where(m => m.Alive).Select(m => m.Id).ToList();

            foreach (var trapId in traps)
            {
                if (!_state.Placeables.TryGetValue(trapId, out var trap) || !trap.Exists)
                {
                    continue;
                }

                foreach (var monsterId in living)
                {
                    if (!_state.Monsters.TryGetValue(monsterId, out var monster) || !monster.Alive)
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

                    _sim.TriggerPlaceable(trapId, monsterId);

                    if (!_state.Placeables.TryGetValue(trapId, out trap) || !trap.Exists)
                    {
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

        /// <summary>
        /// The shell's kill accounting, both halves: placeable kills (Alive already false) and
        /// hero-basic kills (Hp 0, Alive still true). XP credit goes to the one account either way.
        /// </summary>
        private void ReapDeadOnRoster(bool creditPlacer)
        {
            _scratch.Clear();
            _scratch.AddRange(_state.Wave.LivingMonsterIds);

            foreach (var monsterId in _scratch)
            {
                if (!_state.Monsters.TryGetValue(monsterId, out var monster) || monster == null)
                {
                    continue;
                }

                var placeableKill = !monster.Alive;
                var heroKill = monster.Alive && monster.Hp <= 0.0;
                if (!placeableKill && !heroKill)
                {
                    continue;
                }

                var stats = _config.Monsters.TryGet(monster.Type);
                var kill = new MonsterKillRequest
                {
                    MonsterId = monsterId,
                    MonsterType = monster.Type,
                    Bounty = stats == null ? 0 : stats.Bounty,
                    KillerHeroId = _hero.Id,
                };

                _sim.RecordMonsterKill(kill);
                _sim.AwardKillXp(kill, AccountId);
            }
        }

        /// <summary>The bot player: hold SPACE, perfect aim at the nearest living monster in reach.</summary>
        private void FireHeroBasics()
        {
            if (_state.IsOver || _state.Phase != MatchPhase.Combat || !_hero.Alive)
            {
                _attackClock = 0.0;
                return;
            }

            _attackClock += StepSeconds;
            if (_attackClock < AttackCadenceSeconds)
            {
                return;
            }

            _attackClock -= AttackCadenceSeconds;

            Monster nearest = null;
            var best = double.MaxValue;
            foreach (var monster in _state.Monsters.Values)
            {
                if (!monster.Alive)
                {
                    continue;
                }

                var distance = _hero.Pos.DistanceTo(monster.Pos);
                if (distance < best)
                {
                    best = distance;
                    nearest = monster;
                }
            }

            if (nearest == null || best > AimLineLength)
            {
                return;
            }

            var kit = _config.HeroKits.KitFor(_hero.HeroClass);
            _sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = _hero.Id,
                AttackerClass = _hero.HeroClass,
                Damage = kit.BasicAttackDamage,
                EntitiesOnLine = new List<LineEntity>
                {
                    new LineEntity { Id = nearest.Id, Kind = "monster", Pos = nearest.Pos },
                },
            });

            ReapDeadOnRoster(creditPlacer: false);
        }

        /// <summary>MatchSession.AdvanceTheCampaign, verbatim policy.</summary>
        private void AdvanceTheCampaign()
        {
            if (_state.IsOver)
            {
                return;
            }

            if (_state.Phase == MatchPhase.Planning
                && _state.Wave.Number == _waveInTheColony
                && _state.Wave.LivingMonsterIds.Count == 0)
            {
                _sim.BeginPlanningPhase();
                return;
            }

            if (_state.Phase == MatchPhase.Combat && _state.Wave.Number != _waveInTheColony)
            {
                _waveInTheColony = _state.Wave.Number;
                _sim.SpawnWave(_waveInTheColony);
            }
        }

        /// <summary>
        /// The bot's planning turn, once per planning phase: spend the pool per the policy, then
        /// ready up (R-03's early exit — a solo party's ready ends planning immediately).
        /// </summary>
        private void ShopAndReadyDuringPlanning()
        {
            if (_state.IsOver || _state.Phase != MatchPhase.Planning)
            {
                return;
            }

            if (_planningShoppedForWave != _state.Wave.Number)
            {
                _planningShoppedForWave = _state.Wave.Number;
                Shop();
            }

            _sim.SetPlayerReady(_playerId);
        }

        private void Shop()
        {
            if (_buySpikes)
            {
                // A spike line just outside every breach mouth: the wave walks in over it.
                foreach (var tunnel in _map.EntryTunnels)
                {
                    BuyAt(PlaceableType.SpikeTrap, PulledToward(tunnel, _map.TeamSpawn, 4.0));
                }
            }

            if (_buyTurrets)
            {
                // Turret nests beside each shelter (outside the R-24 building exclusion), plus one
                // by the spawn. Range 8 covers the shelter a wave is chewing.
                foreach (var hotspot in _map.Hotspots)
                {
                    BuyAt(PlaceableType.Turret, PulledToward(hotspot.Pos, _map.TeamSpawn, 4.5));
                }

                BuyAt(PlaceableType.Turret, new Vec2(0.0, 3.5));
            }
        }

        /// <summary>Buy one placeable near <paramref name="pos"/>, nudging until the zone accepts.</summary>
        private void BuyAt(string type, Vec2 pos)
        {
            var stats = _config.Placeables.StatsFor(type);
            if (_state.Team.Scrip < stats.Cost)
            {
                return;
            }

            // Standing one of the same type near the spot already? Then this slot is served.
            foreach (var standing in _state.Placeables.Values)
            {
                if (standing.Exists && standing.Type == type && standing.Pos.DistanceTo(pos) < 4.0)
                {
                    return;
                }
            }

            for (var nudge = 0; nudge < 8; nudge++)
            {
                var candidate = new Vec2(
                    pos.X + (nudge % 3 - 1) * 3.2,
                    pos.Y + (nudge / 3 - 1) * 3.2);

                var result = _sim.PurchasePlacement(new PurchaseRequest
                {
                    PlayerId = _playerId,
                    PlaceableType = type,
                    Cost = stats.Cost,
                    Pos = candidate,
                });

                if (result.Accepted)
                {
                    return;
                }
            }
        }

        private static Vec2 PulledToward(Vec2 from, Vec2 toward, double distance)
        {
            var dx = toward.X - from.X;
            var dy = toward.Y - from.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            if (magnitude <= 0.0)
            {
                return from;
            }

            return new Vec2(from.X + (dx / magnitude * distance), from.Y + (dy / magnitude * distance));
        }
    }
}
