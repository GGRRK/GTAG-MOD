# GTAG-MOD

Recording-camera mod for Gorilla Tag (PC / Steam). Adds a floating in-game menu that you touch with your gorilla hand to change FOV.

## Features (v0.8)

Robustness pass on top of v0.7's proxy-sphere triggers:
- **Hand lookup tries `GorillaTagger.Instance.offlineVRRig` first** (the current canonical GT path) and falls back to `GorillaLocomotion.Player.Instance`. F11 dump reports which path bound.
- **Hover feedback**: buttons tint brighter when a proxy sphere enters their volume — the fastest visual confirmation that hand tracking is working.
- **Grip detection reads both** `CommonUsages.gripButton` (bool) **and** `CommonUsages.grip` (float ≥ 0.5) — covers controllers that only populate one.
- **Explicit `GUI/3D Text Shader` material on every TextMesh** — fixes text bleeding through walls regardless of Unity's default font material.
- Bigger handle cubes (5 cm → 7 cm) for easier grabbing.
- Tablet-style menu with wood-brown frame and cream cards
- **FOV card** — current FOV displayed big; red `−` and green `+` buttons to adjust
- **DISCONNECT LOBBY card** — large orange tap zone, leaves the current Photon room
- **A button (right controller) toggles the menu open/closed**
- **Pink handles on each side** — touch with your hand + hold **grip** to drag the menu; release grip to drop
- Menu stays where you put it (no head-follow)
- **Interaction uses position polling** against Gorilla Tag's actual hand transforms (reflected from `GorillaLocomotion.Player.Instance`), not Unity physics triggers — much more reliable inside GT
- Keyboard fallbacks: `F7` (FOV−), `F8` (FOV+), `F9` (disconnect), `F10` (toggle), `F11` (diagnostic dump → BepInEx console)
- FOV clamped to 30°–170°

### Not yet matching the reference image
- Rounded corners (needs custom shader / bundled texture)
- Wood texture on the frame (needs bundled PNG)
- First/Third person toggle, FLIP, RECORD buttons — each is its own feature, ask if you want any built next

## Roadmap
- Camera smoothing for less jittery recordings
- Third-person / free-fly camera modes
- Separate desktop output window
- Hide HUD / UI for clean footage

## What you need to install

All links open in your browser. Install in this order.

| # | Tool | Why you need it | Link |
|---|------|------|------|
| 1 | **Gorilla Tag** (Steam) | The game itself | <https://store.steampowered.com/app/1533390/Gorilla_Tag/> |
| 2 | **Monke Mod Manager** | One-click installer for BepInEx 5 (the runtime that loads mods into Gorilla Tag) | <https://github.com/arielthemonke/MonkeModManager/releases/latest> (active fork) · alternative: <https://github.com/BzzzThe18th/MonkeModManager/releases/latest> |
| 3 | **BepInEx 5** (manual alternative) | Only needed if Monke Mod Manager fails for some reason | <https://github.com/BepInEx/BepInEx/releases> (download `BepInEx_x64_5.4.21.0.zip`) |
| 4 | **The mod DLL** | The actual GTAG-MOD plugin file built from this repo | <https://github.com/GGRRK/GTAG-MOD/releases/latest> → download `GTagCameraMod.dll` |

### Optional / developer extras

| Tool | Why | Link |
|------|-----|------|
| .NET SDK 8 | Build the DLL yourself (not needed if you grab the CI artifact) | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Visual Studio 2022 Community (Windows) | Best C# IDE if you want to edit the mod | <https://visualstudio.microsoft.com/vs/community/> |
| VS Code + C# Dev Kit (any OS) | Lightweight alternative editor | <https://code.visualstudio.com/download> · [C# Dev Kit extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) |
| Git | Pull the latest code on your gaming PC | <https://git-scm.com/download/win> |

## Install steps (gaming PC)

1. Install **Gorilla Tag** from Steam if you don't have it.
2. Download and run **Monke Mod Manager** (link above). Click *Install* — it adds BepInEx 5 to your Gorilla Tag folder automatically. Close it.
3. Open the **Releases** page (link above) and download `GTagCameraMod.dll` from the latest release.
4. Drop `GTagCameraMod.dll` into:
   ```
   <Steam>\steamapps\common\Gorilla Tag\BepInEx\plugins\
   ```
   (default Steam path: `C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag\BepInEx\plugins\`)
5. Launch Gorilla Tag.

The menu spawns 5 seconds after the game loads. Look down and slightly to your right — that's where it floats.

## Build locally (optional)

Requires [.NET SDK 8+](https://dotnet.microsoft.com/download/dotnet/8.0):

```
dotnet build -c Release
```

DLL appears in `bin/Release/net472/GTagCameraMod.dll`.

## Useful references

- [BepInEx documentation](https://docs.bepinex.dev/) — modding runtime docs
- [Gorilla Tag modding wiki / community](https://github.com/legoandmars/utilla) — Utilla mod loader (helper layer many GT mods use)
- [HarmonyX](https://github.com/BepInEx/HarmonyX) — runtime method patching library (used by most BepInEx mods for game-internal hooks)
- [Unity scripting API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/) — Unity 2021.3 reference (matches the version GTAG-MOD targets)
