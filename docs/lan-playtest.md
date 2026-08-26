# Playtest procedure — solo 10-wave and 2-player LAN (branch `fix/playable-lykos-view`)

The 2-player stack is ported ADDITIVELY onto this branch from PR 1
(`origin/cursor/fix-playable-lykos-view-cd88`). `GameEntryBehaviour` stays the scene default
(solo 10-wave + Lykos hotspot facades). Do not wholesale-merge PR 1.

## A. Solo 10-wave (the R-01 bar) — unchanged

Scene: `RedHollow.unity` with `GameEntryBehaviour`. Play auto-hosts a loopback match (no UGS).
WASD / SPACE / shop / turret last-hit / hab facades on hotspot fronts — the already-proven bar.

## B. Two-player LAN / loopback (R-50 stretch)

Both sides use `LanPartyBehaviour` (drop it into a scene INSTEAD of `GameEntryBehaviour`; it
creates its own `NetworkManager` + `UnityTransport`). Solo Play must keep `GameEntryBehaviour`.

1. **Host process**: `LanPartyBehaviour`, defaults (`joinAsClient` off, port 7777). Play → S1 →
   callsign → HOST GAME. S2 shows join code `LAN`. Stay in the lobby until the client knocks —
   R-53 refuses mid-match joins.
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

Single-editor host listen (no second process): drop `/workspace/unity/lan.request`. The open
editor disables `GameEntryBehaviour`, enters Play with `LanPartyBehaviour`, HOST GAMEs into
the lobby, and writes `/workspace/unity/lan.status` (`joinCode=LAN`, port 7777). Exit Play
re-enables GameEntry.

Headless 2P decisions: EditMode fixture `T30_ReplicationTests` (in-memory channel pair +
`LanServices` bring-up). NGO StartHost itself needs Play mode (NetworkManager singleton); use LanPartyBehaviour in Play. Two-process Unity on this box is too heavy (llvmpipe / ~15GB).

Known v1 cuts (deliberate, documented in code): no client-side shop/HUD chrome (mirror + world
view only), no event/feel replication (state renders; stingers are host-side), no client-side
interpolation (R-52's smoothing curve is unstated in the PRD — snapshots at pump rate are
smooth enough on loopback).

## C. If something fails

```bash
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --nologo   # goldens
# EditMode: /workspace/unity/editmode.request  → T10_HostLoopTests / T30_ReplicationTests
```

A failure that reproduces in neither is Unity-side plumbing (`NgoWire`, `NgoMatchChannel`,
`LanPartyBehaviour`, uGUI labels) — the thin, hand-verified surface, same convention as T-20.
