using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 014 (T-14): the R-19 wave-table structure pins. R-19 is explicitly playtest-tuned and
    /// deliberately unfixtured — "the shape is contract, the numbers are taste" — so these tests
    /// pin ONLY the facts the PRD itself decides, and pin them as ranges where the PRD says "≈":
    ///
    ///  * wave 1 ≈ 6 Shamblers (single archetype, single breach);
    ///  * Behemoths first appear at wave 5 — never earlier;
    ///  * wave 10 ≈ 30 mixed monsters through all 4 tunnels.
    ///
    /// Everything between those points — the exact counts, which archetype joins at which wave,
    /// which breach subset opens when — is tuning the owner must be able to move WITHOUT unlocking
    /// a test, so nothing here asserts an exact count where the PRD wrote "≈", and nothing pins
    /// the intermediate waves beyond structural sanity (every row names a real archetype, a
    /// positive count and a valid, distinct tunnel subset — R-19/R-14's "shape is contract").
    ///
    /// The tolerances are deliberate and stated: "≈ 6" is pinned as 4–8, "≈ 30" as 25–35. Outside
    /// those bands the table is no longer the campaign the PRD describes, and a change that far is
    /// a spec conversation, not a tuning pass.
    /// </summary>
    [TestFixture]
    public class T14_WaveTablePinTests
    {
        // ---- structural sanity: the shape is contract (R-19 / R-14) ----------------------------

        /// <summary>
        /// R-01 / R-19. The shipped campaign defines exactly the configured number of waves,
        /// numbered 1..N with no gaps and no duplicates — <see cref="WaveTable.For"/> resolves
        /// every wave the match FSM will ask for, and there is no eleventh.
        /// </summary>
        [Test]
        public void The_shipped_table_defines_exactly_the_campaign_waves()
        {
            var table = WaveTable.V1();
            var totalWaves = new SimConfig().TotalWaves;

            Assert.That(table.Waves.Count, Is.EqualTo(totalWaves),
                "R-01 / R-19: the shipped table carries one spec per campaign wave");

            var numbers = table.Waves.Select(w => w.Number).OrderBy(n => n).ToList();
            Assert.That(numbers, Is.EqualTo(Enumerable.Range(1, totalWaves).ToList()),
                "R-19: numbered 1.." + totalWaves + " with no gaps and no duplicates; found ["
                + string.Join(", ", numbers) + "]");

            for (var wave = 1; wave <= totalWaves; wave++)
            {
                Assert.That(table.For(wave), Is.Not.Null,
                    "R-19: For(" + wave + ") must resolve — the match FSM will ask");
            }
        }

        /// <summary>
        /// R-19 / R-14 / R-17. Every row of every wave is spawnable as shipped: each archetype has
        /// a catalog row (a wave naming an unknown monster is a match that throws at spawn), each
        /// count is positive, and each wave's tunnel subset is non-empty, duplicate-free and made
        /// of valid indices into the one map's four breaches.
        /// </summary>
        [Test]
        public void Every_wave_is_spawnable_against_the_shipped_roster_and_map()
        {
            var table = WaveTable.V1();
            var roster = new MonsterCatalog();
            var tunnelCount = ColonyMap.V1().EntryTunnels.Count;

            Assert.That(tunnelCount, Is.EqualTo(4), "sanity (R-10/R-14): the v1 map has 4 breaches");

            foreach (var wave in table.Waves)
            {
                Assert.That(wave.Groups, Is.Not.Empty,
                    "R-19: wave " + wave.Number + " must send something");

                foreach (var group in wave.Groups)
                {
                    Assert.That(roster.Contains(group.MonsterType), Is.True,
                        "R-19 / R-17: wave " + wave.Number + " names archetype '"
                        + group.MonsterType + "', which has no catalog row — it would throw at "
                        + "spawn (and a NEW name here is also R-73's deferred boss arriving)");
                    Assert.That(group.Count, Is.GreaterThan(0),
                        "R-19: wave " + wave.Number + " has a zero/negative count for '"
                        + group.MonsterType + "'");
                }

                Assert.That(wave.ActiveTunnels, Is.Not.Empty,
                    "R-14: wave " + wave.Number + " must open at least one breach");
                Assert.That(wave.ActiveTunnels.Distinct().Count(), Is.EqualTo(wave.ActiveTunnels.Count),
                    "R-14: wave " + wave.Number + " lists a breach twice ["
                    + string.Join(", ", wave.ActiveTunnels) + "]");
                foreach (var tunnel in wave.ActiveTunnels)
                {
                    Assert.That(tunnel, Is.InRange(0, tunnelCount - 1),
                        "R-14: wave " + wave.Number + " opens tunnel index " + tunnel
                        + ", which the v1 map does not have (valid: 0.." + (tunnelCount - 1) + ")");
                }
            }
        }

        // ---- the decided ramp facts (R-19) ------------------------------------------------------

        /// <summary>
        /// R-19: "wave 1 ≈ 6 Shamblers" — a single-archetype opener through a single breach. The
        /// count is pinned as 4–8 (≈ means the owner may tune it; a wave-1 of 3 or 12 is a
        /// different opening, not a tuning pass), the archetype exactly (single archetype is the
        /// decided fact: one thing to learn on the tutorial wave), and the breach count exactly
        /// (one tunnel is what makes wave 1 the tutorial breach).
        /// </summary>
        [Test]
        public void Wave_one_is_a_handful_of_shamblers_through_a_single_breach()
        {
            var wave1 = WaveTable.V1().For(1);

            Assert.That(wave1.Groups.Select(g => g.MonsterType).Distinct().ToList(),
                Is.EqualTo(new[] { MonsterType.Shambler }),
                "R-19: wave 1 is Shamblers and nothing else — the single-archetype opener is the "
                + "decided fact");

            var count = wave1.Groups.Sum(g => g.Count);
            Assert.That(count, Is.InRange(4, 8),
                "R-19: wave 1 ≈ 6 Shamblers — pinned as 4–8 so tuning stays possible; found "
                + count);

            Assert.That(wave1.ActiveTunnels.Count, Is.EqualTo(1),
                "R-19 / R-14: wave 1 comes through a single breach");
        }

        /// <summary>
        /// R-19: "Behemoths appear from wave 5". Two halves, both decided: no Bull Behemoth
        /// anywhere in waves 1–4 (the early game is winnable without an answer to a 400 HP tank),
        /// and at least one at wave 5 (the appearance point itself). Later waves are free to use
        /// them or not — that is tuning.
        /// </summary>
        [Test]
        public void Behemoths_first_appear_at_wave_five_and_never_earlier()
        {
            var table = WaveTable.V1();

            for (var wave = 1; wave <= 4; wave++)
            {
                var behemoths = table.For(wave).Groups
                    .Where(g => g.MonsterType == MonsterType.BullBehemoth)
                    .Sum(g => g.Count);
                Assert.That(behemoths, Is.EqualTo(0),
                    "R-19: no Behemoth before wave 5 — wave " + wave + " sends " + behemoths);
            }

            var atFive = table.For(5).Groups
                .Where(g => g.MonsterType == MonsterType.BullBehemoth)
                .Sum(g => g.Count);
            Assert.That(atFive, Is.GreaterThanOrEqualTo(1),
                "R-19: Behemoths appear FROM wave 5 — wave 5 must send at least one");
        }

        /// <summary>
        /// R-19: "wave 10 ≈ 30 mixed monsters from all 4 tunnels". Total pinned as 25–35, "mixed"
        /// as more-than-one archetype (the literal decided word — how mixed is tuning), and "all 4
        /// tunnels" exactly: the finale opens every breach the map has.
        /// </summary>
        [Test]
        public void The_finale_is_about_thirty_mixed_monsters_through_all_four_tunnels()
        {
            var finale = WaveTable.V1().For(new SimConfig().TotalWaves);

            var total = finale.Groups.Sum(g => g.Count);
            Assert.That(total, Is.InRange(25, 35),
                "R-19: wave 10 ≈ 30 monsters — pinned as 25–35 so tuning stays possible; found "
                + total);

            Assert.That(finale.Groups.Select(g => g.MonsterType).Distinct().Count(),
                Is.GreaterThanOrEqualTo(2),
                "R-19: wave 10 is MIXED monsters — more than one archetype");

            var tunnels = finale.ActiveTunnels.Distinct().OrderBy(t => t).ToList();
            Assert.That(tunnels, Is.EqualTo(new[] { 0, 1, 2, 3 }),
                "R-19 / R-14: the finale pours out of ALL FOUR tunnels; found ["
                + string.Join(", ", tunnels) + "]");
        }

        /// <summary>
        /// R-19's tunability, which is as much contract as the ramp: the table is per-instance
        /// config, so one match's tuning must never move another's. Two calls to
        /// <see cref="WaveTable.V1"/> hand out independent tables — a static shared instance is
        /// the bug <see cref="MonsterCatalog"/> already guards against, relocated.
        /// </summary>
        [Test]
        public void Tuning_one_table_moves_no_other_table()
        {
            var tuned = WaveTable.V1();
            var pristine = WaveTable.V1();

            Assert.That(tuned, Is.Not.SameAs(pristine), "V1() builds fresh per call");

            var group = tuned.For(1).Groups[0];
            var originalCount = pristine.For(1).Groups[0].Count;
            group.Count = originalCount + 100;

            Assert.That(pristine.For(1).Groups[0].Count, Is.EqualTo(originalCount),
                "R-19: the table is per-instance config — editing one match's wave 1 must not "
                + "move another match's");
        }
    }
}
