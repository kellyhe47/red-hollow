#!/usr/bin/env python3
"""Independently re-derive every golden fixture's domain arithmetic from `given`.

Never reads copied expect values as inputs — each check recomputes the outcome
from given + the game rules, then compares against expect.
Run: python3 eval/verify_fixtures.py
"""
import json
import math
import sys
from pathlib import Path

GOLDEN = Path(__file__).parent / "golden"
failures = []


def load(fid):
    for f in GOLDEN.glob("*.json"):
        d = json.loads(f.read_text())
        if d["id"] == fid:
            return d
    raise KeyError(fid)


def check(fid, label, cond):
    if not cond:
        failures.append(f"{fid}: {label}")


def dist(a, b):
    return math.dist(a, b)


def derive_target(fx):
    """Re-derive targeting per B-001/B-002/B-003 from given only."""
    g = fx["given"]["inputs"]
    m = g["monster"]
    cands = []
    for c in g.get("candidates", []):
        if c["kind"] == "hotspot" and c.get("civilians", 0) < 1:
            continue
        if c["kind"] == "hero" and not c.get("alive", True):
            continue
        if m["type"] == "burrower" and c["kind"] != "hotspot":
            continue
        cands.append(c)
    # ties: lowest id lexicographic
    best = min(cands, key=lambda c: (round(dist(m["pos"], c["pos"]), 9), c["id"]))
    # barricade interposition (non-burrower only)
    if m["type"] != "burrower":
        for b in fx["given"]["inputs"].get("blockers", []):
            if b.get("blocks_path_between") == [m["id"], best["id"]]:
                return b["id"], dist(m["pos"], b["pos"])
    return best["id"], dist(m["pos"], best["pos"])


# --- targeting fixtures G-001..G-005 ---
for fid in ["G-001", "G-002", "G-003", "G-004", "G-005"]:
    fx = load(fid)
    tid, d = derive_target(fx)
    r = fx["expect"]["exact"]["result"]
    check(fid, f"derived target {tid} != {r['target_id']}", tid == r["target_id"])
    check(fid, f"derived distance {d} != {r['distance']}", abs(d - r["distance"]) < 1e-9)

# --- hotspot damage G-006..G-009 ---
for fid in ["G-006", "G-007", "G-008", "G-009"]:
    fx = load(fid)
    g = fx["given"]["inputs"]
    atk = g["attack"]
    hs = {h["id"]: h["civilians"] for h in g["hotspots"]}
    target = atk["target_id"]
    killed = min(math.ceil(atk["damage"] / 10), hs[target])
    remaining = hs[target] - killed
    total_after = sum(hs.values()) - killed
    r = fx["expect"]["exact"]["result"]
    check(fid, "civilians_killed", killed == r["civilians_killed"])
    check(fid, "civilians_remaining", remaining == r["civilians_remaining"])
    check(fid, "total_civilians_remaining", total_after == r["total_civilians_remaining"])
    events = {e["type"] for e in fx["expect"]["exact"]["emitted_events"]}
    check(fid, "defeat iff total==0", (total_after == 0) == ("match_defeat" in events))
    check(fid, "emptied iff remaining==0", (remaining == 0) == ("hotspot_emptied" in events))

# --- kill/bounty/wave G-010..G-012 ---
BOUNTIES = {"shambler": 10, "ravager": 15, "spitter": 20, "burrower": 30, "bull_behemoth": 50}
for fid in ["G-010", "G-011", "G-012"]:
    fx = load(fid)
    kill = fx["given"]["inputs"]["kill"]
    st = fx["given"]["preexisting_state"]
    check(fid, "bounty matches roster table", kill["bounty"] == BOUNTIES[kill["monster_type"]])
    living_after = len(st["wave"]["living_monster_ids"]) - 1
    scrip_after = st["team"]["scrip"] + kill["bounty"]
    wave_complete = living_after == 0
    victory = wave_complete and st["wave"]["number"] == st["wave"]["total_waves"]
    r = fx["expect"]["exact"]["result"]
    check(fid, "scrip_after", scrip_after == r["scrip_after"])
    check(fid, "living_monsters_remaining", living_after == r["living_monsters_remaining"])
    check(fid, "wave_complete", wave_complete == r["wave_complete"])
    check(fid, "map_victory", victory == r["map_victory"])

# --- purchases G-013..G-015 ---
for fid in ["G-013", "G-014", "G-015"]:
    fx = load(fid)
    p = fx["given"]["inputs"]["purchase"]
    st = fx["given"]["preexisting_state"]
    ok = st["match"]["phase"] == "planning" and st["team"]["scrip"] >= p["cost"] and p["zone"] == "valid"
    scrip_after = st["team"]["scrip"] - p["cost"] if ok else st["team"]["scrip"]
    r = fx["expect"]["exact"]["result"]
    check(fid, "accepted", ok == r["accepted"])
    check(fid, "scrip_after", scrip_after == r["scrip_after"])
    if not ok:
        check(fid, "rejected purchase has no state changes", fx["expect"]["exact"]["state_changes"] == [])

# --- carryover G-016 ---
fx = load("G-016")
st = fx["given"]["preexisting_state"]
r = fx["expect"]["exact"]["result"]
check("G-016", "scrip preserved", r["scrip"] == st["team"]["scrip"])
check("G-016", "wave incremented", r["wave"] == st["wave"]["number"] + 1)

# --- ready-up G-017 ---
fx = load("G-017")
players = fx["given"]["preexisting_state"]["players"]
ready_id = fx["given"]["inputs"]["ready"]["player_id"]
all_ready = all(p["ready"] or p["id"] == ready_id for p in players)
r = fx["expect"]["exact"]["result"]
check("G-017", "all_ready", all_ready == r["all_ready"])
check("G-017", "combat starts iff all ready", all_ready == r["combat_started"])
elapsed = fx["given"]["clock"]["sim_elapsed"]
check("G-017", "early start before timer", elapsed < fx["given"]["configuration"]["planning_duration_seconds"])
check("G-017", "elapsed echoed", elapsed == r["planning_elapsed"])

# --- lasso G-018/G-019 ---
fx = load("G-018")
m = fx["given"]["inputs"]["monster"]
cfg = fx["given"]["configuration"]
t = fx["given"]["clock"]["sim_elapsed"]
r = fx["expect"]["exact"]["result"]
check("G-018", "speed halved from base", r["speed_after"] == m["base_speed"] * cfg["lasso_slow_multiplier"])
check("G-018", "expiry = now + duration", r["slow_expires_at"] == t + cfg["lasso_duration_seconds"])

fx = load("G-019")
t = fx["given"]["clock"]["sim_elapsed"]
eff = fx["given"]["preexisting_state"]["status_effects"]["m4"][0]
check("G-019", "tick at exact expiry removes effect", t >= eff["expires_at"])
sc = {(s["entity"], s["field"]): s for s in fx["expect"]["exact"]["state_changes"]}
check("G-019", "speed restored to base", sc[("m4", "current_speed")]["to"] == fx["given"]["inputs"]["monster"]["base_speed"])

# --- sawbones G-020 ---
fx = load("G-020")
atk = fx["given"]["inputs"]["attack"]
hero = fx["given"]["inputs"]["hero"]
red = fx["given"]["configuration"]["sawbones_damage_reduction"]
taken = math.floor(atk["damage"] * (1 - red))
r = fx["expect"]["exact"]["result"]
check("G-020", "damage_taken = floor(dmg*0.7)", taken == r["damage_taken"])
check("G-020", "hp_after", hero["hp"] - taken == r["hp_after"])
check("G-020", "downed iff hp<=0", (hero["hp"] - taken <= 0) == r["downed"])

# --- respawn G-021 ---
fx = load("G-021")
atk = fx["given"]["inputs"]["attack"]
hero = fx["given"]["inputs"]["hero"]
t = fx["given"]["clock"]["sim_elapsed"]
cfg = fx["given"]["configuration"]
taken = atk["damage"]  # gunslinger: no reduction
hp_after = max(0, hero["hp"] - taken)
r = fx["expect"]["exact"]["result"]
check("G-021", "hp_after", hp_after == r["hp_after"])
check("G-021", "downed iff hp==0", (hp_after == 0) == r["downed"])
check("G-021", "respawn_at = now + delay", r["respawn_at"] == t + cfg["respawn_delay_seconds"])

# --- sell G-022 ---
fx = load("G-022")
pl = fx["given"]["inputs"]["placeable"]
st = fx["given"]["preexisting_state"]
refund = math.floor(pl["purchase_cost"] * fx["given"]["configuration"]["sell_refund_ratio"])
r = fx["expect"]["exact"]["result"]
check("G-022", "refund = floor(cost*ratio)", refund == r["refund"])
check("G-022", "scrip_after", st["team"]["scrip"] + refund == r["scrip_after"])
check("G-022", "sell only in planning", st["match"]["phase"] == "planning")

# --- XP/leveling G-023/G-024 ---
def level_for(xp):
    L = 1
    while xp >= 100 * (L + 1) * L // 2:
        L += 1
    return L

for fid in ["G-023", "G-024"]:
    fx = load(fid)
    kill = fx["given"]["inputs"]["kill"]
    prof = fx["given"]["inputs"]["profile"]
    check(fid, "xp equals bounty table", kill["bounty"] == BOUNTIES[kill["monster_type"]])
    total = prof["lifetime_xp"] + kill["bounty"]
    new_level = level_for(total)
    leveled = new_level > prof["level"]
    points = prof["skill_points"] + (new_level - prof["level"])
    floor_xp = 100 * new_level * (new_level - 1) // 2
    next_req = 100 * new_level
    r = fx["expect"]["exact"]["result"]
    check(fid, "lifetime_xp", total == r["lifetime_xp"])
    check(fid, "level", new_level == r["level"])
    check(fid, "leveled_up", leveled == r["leveled_up"])
    check(fid, "skill_points", points == r["skill_points"])
    check(fid, "xp_into_level", total - floor_xp == r["xp_into_level"])
    check(fid, "xp_for_next_level", next_req == r["xp_for_next_level"])
    calls = fx["expect"]["exact"]["external_calls"]
    check(fid, "profile saved iff level-up", leveled == (len(calls) == 1 and calls[0]["op"] == "save"))

# --- skill points G-025/G-026 ---
for fid in ["G-025", "G-026"]:
    fx = load(fid)
    sp = fx["given"]["inputs"]["spend"]
    prof = fx["given"]["inputs"]["profile"]
    ok = prof["skill_points"] >= 1
    r = fx["expect"]["exact"]["result"]
    check(fid, "accepted iff has point", ok == r["accepted"])
    check(fid, "points consumed", r["skill_points_after"] == (prof["skill_points"] - 1 if ok else prof["skill_points"]))
    if not ok:
        check(fid, "no state changes on reject", fx["expect"]["exact"]["state_changes"] == [])
        check(fid, "no persistence on reject", fx["expect"]["exact"]["external_calls"] == [])
    else:
        ab = sp["choice"].split("_")[1]
        check(fid, "chosen ability ranked", r["abilities"][ab] == prof["abilities"][ab] + 1)

# --- placeable effects G-027..G-029 ---
fx = load("G-027")
pl = fx["given"]["inputs"]["placeable"]
mon = fx["given"]["inputs"]["monster"]
r = fx["expect"]["exact"]["result"]
check("G-027", "damage passthrough", r["damage_dealt"] == pl["damage"])
check("G-027", "monster hp", r["monster_hp_after"] == mon["hp"] - pl["damage"])
check("G-027", "triggers decremented", r["triggers_remaining"] == pl["triggers_remaining"] - 1)
check("G-027", "breaks iff triggers hit 0", r["broke"] == (pl["triggers_remaining"] - 1 == 0))

fx = load("G-028")
t = fx["given"]["inputs"]["turret"]
cands = [m for m in fx["given"]["inputs"]["monsters"] if m["alive"] and dist(t["pos"], m["pos"]) <= t["range"]]
best = min(cands, key=lambda m: (round(dist(t["pos"], m["pos"]), 9), m["id"]))
r = fx["expect"]["exact"]["result"]
check("G-028", "nearest living in range", best["id"] == r["target_id"])
check("G-028", "distance", abs(dist(t["pos"], best["pos"]) - r["distance"]) < 1e-9)
check("G-028", "hp after", best["hp"] - t["damage_per_tick"] == r["target_hp_after"])

fx = load("G-029")
pl = fx["given"]["inputs"]["placeable"]
hit = [m["id"] for m in fx["given"]["inputs"]["monsters"] if dist(pl["pos"], m["pos"]) <= pl["blast_radius"]]
r = fx["expect"]["exact"]["result"]
check("G-029", "hit set = in-radius set", sorted(hit) == sorted(r["monsters_hit"]))
check("G-029", "damage each", r["damage_each"] == pl["damage"])
sc = {(s2["entity"], s2["field"]): s2 for s2 in fx["expect"]["exact"]["state_changes"]}
check("G-029", "trap removed once", sc[("dyn1", "exists")]["to"] is False)

# --- friendly fire G-030 ---
fx = load("G-030")
atk = fx["given"]["inputs"]["attack"]
ents = fx["given"]["inputs"]["entities_on_line"]
first_monster = next(e for e in ents if e["kind"] == "monster")
r = fx["expect"]["exact"]["result"]
check("G-030", "only monster hit", r["hit_id"] == first_monster["id"])
check("G-030", "hp after", first_monster["hp"] - atk["damage"] == r["target_hp_after"])
nonmonsters = [e["id"] for e in ents if e["kind"] != "monster"]
touched = {s2["entity"] for s2 in fx["expect"]["exact"]["state_changes"]}
check("G-030", "allies/placeables untouched", not (set(nonmonsters) & touched))

# --- manifest coverage: every behavior's required roles present ---
manifest = json.loads((Path(__file__).parent / "golden-manifest.json").read_text())
fixtures = [json.loads(f.read_text()) for f in sorted(GOLDEN.glob("*.json"))]
for b in manifest["behaviors"]:
    have = {fx["case_type"] for fx in fixtures if b["id"] in fx["covers"]}
    missing = set(b["required_case_types"]) - have
    check(b["id"], f"missing case roles {missing}", not missing)

if failures:
    print("FAIL")
    for f in failures:
        print(" -", f)
    sys.exit(1)
print(f"OK — {len(fixtures)} fixtures, {len(manifest['behaviors'])} behaviors, all derivations match")
