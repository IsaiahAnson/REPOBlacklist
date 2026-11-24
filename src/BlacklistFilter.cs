using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace REPOBlacklist
{
    /// <summary>
    /// Keeps blacklisted things out of what the host rolls. Each hook sits on the one game method
    /// that builds or reads a pool, and only acts for the host (or in singleplayer):
    ///   shop items     - after ShopManager.GetAllItemsFromStatsManager fills the shop pools
    ///   upgrade stand  - after UpgradeStand.GetWeightedUpgradeExcluding picks a rerolled upgrade
    ///   enemies        - around EnemyDirector.PickEnemies, with the count fixed in AmountSetup
    ///   valuables      - around the first step of ValuableDirector.SetupHost
    /// The game's own asset lists (StatsManager.itemDictionary, the enemy difficulty lists and the
    /// level valuable presets) are left as the game made them, so saves, owned items and other
    /// mods still see the full game.
    /// </summary>
    internal static class BlacklistFilter
    {
        private static readonly List<string> ActiveHooks = new List<string>();
        private static readonly List<string> FailedHooks = new List<string>();
        private static readonly HashSet<string> ReportedFailures = new HashSet<string>();

        private static MethodInfo _upgradeRoll;
        private static bool _rerollingUpgrade;

        // Counted over one EnemyDirector.AmountSetup call, for one log line per level.
        private static int _enemyPicksFiltered;

        /// <summary>One line for the log and the menu: which hooks are on.</summary>
        public static string HookSummary
        {
            get
            {
                string text = ActiveHooks.Count > 0 ? string.Join(", ", ActiveHooks) : "none";
                if (FailedHooks.Count > 0) text += " (not available in this game version: " + string.Join(", ", FailedHooks) + ")";
                return text;
            }
        }

        public static bool AnyHookFailed => FailedHooks.Count > 0;

        public static void Install(Harmony harmony)
        {
            Hook("shop items", () =>
                harmony.Patch(Find(typeof(ShopManager), "GetAllItemsFromStatsManager"),
                    postfix: Mine(nameof(ShopPoolsPostfix))));

            Hook("upgrade stand", () =>
            {
                _upgradeRoll = Find(typeof(UpgradeStand), "GetWeightedUpgradeExcluding");
                harmony.Patch(_upgradeRoll, postfix: Mine(nameof(UpgradeRollPostfix)));
            });

            Hook("enemies", () =>
            {
                // The count fix goes in first: leaving a pick out without it would break level loading.
                harmony.Patch(Find(typeof(EnemyDirector), "AmountSetup"),
                    postfix: Mine(nameof(AmountSetupPostfix)));
                harmony.Patch(Find(typeof(EnemyDirector), "PickEnemies"),
                    prefix: Mine(nameof(PickEnemiesPrefix)),
                    postfix: Mine(nameof(PickEnemiesPostfix)));
            });

            Hook("valuables", () =>
                harmony.Patch(Find(typeof(ValuableDirector), "SetupHost"),
                    postfix: Mine(nameof(SetupHostPostfix))));
        }

        private static void Hook(string name, Action install)
        {
            try
            {
                install();
                ActiveHooks.Add(name);
            }
            catch (Exception e)
            {
                FailedHooks.Add(name);
                Plugin.Log.LogError($"The {name} blacklist cannot work with this game version: {e.Message}");
            }
        }

        private static MethodInfo Find(Type type, string name)
        {
            var method = type.GetMethod(name, AccessTools.all);
            if (method == null) throw new MissingMethodException(type.Name + "." + name + " was not found");
            return method;
        }

        private static HarmonyMethod Mine(string name)
        {
            return new HarmonyMethod(typeof(BlacklistFilter).GetMethod(name, AccessTools.all));
        }

        private static bool IsHost()
        {
            return SemiFunc.IsMasterClientOrSingleplayer();
        }

        private static bool Blocked(BlacklistCategory category, string name)
        {
            return Plugin.Store.IsBlacklisted(category, name);
        }

        private static void ReportOnce(string where, Exception e)
        {
            if (ReportedFailures.Add(where))
            {
                Plugin.Log.LogError($"The {where} blacklist failed and was skipped: {e}");
            }
        }

        // ---------------------------------------------------------------- shop items

        private static void ShopPoolsPostfix(ShopManager __instance)
        {
            try
            {
                if (Plugin.Store.Count(BlacklistCategory.Item) == 0 || !IsHost()) return;

                var leftOut = new HashSet<string>(StringComparer.Ordinal);
                RemoveBlocked(__instance.potentialItems, leftOut);
                RemoveBlocked(__instance.potentialItemConsumables, leftOut);
                RemoveBlocked(__instance.potentialItemUpgrades, leftOut);
                RemoveBlocked(__instance.potentialItemHealthPacks, leftOut);
                if (__instance.potentialSecretItems != null)
                {
                    foreach (var pool in __instance.potentialSecretItems.Values) RemoveBlocked(pool, leftOut);
                }

                if (leftOut.Count > 0)
                {
                    Plugin.Log.LogInfo($"Shop: left out {leftOut.Count} blacklisted item(s): {string.Join(", ", leftOut.OrderBy(n => n))}.");
                }
            }
            catch (Exception e)
            {
                ReportOnce("shop items", e);
            }
        }

        private static void RemoveBlocked(List<Item> pool, HashSet<string> leftOut)
        {
            if (pool == null) return;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var item = pool[i];
                if (item == null) continue;
                string name = item.name;
                if (Blocked(BlacklistCategory.Item, name))
                {
                    leftOut.Add(name);
                    pool.RemoveAt(i);
                }
            }
        }

        // The upgrade stand rerolls from the full item list, not from the shop pools above.
        private static void UpgradeRollPostfix(UpgradeStand __instance, ref Item __result, Item excludeItem,
            Dictionary<string, int> displayedCounts, Dictionary<string, int> selectedDuringReroll)
        {
            if (_rerollingUpgrade || __result == null) return;
            try
            {
                if (!Blocked(BlacklistCategory.Item, __result.name) || !IsHost()) return;

                _rerollingUpgrade = true;
                Item pick = null;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    var candidate = (Item)_upgradeRoll.Invoke(__instance,
                        new object[] { excludeItem, displayedCounts, selectedDuringReroll, false });
                    if (candidate == null) break;
                    if (!Blocked(BlacklistCategory.Item, candidate.name))
                    {
                        pick = candidate;
                        break;
                    }
                }

                // Null leaves that slot of the stand empty, which the game handles.
                __result = pick;
            }
            catch (Exception e)
            {
                ReportOnce("upgrade stand", e);
            }
            finally
            {
                _rerollingUpgrade = false;
            }
        }

        // ---------------------------------------------------------------- enemies

        private static void PickEnemiesPrefix(ref List<EnemySetup> _enemiesList, out bool __state)
        {
            __state = false;
            try
            {
                if (_enemiesList == null || Plugin.Store.Count(BlacklistCategory.Enemy) == 0 || !IsHost()) return;

                List<EnemySetup> allowed = null;
                for (int i = 0; i < _enemiesList.Count; i++)
                {
                    var enemy = _enemiesList[i];
                    bool blocked = enemy != null && Blocked(BlacklistCategory.Enemy, enemy.name);
                    if (blocked && allowed == null) allowed = _enemiesList.GetRange(0, i);
                    else if (!blocked && allowed != null) allowed.Add(enemy);
                }

                if (allowed == null) return;

                // The game picks from this copy; its own list keeps every enemy.
                _enemiesList = allowed;
                __state = true;
                _enemyPicksFiltered++;
            }
            catch (Exception e)
            {
                ReportOnce("enemies", e);
            }
        }

        private static void PickEnemiesPostfix(bool __state, List<EnemySetup> ___enemyListCurrent, List<EnemySetup> ___enemyList)
        {
            if (!__state) return;

            // With nothing left to pick from, the game adds an empty slot, and an empty slot stops
            // the level from loading. Take it back out; AmountSetupPostfix lowers the count to match.
            DropEmptyLastSlot(___enemyListCurrent);
            DropEmptyLastSlot(___enemyList);
        }

        private static void DropEmptyLastSlot(List<EnemySetup> list)
        {
            if (list != null && list.Count > 0 && list[list.Count - 1] == null)
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        private static void AmountSetupPostfix(ref int ___totalAmount, List<EnemySetup> ___enemyListCurrent)
        {
            int filtered = _enemyPicksFiltered;
            _enemyPicksFiltered = 0;
            if (filtered == 0 || ___enemyListCurrent == null) return;

            int wanted = ___totalAmount;
            if (___enemyListCurrent.Count < wanted) ___totalAmount = ___enemyListCurrent.Count;

            string line = $"Enemies: blacklist used for {filtered} of {wanted} pick(s) in this level";
            if (___totalAmount < wanted)
            {
                line += $"; {wanted - ___totalAmount} left empty because every enemy of that difficulty is blacklisted";
            }
            Plugin.Log.LogInfo(line + ".");
        }

        // ---------------------------------------------------------------- valuables

        // SetupHost is a coroutine: its first step copies the level's valuable presets into the
        // director's own lists. Blacklisted entries are taken out of the presets just for that
        // step and put back straight after, so the presets end up exactly as the game made them.
        private static void SetupHostPostfix(ref IEnumerator __result)
        {
            if (__result != null) __result = FilteredSetup(__result);
        }

        private static IEnumerator FilteredSetup(IEnumerator original)
        {
            var hidden = new List<KeyValuePair<List<PrefabRef>, PrefabRef[]>>();
            bool more;
            try
            {
                HideBlacklistedValuables(hidden);
                more = original.MoveNext();
            }
            finally
            {
                foreach (var entry in hidden)
                {
                    entry.Key.Clear();
                    entry.Key.AddRange(entry.Value);
                }
            }

            while (more)
            {
                yield return original.Current;
                more = original.MoveNext();
            }
        }

        private static void HideBlacklistedValuables(List<KeyValuePair<List<PrefabRef>, PrefabRef[]>> hidden)
        {
            try
            {
                if (Plugin.Store.Count(BlacklistCategory.Valuable) == 0 || !IsHost()) return;

                var generator = LevelGenerator.Instance;
                var level = generator != null ? generator.Level : null;
                if (level == null || level.ValuablePresets == null) return;

                var pools = new HashSet<List<PrefabRef>>();
                foreach (var preset in level.ValuablePresets)
                {
                    foreach (var pool in SizePools(preset)) pools.Add(pool);
                }

                int total = 0;
                int blockedEntries = 0;
                var leftOut = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pool in pools)
                {
                    foreach (var valuable in pool)
                    {
                        total++;
                        if (!IsBlocked(valuable)) continue;
                        blockedEntries++;
                        leftOut.Add(valuable.PrefabName);
                    }
                }

                if (blockedEntries == 0) return;

                // With every valuable blacklisted the level gets none. The game copes: its spawn loop
                // stops at once, the haul goal becomes $0 and the extraction point completes at $0.
                foreach (var pool in pools)
                {
                    if (!pool.Any(IsBlocked)) continue;
                    hidden.Add(new KeyValuePair<List<PrefabRef>, PrefabRef[]>(pool, pool.ToArray()));
                    pool.RemoveAll(IsBlocked);
                }

                if (blockedEntries >= total)
                {
                    Plugin.Log.LogInfo($"Valuables: all {leftOut.Count} valuable(s) of this level are blacklisted, so this level has none.");
                }
                else
                {
                    Plugin.Log.LogInfo($"Valuables: left out {leftOut.Count} blacklisted valuable(s) in this level: {string.Join(", ", leftOut.OrderBy(n => n))}.");
                }
            }
            catch (Exception e)
            {
                ReportOnce("valuables", e);
            }
        }

        private static bool IsBlocked(PrefabRef valuable)
        {
            return valuable != null && Blocked(BlacklistCategory.Valuable, valuable.PrefabName);
        }

        private static IEnumerable<List<PrefabRef>> SizePools(LevelValuables preset)
        {
            if (preset == null) yield break;
            if (preset.tiny != null) yield return preset.tiny;
            if (preset.small != null) yield return preset.small;
            if (preset.medium != null) yield return preset.medium;
            if (preset.big != null) yield return preset.big;
            if (preset.wide != null) yield return preset.wide;
            if (preset.tall != null) yield return preset.tall;
            if (preset.veryTall != null) yield return preset.veryTall;
        }

        // ---------------------------------------------------------------- menu lists

        /// <summary>Names the menu can offer for a tab, sorted. Empty until the game has loaded them.</summary>
        public static List<string> Discover(BlacklistCategory category)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                switch (category)
                {
                    case BlacklistCategory.Item:
                        if (StatsManager.instance != null && StatsManager.instance.itemDictionary != null)
                        {
                            foreach (var item in StatsManager.instance.itemDictionary.Values)
                            {
                                if (item != null) names.Add(item.name);
                            }
                        }
                        break;

                    case BlacklistCategory.Enemy:
                        var director = EnemyDirector.instance;
                        if (director != null)
                        {
                            AddEnemies(names, director.enemiesDifficulty1);
                            AddEnemies(names, director.enemiesDifficulty2);
                            AddEnemies(names, director.enemiesDifficulty3);
                        }
                        if (names.Count == 0) AddEnemies(names, Resources.FindObjectsOfTypeAll<EnemySetup>());
                        break;

                    case BlacklistCategory.Valuable:
                        var presets = new HashSet<LevelValuables>(Resources.FindObjectsOfTypeAll<LevelValuables>());
                        if (RunManager.instance != null && RunManager.instance.levels != null)
                        {
                            foreach (var level in RunManager.instance.levels)
                            {
                                if (level == null || level.ValuablePresets == null) continue;
                                foreach (var preset in level.ValuablePresets) presets.Add(preset);
                            }
                        }
                        foreach (var preset in presets)
                        {
                            foreach (var pool in SizePools(preset))
                            {
                                foreach (var valuable in pool)
                                {
                                    if (valuable != null) names.Add(valuable.PrefabName);
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                ReportOnce(category + " list", e);
            }

            names.Remove(null);
            names.Remove("");
            var sorted = names.ToList();
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return sorted;
        }

        private static void AddEnemies(HashSet<string> names, IEnumerable<EnemySetup> enemies)
        {
            if (enemies == null) return;
            foreach (var enemy in enemies)
            {
                if (enemy != null) names.Add(enemy.name);
            }
        }

        /// <summary>Writes what the mod sees to the log, for a "this still spawns" report.</summary>
        public static void Dump()
        {
            var log = Plugin.Log;
            log.LogInfo("[dump] " + Plugin.NAME + " " + Plugin.VERSION + ", hooks: " + HookSummary);
            try
            {
                log.LogInfo("[dump] Host or singleplayer: " + IsHost());
            }
            catch (Exception)
            {
                log.LogInfo("[dump] Host or singleplayer: unknown (no game running)");
            }

            foreach (BlacklistCategory category in Enum.GetValues(typeof(BlacklistCategory)))
            {
                var found = Discover(category);
                var listed = Plugin.Store.Snapshot(category).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                log.LogInfo($"[dump] {category}: {listed.Count} blacklisted: {string.Join(", ", listed)}");
                log.LogInfo($"[dump] {category}: {found.Count} found in the game: {string.Join(", ", found)}");
            }
        }
    }
}
