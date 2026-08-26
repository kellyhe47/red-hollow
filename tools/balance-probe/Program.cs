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

            RunPolicy("hero only (no purchases)",
                buyTurrets: false, buySpikes: false, buyWalls: false, useAbilities: false, reposition: false);
            RunPolicy("hero + turrets",
                buyTurrets: true, buySpikes: false, buyWalls: false, useAbilities: false, reposition: false);
            RunPolicy("hero + turrets + spikes",
                buyTurrets: true, buySpikes: true, buyWalls: false, useAbilities: false, reposition: false);
            RunPolicy("hero + walls + turrets + spikes",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: false, reposition: false);
            RunPolicy("full kit: walls/turrets/spikes + abilities + positioning",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true, reposition: true);
            RunPolicy("skilled: full kit + threat-priority aim, anchored at spawn",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true, reposition: false,
                threatAim: true);
            RunPolicy("TWO players, skilled full kit (the R-50 duo)",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true, reposition: false,
                threatAim: true, heroCount: 2);

            // ---- tuning candidates (probe-only; shipped defaults untouched) -------------------
            // R-19's own fixed points survive every candidate: wave 1 stays ~6 shamblers from one
            // breach, Behemoths still arrive at wave 5, wave 10 stays ~30 mixed from all four.
            // Only the free middle (waves 6-9) is trimmed.
            foreach (var trim in new[] { 0.25, 0.40 })
            {
                RunPolicy(
                    "TUNING candidate: solo skilled, waves 6-9 trimmed "
                    + (int)(trim * 100) + "%",
                    buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true,
                    reposition: false, threatAim: true, heroCount: 1, midgameTrim: trim);
            }

            RunPolicy("TUNING check: duo on the 25% trim (must stay a win)",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true,
                reposition: false, threatAim: true, heroCount: 2, midgameTrim: 0.25);

            RunPolicy("solo skilled + SPENDTHRIFT economy (dynamite rebuys, turret rings)",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true,
                reposition: false, threatAim: true, heroCount: 1, spendDown: true);

            RunPolicy("solo skilled + spendthrift + 25% midgame trim",
                buyTurrets: true, buySpikes: true, buyWalls: true, useAbilities: true,
                reposition: false, threatAim: true, heroCount: 1, midgameTrim: 0.25,
                spendDown: true);
        }

        private static void RunPolicy(
            string name, bool buyTurrets, bool buySpikes, bool buyWalls, bool useAbilities,
            bool reposition, bool threatAim = false, int heroCount = 1, double midgameTrim = 0.0,
            bool spendDown = false)
        {
            Console.WriteLine("== policy: " + name + " ==");
            var run = new SoloRun(
                buyTurrets, buySpikes, buyWalls, useAbilities, reposition, threatAim, heroCount,
                midgameTrim, spendDown);
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
        private readonly bool _buyWalls;
        private readonly bool _useAbilities;
        private readonly bool _reposition;
        private readonly bool _threatAim;
        private readonly int _heroCount;
        private readonly bool _spendDown;

        private readonly ColonyMap _map = ColonyMap.V1();
        private readonly SimConfig _config = new SimConfig();
        private readonly SimClock _clock = new SimClock();
        private readonly MatchState _state;
        private readonly MatchSim _sim;

        /// <summary>One scripted player: their hero plus the per-player clocks the shell keeps.</summary>
        private sealed class BotPlayer
        {
            public Hero Hero;
            public string PlayerId;
            public string AccountId;
            public double AttackClock;
            public double QCooldownUntil;
            public double ECooldownUntil;
        }

        private readonly List<BotPlayer> _bots = new List<BotPlayer>();
        private readonly InMemoryProfileStore _profiles = new InMemoryProfileStore();

        // ---- mirrors of the shell's per-step state ----
        private double _turretAccrual = TurretFirePeriodSeconds;
        private readonly HashSet<string> _trapOccupancy = new HashSet<string>();
        private readonly HashSet<string> _trapOccupancyNow = new HashSet<string>();
        private readonly List<string> _scratch = new List<string>();
        private int _waveInTheColony;
        private int _planningShoppedForWave;

        // ---- per-wave report ----
        private int _heroDowns;

        public SoloRun(
            bool buyTurrets, bool buySpikes, bool buyWalls, bool useAbilities, bool reposition,
            bool threatAim = false, int heroCount = 1, double midgameTrim = 0.0,
            bool spendDown = false)
        {
            _buyTurrets = buyTurrets;
            _buySpikes = buySpikes;
            _buyWalls = buyWalls;
            _useAbilities = useAbilities;
            _reposition = reposition;
            _threatAim = threatAim;
            _heroCount = heroCount;
            _spendDown = spendDown;

            _state = _map.CreateMatchState(_config);
            _state.Wave.TotalWaves = _config.TotalWaves;
            _state.Phase = MatchPhase.Combat;
            _state.Status = MatchStatus.InProgress;

            // The production wiring (ColonyMatchFactory): walls actually block lanes.
            var pathOracle = new BarricadePathOracle(_state);
            _sim = new MatchSim(_state, _config, _profiles, _clock, pathOracle) { ColonyMap = _map };
            pathOracle.BlockingRadius = _sim.PlaceableFootprintRadius;

            if (midgameTrim > 0.0)
            {
                _sim.WaveTable = TrimmedMidgame(midgameTrim);
            }

            var kit = _config.HeroKits.KitFor(HeroClass.Gunslinger);
            for (var i = 1; i <= heroCount; i++)
            {
                var accountId = "acc_probe_" + i;
                var hero = new Hero
                {
                    Id = "hero_" + i,
                    HeroClass = HeroClass.Gunslinger,
                    AccountId = accountId,
                    Pos = _map.TeamSpawn,
                    Hp = kit.MaxHp,
                    MaxHp = kit.MaxHp,
                    Alive = true,
                };
                _state.Heroes[hero.Id] = hero;
                _state.Players.Add(new PlayerSlot
                {
                    Id = "player_" + i,
                    AccountId = accountId,
                    HeroClass = HeroClass.Gunslinger,
                    Ready = false,
                    Connected = true,
                });

                _bots.Add(new BotPlayer
                {
                    Hero = hero,
                    PlayerId = "player_" + i,
                    AccountId = accountId,
                });
            }
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

            // ---- ShellBootstrap.HandleCombatActions (one per seated bot player) ----
            foreach (var bot in _bots)
            {
                RepositionHero(bot);
                FireHeroBasics(bot);
                CastAbilities(bot);
            }

            // ---- MatchSession.AdvanceTheCampaign ----
            AdvanceTheCampaign();

            // ---- the bot's planning turn (shop, then ready up) ----
            ShopAndReadyDuringPlanning();
        }

        private void TickRespawnsCountingDowns()
        {
            _sim.TickHeroRespawns();
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
                        var victim = _state.Heroes.TryGetValue(monster.TargetId, out var struck)
                            ? struck
                            : null;
                        var wasAlive = victim != null && victim.Alive;
                        _sim.ApplyHeroDamage(new HeroDamageRequest
                        {
                            AttackerId = monster.Id,
                            AttackerType = monster.Type,
                            Damage = stats.AttackDamage,
                            TargetId = monster.TargetId,
                        });
                        if (wasAlive && victim != null && !victim.Alive)
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
            ReapDeadOnRoster(null);
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
        /// hero-basic kills (Hp 0, Alive still true). XP credits the acting player, or the first
        /// seat for placeable kills (owner credit is exercised by the EditMode suite; the probe
        /// only needs the bounty and the level curve to flow).
        /// </summary>
        private void ReapDeadOnRoster(BotPlayer credited)
        {
            credited = credited ?? _bots[0];

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
                    KillerHeroId = credited.Hero.Id,
                };

                _sim.RecordMonsterKill(kill);
                _sim.AwardKillXp(kill, credited.AccountId);
            }
        }

        /// <summary>One bot player's held SPACE: perfect aim, the shell's cadence and reap.</summary>
        private void FireHeroBasics(BotPlayer bot)
        {
            var hero = bot.Hero;
            if (_state.IsOver || _state.Phase != MatchPhase.Combat || !hero.Alive)
            {
                bot.AttackClock = 0.0;
                return;
            }

            bot.AttackClock += StepSeconds;
            if (bot.AttackClock < AttackCadenceSeconds)
            {
                return;
            }

            bot.AttackClock -= AttackCadenceSeconds;

            var line = AimLineToNearest(hero);
            if (line.Count == 0)
            {
                return;
            }

            var kit = _config.HeroKits.KitFor(hero.HeroClass);
            _sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = hero.Id,
                AttackerClass = hero.HeroClass,
                Damage = kit.BasicAttackDamage,
                EntitiesOnLine = line,
            });

            ReapDeadOnRoster(bot);
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

            foreach (var bot in _bots)
            {
                _sim.SetPlayerReady(bot.PlayerId);
            }
        }

        private void Shop()
        {
            SpendSkillPoints();

            // R-05 — planning knows which breaches the coming wave opens; a real player builds there.
            var preview = _sim.PreviewUpcomingWave();
            var activeMouths = preview.ActiveEntryTunnels
                .Where(i => i >= 0 && i < _map.EntryTunnels.Count)
                .Select(i => _map.EntryTunnels[i])
                .ToList();

            if (_buyWalls)
            {
                // A wall just outside each ACTIVE breach (3.5 clears the 3.0 mouth exclusion):
                // the wave redirects onto it (R-16 via the production oracle) and stalls in the
                // turret/spike kill box instead of walking at a shelter.
                foreach (var mouth in activeMouths)
                {
                    BuyAt(PlaceableType.Barricade, PulledToward(mouth, _map.TeamSpawn, 3.5));
                }
            }

            if (_buySpikes)
            {
                // A spike line just inside every active breach: the wave walks in over it.
                foreach (var mouth in activeMouths)
                {
                    BuyAt(PlaceableType.SpikeTrap, PulledToward(mouth, _map.TeamSpawn, 7.0));
                }
            }

            if (_buyTurrets)
            {
                // Turret nests beside each shelter (outside the R-24 building exclusion), plus one
                // by the spawn. Range 8 covers the shelter a wave is chewing — and the wall a
                // redirected wave is chewing sits in range of the breach-side nests.
                foreach (var hotspot in _map.Hotspots)
                {
                    BuyAt(PlaceableType.Turret, PulledToward(hotspot.Pos, _map.TeamSpawn, 4.5));
                }

                BuyAt(PlaceableType.Turret, new Vec2(0.0, 3.5));
            }

            if (_spendDown)
            {
                SpendTheRestOfThePool(activeMouths);
            }
        }

        /// <summary>
        /// The spendthrift late-game shop: single-use dynamite is re-laid at every active breach
        /// each planning, then the rest of the pool goes into second-ring turrets around the
        /// shelters and mid-lane. A real player banking 400+ scrip into a finale defeat is the
        /// bot's own report from the un-spent runs; this is what spending it looks like.
        /// </summary>
        private void SpendTheRestOfThePool(List<Vec2> activeMouths)
        {
            // Dynamite first: single-use, so every planning re-lays the blast line.
            foreach (var mouth in activeMouths)
            {
                BuyAt(PlaceableType.DynamiteTrap, PulledToward(mouth, _map.TeamSpawn, 5.2));
            }

            // Then turrets until the pool runs dry: a second ring at the shelters, mid-lane
            // nests toward each breach, and a spawn cluster.
            var turretSpots = new List<Vec2>();
            foreach (var hotspot in _map.Hotspots)
            {
                turretSpots.Add(PulledToward(hotspot.Pos, _map.TeamSpawn, 8.0));
            }

            foreach (var mouth in _map.EntryTunnels)
            {
                turretSpots.Add(PulledToward(mouth, _map.TeamSpawn, 12.0));
                turretSpots.Add(PulledToward(mouth, _map.TeamSpawn, 18.0));
            }

            turretSpots.Add(new Vec2(-3.5, 0.0));
            turretSpots.Add(new Vec2(3.5, 0.0));
            turretSpots.Add(new Vec2(0.0, -3.5));

            foreach (var spot in turretSpots)
            {
                if (_state.Team.Scrip < _config.Placeables.StatsFor(PlaceableType.Turret).Cost)
                {
                    return;
                }

                BuyAt(PlaceableType.Turret, spot);
            }
        }

        /// <summary>
        /// R-42 — a real player spends level-ups: unlock Q, unlock E, then rank both to the cap.
        /// Rejections (no points, capped) are ordinary results, so the loop just walks the wishlist.
        /// </summary>
        private void SpendSkillPoints()
        {
            if (!_useAbilities)
            {
                return;
            }

            var wishlist = new[]
            {
                "unlock_Q", "unlock_E", "rank_Q", "rank_E", "rank_Q", "rank_E", "rank_Q", "rank_E",
            };

            foreach (var bot in _bots)
            {
                foreach (var choice in wishlist)
                {
                    var profile = _profiles.Load(bot.AccountId);
                    if (profile.SkillPoints <= 0)
                    {
                        break;
                    }

                    _sim.SpendSkillPoint(new SpendSkillPointRequest
                    {
                        AccountId = bot.AccountId,
                        HeroId = bot.Hero.Id,
                        Choice = choice,
                    });
                }
            }
        }

        /// <summary>
        /// The bot's positioning: hold the busiest lane at a rifleman's distance — walk toward the
        /// living wave's centre of mass, but back off if the nearest monster is inside 8 ground
        /// units. Direction only; the sim owns the pace (R-30).
        /// </summary>
        private void RepositionHero(BotPlayer bot)
        {
            var hero = bot.Hero;
            if (!_reposition || _state.IsOver || _state.Phase != MatchPhase.Combat || !hero.Alive)
            {
                return;
            }

            Monster nearest = null;
            var nearestDistance = double.MaxValue;
            double cx = 0.0, cy = 0.0;
            var living = 0;
            foreach (var monster in _state.Monsters.Values)
            {
                if (!monster.Alive)
                {
                    continue;
                }

                living++;
                cx += monster.Pos.X;
                cy += monster.Pos.Y;

                var distance = hero.Pos.DistanceTo(monster.Pos);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }

            if (living == 0)
            {
                return;
            }

            Vec2 direction;
            if (nearest != null && nearestDistance < 8.0)
            {
                // Back off the closest attacker.
                direction = new Vec2(hero.Pos.X - nearest.Pos.X, hero.Pos.Y - nearest.Pos.Y);
            }
            else if (nearest != null && nearestDistance > 14.0)
            {
                // Close toward the wave's centre of mass to shorten the intercept.
                direction = new Vec2((cx / living) - hero.Pos.X, (cy / living) - hero.Pos.Y);
            }
            else
            {
                return;
            }

            var magnitude = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
            if (magnitude <= 0.0)
            {
                return;
            }

            _sim.MoveHero(new HeroMoveRequest
            {
                HeroId = hero.Id,
                Direction = new Vec2(direction.X / magnitude, direction.Y / magnitude),
                DeltaSeconds = StepSeconds,
            });
        }

        /// <summary>
        /// Q/E on cooldown at the nearest cluster, with an HONEST aim line: only monsters inside
        /// the shell's 1.5-wide corridor toward the nearest monster ride it. Client-side cooldown
        /// tracking mirrors a player watching the HUD; a mistimed cast is just a rejection.
        /// </summary>
        private void CastAbilities(BotPlayer bot)
        {
            var hero = bot.Hero;
            if (!_useAbilities || _state.IsOver || _state.Phase != MatchPhase.Combat || !hero.Alive)
            {
                return;
            }

            var now = _clock.ElapsedSeconds;
            var line = AimLineToNearest(hero);
            if (line.Count == 0)
            {
                return;
            }

            var aim = line[0].Pos;
            var dx = aim.X - hero.Pos.X;
            var dy = aim.Y - hero.Pos.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            var aimDirection = magnitude > 0.0 ? new Vec2(dx / magnitude, dy / magnitude) : new Vec2(0.0, 0.0);

            if (now >= bot.QCooldownUntil)
            {
                var outcome = _sim.CastAbility(new HeroAbilityRequest
                {
                    CasterId = hero.Id,
                    Slot = AbilitySlot.Q,
                    AimDirection = aimDirection,
                    EntitiesOnLine = line,
                });
                if (outcome != null && outcome.Accepted)
                {
                    bot.QCooldownUntil = now + _config.HeroKits.KitFor(hero.HeroClass).QCooldownSeconds;
                    ReapDeadOnRoster(bot);
                }
                else
                {
                    bot.QCooldownUntil = now + 1.0;
                }
            }

            if (now >= bot.ECooldownUntil)
            {
                var outcome = _sim.CastAbility(new HeroAbilityRequest
                {
                    CasterId = hero.Id,
                    Slot = AbilitySlot.E,
                    AimDirection = aimDirection,
                    EntitiesOnLine = line,
                });
                if (outcome != null && outcome.Accepted)
                {
                    bot.ECooldownUntil = now + _config.HeroKits.KitFor(hero.HeroClass).ECooldownSeconds;
                    ReapDeadOnRoster(bot);
                }
                else
                {
                    bot.ECooldownUntil = now + 1.0;
                }
            }
        }

        /// <summary>
        /// The shell's aim-line geometry (length 30, full width 1.5) toward the nearest living
        /// monster: every living monster inside the corridor, nearest first — exactly what
        /// AimLine.EntitiesAlong reports when the cursor tracks the closest body.
        /// </summary>
        private List<LineEntity> AimLineToNearest(Hero hero)
        {
            var nearest = _threatAim ? MostUrgentMonster(hero) : NearestMonsterToHero(hero);

            var line = new List<LineEntity>();
            if (nearest == null || hero.Pos.DistanceTo(nearest.Pos) > AimLineLength)
            {
                return line;
            }

            var dx = nearest.Pos.X - hero.Pos.X;
            var dy = nearest.Pos.Y - hero.Pos.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            if (magnitude <= 0.0)
            {
                line.Add(new LineEntity { Id = nearest.Id, Kind = "monster", Pos = nearest.Pos });
                return line;
            }

            var ux = dx / magnitude;
            var uy = dy / magnitude;

            var candidates = new List<(double Along, Monster Monster)>();
            foreach (var monster in _state.Monsters.Values)
            {
                if (!monster.Alive)
                {
                    continue;
                }

                var relX = monster.Pos.X - hero.Pos.X;
                var relY = monster.Pos.Y - hero.Pos.Y;
                var along = (relX * ux) + (relY * uy);
                if (along <= 0.0 || along > AimLineLength)
                {
                    continue;
                }

                var lateral = Math.Abs((relX * -uy) + (relY * ux));
                if (lateral > 0.75)
                {
                    continue;
                }

                candidates.Add((along, monster));
            }

            candidates.Sort((a, b) => a.Along.CompareTo(b.Along));
            foreach (var candidate in candidates)
            {
                line.Add(new LineEntity
                {
                    Id = candidate.Monster.Id,
                    Kind = "monster",
                    Pos = candidate.Monster.Pos,
                });
            }

            return line;
        }

        private Monster NearestMonsterToHero(Hero hero)
        {
            Monster nearest = null;
            var best = double.MaxValue;
            foreach (var monster in _state.Monsters.Values)
            {
                if (!monster.Alive)
                {
                    continue;
                }

                var distance = hero.Pos.DistanceTo(monster.Pos);
                if (distance < best)
                {
                    best = distance;
                    nearest = monster;
                }
            }

            return nearest;
        }

        /// <summary>
        /// What a skilled player shoots first: the monster closest to LANDING damage — smallest
        /// gap to its own target (a shelter about to lose civilians outranks a walker in the open;
        /// a monster chewing a wall can wait unless nothing else is closer to hurting anyone).
        /// Ties toward the hero so the reticle does not flick across the map for equal threats.
        /// </summary>
        private Monster MostUrgentMonster(Hero shooter)
        {
            Monster best = null;
            var bestUrgency = double.MaxValue;
            var bestHeroDistance = double.MaxValue;

            foreach (var monster in _state.Monsters.Values)
            {
                if (!monster.Alive || shooter.Pos.DistanceTo(monster.Pos) > AimLineLength)
                {
                    continue;
                }

                var urgency = double.MaxValue;
                if (!string.IsNullOrEmpty(monster.TargetId))
                {
                    // Chewing a wall is the defence WORKING; weight it far below a shelter run.
                    if (_state.Hotspots.TryGetValue(monster.TargetId, out var hotspot))
                    {
                        urgency = monster.Pos.DistanceTo(hotspot.Pos);
                    }
                    else if (_state.Heroes.TryGetValue(monster.TargetId, out var hero))
                    {
                        urgency = monster.Pos.DistanceTo(hero.Pos) + 20.0;
                    }
                    else if (_state.Placeables.TryGetValue(monster.TargetId, out var placeable))
                    {
                        urgency = monster.Pos.DistanceTo(placeable.Pos) + 40.0;
                    }
                }

                var heroDistance = shooter.Pos.DistanceTo(monster.Pos);
                if (urgency < bestUrgency
                    || (urgency == bestUrgency && heroDistance < bestHeroDistance))
                {
                    bestUrgency = urgency;
                    bestHeroDistance = heroDistance;
                    best = monster;
                }
            }

            return best;
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
                    PlayerId = _bots[0].PlayerId,
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

        /// <summary>
        /// A tuning-candidate table: the shipped campaign with waves 6-9's headcounts scaled down
        /// by <paramref name="trim"/> (floored, never below 1 of a group). R-19's fixed points are
        /// untouched: wave 1's opener, the wave-5 Behemoth debut, and the ~30-strong four-breach
        /// finale all ship exactly as authored.
        /// </summary>
        private static WaveTable TrimmedMidgame(double trim)
        {
            var table = WaveTable.V1();
            foreach (var wave in table.Waves)
            {
                if (wave.Number < 6 || wave.Number > 9)
                {
                    continue;
                }

                foreach (var group in wave.Groups)
                {
                    var trimmed = (int)Math.Floor(group.Count * (1.0 - trim));
                    group.Count = Math.Max(1, trimmed);
                }
            }

            return table;
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
