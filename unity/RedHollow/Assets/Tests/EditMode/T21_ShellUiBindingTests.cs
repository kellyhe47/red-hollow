using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 021 (T-21), part 1 of 2 — the UI half of the runtime binding. Ticket 012 built and
    /// locked the screen models and the <see cref="UiRouter"/>; nothing at runtime ever constructed
    /// them, so a launched build showed no screen at all. These tests pin the composition root
    /// (<see cref="ShellBootstrap"/>) and its built hierarchy (<see cref="ShellUi"/>):
    ///
    ///  1. <b>A real uGUI hierarchy exists</b> — one Canvas, one distinct root per S1–S7 screen,
    ///     all under one findable shell root.
    ///  2. <b>Activation follows the router</b> — exactly the routed screen's root is active, and
    ///     it keeps following through the flow S2 → S4 → S5 → S3 and to S6/S7, including the S5
    ///     entry that only works if the sim's own <c>wave_complete</c> event reaches
    ///     <see cref="UiRouter.OnSimEvent"/> THROUGH THE PUMP (no test calls it by hand).
    ///  3. <b>Labels show the 012 models' values and stay in step</b> — a replicated value changed
    ///     between pumps is on the label after the next pump, never a first-frame snapshot.
    ///  4. <b>Sim events reach the HUD model through the pump</b> — R-13's red flash and toast.
    ///  5. <b>The binding layer is plain C#</b> — the shape that keeps T-10's Cecil scan green.
    ///
    /// <b>What is deliberately NOT asserted</b>, because the PRD and wireframes are silent and a
    /// guessed value would ship as spec: any label copy or format (assertions are substring
    /// containment of the model's number), layout, anchoring, fonts, colours, render mode, whether
    /// inactive screens are deactivated on themselves or via a parent, and which GameObject each
    /// label lives under beyond "inside the shell root".
    ///
    /// EditMode throughout, T-19's pattern: the session is driven by explicit
    /// <see cref="ShellBootstrap.Pump"/> calls (<c>Pump(0)</c> is the contract's pure refresh), so
    /// nothing here needs a frame to elapse.
    /// </summary>
    [TestFixture]
    public class T21_ShellUiBindingTests
    {
        private const double Step60Hz = 1.0 / 60.0;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";

        /// <summary>The well-known root names the shell composes under, for belt-and-braces teardown.</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private ShellBootstrap _shell;

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
                    // A stub or a half-built shell must not turn a red test into a teardown error.
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
        //  AC1 — a real visible UI hierarchy exists: Canvas + one root per screen
        // ==========================================================================================

        /// <summary>
        /// R-60. The bootstrap builds one Canvas with a distinct root per wireframe screen, all
        /// under a single findable shell root — the hierarchy a launched build actually renders,
        /// not a set of models nothing displays. Asserted before any match exists, because S1/S2
        /// have to be showable when there is nothing to play yet.
        /// </summary>
        [Test]
        public void The_bootstrap_builds_one_canvas_with_a_distinct_root_per_screen()
        {
            var shell = NewHostedShell();

            Assert.That(shell.Ui, Is.Not.Null, "R-60: the shell builds a UI");
            Assert.That(shell.Ui.Root, Is.Not.Null, "the hierarchy hangs from one root");
            Assert.That(shell.Ui.Root.name, Is.EqualTo("RedHollow_Shell"),
                "the root carries the well-known name, so a session can find and tear down the "
                + "whole UI in one call (the RedHollow_Match / RedHollow_MatchViews convention)");

            Assert.That(shell.Ui.Canvas, Is.Not.Null, "the project renders UI through uGUI");
            Assert.That(shell.Ui.Canvas.transform.IsChildOf(shell.Ui.Root.transform)
                        || shell.Ui.Canvas.gameObject == shell.Ui.Root,
                Is.True, "the Canvas lives inside the shell root");

            var roots = new Dictionary<UiScreen, GameObject>();
            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var root = shell.Ui.ScreenRoot(screen);
                Assert.That(root, Is.Not.Null,
                    "R-60: every wireframe screen S1-S7 has a container — missing: " + screen);
                Assert.That(root.transform.IsChildOf(shell.Ui.Root.transform), Is.True,
                    screen + "'s root lives inside the shell hierarchy");
                roots[screen] = root;
            }

            Assert.That(roots.Values.Distinct().Count(), Is.EqualTo(roots.Count),
                "R-60: the screens are distinct containers — one shared root cannot switch");
        }

        // ==========================================================================================
        //  AC1 — exactly the routed screen's root is active, through the whole flow
        // ==========================================================================================

        /// <summary>
        /// R-60 / R-04. Activation follows <see cref="UiRouter.Screen"/> through the front of the
        /// flow: a hosted session shows S2, a started match shows S4, a cleared (non-final) wave
        /// shows S5, and after the router's own declared hold S3 is active for the next wave.
        ///
        /// The S5 entry is the load-bearing one for THIS ticket: the router only enters the
        /// interstitial on a <c>wave_complete</c> event, and no code in this test hands it one —
        /// the sim emitted it (<c>RecordMonsterKill</c>), and only the bootstrap's event feed can
        /// have carried it to <see cref="UiRouter.OnSimEvent"/>. A bootstrap that renders screens
        /// but never wires the feed sticks on S3/S4 here.
        /// </summary>
        [Test]
        public void Exactly_the_routed_screens_root_is_active_and_follows_the_flow()
        {
            var shell = NewHostedShell();

            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.Lobby, "a hosted session is on S2");

            var match = StartMatch(shell);
            shell.Pump(0.0);

            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "sanity (ticket 011): a started match opens in combat");
            AssertOnlyActiveScreen(shell, UiScreen.Combat, "a live combat phase is S4");

            // Clear wave 1 through the sim's own command. The LAST kill emits wave_complete.
            var wave = match.State.Wave.Number;
            KillWave(match, match.State.Wave.LivingMonsterIds.ToList(), bounty: 0);

            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.WaveInterstitial,
                "R-04: the sim's wave_complete must reach the router through the pump's event "
                + "feed — nothing else in this test delivers it");

            // Drive past the router's own declared hold; the session keeps running underneath.
            var holdSteps = (int)Math.Ceiling(shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                shell.Pump(Step60Hz);
            }

            AssertOnlyActiveScreen(shell, UiScreen.Planning,
                "R-04/R-60: after the hold, S5 falls back to S3 for the next wave");
            Assert.That(match.State.Wave.Number, Is.EqualTo(wave + 1),
                "sanity (G-016): the campaign moved on underneath the banner");
        }

        /// <summary>
        /// R-60 / R-01 / R-02. The back of the flow: a finished match's screen root. Victory is
        /// reached by putting the live match on its final wave and clearing it (T-19's shape);
        /// defeat by emptying the colony through the sim's own damage command (T-12's shape). The
        /// session notices the end on the next step (<see cref="NetSession.Step"/>), so one pump
        /// is the whole transition.
        /// </summary>
        [TestCase(true, TestName = "a won match activates the victory root")]
        [TestCase(false, TestName = "a lost match activates the defeat root")]
        public void A_finished_match_lands_on_its_post_match_screen_root(bool victory)
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            if (victory)
            {
                // The wave in the colony becomes the final one, then falls (DEC-RUN-5 makes
                // TotalWaves the authority the clear is judged against).
                match.State.Wave.Number = match.State.Wave.TotalWaves;
                KillWave(match, match.State.Wave.LivingMonsterIds.ToList(), bounty: 0);
                Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                    "sanity (R-01/G-011): clearing the final wave wins the map");
            }
            else
            {
                EmptyTheColony(match);
                Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Defeat),
                    "sanity (R-02/G-008): an emptied colony is the defeat");
            }

            shell.Pump(0.0);

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.PostMatch),
                "sanity (ticket 011): the session noticed the end on the step");
            AssertOnlyActiveScreen(shell, victory ? UiScreen.Victory : UiScreen.Defeat,
                "R-60: the post-match screen matches MatchState.Status");
        }

        // ==========================================================================================
        //  AC1 — labels are bound to the 012 models and a pump keeps them in step
        // ==========================================================================================

        /// <summary>
        /// R-61. The combat HUD's labels show the <see cref="CombatHudModel"/> values for the live
        /// match: wave number, scrip, own-hero HP, monsters remaining, and one label per shelter
        /// carrying its civilian count. Containment only — copy and format are presentation.
        /// Every expected value is read off the replicated state, never typed here, so a retuned
        /// stake or wave table retunes the test.
        /// </summary>
        [Test]
        public void The_hud_labels_show_the_models_values_after_a_pump()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);

            shell.Pump(0.0);

            var state = match.State;
            var ui = shell.Ui;

            AssertLabelShows(ui.WaveLabel, state.Wave.Number, "R-61: the wave label shows CombatHudModel.WaveNumber");
            AssertLabelShows(ui.ScripLabel, state.Team.Scrip, "R-61: the scrip label shows CombatHudModel.Scrip");
            AssertLabelShows(ui.MonstersRemainingLabel, state.Wave.LivingMonsterIds.Count,
                "R-61: the monsters-remaining label follows the living roster");

            var hero = OwnHero(state);
            Assert.That(hero, Is.Not.Null, "sanity: the factory seated the host's hero");
            AssertLabelShows(ui.HpLabel, (int)hero.Hp, "R-61: the HP label shows the own hero's HP");

            Assert.That(ui.HotspotLabels, Is.Not.Null, "R-61: the HUD lists the shelters");
            Assert.That(ui.HotspotLabels.Count, Is.EqualTo(state.Hotspots.Count),
                "R-61: one hotspot label per shelter in the colony");
            foreach (var hotspot in state.Hotspots.Values)
            {
                var count = hotspot.Civilians.ToString(CultureInfo.InvariantCulture);
                Assert.That(ui.HotspotLabels.Any(l => l != null && l.text != null && l.text.Contains(count)),
                    Is.True,
                    "R-61: some hotspot label shows " + hotspot.Id + "'s civilian count (" + count + ")");
            }

            // The labels are part of the built hierarchy, not floating orphans.
            foreach (var label in new[] { ui.WaveLabel, ui.ScripLabel, ui.HpLabel, ui.MonstersRemainingLabel })
            {
                Assert.That(label, Is.Not.Null, "every pinned label exists");
                Assert.That(label.transform.IsChildOf(ui.Root.transform), Is.True,
                    label.name + " lives inside the shell hierarchy");
            }
        }

        /// <summary>
        /// R-61 / R-51. The pump is a REFRESH, not a first-frame snapshot: a replicated value that
        /// changes between pumps is on the label after the next pump. Asserted with values that
        /// cannot pre-exist on the labels (checked before the change), so a label that was built
        /// right by accident and never updated cannot pass.
        /// </summary>
        [Test]
        public void A_state_change_reaches_the_labels_on_the_next_pump()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            var ui = shell.Ui;

            Assert.That(ui.ScripLabel.text, Does.Not.Contain("4917"),
                "anti-vacuity: the sentinel scrip value is not already on the label");
            Assert.That(ui.HpLabel.text, Does.Not.Contain("73"),
                "anti-vacuity: the sentinel HP value is not already on the label");

            match.State.Team.Scrip = 4917;
            OwnHero(match.State).Hp = 73.0;

            shell.Pump(0.0);

            AssertLabelShows(ui.ScripLabel, 4917,
                "R-61: scrip changed after the first pump must be on the label after the next");
            AssertLabelShows(ui.HpLabel, 73,
                "R-61: the HP label keeps reading the replicated hero, not its first frame");
        }

        // ==========================================================================================
        //  AC2 (UI side) — sim events reach the HUD model through the pump
        // ==========================================================================================

        /// <summary>
        /// R-13. A <c>civilians_killed</c> event that actually killed somebody raises the red
        /// flash and the toast — states ticket 012 locked onto <see cref="CombatHudModel"/> but
        /// which no runtime code ever fed. The event is emitted by the sim's own damage command
        /// and reaches the model only if the bootstrap's feed carries it.
        /// </summary>
        [Test]
        public void A_civilians_killed_event_reaches_the_hud_model_through_the_pump()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            Assert.That(shell.Hud, Is.Not.Null, "a live match has a HUD model");
            Assert.That(shell.Hud.RedFlashActive, Is.False,
                "anti-vacuity: nothing has died, so nothing flashes yet");

            var shelter = match.State.Hotspots.Values.First(h => h.Civilians > 0);
            match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
            {
                AttackerId = "m_test",
                AttackerType = MonsterType.Shambler,
                Damage = 1.0,
                TargetId = shelter.Id,
            });

            shell.Pump(0.0);

            Assert.That(shell.Hud.RedFlashActive, Is.True,
                "R-13: a real civilian death must flash the HUD — the sim's civilians_killed "
                + "event has to reach CombatHudModel.OnSimEvent through the pump's feed");
            Assert.That(shell.Hud.Toasts.Any(t => t.Kind == HudToastKind.CiviliansLost
                                                  && t.SubjectId == shelter.Id),
                Is.True,
                "R-13: and raise the civilians-lost toast naming the shelter that was hit");
        }

        // ==========================================================================================
        //  AC4 — the binding layer is plain C#
        // ==========================================================================================

        /// <summary>
        /// R-51 / T-10. <b>Expected GREEN as soon as the stubs compile.</b> The enforcement is
        /// T10's Cecil scan, unchanged by this ticket; this states the shape that keeps it green:
        /// the composition root and the UI handle are plain C# classes. A "ShellBootstrapBehaviour"
        /// that owned the session is exactly the component the IL invariant exists to reject —
        /// the scene's tie stays a two-member pump (<see cref="RedHollow.Game.Host.MatchHostBehaviour"/>'s
        /// shape) that holds one of these and forwards the frame delta to <c>Pump</c>.
        /// </summary>
        [Test]
        public void The_binding_layer_is_plain_C_sharp_so_no_rule_can_enter_a_MonoBehaviour()
        {
            foreach (var seam in new[] { typeof(ShellBootstrap), typeof(ShellUi) })
            {
                Assert.That(typeof(MonoBehaviour).IsAssignableFrom(seam), Is.False,
                    "R-51: " + seam.FullName + " composes and drives the shell, so it must be a "
                    + "plain C# class — a MonoBehaviour here is what T10's IL invariant rejects");
            }
        }

        // ==========================================================================================
        //  scenario builders and helpers
        // ==========================================================================================

        /// <summary>A shell over a loopback session with the host seated — the S2 starting point.</summary>
        private ShellBootstrap NewHostedShell()
        {
            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = new InMemoryProfileStore(),
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
            });

            _shell.Session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            });

            Assert.That(_shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "sanity (R-50): hosting opens a lobby");

            return _shell;
        }

        private static HostedMatch StartMatch(ShellBootstrap shell)
        {
            Assert.That(shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts the match");

            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the session holds the live match");
            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "sanity (R-19): the match opened with its wave in the colony");

            return match;
        }

        /// <summary>Exactly one screen root is active in the hierarchy: the routed one.</summary>
        private static void AssertOnlyActiveScreen(ShellBootstrap shell, UiScreen expected, string because)
        {
            Assert.That(shell.Router.Screen, Is.EqualTo(expected),
                "sanity — the router itself must be on " + expected + ": " + because);

            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var root = shell.Ui.ScreenRoot(screen);
                Assert.That(root, Is.Not.Null, "every screen has a root: " + screen);
                Assert.That(root.activeInHierarchy, Is.EqualTo(screen == expected),
                    "R-60: " + because + " — " + screen + "'s root must be "
                    + (screen == expected ? "the one active container" : "inactive")
                    + " while the router is on " + expected);
            }
        }

        /// <summary>The label's text contains the model's value. Copy and format stay free.</summary>
        private static void AssertLabelShows(UnityEngine.UI.Text label, int value, string because)
        {
            Assert.That(label, Is.Not.Null, because + " (the label must exist)");
            Assert.That(label.text, Does.Contain(value.ToString(CultureInfo.InvariantCulture)),
                because + " — its copy is free, but the value must be on it");
        }

        private static Hero OwnHero(MatchState state)
        {
            return state.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, HostAccount, StringComparison.Ordinal));
        }

        /// <summary>Clears a wave one kill at a time through the sim's own command (T-12's helper).</summary>
        private static void KillWave(HostedMatch match, IEnumerable<string> monsterIds, int bounty)
        {
            foreach (var id in monsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = match.State.Monsters.TryGetValue(id, out var monster) ? monster.Type : null,
                    Bounty = bounty,
                });
            }
        }

        /// <summary>R-02 — the defeat, through the sim's own damage command (T-12's helper).</summary>
        private static void EmptyTheColony(HostedMatch match)
        {
            foreach (var hotspot in match.State.Hotspots.Values.ToList())
            {
                while (hotspot.Civilians > 0)
                {
                    match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
                    {
                        AttackerId = "m_wipeout",
                        AttackerType = MonsterType.Shambler,
                        Damage = 1000.0,
                        TargetId = hotspot.Id,
                    });
                }
            }
        }
    }
}
