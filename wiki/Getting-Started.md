# Getting Started

A walkthrough for using REPOBlacklist in a real lobby, plus answers to questions that won't fit on the Details page.

## Step-by-step

1. **Install on the player who hosts.** Only the host's blacklist decides what spawns. Other players can install too if they sometimes host.
2. **Launch REPO.** The mod loads with BepInEx automatically — no extra setup.
3. **Press F3.** A menu opens with three tabs: **Items**, **Valuables**, **Enemies**.
4. **Pick a tab and tick anything you want gone.** Each row is one thing the game can spawn. Use the **Filter** box at the top to narrow long lists by name. Every tick is saved to disk straight away.
5. **Close with F3, Esc or Close.** The next shop and the next level leave out what you ticked. Things already in the current shop or level stay.

The list is shared by every save and every lobby on this BepInEx install — it lives in `BepInEx/config/REPOBlacklist.json`. Delete that file to empty it.

## What each tab does

- **Items** — the shop's stock (the secret shop included) and the upgrades an upgrade-stand reroll offers.
- **Valuables** — the valuables placed in a level.
- **Enemies** — the enemies picked for a level.

## What the menu shows

Each row is a name as the game stores it. Mods that add items, valuables or enemies show whatever name their author chose.

- A row marked **(not found right now)** is on your blacklist, but the game does not have it loaded at the moment (for example a modded item whose mod is off). You can untick it.
- If a tab is empty, start or join a game and press **Refresh**. In the main menu the game has not loaded its lists yet.
- The line at the bottom says whether your list is in use. If you are not the host it reads "You are not the host here. Your list is used when you host."

## Buttons in the menu

- **Refresh** — reads the names from the game again.
- **Dump** — writes lines starting with `[dump]` to the BepInEx log: which parts of the mod are working, whether you are the host, and for each tab the blacklisted names and the names the game has loaded.
- **Close** — same as F3 or Esc.
- **Clear `<tab>`** — empties the current tab's blacklist. Two clicks: the first changes the label to "Confirm clear?", the second clears.

## FAQ

**Do all players need this mod?**  
No. Only the host's list is used, because the game picks the shop, the valuables and the enemies on the host. Players who never host can skip it.

**Does it work in single-player?**  
Yes — you're the host.

**I blacklisted an enemy and the level has fewer enemies. Is that right?**  
Yes, when you blacklist every enemy of one difficulty. The game picks a set number of enemies per difficulty; with none left to pick, those slots are dropped. Leave at least one enemy of each difficulty unticked if you want the full count.

**What happens if I blacklist every valuable?**  
The level has no valuables. Its haul goal is then $0, so the extraction point completes with nothing in it once you start it.

**Will it leave out modded items / valuables / enemies?**  
Yes, when the mod adds them to the game's own lists, which is how content packs normally work. They show up in the menu like the game's own. This has not been tested with every pack.

**Can I share my blacklist with friends?**  
Yes — the file is plain JSON at `BepInEx/config/REPOBlacklist.json`. Copy it between installs.

**Does the menu pause the game?**  
No. The game keeps running. While the menu is open you cannot walk, look around, grab or open the pause menu, so the mouse and keyboard only reach the blacklist menu. Push-to-talk keeps working.

**What hotkey do I use if F3 conflicts with something?**  
Open `BepInEx/config/isaia.repoblacklist.cfg` and change `MenuToggleKey`. Restart REPO. Any [Unity KeyCode](https://docs.unity3d.com/ScriptReference/KeyCode.html) name works.

**Does this mod patch any REPO code?**  
Yes, since 0.2.3: five methods, at the places where the game picks what to spawn (`ShopManager.GetAllItemsFromStatsManager`, `UpgradeStand.GetWeightedUpgradeExcluding`, `EnemyDirector.PickEnemies`, `EnemyDirector.AmountSetup`, `ValuableDirector.SetupHost`). The patches never skip the game's own code, and they do nothing unless you are the host. Nothing in Unity is patched any more.

## Troubleshooting

**A blacklisted thing is still spawning.**  
- Check that you are the host; the line at the bottom of the menu says so.
- Things already in the current shop or level stay. The list is used from the next shop or level on.
- Still there? Press **Dump** once, open `BepInEx/LogOutput.log` and copy the lines containing `[REPOBlacklist]`. Post them on the mod's source / issues page.

**The menu says only some parts are working.**  
A game update changed one of the places the mod hooks. The log has a line like `The enemies blacklist cannot work with this game version`. The other parts keep working; the mod needs an update for the rest.

**The log says the blacklist file could not be read.**  
The file was damaged or edited into something that is not valid JSON. The mod kept a copy as `REPOBlacklist.json.bak` and started with an empty list. Fix the copy and rename it back, or tick your choices again.

**The menu doesn't open at all.**  
Check `BepInEx/LogOutput.log` for `REPOBlacklist 0.2.x loaded`. If that line is missing, BepInEx didn't load the plugin — verify the DLL is in `<profile>/BepInEx/plugins/REPOBlacklist/` and that you have BepInEx-BepInExPack installed as a dependency.

**I changed the toggle key in the config and it didn't take effect.**  
Restart REPO after editing the file.

**The game stutters every few seconds with the mod installed.**  
That was a 0.2.0 / 0.2.1 issue. Update to the current version.

## Mod compatibility

The blacklist is applied at the moment the game picks, so content that other mods added to the game's lists before that point is covered.

The patches run alongside the game's own code and alongside other mods' patches on the same methods. A mod that replaces the shop, enemy or valuable selection entirely would also bypass the blacklist for that part.

It sends no network traffic of its own, so a host with the mod can play with players who don't have it, and the other way round.
