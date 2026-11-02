# Crony — Township Park

A Unity social-deduction game set in Township Park. Play offline against bots, on the same Wi-Fi, or online with friends using a room code. A round has Crewmates, one Impostor and, once recruited, the Impostor's secret Crony, plus ten checkpoint mini-games, five sabotage types, meetings, voting, evidence, cosmetics, and safety controls.

## Open and play

Open this project in Unity 6000.6.6f1 and press Play in `Assets/Scenes/TownshipPark_Map.unity`. The scene loads the game runtime, which creates the park, characters, HUD, and menus at runtime. The scene is also the project's first build scene.

## Controls

The full, always-current list is in-game under **How to play & controls** (main menu, pause menu, or the top-left button online) and **Settings > Controls**. Both draw from `Assets/Scripts/UI/ControlsGuide.cs`.

- Move: WASD, arrow keys, or gamepad left stick
- Interact or repair: E
- Report a downed player: R
- Call one emergency meeting: M
- As Impostor: Q opens sabotage; F incapacitates a nearby Crewmate; G recruits a nearby Crewmate as your Crony (once per match). In LAN/online matches, N picks the disruption and Q triggers it
- As Crony: Q sabotage (N + Q online). Tasks can be "played" to blend in but never count for the crew
- Mini-games: Space / Enter for the action, number keys 1-5 for choices, A/D or arrow keys to steer
- Escape: close the current panel or pause
- Touch devices get an on-screen movement pad

## Included

- A bounded top-down Township Park with a lake, paths, community buildings, camera follow, and a vision-radius blackout.
- Eight-player local matches with bot movement, task progress, fictional non-graphic incapacitation, rescue, reports, emergency meetings, evidence, votes, and win conditions.
- Ten checkpoint mini-games, one per task: Hawker Rush (conveyor timing), Freezer Memory (repeat the sequence), Hammer Time (power meter), Badminton Rally, Permit Frenzy (falling sort), Bus Departures (tap in time order), Lake Boat (steer and catch), Signal Hunt (find and hold the frequency), Sun Chaser (track the sun), and Flyer Catch (grab the named flyer). Each round won sends the station's next step, so the host still validates every task.
- The Crony twist: once per match, 45 s in and while at least three Crewmates stand, the Impostor can recruit a nearby Crewmate. The Crony learns who the Impostor is, wins or loses with them, counts on the Impostor's side for the majority win, can sabotage, and their task work no longer counts. Voting out the Impostor still wins for the crew.
- Five repairable disruptions matching the brief.
- Meetings in four timed stages, the same offline and online: **Discussion** (15 s; click a player to accuse them), **Voting** (30 s; one final vote or skip, with only a "voted" badge shown), **Reveal** (who voted for whom), and **Ejection** (whether they were the Impostor, the crony, or innocent). The most votes wins; a tie, or as many skips as the leader, sends no one home. Afterwards everyone regroups at the start area and takedowns cool down for 15 s. Lines appear as dialogue boxes. Offline bots argue from what they actually saw: witnesses accuse, the Impostor deflects and counter-accuses, and the crony covers. Online, ballots stay on the host until the reveal.
- Clear checkpoint names: when you walk near a checkpoint, a parchment label with its number and name appears in Quicksand Bold (offline and online), drawn crisply above the fog and turning green when done.
- Chat that teaches instead of just blocking: a blocked message opens a short pop-up explaining the real-world effect of that kind of speech (swearing, bullying, hate, encouraging self-harm, extremism, threats, harassment), the manipulation behind it, and a kinder way to say it. Swearing is held back without a strike; other categories also go through the graduated enforcement.
- Bots that argue from what they saw: each bot remembers the last 60 seconds (who stood by a body, used a hollow stump, ignored a disruption or finished a checkpoint nearby), scores everyone's suspicion, and speaks by role. Crewmates accuse with a concrete reason or give an alibi, the Impostor piles onto the crew's doubts and turns on accusers ("That's a self-report!"), and a crony covers for the Impostor. See `Assets/Scripts/Core/BotMemoryManager.cs` and `BotDiscussionEngine.cs`.
- On-screen impostor buttons offline and online: Knock Down [F] (with cooldown), Sabotage [Q] (pick a disruption) and Recruit Crony [G].
- A cozy, hand-painted look: warm olive and moss grass, animated teal water with caustics, parchment windows with springy pop-ins, pebble buttons, cloth-tag tabs, god rays, dust motes and drifting petals, acoustic-style music and organic sound effects, a tree-stump meeting table with fireflies, and hollow stumps impostors can travel through (E).
- A URP 2D lighting and post-processing pipeline (glowing task beacons and sabotage alarms, bloom, subtle chromatic aberration, a colour-grading LUT, vignette), sub-pixel secondary motion on characters, the pixel-art button kit with hover/pressed states and menu juice, and pixel-font titles over a clean sans for reading. Full specification: `Documentation/VisualPipeline.md`.
- Avatar skin tones and outfit colors; large-text and high-contrast settings; preset text-only chat; local mute, block, and report controls.
- A safety system: chat screened by policy category, four-level graduated enforcement on the host (warn, restrict chat and voice, remove, suspend), private distress support, reports for players, messages, voice and matches, plus appeals and in-game community rules. See `Documentation/CommunicationIntegration.md`.
- Generated background music (menu, park, and a tense meeting loop that cross-fade with the game phase) and a distinct sound for each action: clicks, mini-game taps, hits and misses, task steps and completion, takedowns, reports, emergency meetings, votes, ejections, sabotage alarms and repairs, crony recruitment, chat messages, role reveals, and win/lose jingles. No audio files are needed; imported clips assigned to the Game Audio Manager take priority.
- Settings: master/music/effects volume, mute in background, voice chat and push-to-talk mode; fullscreen, V-Sync and frame-rate limit; large text, high contrast and reduced motion; controls; and safety (rules, your record and appeals, blocked players, text chat on/off, streamer mode, support resources). Settings apply at launch and can be reset to defaults.

## Online and safety boundaries

**Online rooms:** Play Online > **Quick Join** puts you in a public room with players on the same version (or opens one if none is waiting); **Create Private Room** makes a code-only room friends join with **Join Code**. No port forwarding is needed. Rooms need the Unity Cloud project linked in Project Settings > Services, with Relay and Lobby available on that project. Same-Wi-Fi play via Host LAN / Join LAN still works.

**Staying in the match:** if a player's connection drops, their game reconnects automatically (up to 45 s) and the host holds their seat and role for 60 s; a player whose game closed can rejoin with the room code. In online rooms, if the host leaves, another player becomes host and the match continues (host migration). Details and limits: `Documentation/ITCH_RELEASE.md`.

**Releasing:** Crony > Build Release (Windows + Mac), then follow `Documentation/ITCH_RELEASE.md`. Test-only launch options for several windows on one computer: `-authProfile NAME`, `-joinCode CODE`, `-quickJoin`, `-autoJoinLocalhost IP`.

**Testing online on one computer:** use the Unity menu Crony > Multiplayer Launcher. Step 1 builds a test client in `Builds/LocalTestClient` (a universal app on macOS, so it runs on Intel and Apple Silicon Macs). Then press Play, choose Host LAN, and use step 2 to open a second window that joins automatically. To play on two computers, copy that build to the second computer: on the same Wi-Fi, one player chooses Host LAN and the other types the host's LAN IP and chooses Join LAN. On different networks, use Create Online Room and Join Code.

**Room settings (host):** in the lobby, ROOM SETTINGS sets impostors (Auto, 1 or 2; 2 needs 5+ players), discussion and voting time, knock-down cooldown and round length, and lets the host remove a player for the rest of the session. Everyone else sees the rules read-only.

**Versions:** players must run the same version to play together. A mismatched player is turned away with a message telling them to update. Bump `GameVersion` in `Assets/Editor/CronyProjectSetup.cs` for every release.

**Problem log:** errors are written to `crony-errors.log` in the game's data folder (Settings > Display > Problem log > OPEN). Nothing is uploaded; players can attach it to bug reports.

**Support:** Settings > Safety > Need support? and the private distress panel point to Find A Helpline (findahelpline.com), which lists free helplines in each player's own country. Main menu > Credits lists the fonts' licences.

**Privacy:** a draft policy for the itch.io page is in `Documentation/PRIVACY.md` (review it and add your contact details first).

Not built yet: accounts and age controls, cloud saves, and a hosted moderation and appeals service (see `Documentation/CommunicationIntegration.md`).

The five seasonal worlds, optional voice, and community events are expansion scope in the blueprint. No monetization is configured.
