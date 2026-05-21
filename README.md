# GTAG-MOD

Recording-camera mod for Gorilla Tag (PC / Steam). Adds a floating in-game menu that you touch with your gorilla hand to change FOV.

## Features (v0.2)
- Floating world-space menu that follows your head
- Touch the red `−` button to narrow FOV (zoom in)
- Touch the green `+` button to widen FOV (zoom out)
- Keyboard fallback: `F7` / `F8`
- FOV clamped to 30°–170°

## Roadmap
- Camera smoothing for less jittery recordings
- Third-person / free-fly camera modes
- Separate desktop output window
- Hide HUD / UI for clean footage

## Install (gaming PC)
1. Install BepInEx 5.4.x for Gorilla Tag (easiest: Monke Mod Manager)
2. Download `GTagCameraMod.dll` from the **Actions** tab → latest successful run → "GTagCameraMod-dll" artifact
3. Drop the DLL into `<Gorilla Tag>/BepInEx/plugins/`
4. Launch Gorilla Tag

The menu spawns 5 seconds after the game loads. Look down and slightly to your right — that's where it floats.

## Build locally
Requires .NET SDK 8+:
```
dotnet build -c Release
```
DLL appears in `bin/Release/net472/GTagCameraMod.dll`.
