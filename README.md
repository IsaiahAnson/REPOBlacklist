# REPOBlacklist

[**Get it on Thunderstore**](https://thunderstore.io/c/repo/p/Mentalize/REPOBlacklist/)

<img src="icon.png" width="120" align="right" alt="REPOBlacklist icon">

A BepInEx plugin for R.E.P.O. that lets the host pick which shop items, level valuables, and enemies should never spawn. Selections are made through an in-game IMGUI menu (default hotkey **F3**) and persist across every save file on the install.

Only the player hosting the lobby's blacklist takes effect — shop, level valuable, and enemy selection all run host-side in REPO and replicate to clients via Photon. Non-hosts can still install the mod and use the menu; their toggles save locally and become active the next time they host.

**Live release:** [thunderstore.io/c/repo/p/Mentalize/REPOBlacklist](https://thunderstore.io/c/repo/p/Mentalize/REPOBlacklist/) — published under the author handle Mentalize, install via Thunderstore Mod Manager or by dropping the DLL into BepInEx (see [Install](#install)).

## Approach, tools, and assumptions

**Approach.** Patching REPO's spawn directors directly would be brittle across REPO updates (class names, field shapes, and pool storage all change). Instead, the plugin uses *generic reflection-based pool discovery*: it walks loaded REPO assemblies, finds `MonoBehaviour` types with `List<T>`, `T[]`, or `Dictionary<K, V>` fields where `T` (or `K`/`V`) is assignable to `Item`, `ValuableObject`, or `EnemySetup`, caches that field map per Type once per session, and mutates those collections in place at scene load. The game's directors then pick from a now-shorter pool — blacklisted assets are simply never rolled. The approach is intentionally manager-agnostic so it survives REPO renames.

**Tools.**
- C# / .NET Standard 2.1, BepInEx 5.4.2100 (Mono build).
- HarmonyX 2.10 for Unity `Input` / `InputAction` patching only — REPO's code is never Harmony-patched, the filter is pure reflection.
- `Newtonsoft.Json` (already shipped by REPO) for the persisted blacklist file.
- Unity IMGUI for the in-game tabbed menu with text filter, Dump, and per-tab Clear with two-click confirmation.
- Build via `dotnet build -c Release`; the csproj's `ThunderstorePack` target produces a ready-to-upload zip alongside the DLL.

**Assumptions.**
- REPO's spawn pools are stored on `MonoBehaviour` instance fields or on static fields of game-assembly types — not behind opaque accessors that reflection can't see. The 0.2.0 scan was widened to include `Dictionary<,>`, static fields, `DontDestroyOnLoad`, and inactive objects to cover singleton-style managers; the Dump button surfaces any field the filter visited so future-REPO gaps are diagnosable.
- Asset identity is reliable: the runtime `UnityEngine.Object.name` is preserved across scene loads and matches what the game uses internally. The blacklist file stores names verbatim.
- Only the host's blacklist is authoritative — REPO's spawn directors run host-side and replicate via Photon. Non-host blacklists are silent no-ops on a guest but still save and activate when they next host.
- The new InputSystem may or may not be loaded; the `InputAction.ReadValue<T>()` patches are bound dynamically via reflection so the plugin loads either way.

## What the DLL actually does

On plugin `Awake()`, it:

- **Reads its config.** A standard BepInEx config file (`BepInEx/config/isaia.repoblacklist.cfg`) defines the menu toggle key (default `F3`).
- **Loads the blacklist.** Reads `BepInEx/config/REPOBlacklist.json` (created on first save). The file holds three sorted string lists — items, valuables, enemies — keyed by asset name (the Unity `Object.name` of the underlying asset). Toggles in the menu write through to this file immediately.
- **Registers an IMGUI overlay.** Adds a `MonoBehaviour` that draws a windowed menu while open. The menu has three tabs (Items / Valuables / Enemies), a text filter, and **Refresh**, **Dump** (logs diagnostic info to the BepInEx console), **Close**, and a per-tab **Clear** button with a two-click confirmation step. Each row is a checkbox bound to the JSON store.
- **Subscribes to `SceneManager.sceneLoaded`.** One frame after each scene loads, runs the runtime filter described below. Also re-runs the filter when the menu is closed.
- **Applies Harmony input-blocking patches.** While the menu is open (`BlockInput == true`), the following methods are short-circuited so cursor movement doesn't drive the camera and clicks don't fire weapons:
  - Legacy: `UnityEngine.Input.GetAxis`, `GetAxisRaw`, `GetMouseButton`, `GetMouseButtonDown`, `GetMouseButtonUp` — return `0f` / `false`.
  - New: `UnityEngine.InputSystem.InputAction.ReadValue<T>()` for `Vector2`, `float`, and `Vector3` — return `default(T)`. Bound dynamically via reflection, so the plugin still loads if the new InputSystem assembly is absent.
  - `Input.GetKey` / `GetKeyDown` / `GetKeyUp` are deliberately **not** patched, so the menu hotkey (and any other plugin hotkeys) keep working.
  - These patches are always installed but gated on a single static flag — they're inert when the menu is closed.

## Runtime filter

The filter does **not** Harmony-patch any REPO method. It runs once per scene load (one frame deferred) and once on menu close, and removes blacklisted assets from in-memory pool collections that the game's directors read from. Specifically:

1. **Type resolution (cached once per session).** Walks loaded assemblies and resolves the first match for each category: `Item` for items; `ValuableObject` then `Valuable` for valuables; `EnemySetup` then `EnemiesSetup` then `EnemyParent` for enemies. The resolved type names are logged on first use as `Resolved types: Item=…, Valuable=…, Enemy=…`. If a name can't be resolved (e.g. a future REPO rename), that category is skipped silently.
2. **Field discovery (cached per Type).** For each `MonoBehaviour` type seen on first scan, reflects over its public + non-public instance fields once and remembers any whose declared type is `List<T>`, `T[]`, or `Dictionary<K, V>` where `T` (or `K`, or `V`) is assignable to a resolved category type. The same scan is done once over every type in non-external assemblies for static fields. After this one-time pass, repeat scans skip `GetFields` entirely.
3. **Filter pass.** Iterates `Object.FindObjectsOfType<MonoBehaviour>(true)` (active scene + DontDestroyOnLoad + inactive, excluding prefab assets) and the cached static-field list. For each matched container, removes any element whose `UnityEngine.Object.name` (or, for dictionary `string` keys, the key itself) is present in the blacklist. Lists and dictionaries are mutated in place; arrays are reallocated with the surviving entries and the field reassigned.

The directors then pick from the now-shorter pool, so blacklisted entries are simply never rolled. The approach is intentionally generic — it does not depend on knowing REPO's specific manager class names — but it does mean a blacklist only takes effect on assets reachable through one of the three supported container shapes. If a future REPO build stores a pool in a more exotic structure (a custom weighted-pool class, for example), the **Dump** button lists every visited field with counts, which makes the gap obvious.

External assemblies (`UnityEngine.*`, `Unity.*`, `System.*`, `mscorlib`, `netstandard`, `BepInEx*`, `0Harmony`, `HarmonyX*`, `Mono.Cecil`, `Microsoft.*`, and the plugin itself) are skipped during type resolution and field discovery.

## Install

Thunderstore Mod Manager: open the [live release](https://thunderstore.io/c/repo/p/Mentalize/REPOBlacklist/) and click Install. Manual: drop `REPOBlacklist.dll` into `<profile>/BepInEx/plugins/REPOBlacklist/`. Requires `BepInEx-BepInExPack`.

The blacklist file at `BepInEx/config/REPOBlacklist.json` is shared across every save and every lobby on that BepInEx install. Deleting it resets the blacklist to empty.

## Building from source

**Prerequisites.**
- .NET SDK 6.0 or newer (verify with `dotnet --version`).
- A local R.E.P.O. install. The csproj defaults to `C:\Program Files (x86)\Steam\steamapps\common\REPO` for its `Assembly-CSharp.dll` reference; override on the build command line with `-p:REPOPath="<your install root>"` if your install lives elsewhere.

**Build.**

```
dotnet build -c Release
```

**Output.**
- `bin/Release/netstandard2.1/REPOBlacklist.dll` — the plugin binary.
- `dist/REPOBlacklist-<version>.zip` — Thunderstore-ready bundle (DLL + `manifest.json` + `README.md` + `icon.png`), produced by the `ThunderstorePack` MSBuild target.

To run locally: drop the DLL into `<BepInEx profile>/BepInEx/plugins/REPOBlacklist/` and launch the game. To publish: upload the zip via the Thunderstore web uploader.

## Compatibility

- **Host-side gameplay effect.** Only the host's blacklist filters what spawns. A non-host's blacklist does nothing visible — the host has already chosen what to spawn and replicates it via Photon. (The menu and input-blocking are local to whoever opens them, regardless of host status.)
- **No Harmony patches on REPO code.** Patches target only `UnityEngine.Input` (legacy) and `UnityEngine.InputSystem.InputAction` (new). The runtime filter mutates REPO collection fields via reflection at scene load and on menu close — it does not intercept any REPO method.
- **No Photon / network traffic of its own.** The plugin does not send or receive any custom networked messages.
- **Per-install config.** Blacklist edits affect only your local install. Each host manages their own list.

## Changelog

### 0.2.2
- Removed the 5-second periodic re-apply that was added in 0.2.0; the filter now runs only on scene load and on menu close. Eliminated a recurring stutter.
- Cached per-Type instance-field match info and switched the scene scan from `Resources.FindObjectsOfTypeAll` to `Object.FindObjectsOfType(true)` to skip prefab assets.

### 0.2.1
- Added dynamic Harmony patches for `UnityEngine.InputSystem.InputAction.ReadValue<Vector2/float/Vector3>()` so the new InputSystem mouse-look path is also blocked while the menu is open. Bound via reflection.

### 0.2.0
- Added the input-blocking layer (Harmony prefixes on legacy `UnityEngine.Input` axis and mouse-button reads) so cursor movement no longer drives the camera while the menu is open.
- Filter now walks `Dictionary<,>` fields and static fields on game-assembly types, in addition to instance `List<T>` / `T[]` fields, and includes DontDestroyOnLoad and inactive objects in the scan. Catches `StatsManager`-style singleton pools.
- Added **Dump** button (logs every visited field for diagnostics) and per-tab **Clear** button (two-click confirmation).

### 0.1.0
- Initial implementation: F3 IMGUI menu, JSON blacklist file, scene-load filter pass over `List<T>` and `T[]` fields on scene `MonoBehaviour`s for `Item` / `ValuableObject` / `EnemySetup` element types.
