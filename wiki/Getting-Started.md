# Getting Started

A walkthrough for using REPOBlacklist in a real lobby, plus answers to questions that won't fit on the Details page.

## Step-by-step

1. **Install on the player who hosts.** Only the host's blacklist filters spawns. Other players can install too if they sometimes host or just want the menu locally.
2. **Launch REPO.** The mod loads with BepInEx automatically — no extra setup.
3. **Press F3.** A windowed menu opens with three tabs: **Items**, **Valuables**, **Enemies**.
4. **Pick a tab and tick anything you want gone.** Each row is one asset. Use the **Filter** box at the top to narrow long lists by name. Toggles save to disk the moment you click them.
5. **Press F3 again to close.** The filter re-applies immediately on close, so the next shop refresh / level / enemy roll skips your blacklisted entries.

The list is shared across every save and every lobby on this BepInEx install — it lives in `BepInEx/config/REPOBlacklist.json`. Delete that file to wipe everything.

## What the menu actually shows

Each row is a real asset name — the Unity `Object.name` of the underlying ScriptableObject. For vanilla REPO you'll see things prefixed `Item …`, `Valuable …`, and `Enemy …`. Modded packs show whatever name their author chose.

If a tab looks empty:
- The plugin only sees assets Unity has actually loaded into memory. Some pools don't load until you enter the shop or start a level. Hit **Refresh** after that.
- If a tab is **still** empty after loading a level, check `BepInEx/LogOutput.log` for a line like:
  ```
  [REPOBlacklist] Resolved types: Item=Item, Valuable=ValuableObject, Enemy=EnemySetup
  ```
  A `?` for any category means REPO renamed that class in an update. Report the line on the mod's source / issues page and a fix can be added to the candidate list.

## Buttons in the menu

- **Refresh** — re-scans loaded assets. Use after loading into a level if a tab looked empty.
- **Dump** — writes a diagnostic line to the BepInEx console for every collection field the filter visited (owner type, field name, container size, removed count). Use this if a blacklisted thing is still spawning — paste those `[dump]` lines on the issues page.
- **Close** — same as F3.
- **Clear `<tab>`** — wipes the current tab's blacklist. Two-click confirmation: first click changes the label to "Confirm clear?", second click wipes.

## FAQ

**Do all players need this mod?**  
No. The blacklist effect is host-only because shop, valuable, and enemy selection run host-side in REPO and replicate to clients via Photon. Players who don't host can skip the install (or install it anyway — their toggles save and become active the first time they host).

**Does it work in single-player?**  
Yes — you're the host.

**Will it filter modded items / valuables / enemies?**  
Yes, as long as the modded assets register into the same `List<Item>` / `Item[]` / `Dictionary<…, Item>` style pools the vanilla game uses (and the equivalent for valuables/enemies). Most REPOLib-based content packs do exactly this, so blacklisting works the same as for vanilla.

**Can I share my blacklist with friends?**  
Sure — the file is plain JSON at `BepInEx/config/REPOBlacklist.json`. Copy it between installs.

**Does the menu pause the game?**  
No. The game keeps running. While the menu is open, mouse-look axes and mouse buttons are zeroed out so the cursor doesn't drive your camera or fire weapons, but everything else (including your teammates and any on-the-clock events) keeps going normally.

**Why is the camera still even though my hand-cam isn't disabled?**  
The mod blocks input, not motion. Other players' actions and host-driven events still play through. You'll just stand there until you close the menu.

**What hotkey do I use if F3 conflicts with something?**  
Open `BepInEx/config/isaia.repoblacklist.cfg` and change `MenuToggleKey`. Restart REPO. Any [Unity KeyCode](https://docs.unity3d.com/ScriptReference/KeyCode.html) name works.

**Does this mod patch any REPO code?**  
No. It only Harmony-patches Unity's input methods (`UnityEngine.Input` and `UnityEngine.InputSystem.InputAction.ReadValue<T>()`), and only while the menu is open. The blacklist itself is applied by removing entries from REPO's pool collections via reflection at scene load and on menu close — no method-call interception.

## Troubleshooting

**The camera still drifts slightly with the menu open.**  
Make sure you're on **0.2.1+**. The 0.2.0 build only blocked legacy `Input.GetAxis`; the new InputSystem path was added in 0.2.1.

**The game stutters every few seconds with the mod installed.**  
That was a 0.2.0 / 0.2.1 issue (a periodic safety-net scan). Update to **0.2.2+**, which removes the periodic tick. The filter now only runs on scene load and on menu close.

**A blacklisted thing is still spawning.**  
- Open the menu in-game while in the shop or in a level where it spawned.
- Click **Dump** once.
- Open `BepInEx/LogOutput.log` and copy the lines starting with `[REPOBlacklist] [dump]` plus the `Filter pass: …` summary line.
- Post them on the mod's source / issues page. They identify exactly which collection field the asset is in, which is enough to extend the filter to cover that shape.

**The menu doesn't open at all.**  
Check `BepInEx/LogOutput.log` for `REPOBlacklist 0.2.x loaded`. If that line is missing, BepInEx didn't load the plugin — verify the DLL is in `<profile>/BepInEx/plugins/REPOBlacklist/` and that you have BepInEx-BepInExPack installed as a dependency.

**I changed the toggle key in the config and it didn't take effect.**  
Config is read once on plugin `Awake()`. Restart REPO after editing the file.

## Mod compatibility

REPOBlacklist runs its filter pass one frame after `SceneManager.sceneLoaded`, which is late enough that mods which add assets to vanilla pools (REPOLib content packs, custom item / valuable / enemy mods) have already populated their entries — so blacklisting modded content works the same as vanilla.

It does not Harmony-patch any REPO method, so it should not conflict with mods that patch shop, valuable, or enemy selection logic. If two mods both modify the same pool, REPOBlacklist's removal happens last (deferred one frame), so its blacklist wins on contested entries.

It sends no Photon traffic of its own, so there's no lobby-compatibility requirement: a host with the mod can play with clients who don't have it, and vice versa.
