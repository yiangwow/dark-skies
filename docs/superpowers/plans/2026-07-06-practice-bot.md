# Practice Bot — plan (2026-07-06)

## Why
The game is only alive in a scheduled 1v1. A dumb-but-fun bot makes the combat
loop playable solo, which unblocks every feel verdict downstream (rocket 150,
impact frames, match flow).

## Shape
Reuse the NetworkPlayer prefab **unchanged**. The bot is a server-spawned
instance with no owner; a runtime-added plain MonoBehaviour drives it. Plain
components added before `ServerManager.Spawn` exist only on the server, so the
prefab's NetworkBehaviour list (and the prefab trio) stays untouched — clients
see the bot exactly like any remote player: default replicates + reconciles.

## Pieces
1. **BotBrain** (pure C#, seeded, EditMode-tested): orbit-strafe movement
   (approach when far / back off when crowding / strafe on the ring, orbit
   direction flips on a jittered timer), hop on a timer (doubles as the planet
   launch key), fire when target visible + in range + cadence elapsed, aim
   pushed off-true by a deliberate error cone.
2. **BotDriver** (plain MonoBehaviour, server-only): senses the world (nearest
   live non-bot PlayerCombat, eye-to-eye linecast for LOS), ticks the brain on
   the network tick, feeds the motor via `NetworkPlayerMotor.ExternalInput`,
   fires through `PlayerCombat.ServerFireRocket`.
3. **Motor hook**: `ExternalInput` func — when set, `BuildData()` returns bot
   input on the server (gated by `InputsEnabled`, so death/round-over freeze
   the bot like everyone else).
4. **PlayerCombat**: rocket-fire body extracted from the ServerRpc into a
   public server method (shared validated path); `ServerNotifyHit` guards
   against notifying an invalid Owner (bots have no client).
5. **BotSpawner** (scene, wired by builder): host presses **B** to spawn or
   despawn the bot. ConnectHUD shows the hint while hosting.

## Non-goals (v1)
Grapple/zip use, weapon switching (rockets only), bot-vs-bot targeting,
distinct bot body tint, fire sounds audible to others (existing gap for
remote players too — flagged for a later audio pass).

## Risks
- Server-driven prediction: bot input must come from the tick path (not
  Update) so replays stay deterministic — hence the `ExternalInput` hook in
  `BuildData`, not a parallel mover.
- Match flow: MatchManager scans every PlayerCombat, so the bot participates
  in rounds automatically (killable to 10, resets on new round). Bot as
  winner shows DEFEATED to the human via the existing OwnerId comparison.
