using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 022 (T-22) — the play-mode entry point. Found by the owner pressing Play
    /// (2026-08-26): <c>Assets/Scenes/RedHollow.unity</c> contains ZERO MonoBehaviours, so the
    /// shell ticket 021 built and locked never runs outside a test. These tests pin the
    /// scene-resident entry (<see cref="GameEntryBehaviour"/>):
    ///
    ///  1. <b>The committed scene carries it</b> — exactly one enabled entry, plus the camera the
    ///     scene already owns. (RED until the implementer edits and saves the scene asset itself —
    ///     the whole point of the ticket is that the .unity file changes.)
    ///  2. <b>Lifecycle</b>, driven reflectively in EditMode, no play mode: Awake constructs a
    ///     loopback <see cref="ShellBootstrap"/> (offline defaults — pressing Play needs no UGS
    ///     id) and ensures an <see cref="EventSystem"/>; each Update samples
    ///     <see cref="GameEntryBehaviour.DeltaSource"/> exactly once and pumps exactly once with
    ///     it; OnDestroy tears the shell down idempotently.
    ///  3. <b>Input wiring</b> (R-30): the shell accepts an <see cref="IInputSource"/> via
    ///     <see cref="ShellBootstrapOptions.InputSource"/> and a fake source's held W walks the
    ///     LOCAL hero forward through the pump — keys move, the cursor does not (DEC-017). The
    ///     entry supplies a non-null source, so a launched build has a working keyboard.
    ///     EditMode cannot press physical keys, so device reads themselves stay untested here —
    ///     the R-30 mapping tables (T16) and this wiring together are the EditMode-provable whole.
    ///  4. <b>Thinness</b> — the entry is a MonoBehaviour in the shell assembly, so T10's Cecil
    ///     scan already forbids it sim writes mechanically; a light shape guard keeps it near
    ///     <see cref="RedHollow.Game.Host.MatchHostBehaviour"/>'s pump reputation.
    ///
    /// <b>Deliberately NOT pinned</b>: the concrete device-source type (legacy or Input System —
    /// <c>activeInputHandler: 2</c> allows either), any camera creation by the entry (the scene
    /// already owns its top-down camera, R-30 — the entry must not add more), label copy, layout.
    /// </summary>
    [TestFixture]
    public class T22_PlayEntryTests
    {
        private const string ScenePath = "Assets/Scenes/RedHollow.unity";
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-6;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";

        /// <summary>The well-known roots the shell composes under (T21's teardown convention).</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private ShellBootstrap _shell;
        private EventSystem[] _preExistingEventSystems;

        [SetUp]
        public void SnapshotEventSystems()
        {
            _preExistingEventSystems = UnityEngine.Object.FindObjectsOfType<EventSystem>();
        }

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

            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();

            foreach (var name in ShellRootNames)
            {
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            // EventSystems the entry created during the test (Awake ensures one exists).
            foreach (var es in UnityEngine.Object.FindObjectsOfType<EventSystem>())
            {
                if (es != null && Array.IndexOf(_preExistingEventSystems, es) < 0)
                {
                    UnityEngine.Object.DestroyImmediate(es.gameObject);
                }
            }
        }

        // ==========================================================================================
        //  AC1 — the committed scene actually contains the entry (and its camera)
        // ==========================================================================================

        /// <summary>
        /// The scene asset itself, not a hierarchy a test builds: pressing Play runs whatever is
        /// serialized in <c>RedHollow.unity</c>, and until this ticket that was nothing. Exactly
        /// one enabled <see cref="GameEntryBehaviour"/> must be saved in it, alongside the enabled
        /// camera the SceneBuilder already authored (R-30's top-down view — the entry relies on
        /// it rather than creating its own).
        ///
        /// RED until the implementer EDITS AND SAVES the scene asset — a correct component that
        /// only tests ever AddComponent still boots nothing on Play.
        /// </summary>
        [Test]
        public void The_committed_scene_contains_exactly_one_enabled_entry_and_a_camera()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True,
                    "sanity: the committed scene asset opens — " + ScenePath);

                var entries = ComponentsInScene<GameEntryBehaviour>(scene);
                Assert.That(entries.Count, Is.EqualTo(1),
                    "T-22: pressing Play must boot the shell, so the SAVED scene contains exactly "
                    + "one GameEntryBehaviour — zero boots nothing (the bug this ticket fixes), "
                    + "two would build two shells over one loopback");

                Assert.That(entries[0].enabled && entries[0].gameObject.activeInHierarchy, Is.True,
                    "T-22: the serialized entry is enabled and active, or Awake/Update never run");

                Assert.That(
                    ComponentsInScene<Camera>(scene)
                        .Any(c => c.enabled && c.gameObject.activeInHierarchy),
                    Is.True,
                    "R-30: the scene keeps an enabled camera (the SceneBuilder's top-down rig) — "
                    + "the entry does not create cameras, so the saved scene must still carry one");
            }
            finally
            {
                RestoreEditorScenes(setup);
            }
        }

        // ==========================================================================================
        //  AC2 — Awake: a loopback shell exists, shows S1, and has its uGUI plumbing
        // ==========================================================================================

        /// <summary>
        /// Awake is the composition moment: one <see cref="ShellBootstrap"/> with the offline
        /// defaults (loopback — no UGS project id, so Play works with no network and no cloud
        /// account), visible through a public accessor; the title screen (S1) is the one active
        /// root; a non-null input source reached the composition (R-30 — a launched build must
        /// have a working keyboard, and null here is the "heroes stand still forever" bug); and
        /// an <see cref="EventSystem"/> exists so the uGUI buttons T21 built are clickable.
        /// </summary>
        [Test]
        public void Awake_builds_a_loopback_shell_on_the_title_screen_with_input_and_an_event_system()
        {
            var entry = NewEntry();

            Drive(entry, "Awake");

            var shell = entry.Shell;
            _shell = shell;
            Assert.That(shell, Is.Not.Null, "T-22: Awake constructs the ShellBootstrap");
            Assert.That(shell.Session, Is.Not.Null, "the shell fronts a session from birth");
            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Offline),
                "nothing is hosted yet — Awake composes, it does not join anything");

            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Title),
                "R-60: a freshly launched shell is on S1");
            AssertOnlyActiveScreen(shell, UiScreen.Title, "pressing Play lands on the title");

            Assert.That(shell.Input, Is.Not.Null,
                "R-30: the entry supplies a device-backed IInputSource to the composition — a "
                + "shell launched with none has heroes no key can ever move");

            Assert.That(UnityEngine.Object.FindObjectsOfType<EventSystem>(), Is.Not.Empty,
                "R-60: uGUI clicks need an EventSystem — the entry ensures one exists on Awake");

            // Loopback proof: hosting works offline, with no UGS id configured anywhere.
            shell.Session.StartHost(HostPeer());
            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-50: the default (no NetConfig) session is loopback — hosting succeeds with no "
                + "UGS project id, which is what makes pressing Play work offline");
        }

        /// <summary>
        /// R-43 / R-44 — a LAUNCHED game keeps lifetime XP across a restart: the entry composes a
        /// persistent <see cref="JsonProfileStore"/> (the shell's own default is in-memory, which
        /// is "XP dies on quit"), pointed under Unity's per-app data directory. The store type and
        /// its location are the honest pins; the document format is the store's own contract.
        /// </summary>
        [Test]
        public void Awake_composes_a_persistent_profile_store_so_launched_xp_survives_a_restart()
        {
            var entry = NewEntry();

            Drive(entry, "Awake");
            _shell = entry.Shell;

            var store = _shell.Profiles as JsonProfileStore;
            Assert.That(store, Is.Not.Null,
                "R-43: the entry composes the persistent store — the in-memory default is a "
                + "launched game whose accounts reset on every boot");
            Assert.That(store.FilePath, Does.StartWith(Application.persistentDataPath),
                "R-44: the server-local document lives in the app's own data directory, not the "
                + "working directory of whatever launched the process");
        }

        /// <summary>
        /// "Ensure", not "add": a scene that already owns an EventSystem must not gain a second —
        /// two EventSystems fight over uGUI focus and Unity logs errors about it.
        /// </summary>
        [Test]
        public void Awake_does_not_duplicate_an_existing_EventSystem()
        {
            var existing = new GameObject("t22_pre_existing_eventsystem", typeof(EventSystem));
            _spawned.Add(existing);

            var before = UnityEngine.Object.FindObjectsOfType<EventSystem>().Length;
            var entry = NewEntry();

            Drive(entry, "Awake");
            _shell = entry.Shell;

            Assert.That(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length, Is.EqualTo(before),
                "T-22: the entry ensures an EventSystem exists — present-or-created, never doubled");
        }

        // ==========================================================================================
        //  AC2 — Update: one Update is one Pump(DeltaSource())
        // ==========================================================================================

        /// <summary>
        /// The pump cadence, made observable through the clock seam: a scripted
        /// <see cref="GameEntryBehaviour.DeltaSource"/> counts its samples, and one sample is one
        /// frame. The pump's EFFECT is observed on the shell's own state — hosting changes the
        /// session, but only a pump moves the router/UI (T21's contract), so "S2 appears after
        /// exactly one Update" is "Update pumped".
        /// </summary>
        [Test]
        public void Each_update_samples_the_clock_once_and_pumps_the_shell()
        {
            var entry = NewEntry();
            Drive(entry, "Awake");
            var shell = entry.Shell;
            _shell = shell;

            var samples = 0;
            entry.DeltaSource = () =>
            {
                samples++;
                return Step60Hz;
            };

            shell.Session.StartHost(HostPeer());

            // Anti-vacuity: hosting alone must not repaint — the presentation moves per pump,
            // per frame, exactly as it will in play mode.
            AssertOnlyActiveScreen(shell, UiScreen.Title,
                "no Update has run since hosting, so the UI still shows S1");

            Drive(entry, "Update");

            Assert.That(samples, Is.EqualTo(1),
                "T-22: one Update samples DeltaSource exactly once — its value IS the frame");
            AssertOnlyActiveScreen(shell, UiScreen.Lobby,
                "R-60: the first Update after hosting pumps the shell, and the pump routes S2 in");

            Drive(entry, "Update");
            Drive(entry, "Update");
            Drive(entry, "Update");

            Assert.That(samples, Is.EqualTo(4),
                "T-22: every Update is exactly one clock sample / one pump — never zero, never a "
                + "catch-up loop");
        }

        /// <summary>
        /// The sampled delta actually reaches <see cref="ShellBootstrap.Pump"/> as the frame's
        /// time: the wave interstitial (S5) holds for the router's own declared seconds, so
        /// driving ceil(hold / delta) + slack Updates through a 60Hz scripted clock must walk the
        /// UI through S4 → S5 → S3. An entry that pumped 0, or pumped some constant of its own,
        /// either sticks on S5 forever or leaves it in one frame.
        /// </summary>
        [Test]
        public void The_sampled_delta_reaches_the_pump_as_frame_time()
        {
            var entry = NewEntry();
            Drive(entry, "Awake");
            var shell = entry.Shell;
            _shell = shell;

            entry.DeltaSource = () => Step60Hz;

            shell.Session.StartHost(HostPeer());
            Assert.That(shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts a solo match");
            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the session holds the live match");

            Drive(entry, "Update");
            AssertOnlyActiveScreen(shell, UiScreen.Combat, "a started match is on S4");

            KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            Drive(entry, "Update");
            AssertOnlyActiveScreen(shell, UiScreen.WaveInterstitial,
                "R-04: the sim's wave_complete reached the router through the entry's pump");

            var holdSteps = (int)Math.Ceiling(shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                Drive(entry, "Update");
            }

            AssertOnlyActiveScreen(shell, UiScreen.Planning,
                "R-04/R-60: the interstitial hold elapsed in DeltaSource time — each Update "
                + "forwarded the sampled delta to Pump, so 60Hz frames add up to the hold");
        }

        // ==========================================================================================
        //  AC2 — OnDestroy: teardown, idempotently
        // ==========================================================================================

        /// <summary>
        /// Leaving play mode destroys the entry, and the entry owns the shell's lifetime: after
        /// OnDestroy the shell's hierarchy (the "RedHollow_Shell" root T21 pinned) is gone, and a
        /// second OnDestroy — Unity calls lifecycle methods in orders nobody should bet on — is a
        /// no-op, exactly the promise <see cref="ShellBootstrap.TearDown"/> already makes.
        /// </summary>
        [Test]
        public void OnDestroy_tears_the_shell_down_and_is_idempotent()
        {
            var entry = NewEntry();
            Drive(entry, "Awake");
            _shell = entry.Shell;

            Assert.That(GameObject.Find("RedHollow_Shell"), Is.Not.Null,
                "sanity: Awake built the shell hierarchy under the well-known root");

            Drive(entry, "OnDestroy");

            Assert.That(GameObject.Find("RedHollow_Shell"), Is.Null,
                "T-22: OnDestroy tears the shell down — a play session must not leak its UI");

            Assert.DoesNotThrow(() => Drive(entry, "OnDestroy"),
                "T-22: teardown is idempotent — a second OnDestroy destroys nothing twice");
        }

        // ==========================================================================================
        //  AC3 — input wiring: a source given to the shell moves the local hero through the pump
        // ==========================================================================================

        /// <summary>
        /// R-30, at the seam EditMode can prove: a fake <see cref="IInputSource"/> handed to
        /// <see cref="ShellBootstrapOptions.InputSource"/> drives the LOCAL hero through nothing
        /// but the pump — held W walks it forward (speed x delta stays the sim's business, so only
        /// direction and motion are pinned), the cursor parked BEHIND the hero moves it not one
        /// step sideways (DEC-017 — no click-to-move), and released keys hold ground. The intent
        /// must have travelled InputSource → DefaultHeroInputMap → the session's hero-intent seam,
        /// because no code in this test touches the sim's move command.
        /// </summary>
        [Test]
        public void A_held_W_on_the_shells_input_source_walks_the_local_hero_forward_through_the_pump()
        {
            var fake = new FakeInputSource { Cursor = new Vector2(-9f, -9f) };
            fake.Held.Add(PlayerKey.W);

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = new InMemoryProfileStore(),
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
                InputSource = fake,
            });

            Assert.That(_shell.Input, Is.SameAs(fake),
                "T-22: the shell exposes the source it was composed with — the accessor the entry "
                + "tests read to prove the wiring");

            _shell.Session.StartHost(HostPeer());
            Assert.That(_shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts a solo match");
            var match = _shell.Session.Match;

            _shell.Pump(0.0);

            var hero = OwnHero(match.State);
            Assert.That(hero, Is.Not.Null, "sanity: the factory seated the host's hero");
            var before = hero.Pos;

            _shell.Pump(0.25);

            Assert.That(hero.Pos.Y, Is.GreaterThan(before.Y),
                "R-30: held W must reach the local hero as a forward move through the pump — "
                + "InputSource sampled, DefaultHeroInputMap resolved, hero-intent seam fed");
            Assert.That(hero.Pos.X, Is.EqualTo(before.X).Within(SimTolerance),
                "R-30 / DEC-017: the cursor sits at (-9,-9) and the hero still walked straight "
                + "forward — a hero drifting toward the cursor is click-to-move in a WASD hat");

            fake.Held.Clear();
            var held = hero.Pos;

            _shell.Pump(0.25);
            _shell.Pump(0.25);

            Assert.That(hero.Pos, Is.EqualTo(held),
                "R-30: released keys are a zero intent — no repeat of the last direction, no "
                + "step toward the cursor");
        }

        // ==========================================================================================
        //  AC4 — thinness: scanned by T10, shaped like MatchHostBehaviour
        // ==========================================================================================

        /// <summary>
        /// The enforcement is T10's Cecil scan — this pins that the scan actually covers the
        /// entry (a MonoBehaviour, compiled into the same shell assembly the scan reads), and
        /// adds the one bound Cecil cannot: member count. <see cref="RedHollow.Game.Host.MatchHostBehaviour"/>
        /// pumps with one field; the entry earns a few more (the shell, the clock seam, teardown
        /// bookkeeping) but a double-digit field count is a composition root growing logic.
        /// Six instance fields is deliberate slack, not a target.
        /// </summary>
        [Test]
        public void The_entry_is_a_MonoBehaviour_in_the_scanned_assembly_and_stays_a_thin_pump()
        {
            var entry = typeof(GameEntryBehaviour);

            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(entry), Is.True,
                "the entry lives in the scene, so it is a MonoBehaviour — and thereby inside "
                + "T10's Cecil invariant automatically");
            Assert.That(entry.Assembly, Is.SameAs(typeof(ShellBootstrap).Assembly),
                "T-10: the scan reads the shell assembly; an entry compiled elsewhere would "
                + "escape the IL invariant");

            var fields = entry.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);

            Assert.That(fields.Length, Is.LessThanOrEqualTo(6),
                "T-22: the entry is a thin pump (MatchHostBehaviour's reputation) — it holds the "
                + "shell, a clock seam and wiring, never working state. Fields found: "
                + string.Join(", ", fields.Select(f => f.FieldType.Name + " " + f.Name)));

            foreach (var field in fields)
            {
                Assert.That(field.FieldType.Namespace, Is.Not.EqualTo("RedHollow.Sim"),
                    "R-51: the entry holds no sim state — " + field.Name + " is " + field.FieldType
                    + ", which belongs behind ShellBootstrap, not in a MonoBehaviour");
            }
        }

        // ==========================================================================================
        //  scenario builders and helpers
        // ==========================================================================================

        private GameEntryBehaviour NewEntry()
        {
            var go = new GameObject("t22_entry");
            _spawned.Add(go);

            // AddComponent in EditMode runs no lifecycle — the tests drive Awake/Update/OnDestroy
            // reflectively, which is exactly why this contract is provable without play mode.
            return go.AddComponent<GameEntryBehaviour>();
        }

        /// <summary>Invoke a (private) lifecycle method, unwrapping the reflection envelope.</summary>
        private static void Drive(GameEntryBehaviour entry, string method)
        {
            var m = typeof(GameEntryBehaviour).GetMethod(
                method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null,
                "T-22: GameEntryBehaviour declares " + method + " — the lifecycle is the contract");

            try
            {
                m.Invoke(entry, null);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
            {
                throw wrapped.InnerException;
            }
        }

        private static NetPeer HostPeer()
        {
            return new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            };
        }

        private static Hero OwnHero(MatchState state)
        {
            return state.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, HostAccount, StringComparison.Ordinal));
        }

        /// <summary>Exactly one screen root is active: the routed one (T21's helper).</summary>
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

        /// <summary>Clears a wave through the sim's own kill command (T-12/T-21's helper).</summary>
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

        private static List<T> ComponentsInScene<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return found;
        }

        /// <summary>
        /// Put the editor back the way the test found it, so the scene-opening test leaves no
        /// footprint on the rest of the suite. Untitled scenes cannot be restored by path, so an
        /// all-untitled setup falls back to a fresh empty scene — the state every other EditMode
        /// test in this repo already assumes.
        /// </summary>
        private static void RestoreEditorScenes(SceneSetup[] setup)
        {
            var restorable = setup?.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
            if (restorable != null && restorable.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(restorable);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>
        /// A scripted device: the keys "held" and the cursor's ground point, sampled exactly the
        /// way a real source is. What EditMode cannot do — press a physical key — is exactly the
        /// part this fake replaces; everything downstream of <see cref="IInputSource"/> is real.
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
