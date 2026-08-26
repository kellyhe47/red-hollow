# Playtest procedure — solo 10-wave and 2-player LAN (branch `cursor/fix-playable-lykos-view-cd88`)

Written for the local coordinator: this branch's agent has no Unity editor, so everything below
is the hand-verify half. Everything decision-shaped already runs headless and green
(`dotnet test sim/GameSim.Tests/GameSim.Tests.csproj` → 391, incl. all 30 goldens;
`dotnet test tools/compile-check/ShellCheck.csproj` → 75 executed EditMode tests, incl. the
turret-stall, barricade-redirect and replication pins).

## A. Solo 10-wave (the R-01 bar)

Scene: the existing `RedHollow.unity` with `GameEntryBehaviour` (unchanged). Play →
callsign → HOST GAME → pick (or skip; defaults gunslinger) → READY.

Per-wave loop to verify:

1. **Wave 1 opens in combat** (factory lock). WASD moves, hero faces cursor, SPACE fires at the
   cursor, Q/E once unlocked (level up → `L` opens the picker).
2. **Wave clear → S5 banner → planning**: top bar now shows the countdown (`0:47`) and the
   ready fraction (`1/1 ready`). Shop bar: click item → ghost follows cursor, red tint in
   invalid zones → click to place. Click a standing placeable → sell. READY UP ends planning.
3. **Placeables fight** (all previously dead in the shipped composition, all fixed on this
   branch, all executed-pinned):
   - a **barricade** across a lane redirects the wave onto itself, gets chewed down, and its
     collapse releases the lane (production `BarricadePathOracle` — was `OpenPathOracle`,
     "nothing ever blocks");
   - a **turret** fires ~1/s; a turret LAST-HIT removes the monster from the wave and pays
     bounty + placer XP (this was the local-playtest wave-stall: `TurretTick` flips `Alive`,
     and only `MatchSession`'s reap turns that into `RecordMonsterKill`);
   - **spike/dynamite** trigger on walk-over (footprint entry, not per-frame).
4. **Spitters (wave 4+) stop ~10 out and drain shelters from range** — R-17's row, newly
   implemented. Shooting the nearest walker while spitters work is a losing habit *by design*:
   see `tools/balance-probe/FINDINGS.md` — threat-priority solo WINS at shipped numbers (4/20
   civilians), naive solo dies around wave 6. Do not read the wave-6 collapse as a bug.
5. Lifetime XP survives quitting and relaunching (JSON store under `persistentDataPath`).

Presentation must stay DEC-026: ~65° tilt, hab meshes with rust/window glow, lanterns, no sun,
camera-facing 2.5D cards with blob shadows. If monsters are invisible, check the standing-card
material path (URP Unlit + `_BaseMap` — already the branch default) before suspecting the sim.

## B. Two-process LAN / loopback (the R-50 "up to 2 players" stretch)

Both sides use `LanPartyBehaviour` (drop it into a scene INSTEAD of `GameEntryBehaviour`; it
creates its own `NetworkManager` + `UnityTransport`).

1. **Host process**: `LanPartyBehaviour`, defaults (`joinAsClient` off, port 7777). Play → S1 →
   callsign → HOST GAME. S2 shows join code `LAN`.
2. **Client process** (second editor instance or a build, same machine): `LanPartyBehaviour`
   with `joinAsClient` ON and `joinCode` = `LAN` (same machine) or `LAN:<host-ip>:7777`.
   The client connects at Awake; its hello (peer/account/class) is seated through
   `NetSession.TryJoin` on the host — the party cap and no-mid-match-join rules stay in force,
   refusals kick the connection.
3. Host picks/readies (client seat auto-readies) → match starts. Verify on the client:
   - the colony renders live from snapshots (entities, wave counter, scrip);
   - WASD walks the CLIENT's hero on the host (watch both screens);
   - SPACE/Q/E resolve host-side; client kills pay bounty and credit the client's account;
   - the client auto-readies each planning phase (v1: no client shop UI — the host shops;
     R-25 makes placements team property).
4. Disconnect the client mid-match: host gets the R-53 toast, the client's hero despawns, its
   held input stops (no ghost-walking hero).

Known v1 cuts (deliberate, documented in code): no client-side shop/HUD chrome (mirror + world
view only), no event/feel replication (state renders; stingers are host-side), no client-side
interpolation (R-52's smoothing curve is unstated in the PRD — snapshots at pump rate are
smooth enough on loopback).

## C. If something fails

The decision layers are all headless — reproduce there first:

```bash
dotnet test tools/compile-check/ShellCheck.csproj --nologo   # T11/T12/T14/T20/T30, executed
dotnet run --project tools/balance-probe/BalanceProbe.csproj # plays full campaigns at shipped numbers
```

A failure that reproduces in neither is Unity-side plumbing (`NgoWire`, `NgoMatchChannel`,
`LanPartyBehaviour`, uGUI labels) — the thin, hand-verified surface, same convention as T-20.
