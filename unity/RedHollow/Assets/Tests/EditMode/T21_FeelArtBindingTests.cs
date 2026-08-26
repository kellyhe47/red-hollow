using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Art;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 021 (T-21), part 2 of 2 — the feel feed and the art chain. Ticket 013 built and
    /// locked <see cref="FeelRouter"/>/<see cref="FeelRig"/> and
    /// <see cref="ArtVisualResolver"/>/<see cref="ArtCatalog"/>; nothing at runtime constructed
    /// either, so no sim event ever produced juice and every view wore the placeholder forever.
    ///
    /// Pinned here:
    ///
    ///  1. <b>The feel feed is live</b> — a <c>monster_damaged</c> event emitted by the sim during
    ///     a driven match (nobody calls <see cref="FeelRouter.Route"/> by hand) flashes and nudges
    ///     that monster's VIEW TRANSFORM on the next <see cref="ShellBootstrap.Pump"/>, while the
    ///     sim-authoritative position and the view's authoritative
    ///     <see cref="MonsterView.WorldPosition"/> stay exactly where replication put them.
    ///  2. <b>Feel is ticked and temporary</b> — the flash expires and the nudge decays back as
    ///     pumps advance, leaving the view standing at its authoritative position again.
    ///  3. <b>The default catalog is the four delivered representatives</b> — registered under the
    ///     <see cref="ShellArtKeys"/> spellings, each resolving to real art
    ///     (<c>IsPlaceholder == false</c>) through the chained resolver, with the character key
    ///     spelled as the hero-class literal the binder actually resolves with.
    ///  4. <b>The bootstrap's binder resolves through that chain</b> — a hero (registered key)
    ///     stands in real art, a shambler (no representative delivered) stands in the placeholder,
    ///     both through the one resolver the bootstrap built.
    ///
    /// <b>Not asserted</b>: nudge direction/magnitude and flash duration (playtest's numbers —
    /// only "present, then gone" is contract, with a generous 1s bound), audio actually playing,
    /// what shape real art instantiates as, and how the default catalog loads its assets (any
    /// runtime-legal mechanism; T-13's imported paths must simply stay put for its locked tests).
    /// </summary>
    [TestFixture]
    public class T21_FeelArtBindingTests
    {
        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";

        /// <summary>Presentation offsets cross float math; anything above this is "an offset".</summary>
        private const float OffsetEpsilon = 1e-4f;

        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private ShellBootstrap _shell;

        private readonly List<GameObject> _spawned = new List<GameObject>();

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

            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        // ==========================================================================================
        //  AC2 — the sim's event stream reaches FeelRouter, and feel lands on the view transform
        // ==========================================================================================

        /// <summary>
        /// R-64 / R-51. The whole feed in one chain: the sim emits <c>monster_damaged</c>
        /// (<see cref="MatchSim.ResolveHeroAttack"/>, the real command), the next pump routes it,
        /// and the hit monster's view is flashing and standing OFF its authoritative position by
        /// the rig's nudge — while the sim's own position and the view's
        /// <see cref="MonsterView.WorldPosition"/> have not moved by a nanometre.
        ///
        /// <c>Pump(0.0)</c> throughout, so the ONLY thing that can displace anything is the feel
        /// layer: a zero-delta pump advances no clock and moves no monster, which is what makes
        /// "sim position untouched" assertable as exact equality rather than a tolerance.
        /// </summary>
        [Test]
        public void A_monster_damaged_event_flashes_and_nudges_the_view_and_never_the_sim()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            var monsterId = match.State.Wave.LivingMonsterIds[0];
            var monster = match.State.Monsters[monsterId];
            var view = shell.Views.MonsterViewFor(monsterId);

            Assert.That(view, Is.Not.Null, "sanity (R-51): the spawned monster has a bound view");
            Assert.That((view.transform.position - view.WorldPosition).magnitude,
                Is.LessThan(OffsetEpsilon),
                "anti-vacuity: before any hit the view stands exactly at its authoritative position");

            var posBefore = monster.Pos;

            var hero = OwnHero(match.State);
            var result = match.Sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = hero.Id,
                AttackerClass = hero.HeroClass,
                Damage = 5.0,
                EntitiesOnLine = new List<LineEntity>
                {
                    new LineEntity { Id = monsterId, Kind = "monster", Pos = monster.Pos },
                },
            });
            Assert.That(result.HitId, Is.EqualTo(monsterId),
                "sanity (R-26): the attack really hit the monster, so monster_damaged was emitted");

            shell.Pump(0.0);

            var feel = shell.Feel.FeelFor(monsterId);
            Assert.That(feel.IsFlashing, Is.True,
                "R-64: the sim's monster_damaged must reach FeelRouter through the pump's feed — "
                + "nothing in this test calls Route by hand");
            Assert.That(feel.NudgeOffset.magnitude, Is.GreaterThan(OffsetEpsilon),
                "R-64: a landed hit shoves the presentation");

            var offset = view.transform.position - view.WorldPosition;
            Assert.That(offset.magnitude, Is.GreaterThan(OffsetEpsilon),
                "R-64: the pump applies FeelRig on top of the binder's sync — a router that "
                + "accumulates feel nobody applies is juice nobody sees");
            Assert.That((offset - feel.NudgeOffset).magnitude, Is.LessThan(OffsetEpsilon),
                "the transform's displacement IS the router's nudge — not some second offset");

            Assert.That(monster.Pos, Is.EqualTo(posBefore),
                "R-51/T-10: feel is presentation only — the sim-authoritative position is exactly "
                + "untouched (a zero-delta pump moves nothing)");
            Assert.That((view.WorldPosition - SimSpace.ToWorld(posBefore)).magnitude,
                Is.LessThan(OffsetEpsilon),
                "R-51: WorldPosition stays the sim's answer; the nudge rides on the transform only");
        }

        /// <summary>
        /// R-64. Feel is temporary: the pump ticks <see cref="FeelRouter.Tick"/> with its delta, so
        /// after a second of driven pumps the flash is out and the view stands (essentially) at its
        /// authoritative position again. One second is deliberately generous — duration is
        /// playtest's number, "temporary" is the contract.
        /// </summary>
        [Test]
        public void The_flash_expires_and_the_nudge_decays_as_pumps_tick()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            var monsterId = match.State.Wave.LivingMonsterIds[0];
            var monster = match.State.Monsters[monsterId];
            var view = shell.Views.MonsterViewFor(monsterId);
            var hero = OwnHero(match.State);

            match.Sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = hero.Id,
                AttackerClass = hero.HeroClass,
                Damage = 5.0,
                EntitiesOnLine = new List<LineEntity>
                {
                    new LineEntity { Id = monsterId, Kind = "monster", Pos = monster.Pos },
                },
            });

            shell.Pump(0.0);

            var initial = (view.transform.position - view.WorldPosition).magnitude;
            Assert.That(initial, Is.GreaterThan(OffsetEpsilon),
                "sanity: the hit displaced the view before decay is measured");

            // One second of driven pumps. The match keeps running underneath (monsters walk); the
            // offset is always measured against WorldPosition, so movement cannot fake a decay.
            for (var i = 0; i < 20; i++)
            {
                shell.Pump(0.05);
            }

            Assert.That(shell.Feel.FeelFor(monsterId).IsFlashing, Is.False,
                "R-64: a hit flash is temporary — still flashing a second later means Tick is "
                + "never driven with the pump's delta");

            var settled = (view.transform.position - view.WorldPosition).magnitude;
            Assert.That(settled, Is.LessThan(initial * 0.25f),
                "R-64: the nudge decays back toward the authoritative position (it was " + initial
                + ", still " + settled + " a second later — the spring is not being ticked)");
        }

        // ==========================================================================================
        //  AC3 — the default catalog registers the four delivered representative assets
        // ==========================================================================================

        /// <summary>
        /// R-15. The default catalog is DATA naming the four delivered representatives (the exact
        /// assets T-13's seam tests import-check), and through the chained resolver every one of
        /// them is real art — <c>IsPlaceholder == false</c>, an instance in the scene — while an
        /// unknown key still falls through to the placeholder. The character key is pinned to the
        /// hero-class literal, because that is the artKey <see cref="MatchViewBinder"/> actually
        /// resolves heroes with: art registered under a spelling nothing resolves is art nobody
        /// ever sees.
        /// </summary>
        [Test]
        public void The_default_catalog_registers_the_four_representative_assets_as_real_art()
        {
            var catalog = ShellBootstrap.LoadRepresentativeArt();
            Assert.That(catalog, Is.Not.Null, "the default catalog exists");

            Assert.That(ShellArtKeys.GunslingerCharacter, Is.EqualTo(HeroClass.Gunslinger),
                "the character key must be the class literal the binder resolves with — any other "
                + "spelling is a catalog full of art no hero view can find");

            var expectations = new[]
            {
                (key: ShellArtKeys.GroundTile, visualClass: VisualClass.Ground),
                (key: ShellArtKeys.GunslingerCharacter, visualClass: VisualClass.Hero),
                (key: ShellArtKeys.RevolverShotIcon, visualClass: VisualClass.Placeable),
                (key: ShellArtKeys.ButtonFrame, visualClass: VisualClass.Placeable),
            };

            Assert.That(expectations.Select(e => e.key).Distinct().Count(), Is.EqualTo(4),
                "sanity: four distinct representatives, one per delivered asset class");

            var resolver = new ArtVisualResolver(catalog, new PlaceholderVisualResolver());

            foreach (var (key, visualClass) in expectations)
            {
                Assert.That(catalog.Contains(key), Is.True,
                    "R-15: the delivered representative must be registered — missing: " + key);

                var handle = resolver.Resolve(visualClass, key);
                Track(handle == null ? null : handle.Instance);

                Assert.That(handle, Is.Not.Null, key + " resolves");
                Assert.That(handle.IsPlaceholder, Is.False,
                    "R-15: " + key + " is delivered art, and the handle must say so");
                Assert.That(handle.Instance, Is.Not.Null,
                    "R-15: " + key + " instantiates something in the scene");
            }

            var absent = resolver.Resolve(VisualClass.Monster, MonsterType.Shambler);
            Track(absent.Instance);
            Assert.That(absent.IsPlaceholder, Is.True,
                "no monster representative was delivered, so the shambler key honestly falls "
                + "through to the placeholder — registering art nobody made would be a lie");
        }

        // ==========================================================================================
        //  AC3 — the bootstrap's binder resolves through the chained resolver
        // ==========================================================================================

        /// <summary>
        /// R-15 / R-51. The chain end to end, through the bootstrap's OWN binder in a driven match
        /// (the criterion this ticket was opened for — <c>MatchViewBinder(visuals: null)</c>
        /// defaulted to placeholder-only everywhere): the host's gunslinger hero stands in real art
        /// because its class key is registered, and a shambler stands in the placeholder because no
        /// monster representative exists. Both answers come from the one resolver the bootstrap
        /// exposes, and that resolver stays total.
        /// </summary>
        [Test]
        public void The_bootstraps_binder_dresses_a_hero_in_real_art_and_a_shambler_in_the_placeholder()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            // The default catalog rode into the bootstrap: no options named one.
            foreach (var key in new[]
            {
                ShellArtKeys.GroundTile, ShellArtKeys.GunslingerCharacter,
                ShellArtKeys.RevolverShotIcon, ShellArtKeys.ButtonFrame,
            })
            {
                Assert.That(shell.Art.Contains(key), Is.True,
                    "R-15: a shell built with no catalog uses the representative default — "
                    + "missing: " + key);
            }

            var hero = OwnHero(match.State);
            Assert.That(hero.HeroClass, Is.EqualTo(HeroClass.Gunslinger), "sanity: the host picked gunslinger");

            var heroView = shell.Views.HeroViewFor(hero.Id);
            Assert.That(heroView, Is.Not.Null, "sanity (R-51): the seated hero has a view");
            Assert.That(heroView.Visual, Is.Not.Null, "the view wears a resolved visual");
            Assert.That(heroView.Visual.IsPlaceholder, Is.False,
                "R-15: the binder resolves through the art chain, so a registered key (the "
                + "gunslinger class literal) is REAL art on the live view — a binder still built "
                + "over the bare placeholder is exactly the gap this ticket closes");

            var monsterView = shell.Views.MonsterViewFor(match.State.Wave.LivingMonsterIds[0]);
            Assert.That(monsterView, Is.Not.Null, "sanity (R-51): the spawned monster has a view");
            Assert.That(monsterView.Visual, Is.Not.Null, "the monster view wears a resolved visual");
            Assert.That(monsterView.Visual.IsPlaceholder, Is.True,
                "R-15: an unregistered key (no monster representative was delivered) falls through "
                + "the same chain to the placeholder — the seam stays total in both directions");

            // And the resolver the bootstrap exposes answers both ways itself.
            var real = shell.Visuals.Resolve(VisualClass.Hero, ShellArtKeys.GunslingerCharacter);
            Track(real.Instance);
            Assert.That(real.IsPlaceholder, Is.False, "the exposed resolver resolves registered keys to real art");

            var absent = shell.Visuals.Resolve(VisualClass.Monster, "characters/never-generated_v1");
            Track(absent.Instance);
            Assert.That(absent.IsPlaceholder, Is.True, "and unknown keys to the placeholder, never a throw");
        }

        // ==========================================================================================
        //  scenario builders and helpers
        // ==========================================================================================

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

            return _shell;
        }

        private static HostedMatch StartMatch(ShellBootstrap shell)
        {
            Assert.That(shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts the match");

            var match = shell.Session.Match;
            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "sanity (R-19): the match opened with its wave in the colony");

            return match;
        }

        private static Hero OwnHero(MatchState state)
        {
            var hero = state.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, HostAccount, StringComparison.Ordinal));
            Assert.That(hero, Is.Not.Null, "sanity: the factory seated the host's hero");
            return hero;
        }

        private GameObject Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }

            return go;
        }
    }
}
