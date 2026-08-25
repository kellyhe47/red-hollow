using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Raised when a fixture's `given` (or `when`) cannot be expressed through the production types.
    /// It is deliberately loud and specific: a fixture failing with this is an adapter/contract
    /// problem to escalate, never a rule that is merely unimplemented.
    /// </summary>
    internal sealed class FixtureContractException : Exception
    {
        internal FixtureContractException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// A fixture's `given` realised as the real product objects the sim is constructed from.
    /// Nothing here is a shadow model: every field lives on a production type, so a rename in
    /// the sim breaks the adapter at compile time instead of silently drifting.
    /// </summary>
    internal sealed class Scenario
    {
        internal MatchSim Sim;
        internal MatchState State;
        internal SimConfig Config;
        internal SimClock Clock;
        internal InMemoryProfileStore Profiles;

        /// <summary>Nearest-first, as the shell's raycast reports it (`inputs.entities_on_line`).</summary>
        internal readonly List<LineEntity> EntitiesOnLine = new List<LineEntity>();

        /// <summary>
        /// Accounts the fixture seeded. The store itself cannot answer this — R-44 makes an unknown
        /// callsign a fresh account rather than a miss — so the loader records what it planted.
        /// </summary>
        internal readonly HashSet<string> SeededAccounts = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds a real <see cref="MatchSim"/> from a fixture's `given`.
    ///
    /// The loader is driven by the JSON keys actually present, not by a per-fixture switch — that is
    /// what lets a new fixture with a familiar shape load with no adapter change. Any key it does not
    /// recognise throws <see cref="FixtureContractException"/> rather than being ignored, because a
    /// silently dropped `given` would make a fixture pass (or fail) for the wrong reason.
    /// </summary>
    internal static class ScenarioLoader
    {
        internal static Scenario Load(Fixture fixture)
        {
            var scenario = new Scenario
            {
                State = new MatchState(),
                Config = new SimConfig(),
                Profiles = new InMemoryProfileStore(),
            };

            ApplyConfiguration(fixture, scenario.Config);
            scenario.Clock = new SimClock(ClockElapsed(fixture));

            // Order matters: world shell (phase/wave/team/players) -> entities -> effects riding on
            // entities. `preexisting_state.status_effects` names monsters that `inputs` introduces.
            ApplyPreexistingState(fixture, scenario);
            var oracle = ApplyInputs(fixture, scenario);
            ApplyStatusEffects(fixture, scenario);

            scenario.Sim = new MatchSim(
                scenario.State, scenario.Config, scenario.Profiles, scenario.Clock, oracle);
            return scenario;
        }

        // ---- clock -------------------------------------------------------------------------------

        /// <summary>
        /// `clock.as_of` is a provenance stamp for the fixture author; the sim never reads a wall
        /// clock (R-51), so only `sim_elapsed` crosses the boundary.
        /// </summary>
        private static double ClockElapsed(Fixture fixture)
        {
            foreach (var key in Json.Keys(fixture.Clock))
            {
                if (key != "sim_elapsed" && key != "as_of")
                {
                    throw Unknown(fixture, "given.clock", key);
                }
            }

            return Json.Num(fixture.Clock, "sim_elapsed");
        }

        // ---- configuration -----------------------------------------------------------------------

        private static void ApplyConfiguration(Fixture fixture, SimConfig config)
        {
            var configuration = fixture.Configuration;
            foreach (var key in Json.Keys(configuration))
            {
                switch (key)
                {
                    case "total_waves":
                        config.TotalWaves = Json.Int(configuration, key);
                        break;
                    case "planning_duration_seconds":
                        config.PlanningDurationSeconds = Json.Num(configuration, key);
                        break;
                    case "starting_scrip":
                        config.StartingScrip = Json.Int(configuration, key);
                        break;
                    case "damage_per_civilian":
                        config.DamagePerCivilian = Json.Num(configuration, key);
                        break;
                    case "sell_refund_ratio":
                        config.SellRefundRatio = Json.Num(configuration, key);
                        break;
                    case "sawbones_damage_reduction":
                        config.SawbonesDamageReduction = Json.Num(configuration, key);
                        break;
                    case "lasso_slow_multiplier":
                        config.LassoSlowMultiplier = Json.Num(configuration, key);
                        break;
                    case "lasso_duration_seconds":
                        config.LassoDurationSeconds = Json.Num(configuration, key);
                        break;
                    case "respawn_delay_seconds":
                        config.RespawnDelaySeconds = Json.Num(configuration, key);
                        break;
                    case "respawn_point":
                        config.RespawnPoint = Json.Pos(configuration, key);
                        break;
                    case "max_ability_rank":
                        config.MaxAbilityRank = Json.Int(configuration, key);
                        break;
                    case "friendly_fire":
                        config.FriendlyFire = Json.Bool(configuration, key);
                        break;
                    case "level_threshold_coefficient":
                        config.LevelThresholdCoefficient = Json.Num(configuration, key);
                        break;
                    case "regen_hp_per_second":
                        config.RegenHpPerSecond = Json.Num(configuration, key);
                        break;
                    case "regen_delay_seconds":
                        config.RegenDelaySeconds = Json.Num(configuration, key);
                        break;
                    case "monster_attack_interval_seconds":
                        config.MonsterAttackIntervalSeconds = Json.Num(configuration, key);
                        break;

                    // Formula descriptors: the fixture states the rule in prose, and the tunable that
                    // implements it already carries the number. Nothing to set — but we check the
                    // prose still agrees with the tunable, so a config default drifting away from a
                    // fixture's stated formula is caught here instead of as a mystery mismatch.
                    case "civilians_killed_per_hit":
                        RequireDescriptor(fixture, key, Json.Str(configuration, key),
                            "ceil(damage/" + Number(config.DamagePerCivilian) + ")");
                        break;
                    case "level_threshold":
                        RequireDescriptor(fixture, key, Json.Str(configuration, key),
                            Number(config.LevelThresholdCoefficient) + "*L*(L-1)/2");
                        break;
                    case "xp_per_kill":
                        RequireDescriptor(fixture, key, Json.Str(configuration, key), "bounty");
                        break;

                    default:
                        throw Unknown(fixture, "given.configuration", key);
                }
            }
        }

        // ---- preexisting state -------------------------------------------------------------------

        private static void ApplyPreexistingState(Fixture fixture, Scenario scenario)
        {
            var preexisting = fixture.Preexisting;
            var state = scenario.State;

            foreach (var key in Json.Keys(preexisting))
            {
                var node = Json.Node(preexisting, key);
                switch (key)
                {
                    case "match":
                        foreach (var field in Json.Keys(node))
                        {
                            switch (field)
                            {
                                case "phase":
                                    state.Phase = Json.Str(node, field);
                                    break;
                                case "status":
                                    state.Status = Json.Str(node, field);
                                    break;
                                case "planning_started_at":
                                    state.PlanningStartedAt = Json.Num(node, field);
                                    break;
                                default:
                                    throw Unknown(fixture, "given.preexisting_state.match", field);
                            }
                        }

                        break;

                    case "wave":
                        foreach (var field in Json.Keys(node))
                        {
                            switch (field)
                            {
                                case "number":
                                    state.Wave.Number = Json.Int(node, field);
                                    break;
                                case "total_waves":
                                    state.Wave.TotalWaves = Json.Int(node, field);
                                    break;
                                case "living_monster_ids":
                                    // The living roster is the wave's own state AND the existence
                                    // claim for those monsters; materialise both.
                                    foreach (var id in Json.Arr(node, field).Select(n => n.GetValue<string>()))
                                    {
                                        state.Wave.LivingMonsterIds.Add(id);
                                        if (!state.Monsters.ContainsKey(id))
                                        {
                                            state.Monsters[id] = new Monster { Id = id, Alive = true };
                                        }
                                    }

                                    break;
                                default:
                                    throw Unknown(fixture, "given.preexisting_state.wave", field);
                            }
                        }

                        break;

                    case "team":
                        foreach (var field in Json.Keys(node))
                        {
                            if (field != "scrip")
                            {
                                throw Unknown(fixture, "given.preexisting_state.team", field);
                            }

                            state.Team.Scrip = Json.Int(node, field);
                        }

                        break;

                    case "players":
                        foreach (var player in node.AsArray())
                        {
                            foreach (var field in Json.Keys(player))
                            {
                                if (field != "id" && field != "ready" && field != "account_id"
                                    && field != "hero_class" && field != "connected")
                                {
                                    throw Unknown(fixture, "given.preexisting_state.players[]", field);
                                }
                            }

                            state.Players.Add(new PlayerSlot
                            {
                                Id = Json.Str(player, "id"),
                                AccountId = Json.Str(player, "account_id"),
                                HeroClass = Json.Str(player, "hero_class"),
                                Ready = Json.Bool(player, "ready"),
                                Connected = Json.Bool(player, "connected", true),
                            });
                        }

                        break;

                    case "status_effects":
                        // Applied after `inputs`, once the monsters they ride on exist.
                        break;

                    default:
                        throw Unknown(fixture, "given.preexisting_state", key);
                }
            }
        }

        private static void ApplyStatusEffects(Fixture fixture, Scenario scenario)
        {
            var effects = Json.Node(fixture.Preexisting, "status_effects");
            foreach (var monsterId in Json.Keys(effects))
            {
                if (!scenario.State.Monsters.TryGetValue(monsterId, out var monster))
                {
                    throw new FixtureContractException(
                        fixture.Id + ": given.preexisting_state.status_effects names monster '" + monsterId
                        + "', which no input declares");
                }

                foreach (var effect in Json.Arr(effects, monsterId))
                {
                    monster.StatusEffects.Add(
                        new StatusEffect(Json.Str(effect, "type"), Json.Num(effect, "expires_at")));
                }
            }
        }

        // ---- inputs ------------------------------------------------------------------------------

        /// <summary>
        /// Materialises every entity the fixture declares and returns the path oracle it implies:
        /// a <see cref="DeclaredPathOracle"/> when blockers are declared, otherwise an
        /// <see cref="OpenPathOracle"/> — the sim has no NavMesh to ask here (R-51).
        /// </summary>
        private static IPathOracle ApplyInputs(Fixture fixture, Scenario scenario)
        {
            var inputs = fixture.Inputs;
            var state = scenario.State;
            DeclaredPathOracle declared = null;

            foreach (var key in Json.Keys(inputs))
            {
                var node = Json.Node(inputs, key);
                switch (key)
                {
                    case "monster":
                        AddMonster(fixture, state, node);
                        break;

                    case "monsters":
                        foreach (var monster in node.AsArray())
                        {
                            AddMonster(fixture, state, monster);
                        }

                        break;

                    case "hotspots":
                        foreach (var hotspot in node.AsArray())
                        {
                            AddHotspot(fixture, state, hotspot);
                        }

                        break;

                    case "hero":
                        AddHero(fixture, state, node);
                        break;

                    case "heroes":
                        foreach (var hero in node.AsArray())
                        {
                            AddHero(fixture, state, hero);
                        }

                        break;

                    case "placeable":
                        AddPlaceable(fixture, state, node);
                        break;

                    case "placeables":
                        foreach (var placeable in node.AsArray())
                        {
                            AddPlaceable(fixture, state, placeable);
                        }

                        break;

                    case "turret":
                        AddPlaceable(fixture, state, node);
                        break;

                    case "candidates":
                        foreach (var candidate in node.AsArray())
                        {
                            AddByKind(fixture, scenario, candidate, "given.inputs.candidates[]");
                        }

                        break;

                    case "blockers":
                        declared = declared ?? new DeclaredPathOracle();
                        foreach (var blocker in node.AsArray())
                        {
                            AddByKind(fixture, scenario, blocker, "given.inputs.blockers[]");
                            var between = Json.Arr(blocker, "blocks_path_between");
                            if (between.Count != 2)
                            {
                                throw new FixtureContractException(
                                    fixture.Id + ": blocks_path_between must be [mover_id, target_id]");
                            }

                            declared.Declare(
                                between[0].GetValue<string>(),
                                between[1].GetValue<string>(),
                                Json.Str(blocker, "id"));
                        }

                        break;

                    case "entities_on_line":
                        foreach (var entity in node.AsArray())
                        {
                            AddByKind(fixture, scenario, entity, "given.inputs.entities_on_line[]");
                            scenario.EntitiesOnLine.Add(new LineEntity
                            {
                                Id = Json.Str(entity, "id"),
                                Kind = Json.Str(entity, "kind"),
                                Pos = Json.Pos(entity, "pos"),
                            });
                        }

                        break;

                    case "profile":
                        AddProfile(fixture, scenario, node);
                        break;

                    case "kill":
                        // The kill descriptor is the command's payload, but it also types the monster
                        // the wave roster already listed as living.
                        var killedId = Json.Str(node, "monster_id");
                        if (killedId != null && state.Monsters.TryGetValue(killedId, out var killed))
                        {
                            killed.Type = Json.Str(node, "monster_type", killed.Type);
                        }

                        break;

                    // Pure command payloads: read by OperationDispatch, no entity of their own.
                    case "attack":
                    case "ability":
                    case "crossing":
                    case "purchase":
                    case "ready":
                    case "sell":
                    case "spend":
                    case "transition":
                        break;

                    default:
                        throw Unknown(fixture, "given.inputs", key);
                }
            }

            return (IPathOracle)declared ?? new OpenPathOracle();
        }

        private static void AddByKind(Fixture fixture, Scenario scenario, JsonNode node, string where)
        {
            var kind = Json.Str(node, "kind");
            switch (kind)
            {
                case "hero":
                    AddHero(fixture, scenario.State, node);
                    break;
                case "hotspot":
                    AddHotspot(fixture, scenario.State, node);
                    break;
                case "monster":
                    AddMonster(fixture, scenario.State, node);
                    break;
                case "barricade":
                    AddPlaceable(fixture, scenario.State, node, PlaceableType.Barricade);
                    break;
                default:
                    throw new FixtureContractException(
                        fixture.Id + ": " + where + " declares kind '" + kind + "', which maps to no sim entity");
            }
        }

        private static void AddMonster(Fixture fixture, MatchState state, JsonNode node)
        {
            var id = Json.Str(node, "id");
            RequireId(fixture, id, "monster");
            var baseSpeed = Json.Num(node, "base_speed");
            state.Monsters[id] = new Monster
            {
                Id = id,
                Type = Json.Str(node, "type"),
                Pos = Json.Pos(node, "pos"),
                Hp = Json.Num(node, "hp"),
                Alive = Json.Bool(node, "alive", true),
                BaseSpeed = baseSpeed,
                // A monster given only a base speed is moving at it.
                CurrentSpeed = Json.Has(node, "current_speed") ? Json.Num(node, "current_speed") : baseSpeed,
            };
        }

        private static void AddHero(Fixture fixture, MatchState state, JsonNode node)
        {
            var id = Json.Str(node, "id");
            RequireId(fixture, id, "hero");
            var hp = Json.Num(node, "hp");
            state.Heroes[id] = new Hero
            {
                Id = id,
                // Fixtures spell the class field "class" (a C# keyword) or "hero_class".
                HeroClass = Json.Str(node, "class") ?? Json.Str(node, "hero_class"),
                AccountId = Json.Str(node, "account_id"),
                Pos = Json.Pos(node, "pos"),
                Hp = hp,
                MaxHp = Json.Has(node, "max_hp") ? Json.Num(node, "max_hp") : hp,
                Alive = Json.Bool(node, "alive", true),
            };
        }

        private static void AddHotspot(Fixture fixture, MatchState state, JsonNode node)
        {
            var id = Json.Str(node, "id");
            RequireId(fixture, id, "hotspot");
            state.Hotspots[id] = new Hotspot
            {
                Id = id,
                Pos = Json.Pos(node, "pos"),
                Civilians = Json.Int(node, "civilians"),
            };
        }

        private static void AddPlaceable(Fixture fixture, MatchState state, JsonNode node, string typeOverride = null)
        {
            var id = Json.Str(node, "id");
            RequireId(fixture, id, "placeable");
            state.Placeables[id] = new Placeable
            {
                Id = id,
                Type = typeOverride ?? Json.Str(node, "type"),
                Pos = Json.Pos(node, "pos"),
                OwnerPlayerId = Json.Str(node, "owner_player_id"),
                PurchaseCost = Json.Int(node, "purchase_cost"),
                Hp = Json.Num(node, "hp"),
                Exists = Json.Bool(node, "exists", true),
                // A turret spells its per-tick damage `damage_per_tick`; traps spell it `damage`.
                Damage = Json.Has(node, "damage_per_tick") ? Json.Num(node, "damage_per_tick") : Json.Num(node, "damage"),
                TriggersRemaining = Json.Int(node, "triggers_remaining"),
                BlastRadius = Json.Num(node, "blast_radius"),
                Range = Json.Num(node, "range"),
            };
        }

        /// <summary>
        /// Seeds the fixture-backed profile fake — the injected IProfileStore seam of R-43/R-44.
        /// A declared `hero_id` is the fixture stating which hero this account is playing, and the
        /// only place that association can live is the Hero entity itself.
        /// </summary>
        private static void AddProfile(Fixture fixture, Scenario scenario, JsonNode node)
        {
            foreach (var field in Json.Keys(node))
            {
                if (field != "account_id" && field != "hero_id" && field != "lifetime_xp"
                    && field != "level" && field != "skill_points" && field != "abilities")
                {
                    throw Unknown(fixture, "given.inputs.profile", field);
                }
            }

            var accountId = Json.Str(node, "account_id");
            RequireId(fixture, accountId, "profile");

            var profile = new AccountProfile
            {
                AccountId = accountId,
                LifetimeXp = Json.Num(node, "lifetime_xp"),
                Level = Json.Int(node, "level", 1),
                SkillPoints = Json.Int(node, "skill_points"),
            };

            var abilities = Json.Node(node, "abilities");
            foreach (var ability in Json.Keys(abilities))
            {
                profile.Abilities[ability] = Json.Int(abilities, ability);
            }

            scenario.Profiles.Seed(profile);
            scenario.SeededAccounts.Add(accountId);

            var heroId = Json.Str(node, "hero_id");
            if (heroId != null)
            {
                if (scenario.State.Heroes.TryGetValue(heroId, out var hero))
                {
                    hero.AccountId = accountId;
                }
                else
                {
                    scenario.State.Heroes[heroId] = new Hero { Id = heroId, AccountId = accountId };
                }
            }
        }

        // ---- diagnostics -------------------------------------------------------------------------

        private static void RequireId(Fixture fixture, string id, string what)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new FixtureContractException(fixture.Id + ": a declared " + what + " has no id");
            }
        }

        private static void RequireDescriptor(Fixture fixture, string key, string declared, string implemented)
        {
            if (declared != implemented)
            {
                throw new FixtureContractException(
                    fixture.Id + ": given.configuration." + key + " declares \"" + declared
                    + "\" but SimConfig implements \"" + implemented + "\" — spec and product disagree");
            }
        }

        private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static FixtureContractException Unknown(Fixture fixture, string where, string key) =>
            new FixtureContractException(
                fixture.Id + " (" + fixture.FileName + "): " + where + "." + key
                + " is declared by the fixture but the adapter has no production type to load it into");
    }
}
