# Project: AbominableSnowman

Project layer for `WWTHP/oth-abominable-snowman`. Game layer: [Over the Hill](../games/over-the-hill/README.md).

A yeti (about 3.3 m tall, furry, roaring) guards **snowy challenge trails**. He paces back and
forth across the trail. Drive past while he's off to the side, or he grabs your car and throws
it ~200 ft in a random direction. Synced with every player in the session who runs the mod.

Current version: `Mod.Version` in `AbominableSnowman/Mod.cs`.
Settings: the settings hub tab **Yeti** (oth-settings-hub, F5); without the hub, menu key **F6**.
Stored in `MelonPreferences.cfg [AbominableSnowman]`.

## Layout

| File | What |
| --- | --- |
| `Mod.cs` | lifecycle wiring |
| `Settings.cs` | local settings + the **synced behaviour group** (`Settings.Synced`, order = wire order) |
| `Menu.cs`, `Toast.cs` | IMGUI menu (`DrawBody` = hub tab, own window without the hub), on-screen messages |
| `HubTab.cs` | settings hub snippet (mod-template `snippets/generic/HubTab.cs.txt`) |
| `Snow.cs` | snow detection: terrain layer names matching `SnowKeywords`, a trail counts as snowy if `SnowTrailFraction` of waypoints are on snow (cached once terrain has streamed in) |
| `Yetis.cs` | spawning per `ChallengeTrail`, pacing schedule, grab detection, throws, sync messages |
| `YetiModel.cs`, `YetiFur.cs` | procedural model; fur = hundreds of tufts in one GPU-skinned mesh (root + tip bone per tuft), animated sway/wind/bristle |
| `Roar.cs` | roar via FMOD core API (3D). Synthesizes `roar_generated_N.wav` into `UserData/AbominableSnowman/`; user `roar*.wav/ogg/mp3` files there replace them |
| `Cars.cs` | finding cars by player, applying throw velocity/spin to cars we have state authority over |
| `Net.cs` | Fusion messaging: the reference implementation of the game layer's §Fusion technique |
| `CrashGuard.cs` | crash stage markers (stage `net` + others) |

## Design notes

- **No position streaming.** Each yeti's position comes from a fixed schedule (walk across,
  pause, walk back, pause) computed from the session's shared network clock, so every player sees
  him in the same place.
- **Each player checks only their own car.** On a grab, that player's game picks a random direction,
  throws the car, and broadcasts `Throw` so everyone sees it and whoever owns the car's physics
  applies it.
- Messages (`Net.Msg`): `Hello`, `Snapshot` (host → joiner: settings + manual yeti specs),
  `Settings`, `Spawn`, `DespawnAll`, `Throw(id, victim, velocity, spin, time)`. Tag `"YETI"`
  `0x49544559`, protocol 1. The host relays client messages to other modded peers.
- The synced behaviour settings: last change wins; the host's values go to joiners.

## Changelog

| Version | Date | Change |
| --- | --- | --- |
| 0.2.0 | 2026-11-17 | Menu is a tab in the settings hub (F5); F6 only opens it when the hub isn't installed. Save happens when leaving the tab. |
| 0.1.5 | 2026-11-17 | Analyzer/format cleanups (sealed classes, `Split(',')`, format pragmas). No behaviour change intended. Released without an in-game run (user decision); **needs an in-game smoke test**. |
| 0.1.4 | 2026-11-11 | Fur density/movement settings and menu work (see git history of the overthehill workspace; not preserved). |
| 0.1.1 | 2026-11-10 | Network receive moved from MelonLoader NativeHook to vtable swap after the join handshake broke under Proton. |
| 0.1.0 | 2026-11-10 | First version: yetis on snow trails, throws, roar, F6 menu, MP sync. |

## Next steps

- No unit tests yet. Move the packet encoding (`WriteSpecs`/`ReadSpecs`, `WriteSettings`/`ReadSettings`,
  `Throw` payload) into `Core/` with range validation, and add `tests/AbominableSnowman.Tests/`
  (see generic testing.md). Today `ReadSettings` rejects NaN/Infinity but applies any other value
  (no range check, e.g. a huge `Size` or `ThrowDistanceFeet` from a peer), and `ReadSpecs` applies
  positions/axes/phase without NaN or range checks. Add per-setting ranges matching the menu.
