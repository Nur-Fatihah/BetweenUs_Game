# Between Us — Township Park

An offline, text-first Unity social-deduction game built from the supplied blueprint. The first release slice provides a complete Township Park round with Crewmate and Impostor roles, local bot opponents, ten interactive task stations, five sabotage types, meetings, voting, evidence, cosmetics, and local safety controls.

## Open and play

Open this project in Unity 6000.6.6f1 and press Play in `Assets/Scenes/TownshipPark_Map.unity`. The scene loads the game runtime, which creates the park, characters, HUD, and menus at runtime. The scene is also the project's first build scene.

## Controls

- Move: WASD, arrow keys, or gamepad left stick
- Interact or repair: E
- Report a downed player: R
- Call one emergency meeting: M
- As Impostor: Q opens sabotage; F incapacitates a nearby Crewmate
- Escape: close the current panel or pause
- Touch devices get an on-screen movement pad

## Included

- A bounded top-down Township Park with a lake, paths, community buildings, camera follow, and a vision-radius blackout.
- Eight-player local matches with bot movement, task progress, fictional non-graphic incapacitation, rescue, reports, emergency meetings, evidence, votes, and win conditions.
- Ten launch-map tasks and five repairable disruptions matching the brief.
- Avatar skin tones and outfit colors; large-text and high-contrast settings; preset text-only chat; local mute, block, and report controls.
- Synthesized UI, task-completion, and alert sounds. No voice chat or real-world cultural audio is included.

## Online and safety boundaries

This build is **offline practice with local bots**. Private Lobby starts a bot match on this device; it does not connect another device or create an online invite. Public matchmaking is disabled until a hosted, server-authoritative service is configured. Online reliability, account and age controls, cloud saves, reconnects, remote moderation, appeals, and production reporting remain to be implemented. Local reports exist only for the current session. Voice chat is not enabled.

The five seasonal worlds, optional voice, and community events are expansion scope in the blueprint. No monetization is configured.
