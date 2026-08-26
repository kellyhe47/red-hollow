using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 025 (T-25) — combat action routing. The final playability audit (2026-08-26) found
    /// that <see cref="DefaultHeroInputMap"/> produces BasicAttack/Ability intents that NOTHING
    /// consumes: in Play, SPACE/Q/E are dead and the player cannot kill a monster. These tests pin
    /// the missing routing at the same seam every other input ticket used — a fake
    /// <see cref="IInputSource"/> driven through <see cref="ShellBootstrap.Pump"/>:
    ///
    ///  1. <b>Aim-line geometry</b> (<see cref="AimLine"/>, pure over <see cref="MatchState"/>):
    ///     the nearest-first <see cref="LineEntity"/> list the sim's requests carry. The report is
    ///     HONEST — friendlies on the line are listed with their fixture kind and the SIM's
    ///     monster allowlist is what keeps them unhurt (R-34); the only omissions are entities no
    ///     longer in the world (the attacker itself, dead monsters, broken placeables — verified
    ///     necessary: <c>MatchSim.FirstMonsterOnLine</c> does not re-check <c>Alive</c>, so an
    ///     offered corpse would soak basics forever).
    ///  2. <b>Basic attack</b> (R-30/R-26): held SPACE fires <c>ResolveHeroAttack</c> requests at
    ///     the configured cadence — the press fires immediately (the same pump-edge semantics as
    ///     T-24's click), holding re-fires once per window, planning fires nothing — with
    ///     <c>Damage</c> read from the hero-kit catalog (per-pellet for the Rancher, DEC-RUN-8),
    ///     and a monster at 0 HP actually DIES through the pump path (kill accounting: `alive`,
    ///     roster, bounty — HostLoop issues the same command after a turret/trap last-hit).
    ///  3. <b>Abilities</b> (R-31/R-32): Q/E issue ONE <c>HeroAbilityRequest</c> per press-edge
    ///     for the mapped slot; sim-side rejections (ability_locked / ability_cooling) surface on
    ///     <see cref="ShellBootstrap.LastAbilityOutcome"/> without breaking the loop; a second
    ///     press after the cooldown casts again.
    ///  4. <b>No double-consumption</b>: movement (T-22) and the planning pointer (T-24) keep
    ///     working with attack keys in the same snapshot.
    ///
    /// <b>Deliberately NOT pinned</b>: the shipped cadence/length/width numbers (config-shaped
    /// shell policy — <see cref="CombatActionConfig"/>; the tests always pass their own), the
    /// inclusive/exclusive edge of the line's width/length bounds, whether the shell also sets
    /// <c>HeroAbilityRequest.TargetId</c>/<c>AimDirection</c> (the observable is that line-shaped
    /// casts hit along the cursor line — the sim's own fallback covers the rest), the kind
    /// spelling of non-barricade placeables on a line (the sim ignores every non-monster kind),
    /// and ability presses during planning (unspecced; the sim would gate them anyway).
    /// </summary>
    [TestFixture]
    public class T25_CombatActionTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-6;

        /// <summary>The cadence the tests compose the shell with — a test value, not a shipped pin.</summary>
        private const double Cadence = 0.25;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";
        private const string AllyAccount = "acc_doc";

        /// <summary>The well-known roots the shell and the scene builder compose under.</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private ShellBootstrap _shell;
        private InMemoryProfileStore _profiles;
        private FakeInputSource _input;

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            if (_shell != null)
            {
                try
                {
                    _shell.TearDown();
                }
                catch (Exception)
                {
                    // A stub or half-built shell must not turn a red test into a teardown error.
                }

                _shell = null;
            }

            foreach (var name in ShellRootNames)
            {
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ==========================================================================================
        //  1 — aim-line geometry: pure over MatchState, ordered nearest-first
        // ==========================================================================================

        /// <summary>
        /// The one ordering contract <c>HeroAttackRequest.EntitiesOnLine</c> documents: nearest
        /// first. The sim resolves "the nearest monster on the line" by taking the FIRST matching
        /// entry, so a shell that reported farthest-first would silently shoot the wrong body.
        /// </summary>
        [Test]
        public void Entities_on_the_line_come_back_nearest_first()
        {
            var state = new MatchState();
            state.Monsters["m_far"] = MonsterAt(new Vec2(10.0, 0.0), "m_far");
            state.Monsters["m_near"] = MonsterAt(new Vec2(4.0, 0.0), "m_near");
            state.Monsters["m_mid"] = MonsterAt(new Vec2(7.0, 0.0), "m_mid");

            var line = AimLine.EntitiesAlong(
                state, "h_me", new Vec2(0.0, 0.0), new Vec2(20.0, 0.0), length: 30.0, width: 2.0);

            Assert.That(line.Select(e => e.Id), Is.EqualTo(new[] { "m_near", "m_mid", "m_far" }),
                "T-25: EntitiesOnLine is ordered nearest-first from the attacker — the sim takes "
                + "the FIRST monster in the list as the hit");
        }

        /// <summary>
        /// The line is a bounded corridor, not an infinite ray: entities beside it (outside the
        /// width), beyond its length, or BEHIND the attacker are not on it. The exact boundary
        /// semantics at width/2 and at length are unpinned — the data sits clear of the edges.
        /// </summary>
        [Test]
        public void Only_entities_inside_the_lines_width_and_length_are_on_it()
        {
            var state = new MatchState();
            state.Monsters["m_on"] = MonsterAt(new Vec2(5.0, 0.0), "m_on");
            state.Monsters["m_off_side"] = MonsterAt(new Vec2(5.0, 3.0), "m_off_side");
            state.Monsters["m_near_axis"] = MonsterAt(new Vec2(8.0, 0.4), "m_near_axis");
            state.Monsters["m_beyond"] = MonsterAt(new Vec2(35.0, 0.0), "m_beyond");
            state.Monsters["m_behind"] = MonsterAt(new Vec2(-3.0, 0.0), "m_behind");

            var line = AimLine.EntitiesAlong(
                state, "h_me", new Vec2(0.0, 0.0), new Vec2(20.0, 0.0), length: 30.0, width: 2.0);

            Assert.That(line.Select(e => e.Id), Is.EqualTo(new[] { "m_on", "m_near_axis" }),
                "T-25: a 2-wide, 30-long line along +x holds exactly the two bodies inside the "
                + "corridor — 3 units beside it, 35 units out, and behind the attacker are all off "
                + "the line");
        }

        /// <summary>
        /// The kind spellings are the fixture loader's contract (hero / hotspot / monster /
        /// barricade — ticket 025 handoff): the sim's monster allowlist matches on the literal
        /// "monster", so a shell that spelled a kind its own way would make every attack a miss.
        /// Id and Pos ride along so the sim (and the fixtures) can address what was crossed.
        /// </summary>
        [Test]
        public void Kinds_are_spelled_hero_hotspot_monster_barricade()
        {
            var state = new MatchState();
            state.Heroes["h_me"] = HeroAt(new Vec2(0.0, 0.0), "h_me", HeroClass.Gunslinger);
            state.Heroes["h_other"] = HeroAt(new Vec2(3.0, 0.0), "h_other", HeroClass.Sawbones);
            state.Hotspots["hs_1"] = new Hotspot { Id = "hs_1", Pos = new Vec2(5.0, 0.0), Civilians = 10 };
            state.Monsters["m_1"] = MonsterAt(new Vec2(7.0, 0.0), "m_1");
            state.Placeables["p_1"] = BarricadeAt(new Vec2(9.0, 0.0), "p_1");

            var line = AimLine.EntitiesAlong(
                state, "h_me", new Vec2(0.0, 0.0), new Vec2(20.0, 0.0), length: 30.0, width: 2.0);

            Assert.That(line.Select(e => e.Id), Is.EqualTo(new[] { "h_other", "hs_1", "m_1", "p_1" }),
                "T-25: every entity class the state holds is reported, nearest-first, friendlies "
                + "included — the shell reports honestly and the SIM decides who is hit");
            Assert.That(line.Select(e => e.Kind),
                Is.EqualTo(new[] { "hero", "hotspot", "monster", "barricade" }),
                "T-25: kinds use the fixture spellings — the sim's allowlist matches the literal "
                + "\"monster\" and the fixture loader understands exactly these four");
            Assert.That(line[2].Pos.X, Is.EqualTo(7.0).Within(SimTolerance),
                "T-25: LineEntity.Pos carries the entity's sim position (x)");
            Assert.That(line[2].Pos.Y, Is.EqualTo(0.0).Within(SimTolerance),
                "T-25: LineEntity.Pos carries the entity's sim position (y)");
        }

        /// <summary>
        /// The attacker stands at the line's origin — distance zero, always geometrically "on" its
        /// own line. It must never be reported: the request doc's honesty rule covers the world in
        /// front of the muzzle, not the muzzle.
        /// </summary>
        [Test]
        public void The_attacker_is_never_on_its_own_line()
        {
            var state = new MatchState();
            state.Heroes["h_me"] = HeroAt(new Vec2(0.0, 0.0), "h_me", HeroClass.Gunslinger);
            state.Heroes["h_other"] = HeroAt(new Vec2(5.0, 0.0), "h_other", HeroClass.Rancher);

            var line = AimLine.EntitiesAlong(
                state, "h_me", new Vec2(0.0, 0.0), new Vec2(20.0, 0.0), length: 30.0, width: 2.0);

            Assert.That(line.Select(e => e.Id), Does.Not.Contain("h_me"),
                "T-25: the attacker itself is never on its own line");
            Assert.That(line.Select(e => e.Id), Does.Contain("h_other"),
                "sanity: the OTHER hero on the segment is still reported (kind hero)");
        }

        /// <summary>
        /// Verified against the sim before pinning: <c>MatchSim.FirstMonsterOnLine</c> resolves a
        /// LineEntity by id against <c>State.Monsters</c> WITHOUT re-checking <c>Alive</c>, and
        /// kills leave the corpse in that dictionary. Excluding the dead is therefore the SHELL's
        /// job — an offered corpse would soak every basic while living monsters walk past. Broken
        /// placeables (sold, or spent traps: <c>Exists</c> false) are ground again, same rule.
        /// </summary>
        [Test]
        public void Dead_monsters_and_broken_placeables_are_not_on_the_line()
        {
            var state = new MatchState();
            var corpse = MonsterAt(new Vec2(4.0, 0.0), "m_dead");
            corpse.Alive = false;
            state.Monsters["m_dead"] = corpse;
            state.Monsters["m_alive"] = MonsterAt(new Vec2(8.0, 0.0), "m_alive");
            var rubble = BarricadeAt(new Vec2(6.0, 0.0), "p_broken");
            rubble.Exists = false;
            state.Placeables["p_broken"] = rubble;

            var line = AimLine.EntitiesAlong(
                state, "h_me", new Vec2(0.0, 0.0), new Vec2(20.0, 0.0), length: 30.0, width: 2.0);

            Assert.That(line.Select(e => e.Id), Is.EqualTo(new[] { "m_alive" }),
                "T-25: dead monsters and broken placeables have left the world — the sim does not "
                + "re-check Alive on the line, so the shell must not offer corpses");
        }

        /// <summary>
        /// A cursor parked exactly on the hero gives the line no direction. That is an ordinary
        /// frame, not an error: empty list, no NaN, no throw — sixty times a second.
        /// </summary>
        [Test]
        public void A_zero_length_aim_produces_an_empty_line()
        {
            var state = new MatchState();
            state.Monsters["m_1"] = MonsterAt(new Vec2(2.0, 0.0), "m_1");

            List<LineEntity> line = null;
            Assert.DoesNotThrow(
                () => line = AimLine.EntitiesAlong(
                    state, "h_me", new Vec2(1.0, 1.0), new Vec2(1.0, 1.0), length: 30.0, width: 2.0),
                "T-25: an aim with no direction must not throw mid-frame");
            Assert.That(line, Is.Empty,
                "T-25: no direction means no line and nothing on it");
        }

        /// <summary>
        /// The line's footprint and the attack cadence are SHELL policy, not PRD numbers: pinned
        /// only as config — settable, composed through <see cref="ShellBootstrapOptions"/>, and
        /// exposed by the shell it built (the same accessor pattern as
        /// <see cref="ShellBootstrap.Input"/>). The shipped defaults are pinned as sane (positive,
        /// finite), never as specific values — those are flagged to the owner, not locked.
        /// </summary>
        [Test]
        public void Combat_tunables_are_config_shaped_and_the_shell_exposes_what_it_was_composed_with()
        {
            var defaults = new CombatActionConfig();
            Assert.That(defaults.AttackCadenceSeconds, Is.Positive,
                "T-25: the shipped cadence default is a usable number (value itself unpinned)");
            Assert.That(defaults.AimLineLength, Is.Positive,
                "T-25: the shipped line length default is a usable number (value itself unpinned)");
            Assert.That(defaults.AimLineWidth, Is.Positive,
                "T-25: the shipped line width default is a usable number (value itself unpinned)");

            var custom = new CombatActionConfig
            {
                AttackCadenceSeconds = 0.5,
                AimLineLength = 12.0,
                AimLineWidth = 3.0,
            };

            NewShell(custom);

            Assert.That(_shell.CombatActions, Is.SameAs(custom),
                "T-25: the shell exposes the combat config it was composed with — the seam a "
                + "tuning ticket retunes without touching the routing");
        }

        // ==========================================================================================
        //  2 — the sim's honoring of the line: shell reports, sim filters (R-34)
        // ==========================================================================================

        /// <summary>
        /// The division of labour, end to end against the REAL sim: an ally, a shelter and a
        /// barricade stand between the attacker and a monster. <see cref="AimLine"/> reports all
        /// of them (nearest-first, honest kinds); <c>ResolveHeroAttack</c> hits the FIRST entry
        /// whose kind is monster and nothing else. R-34's "no friendly fire" is the sim's
        /// allowlist, never a shell-side omission — a shell that hid friendlies would also hide
        /// them from every future rule that wants to know they were crossed.
        /// </summary>
        [Test]
        public void The_sim_hits_only_the_first_monster_on_an_honestly_reported_line()
        {
            var factory = new ColonyMatchFactory(null, new SimConfig(), new InMemoryProfileStore());
            var match = factory.CreateMatch(new[]
            {
                new NetPeer { PeerId = HostPeerId, AccountId = HostAccount, HeroClass = HeroClass.Gunslinger, IsHost = true },
                new NetPeer { PeerId = "peer_ally", AccountId = AllyAccount, HeroClass = HeroClass.Sawbones },
            });
            var state = match.State;

            var attacker = state.Heroes.Values.First(h => h.AccountId == HostAccount);
            var ally = state.Heroes.Values.First(h => h.AccountId == AllyAccount);
            attacker.Pos = new Vec2(100.0, 100.0);
            ally.Pos = new Vec2(102.0, 100.0);

            var hotspot = state.Hotspots.Values.First();
            hotspot.Pos = new Vec2(103.0, 100.0);
            var civiliansBefore = hotspot.Civilians;

            state.Placeables["p_line"] = BarricadeAt(new Vec2(104.0, 100.0), "p_line");
            state.Monsters["m_line"] = MonsterAt(new Vec2(105.0, 100.0), "m_line", hp: 200.0);

            var line = AimLine.EntitiesAlong(
                state, attacker.Id, attacker.Pos, new Vec2(110.0, 100.0), length: 30.0, width: 2.0);

            Assert.That(line.Select(e => e.Id),
                Is.EqualTo(new[] { ally.Id, hotspot.Id, "p_line", "m_line" }),
                "T-25: everything the segment crosses is reported nearest-first — the ally "
                + "included, because the shell's report is honest and the SIM does the filtering");

            var kit = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger);
            var allyHpBefore = ally.Hp;
            var barricadeHpBefore = state.Placeables["p_line"].Hp;

            var result = match.Sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = attacker.Id,
                AttackerClass = attacker.HeroClass,
                Damage = kit.BasicAttackDamage,
                EntitiesOnLine = line,
            });

            Assert.That(result.HitId, Is.EqualTo("m_line"),
                "R-26/R-36: the nearest MONSTER takes the hit — three friendlies stand nearer and "
                + "none of them is it");
            Assert.That(state.Monsters["m_line"].Hp,
                Is.EqualTo(200.0 - kit.BasicAttackDamage).Within(SimTolerance),
                "R-30: the monster lost exactly the class's catalog basic damage");
            Assert.That(ally.Hp, Is.EqualTo(allyHpBefore).Within(SimTolerance),
                "R-34: the ally first on the line is unhurt — the sim's allowlist, not a shell omission");
            Assert.That(hotspot.Civilians, Is.EqualTo(civiliansBefore),
                "R-34: the shelter on the line is unhurt");
            Assert.That(state.Placeables["p_line"].Hp, Is.EqualTo(barricadeHpBefore).Within(SimTolerance),
                "R-34: the barricade on the line is unhurt");
        }

        // ==========================================================================================
        //  3 — basic attack routing: held SPACE through the pump, at the configured cadence
        // ==========================================================================================

        /// <summary>
        /// The core of the ticket: SPACE finally kills things. The press fires immediately (the
        /// same pump-edge semantics T-24 gave the mouse — a zero-delta pump routes the input);
        /// holding fires exactly once per cadence window, never once per pump. Damage is read off
        /// the hero-kit catalog, not a literal.
        /// </summary>
        [Test]
        public void Held_SPACE_fires_one_catalog_damage_attack_per_cadence_window()
        {
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(102.0, 100.0);
            target.Hp = 1000.0;
            KillAllBut(match, target.Id);

            var d = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger).BasicAttackDamage;

            _input.Held.Add(PlayerKey.Space);
            AimAt(target);
            _shell.Pump(0.0);

            Assert.That(target.Hp, Is.EqualTo(1000.0 - d).Within(SimTolerance),
                "R-30: pressing SPACE fires one basic attack along the cursor line, for the "
                + "class's catalog damage — the press must not wait out a cadence window");

            PumpHeldAiming(target, steps: 12);   // 0.2 s < the 0.25 s cadence
            Assert.That(target.Hp, Is.EqualTo(1000.0 - d).Within(SimTolerance),
                "T-25: holding SPACE for less than one cadence window re-fires nothing — one "
                + "request per window, never one per pump");

            PumpHeldAiming(target, steps: 4);    // total 0.267 s — one window elapsed
            Assert.That(target.Hp, Is.EqualTo(1000.0 - (2.0 * d)).Within(SimTolerance),
                "T-25: crossing the cadence window while held fires exactly one more attack");

            PumpHeldAiming(target, steps: 15);   // one more full window
            Assert.That(target.Hp, Is.EqualTo(1000.0 - (3.0 * d)).Within(SimTolerance),
                "T-25: the hold keeps re-firing at the cadence, one request per window");
        }

        /// <summary>
        /// Planning fires nothing — proven by the sim's own crit rhythm rather than by absence:
        /// the Gunslinger's every-4th-basic crit counter advances on EVERY issued request, hit or
        /// miss (verified in <c>MatchSim.BasicAttackDamage</c> — the rhythm moves before the line
        /// is read). Two attacks land in wave 1; SPACE is held through planning; the next two
        /// attacks in wave 2 must be #3 (normal) and #4 (the double crit, R-31). Any request
        /// issued during planning would misalign the rhythm and fail this.
        /// </summary>
        [Test]
        public void SPACE_during_planning_issues_no_attack_and_the_crit_rhythm_proves_it()
        {
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(102.0, 100.0);
            target.Hp = 1000.0;
            KillAllBut(match, target.Id);

            var d = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger).BasicAttackDamage;

            // Attacks #1 and #2, in combat.
            _input.Held.Add(PlayerKey.Space);
            AimAt(target);
            _shell.Pump(0.0);
            PumpHeldAiming(target, steps: 16);
            Assert.That(target.Hp, Is.EqualTo(1000.0 - (2.0 * d)).Within(SimTolerance),
                "sanity: exactly two basics (#1, #2) landed in wave 1 — neither is the 4th, so "
                + "neither crits");
            _input.Held.Remove(PlayerKey.Space);

            // Clear the wave and ride S5's hold into planning (T-24's recipe).
            KillWave(match, new[] { target.Id });
            _shell.Pump(0.0);
            var holdSteps = (int)Math.Ceiling(_shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                _shell.Pump(Step60Hz);
            }

            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity (R-04): the interstitial fell back to planning");

            // SPACE held through planning: no monsters exist, but a routed request would still
            // advance the crit rhythm (a miss is a basic too). Two windows' worth of pumps.
            _input.Held.Add(PlayerKey.Space);
            for (var i = 0; i < 30; i++)
            {
                _shell.Pump(Step60Hz);
            }

            _input.Held.Remove(PlayerKey.Space);
            _shell.Pump(0.0);

            // Into wave 2 through the locked ready-up seam.
            _shell.Planning.ReadyUp();
            _shell.Pump(Step60Hz);
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "sanity (R-03): all-ready ended planning early");
            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "sanity (R-19): wave 2 walked in");

            hero.Pos = new Vec2(100.0, 100.0);
            var target2 = FirstLivingMonster(match);
            target2.Pos = new Vec2(102.0, 100.0);
            target2.Hp = 1000.0;
            KillAllBut(match, target2.Id);

            // Attack #3 — NOT the crit. If planning had issued requests this would be #5+ and the
            // rhythm below could not land 2d on the very next window.
            _input.Held.Add(PlayerKey.Space);
            AimAt(target2);
            _shell.Pump(0.0);
            Assert.That(target2.Hp, Is.EqualTo(1000.0 - d).Within(SimTolerance),
                "T-25: the first attack after planning is #3 of the rhythm — held SPACE during "
                + "planning issued NO requests (a planning request would advance the crit counter "
                + "even as a miss)");

            // Attack #4 — the R-31 every-4th crit, for double.
            PumpHeldAiming(target2, steps: 16);
            Assert.That(target2.Hp, Is.EqualTo(1000.0 - d - (2.0 * d)).Within(SimTolerance),
                "R-31: attack #4 crits for double — the rhythm survived planning untouched");
        }

        /// <summary>
        /// "The player cannot kill a monster" is the audit finding, so the kill is the pin — all
        /// the way through: <c>ResolveHeroAttack</c> deliberately does NOT kill at 0 HP (kill
        /// accounting is <c>RecordMonsterKill</c>: `alive`, the wave roster, the R-20 bounty), and
        /// nothing else in the shipped shell issues it. The pump path must, or Play's monsters
        /// stand at 0 HP forever soaking nothing.
        /// </summary>
        [Test]
        public void A_monster_dies_through_the_pump_path_and_pays_its_bounty_once()
        {
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(102.0, 100.0);

            // Keep a second monster alive but OFF the line (30 units beside a 12-wide corridor),
            // so the wave — and combat — outlives the target.
            var spare = match.State.Wave.LivingMonsterIds
                .Where(id => id != target.Id)
                .Select(id => match.State.Monsters[id])
                .First();
            spare.Pos = new Vec2(100.0, 130.0);
            KillAllBut(match, target.Id, spare.Id);

            var kit = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger);
            var bounty = match.Sim.Config.Monsters.StatsFor(target.Type).Bounty;
            var scripBefore = match.State.Team.Scrip;

            // A wave-1 Shambler carries 60 HP against a 25-damage basic: attacks #1–#3 (none the
            // 4th, so none crit) take it to zero.
            Assert.That(target.Hp, Is.LessThanOrEqualTo(3.0 * kit.BasicAttackDamage),
                "sanity: three basics suffice for a wave-1 monster");

            _input.Held.Add(PlayerKey.Space);
            AimAt(target);
            _shell.Pump(0.0);
            PumpHeldAiming(target, steps: 16);   // window 2 → attack #2
            PumpHeldAiming(target, steps: 16);   // window 3 → attack #3
            PumpHeldAiming(target, steps: 4);    // slack for the reap to land on a pump

            Assert.That(target.Hp, Is.EqualTo(0.0).Within(SimTolerance),
                "R-30: three basics through the pump path emptied the monster's HP");
            Assert.That(target.Alive, Is.False,
                "T-25: a monster at 0 HP DIES through the pump path — the routing issues the kill "
                + "accounting, since ResolveHeroAttack deliberately never does");
            Assert.That(match.State.Wave.LivingMonsterIds, Does.Not.Contain(target.Id),
                "R-02: the kill left the wave roster, so wave progress can complete");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore + bounty),
                "R-20: the kill paid its catalog bounty into the shared pool exactly once");
        }

        /// <summary>
        /// DEC-RUN-8: the Rancher's catalog number is the PER-PELLET quantum — one
        /// <c>ResolveHeroAttack</c> per trigger pull carrying <c>Damage</c> = the kit value, with
        /// pellet connection resolved shell-side as line geometry and the "hits up to 2 targets"
        /// spread applied SIM-side to the second monster on the same line. A shell that sent the
        /// PRD row's x5 total would quintuple every hit.
        /// </summary>
        [Test]
        public void Rancher_basic_damage_is_the_per_pellet_kit_value_and_the_spread_rides_the_line()
        {
            var match = StartSoloMatch(HeroClass.Rancher);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var living = match.State.Wave.LivingMonsterIds
                .Select(id => match.State.Monsters[id]).ToList();
            Assert.That(living.Count, Is.GreaterThanOrEqualTo(2),
                "sanity: wave 1 offers two bodies for the spread");
            var near = living[0];
            var far = living[1];
            near.Pos = new Vec2(103.0, 100.0);
            near.Hp = 500.0;
            far.Pos = new Vec2(106.0, 100.0);
            far.Hp = 500.0;
            KillAllBut(match, near.Id, far.Id);

            var perPellet = match.Sim.Config.HeroKits.KitFor(HeroClass.Rancher).BasicAttackDamage;

            _input.Held.Add(PlayerKey.Space);
            _input.Cursor = new Vector2(110f, 100f);   // through both bodies
            _shell.Pump(0.0);

            Assert.That(near.Hp, Is.EqualTo(500.0 - perPellet).Within(SimTolerance),
                "DEC-RUN-8: the nearest monster lost exactly the PER-PELLET catalog value — not "
                + "the PRD row's x5 trigger-pull total");
            Assert.That(far.Hp, Is.EqualTo(500.0 - perPellet).Within(SimTolerance),
                "R-31: the Rancher spread carried the same per-pellet damage to the second monster "
                + "on the line — sim-side, off the honest line report");
        }

        // ==========================================================================================
        //  4 — ability routing: Q/E press-edges into HeroAbilityRequest
        // ==========================================================================================

        /// <summary>
        /// Q is a press-EDGE, not a level: one <c>HeroAbilityRequest</c> with slot Q per press,
        /// resolved by the sim from the same honest aim line. Holding across pumps must not spam
        /// commands every frame — the sim would reject the spam on cooldown, and that rejection
        /// overwriting the accepted outcome is exactly how the spam would betray itself here.
        /// </summary>
        [Test]
        public void Q_casts_once_on_press_edge_and_holding_does_not_refire()
        {
            SeedUnlockedAbilities();
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(103.0, 100.0);
            target.Hp = 2000.0;
            KillAllBut(match, target.Id);

            var kit = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger);
            var burst = kit.Q.Damage * kit.Q.Hits;   // rank 1 — no rank scaling yet

            _input.Held.Add(PlayerKey.Q);
            AimAt(target);
            _shell.Pump(0.0);

            Assert.That(target.Hp, Is.EqualTo(2000.0 - burst).Within(SimTolerance),
                "R-31: pressing Q cast the class's Q for its full catalog burst along the aim line");
            Assert.That(_shell.LastAbilityOutcome, Is.Not.Null,
                "T-25: the pump surfaces the cast's outcome");
            Assert.That(_shell.LastAbilityOutcome.Accepted, Is.True,
                "R-31: an unlocked rank-1 Q off cooldown is accepted");
            Assert.That(_shell.LastAbilityOutcome.Slot, Is.EqualTo(AbilitySlot.Q),
                "R-30: Q maps to the sim's Q slot");
            Assert.That(_shell.LastAbilityOutcome.Ability, Is.EqualTo(kit.Q.Name),
                "R-31: the slot resolved to the class's own Q ability");

            PumpHeldAiming(target, steps: 30);   // half a second of held Q

            Assert.That(target.Hp, Is.EqualTo(2000.0 - burst).Within(SimTolerance),
                "T-25: holding Q issued no second cast — one request per press-edge");
            Assert.That(_shell.LastAbilityOutcome.Accepted, Is.True,
                "T-25: the accepted outcome still stands — per-frame spam would have overwritten "
                + "it with the sim's ability_cooling rejection");
        }

        /// <summary>
        /// E maps to the sim's E slot, and its payload is the same honest line: the Gunslinger's E
        /// (Deadeye) pierces EVERY monster on the aim line, so two bodies both losing the catalog
        /// damage proves the shell filled <c>EntitiesOnLine</c> from the cursor geometry.
        /// </summary>
        [Test]
        public void E_casts_the_E_slot_along_the_aim_line()
        {
            SeedUnlockedAbilities();
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var living = match.State.Wave.LivingMonsterIds
                .Select(id => match.State.Monsters[id]).ToList();
            Assert.That(living.Count, Is.GreaterThanOrEqualTo(2),
                "sanity: wave 1 offers two bodies for the pierce");
            var near = living[0];
            var far = living[1];
            near.Pos = new Vec2(103.0, 100.0);
            near.Hp = 500.0;
            far.Pos = new Vec2(106.0, 100.0);
            far.Hp = 500.0;
            KillAllBut(match, near.Id, far.Id);

            var kit = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger);

            _input.Held.Add(PlayerKey.E);
            _input.Cursor = new Vector2(110f, 100f);
            _shell.Pump(0.0);

            Assert.That(_shell.LastAbilityOutcome, Is.Not.Null.And.Property("Slot").EqualTo(AbilitySlot.E),
                "R-30: E maps to the sim's E slot");
            Assert.That(_shell.LastAbilityOutcome.Ability, Is.EqualTo(kit.E.Name),
                "R-31: the slot resolved to the class's own E ability");
            Assert.That(near.Hp, Is.EqualTo(500.0 - kit.E.Damage).Within(SimTolerance),
                "R-31: the piercing line hit the near monster for the catalog damage");
            Assert.That(far.Hp, Is.EqualTo(500.0 - kit.E.Damage).Within(SimTolerance),
                "R-31: ...and pierced through to the far monster — the request carried the cursor "
                + "line's honest entity list");
        }

        /// <summary>
        /// Locks stay sim-side (R-31: a fresh account is basic-attack only) and the shell only
        /// surfaces the refusal: the rejection lands on <see cref="ShellBootstrap.LastAbilityOutcome"/>
        /// with the sim's own fixture-shaped reason, nothing takes damage, and — the part that
        /// matters at sixty frames a second — the pump keeps running: movement still works on the
        /// very next pumps.
        /// </summary>
        [Test]
        public void A_locked_ability_rejection_surfaces_without_breaking_the_loop()
        {
            // No profile seeded: R-44 makes the account fresh, so Q is rank 0 — locked.
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(103.0, 100.0);
            target.Hp = 500.0;
            KillAllBut(match, target.Id);

            _input.Held.Add(PlayerKey.Q);
            AimAt(target);
            _shell.Pump(0.0);
            _input.Held.Remove(PlayerKey.Q);

            Assert.That(_shell.LastAbilityOutcome, Is.Not.Null,
                "T-25: the refused cast still surfaces an outcome — silence would look like a "
                + "dead key, which is this ticket's own bug class");
            Assert.That(_shell.LastAbilityOutcome.Accepted, Is.False,
                "R-31: rank 0 is locked; the sim refused the cast");
            Assert.That(_shell.LastAbilityOutcome.RejectionReason, Is.EqualTo("ability_locked"),
                "R-31: the sim's fixture-shaped reason rides through untranslated");
            Assert.That(target.Hp, Is.EqualTo(500.0).Within(SimTolerance),
                "R-31: a refused cast damages nothing");

            var before = hero.Pos;
            _input.Held.Add(PlayerKey.W);
            _shell.Pump(0.25);

            Assert.That(hero.Pos.Y, Is.GreaterThan(before.Y),
                "T-25: the rejection broke nothing — the pump still routes movement on the next frame");
        }

        /// <summary>
        /// R-32 end to end through the pump: cast, re-press into the running cooldown (refused
        /// with the running deadline's reason), wait the kit's own cooldown out in pumped time,
        /// press again — accepted, and the damage lands again.
        /// </summary>
        [Test]
        public void A_second_press_after_the_cooldown_casts_again()
        {
            SeedUnlockedAbilities();
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(103.0, 100.0);
            target.Hp = 2000.0;
            KillAllBut(match, target.Id);

            var kit = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger);
            var burst = kit.Q.Damage * kit.Q.Hits;

            // Cast 1 — accepted.
            _input.Held.Add(PlayerKey.Q);
            AimAt(target);
            _shell.Pump(0.0);
            Assert.That(target.Hp, Is.EqualTo(2000.0 - burst).Within(SimTolerance),
                "sanity (R-31): the first cast landed");

            // Release, then re-press into the running cooldown: a FRESH edge, honestly issued,
            // honestly refused by the sim (the shell does not client-side-gate cooldowns).
            _input.Held.Remove(PlayerKey.Q);
            PumpHeldAiming(target, steps: 2);
            _input.Held.Add(PlayerKey.Q);
            AimAt(target);
            _shell.Pump(0.0);
            _input.Held.Remove(PlayerKey.Q);

            Assert.That(_shell.LastAbilityOutcome.Accepted, Is.False,
                "R-32: the re-press inside the cooldown was issued and refused sim-side");
            Assert.That(_shell.LastAbilityOutcome.RejectionReason, Is.EqualTo("ability_cooling"),
                "R-32: with the sim's running-cooldown reason");
            Assert.That(target.Hp, Is.EqualTo(2000.0 - burst).Within(SimTolerance),
                "R-32: a cooling cast damages nothing");

            // Wait the class's OWN cooldown out (read, not literal), in pumped sim time.
            var waitSteps = (int)Math.Ceiling((kit.QCooldownSeconds + 0.1) / Step60Hz);
            PumpHeldAiming(target, steps: waitSteps);

            // Cast 2 — accepted again.
            _input.Held.Add(PlayerKey.Q);
            AimAt(target);
            _shell.Pump(0.0);
            _input.Held.Remove(PlayerKey.Q);

            Assert.That(_shell.LastAbilityOutcome.Accepted, Is.True,
                "R-32: a second press after the cooldown is accepted");
            Assert.That(target.Hp, Is.EqualTo(2000.0 - (2.0 * burst)).Within(SimTolerance),
                "R-32: ...and its damage landed again");
        }

        // ==========================================================================================
        //  5 — no double-consumption: T-22 movement and T-24 pointer survive combat keys
        // ==========================================================================================

        /// <summary>
        /// One snapshot, two channels (R-30's whole point — the channels never collapse): held
        /// W+SPACE walks the hero forward AND fires the basic, in the same pumps, with the cursor
        /// parked on the monster to the EAST — so any X drift would be the aim leaking into
        /// movement (DEC-017, re-pinned with combat live).
        /// </summary>
        [Test]
        public void Movement_and_attack_in_the_same_snapshot_both_land()
        {
            var match = StartSoloMatch(HeroClass.Gunslinger);
            var hero = OwnHero(match.State);
            hero.Pos = new Vec2(100.0, 100.0);

            var target = FirstLivingMonster(match);
            target.Pos = new Vec2(104.0, 100.0);
            target.Hp = 1000.0;
            KillAllBut(match, target.Id);

            var d = match.Sim.Config.HeroKits.KitFor(HeroClass.Gunslinger).BasicAttackDamage;
            var before = hero.Pos;

            _input.Held.Add(PlayerKey.W);
            _input.Held.Add(PlayerKey.Space);
            AimAt(target);
            _shell.Pump(0.0);
            PumpHeldAiming(target, steps: 15);

            Assert.That(hero.Pos.Y, Is.GreaterThan(before.Y),
                "T-22/R-30: held W still walks the hero forward with SPACE in the same snapshot — "
                + "the attack consumed nothing from the movement channel");
            Assert.That(hero.Pos.X, Is.EqualTo(before.X).Within(SimTolerance),
                "DEC-017: the cursor sits east on the monster and the hero walked straight north — "
                + "aim is not movement, with combat routing live");
            Assert.That(target.Hp, Is.LessThanOrEqualTo(1000.0 - d),
                "R-30: ...and the held SPACE in those same snapshots attacked the monster");
        }

        /// <summary>
        /// Born-green regression pin for the other direction: the T-24 planning pointer with
        /// SPACE held in the same snapshot. SPACE is phase-dead in planning (this ticket) and the
        /// click still places — neither path may eat the other's input.
        /// </summary>
        [Test]
        public void A_planning_click_still_places_with_SPACE_held_in_the_same_snapshot()
        {
            var match = StartSoloMatch(HeroClass.Gunslinger);
            ReachPlanning(match);

            var item = _shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            Assert.That(item.Affordable, Is.True, "sanity: the cheapest item is affordable");
            _shell.Controls.ShopItemButton(item.Type).onClick.Invoke();
            Assert.That(_shell.Planning.GhostActive, Is.True,
                "sanity (R-63): the shop click started the ghost");

            _input.Held.Add(PlayerKey.Space);
            _input.Held.Add(PlayerKey.MouseLeft);
            _input.Cursor = new Vector2(5f, 5f);   // clear colony ground
            _shell.Pump(0.0);

            Assert.That(match.State.PlaceableCount, Is.EqualTo(1),
                "T-24/T-25: the planning click landed the placement with SPACE held in the same "
                + "snapshot — combat routing consumed nothing from the pointer path");
        }

        // ==========================================================================================
        //  6 — thinness: the routing is plain C# (T-10's invariant covers the rest)
        // ==========================================================================================

        /// <summary>
        /// Born-green shape guard: the new pieces are plain C# in the scanned shell assembly —
        /// never MonoBehaviours — so T-10's Cecil invariant (no MonoBehaviour writes sim state)
        /// mechanically covers everything this ticket adds to the pump.
        /// </summary>
        [Test]
        public void The_combat_routing_pieces_are_plain_CSharp_in_the_scanned_assembly()
        {
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(CombatActionConfig)), Is.False,
                "T-10/R-51: combat config is plain data, not a component");
            Assert.That(typeof(AimLine).IsAbstract && typeof(AimLine).IsSealed, Is.True,
                "T-25: AimLine is a static pure-geometry class — no instance, no lifetime, no scene");
            Assert.That(typeof(AimLine).Assembly, Is.SameAs(typeof(ShellBootstrap).Assembly),
                "T-10: the routing compiles into the shell assembly the Cecil scan reads");
            Assert.That(typeof(CombatActionConfig).Assembly, Is.SameAs(typeof(ShellBootstrap).Assembly),
                "T-10: ...config included");
        }

        // ==========================================================================================
        //  scenario builders and helpers
        // ==========================================================================================

        /// <summary>
        /// A generous corridor for the pump tests: monsters walk while pumps advance time, so the
        /// line is kept wide and long and the cursor re-aimed every pump. Cadence is the tests'
        /// own 0.25 s — a composed value, exactly because the shipped number is unpinned policy.
        /// </summary>
        private static CombatActionConfig WideLine() => new CombatActionConfig
        {
            AttackCadenceSeconds = Cadence,
            AimLineLength = 200.0,
            AimLineWidth = 12.0,
        };

        private ShellBootstrap NewShell(CombatActionConfig combat)
        {
            _profiles = _profiles ?? new InMemoryProfileStore();
            _input = new FakeInputSource();

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = _profiles,
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
                InputSource = _input,
                CombatActions = combat,
            });

            return _shell;
        }

        /// <summary>
        /// R-44 — unlock Q and E at rank 1 on the host's saved profile BEFORE the match adopts
        /// allocations, through the same store the factory reads.
        /// </summary>
        private void SeedUnlockedAbilities()
        {
            _profiles = _profiles ?? new InMemoryProfileStore();
            var profile = new AccountProfile { AccountId = HostAccount };
            profile.Abilities[AbilitySlot.Q] = 1;
            profile.Abilities[AbilitySlot.E] = 1;
            _profiles.Seed(profile);
        }

        /// <summary>Host a solo lobby as <paramref name="heroClass"/> and start the match (T-24's recipe).</summary>
        private HostedMatch StartSoloMatch(string heroClass)
        {
            NewShell(WideLine());

            _shell.Session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = heroClass,
                IsHost = true,
            });
            Assert.That(_shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts a solo match");

            var match = _shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the session holds the live match");

            _shell.Pump(0.0);
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "sanity (R-19): a started match opens in combat with wave 1 living");

            return match;
        }

        /// <summary>Pump <paramref name="steps"/> 60 Hz frames, re-aiming the cursor at the (moving) target.</summary>
        private void PumpHeldAiming(Monster target, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                AimAt(target);
                _shell.Pump(Step60Hz);
            }
        }

        private void AimAt(Monster target)
        {
            _input.Cursor = new Vector2((float)target.Pos.X, (float)target.Pos.Y);
        }

        private Monster FirstLivingMonster(HostedMatch match)
        {
            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "sanity (R-19): the wave walked in");
            return match.State.Monsters[match.State.Wave.LivingMonsterIds[0]];
        }

        /// <summary>Clear every living monster except the keepers, through the sim's own kill command.</summary>
        private static void KillAllBut(HostedMatch match, params string[] keepIds)
        {
            var doomed = match.State.Wave.LivingMonsterIds
                .Where(id => Array.IndexOf(keepIds, id) < 0)
                .ToList();
            KillWave(match, doomed);
        }

        /// <summary>Kill by the sim's own command (T-12/T-21's helper).</summary>
        private static void KillWave(HostedMatch match, IEnumerable<string> monsterIds)
        {
            foreach (var id in monsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType =
                        match.State.Monsters.TryGetValue(id, out var monster) ? monster.Type : null,
                    Bounty = 0,
                });
            }
        }

        /// <summary>Clear the live wave and ride S5's hold into S3 (T-24's recipe).</summary>
        private void ReachPlanning(HostedMatch match)
        {
            _shell.Pump(0.0);
            KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            _shell.Pump(0.0);

            var holdSteps = (int)Math.Ceiling(_shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                _shell.Pump(Step60Hz);
            }

            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity: the sim is in its planning phase");
        }

        private static Hero OwnHero(MatchState state)
        {
            var hero = state.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, HostAccount, StringComparison.Ordinal));
            Assert.That(hero, Is.Not.Null, "sanity: the factory seated the host's hero");
            return hero;
        }

        private static Monster MonsterAt(Vec2 pos, string id, double hp = 60.0) => new Monster
        {
            Id = id,
            Type = MonsterType.Shambler,
            Pos = pos,
            Hp = hp,
        };

        private static Hero HeroAt(Vec2 pos, string id, string heroClass) => new Hero
        {
            Id = id,
            HeroClass = heroClass,
            Pos = pos,
            Hp = 100.0,
            MaxHp = 100.0,
        };

        private static Placeable BarricadeAt(Vec2 pos, string id) => new Placeable
        {
            Id = id,
            Type = PlaceableType.Barricade,
            Pos = pos,
            Hp = 300.0,
        };

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>
        /// A scripted device (T-22's fake): the keys "held" and the cursor's ground point, sampled
        /// exactly the way a real source is. Everything downstream of <see cref="IInputSource"/>
        /// is real.
        /// </summary>
        private sealed class FakeInputSource : IInputSource
        {
            public readonly HashSet<PlayerKey> Held = new HashSet<PlayerKey>();
            public Vector2 Cursor;

            public InputSnapshot Sample()
            {
                var snapshot = new InputSnapshot { CursorGroundPoint = Cursor };
                foreach (var key in Held)
                {
                    snapshot.Pressed.Add(key);
                }

                return snapshot;
            }
        }
    }
}
