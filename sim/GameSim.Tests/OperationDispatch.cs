using System;
using System.Text.Json.Nodes;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Maps a fixture's `when.operation` onto the real product entry point on <see cref="MatchSim"/>.
    ///
    /// This is the one place a per-operation switch is correct: the 15 operations ARE 15 distinct
    /// methods with distinct request types, and binding them by name is exactly what makes the
    /// fixtures grade the product rather than a test-only re-implementation. Anything thrown by the
    /// entry point (today: NotImplementedException) propagates untouched — that exception is the
    /// grade, so the adapter must never swallow or re-wrap it.
    /// </summary>
    internal static class OperationDispatch
    {
        internal static void Invoke(Fixture fixture, Scenario scenario)
        {
            var sim = scenario.Sim;
            var inputs = fixture.Inputs;

            switch (fixture.Operation)
            {
                case "select_target":
                {
                    sim.SelectTarget(Json.Str(Json.Node(inputs, "monster"), "id"));
                    return;
                }

                case "apply_hotspot_attack":
                {
                    var attack = Json.Node(inputs, "attack");
                    sim.ApplyHotspotAttack(new HotspotAttackRequest
                    {
                        AttackerId = Json.Str(attack, "attacker_id"),
                        AttackerType = Json.Str(attack, "attacker_type"),
                        Damage = Json.Num(attack, "damage"),
                        TargetId = Json.Str(attack, "target_id"),
                    });
                    return;
                }

                case "record_monster_kill":
                {
                    sim.RecordMonsterKill(KillRequest(inputs));
                    return;
                }

                case "begin_planning_phase":
                {
                    sim.BeginPlanningPhase();
                    return;
                }

                case "set_player_ready":
                {
                    sim.SetPlayerReady(Json.Str(Json.Node(inputs, "ready"), "player_id"));
                    return;
                }

                case "purchase_placement":
                {
                    var purchase = Json.Node(inputs, "purchase");
                    sim.PurchasePlacement(new PurchaseRequest
                    {
                        PlayerId = Json.Str(purchase, "player_id"),
                        PlaceableType = Json.Str(purchase, "placeable_type"),
                        Cost = Json.Int(purchase, "cost"),
                        Pos = Json.Pos(purchase, "pos"),
                        // R-24: the shell's placement checker decides zone validity; the fixture
                        // states its verdict as a word rather than re-deriving geometry here.
                        ZoneValid = Json.Str(purchase, "zone", "valid") == "valid",
                    });
                    return;
                }

                case "sell_placement":
                {
                    var sell = Json.Node(inputs, "sell");
                    sim.SellPlacement(new SellRequest
                    {
                        PlayerId = Json.Str(sell, "player_id"),
                        PlaceableId = Json.Str(sell, "placeable_id"),
                    });
                    return;
                }

                case "trigger_placeable":
                {
                    var crossing = Json.Node(inputs, "crossing");
                    sim.TriggerPlaceable(
                        Json.Str(crossing, "placeable_id"),
                        Json.Str(crossing, "monster_id"));
                    return;
                }

                case "turret_tick":
                {
                    sim.TurretTick(Json.Str(Json.Node(inputs, "turret"), "id"));
                    return;
                }

                case "apply_hero_damage":
                {
                    var attack = Json.Node(inputs, "attack");
                    sim.ApplyHeroDamage(new HeroDamageRequest
                    {
                        AttackerId = Json.Str(attack, "attacker_id"),
                        AttackerType = Json.Str(attack, "attacker_type"),
                        Damage = Json.Num(attack, "damage"),
                        TargetId = Json.Str(attack, "target_id"),
                    });
                    return;
                }

                case "resolve_hero_attack":
                {
                    var attack = Json.Node(inputs, "attack");
                    sim.ResolveHeroAttack(new HeroAttackRequest
                    {
                        AttackerId = Json.Str(attack, "attacker_id"),
                        AttackerClass = Json.Str(attack, "attacker_class"),
                        Damage = Json.Num(attack, "damage"),
                        // `attack.aim_line` is the shell's geometry; the sim is handed only who the
                        // raycast crossed, nearest-first, which is `inputs.entities_on_line`.
                        EntitiesOnLine = scenario.EntitiesOnLine,
                    });
                    return;
                }

                case "apply_ability":
                {
                    var ability = Json.Node(inputs, "ability");
                    sim.ApplyAbility(new AbilityCastRequest
                    {
                        CasterId = Json.Str(ability, "caster_id"),
                        Ability = Json.Str(ability, "ability"),
                        TargetId = Json.Str(ability, "target_id"),
                    });
                    return;
                }

                case "tick_status_effects":
                {
                    sim.TickStatusEffects();
                    return;
                }

                case "award_kill_xp":
                {
                    // R-40: XP is credited to an account, and the fixture's profile names it.
                    sim.AwardKillXp(KillRequest(inputs), Json.Str(Json.Node(inputs, "profile"), "account_id"));
                    return;
                }

                case "spend_skill_point":
                {
                    var spend = Json.Node(inputs, "spend");
                    sim.SpendSkillPoint(new SpendSkillPointRequest
                    {
                        AccountId = Json.Str(spend, "account_id"),
                        HeroId = Json.Str(spend, "hero_id"),
                        Choice = Json.Str(spend, "choice"),
                    });
                    return;
                }

                default:
                    throw new FixtureContractException(
                        fixture.Id + " (" + fixture.FileName + "): when.operation '" + fixture.Operation
                        + "' has no MatchSim entry point bound in the adapter");
            }
        }

        private static MonsterKillRequest KillRequest(JsonNode inputs)
        {
            var kill = Json.Node(inputs, "kill");
            return new MonsterKillRequest
            {
                MonsterId = Json.Str(kill, "monster_id"),
                MonsterType = Json.Str(kill, "monster_type"),
                Bounty = Json.Int(kill, "bounty"),
                KillerHeroId = Json.Str(kill, "killer_hero_id"),
            };
        }
    }
}
