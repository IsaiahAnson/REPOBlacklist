# Changelog

## 0.2.3
- Checked against the current game (R.E.P.O. v0.5.3, Unity 6).
- The blacklist is now applied at the four places where the game picks what to spawn: the shop's stock, upgrade-stand rerolls, the enemy picks for a level, and the valuables for a level. 0.2.2 searched every loaded object for lists and removed entries from them, including the game's own item dictionary and enemy lists.
- The valuables blacklist works again. The current game keeps a level's valuables as prefab references, which 0.2.2 could not match.
- Upgrades offered by an upgrade-stand reroll now follow the items blacklist.
- Blacklisting every enemy of one difficulty no longer leaves an empty enemy slot in the level. That level gets fewer enemies instead, and the log says how many.
- If every valuable of a level is blacklisted, the level has no valuables and its haul goal is $0.
- A change takes effect from the next shop or level on. The scan after every scene load is gone.
- The menu holds the game's controls with the game's own input switches, the way its menus do: no walking, looking, grabbing or pause menu while it is open. Push-to-talk keeps working. The patches on Unity's input methods are gone.
- Esc closes the menu.
- The menu lists blacklisted names the game does not have loaded right now, so they can be unticked, and says when you are not the host.
- The blacklist file is written to a temporary file and swapped in, so a crash while saving cannot leave half a list. A file that cannot be read is kept as `REPOBlacklist.json.bak` instead of being overwritten without notice.
- Clearing a tab writes the file once, not once per entry.
- If a game update removes one of the four places, the log names it and the menu says which parts still work.
- The DLL now reports the same version as the package.
- Added `CHANGELOG.md` to the package so the Thunderstore Changelog tab is populated.

## 0.2.2
- Removed the 5-second periodic re-apply that was added in 0.2.0; the filter now runs only on scene load and on menu close. Eliminated a recurring stutter.
- Cached per-Type instance-field match info and switched the scene scan from `Resources.FindObjectsOfTypeAll` to `Object.FindObjectsOfType(true)` to skip prefab assets.

## 0.2.1
- Added dynamic Harmony patches for `UnityEngine.InputSystem.InputAction.ReadValue<Vector2/float/Vector3>()` so the new InputSystem mouse-look path is also blocked while the menu is open. Bound via reflection.

## 0.2.0
- Added the input-blocking layer (Harmony prefixes on legacy `UnityEngine.Input` axis and mouse-button reads) so cursor movement no longer drives the camera while the menu is open.
- Filter now walks `Dictionary<,>` fields and static fields on game-assembly types, in addition to instance `List<T>` / `T[]` fields, and includes DontDestroyOnLoad and inactive objects in the scan. Catches `StatsManager`-style singleton pools.
- Added **Dump** button (logs every visited field for diagnostics) and per-tab **Clear** button (two-click confirmation).

## 0.1.0
- Initial implementation: F3 IMGUI menu, JSON blacklist file, scene-load filter pass over `List<T>` and `T[]` fields on scene `MonoBehaviour`s for `Item` / `ValuableObject` / `EnemySetup` element types.
