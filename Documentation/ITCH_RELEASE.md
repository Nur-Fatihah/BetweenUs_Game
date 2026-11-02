# Releasing Crony on itch.io

## 1. Build

1. In Unity, bump `GameVersion` in `Assets/Editor/CronyProjectSetup.cs` (for example `1.0.0` → `1.0.1`). Players on
   different versions can't play together, and the game tells them to update.
2. Run **Crony > Build Release (Windows + Mac)**. Output: `Builds/Release/Windows/` and `Builds/Release/Mac/Crony.app`.
3. Zip each one (from Terminal, in `Builds/Release`):
   ```
   mkdir -p itch
   (cd Windows && zip -qr ../itch/Crony-<version>-windows.zip . -x "Crony_BackUpThisFolder_ButDontShipItWithYourGame/*")
   ditto -c -k --keepParent Mac/Crony.app itch/Crony-<version>-mac.zip
   ```
   Never upload `Crony_BackUpThisFolder_ButDontShipItWithYourGame` (debug data). Use `ditto` for the Mac zip so the
   app stays runnable.

## 2. itch.io project page

- **Kind of project:** Downloadable. **Pricing:** your choice (free / pay what you want).
- **Uploads:** add both zips. Tick **Windows** for the Windows zip and **macOS** for the Mac zip.
- **Classification:** Game · Genre: Social deduction / Party · Tags: multiplayer, online, pixel-art, cozy, impostor.
- **Age:** mark the game as having multiplayer text and voice chat; recommend 13+.
- **Privacy policy:** paste `Documentation/PRIVACY.md` (after adding your contact email and date).
- **Install notes for players** (put these in the description):
  - Windows: if SmartScreen says "Windows protected your PC", click **More info > Run anyway** (the game is not
    code-signed).
  - Mac: right-click **Crony.app > Open** the first time (the app is not notarized by Apple).
  - Online play needs an internet connection; Same Wi-Fi play works on a local network.
- **Minimum specs (suggested):** 64-bit Windows 10 or macOS 12+, 4 GB RAM, any GPU from the last ~8 years. If the
  game feels slow, Settings > Display > Full visual effects off.

## 3. Before each release

- [ ] Unity Cloud dashboard: Relay and Lobby enabled for this project; check usage against the free tier.
- [ ] Vivox enabled if you keep voice chat.
- [ ] Play one online match with friends on the new build (Quick Join and a private room code).
- [ ] Version bumped and both zips uploaded together.

## How online play holds up

- **Quick Join** puts players into a public room on the same version that hasn't started yet, or opens one.
- **Dropped connection:** the game reconnects by itself for up to 45 seconds; the host holds the player's seat
  (role, position) for 60 seconds. A player whose game closed can rejoin with the same room code.
- **Host leaves (online rooms):** another player becomes host and the match continues; a meeting in progress
  restarts as normal play. Same Wi-Fi (LAN) games end when the host leaves.

## Known limitations

- Builds are not code-signed (Windows) or notarized (macOS); see install notes above.
- The Windows build was compiled on a Mac and could not be run here; test it on a Windows PC before announcing.
- A player who rejoins after their game closed uses the room's reconnect path, which doesn't carry host migration;
  if the host then leaves, that player needs the room code again.
