using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace REPOBlacklist
{
    /// <summary>
    /// Discovers REPO's asset types (Item / ValuableObject / EnemySetup) by name from
    /// loaded assemblies, then strips blacklisted entries out of any List/array/Dictionary
    /// fields we can find — both on scene MonoBehaviours (including DDOL/inactive) and
    /// on static fields of game-assembly types. Type-resolution is cached.
    ///
    /// Type-name lookup is intentionally lenient: REPO has historically renamed these
    /// classes between updates. We try a list of candidates and use the first match.
    /// </summary>
    public static class BlacklistFilter
    {
        private static readonly string[] ItemTypeCandidates = { "Item" };
        private static readonly string[] ValuableTypeCandidates = { "ValuableObject", "Valuable" };
        private static readonly string[] EnemyTypeCandidates = { "EnemySetup", "EnemiesSetup", "EnemyParent" };

        private static Type _itemType;
        private static Type _valuableType;
        private static Type _enemyType;
        private static bool _resolved;

        /// <summary>Cached static-field hits — built once, applied each tick.</summary>
        private static List<FieldHit> _staticFieldCache;

        /// <summary>Per-Type instance-field match cache — built lazily on first sight.
        /// Reflection over fields is the hot path on big scenes; this turns
        /// "GetFields() then filter" into a one-shot lookup.</summary>
        private static readonly Dictionary<Type, FieldHit[]> _instanceFieldCache = new Dictionary<Type, FieldHit[]>();
        private static readonly FieldHit[] _emptyHits = Array.Empty<FieldHit>();

        private struct FieldHit
        {
            public FieldInfo Field;
            public BlacklistCategory Cat;
            public Type Elem;
            public ContainerKind Kind;
        }

        public static Type ItemType { get { Resolve(); return _itemType; } }
        public static Type ValuableType { get { Resolve(); return _valuableType; } }
        public static Type EnemyType { get { Resolve(); return _enemyType; } }

        public static Type TypeFor(BlacklistCategory cat)
        {
            switch (cat)
            {
                case BlacklistCategory.Item: return ItemType;
                case BlacklistCategory.Valuable: return ValuableType;
                case BlacklistCategory.Enemy: return EnemyType;
                default: return null;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _itemType = ResolveOne(ItemTypeCandidates);
            _valuableType = ResolveOne(ValuableTypeCandidates);
            _enemyType = ResolveOne(EnemyTypeCandidates);
            Plugin.Log?.LogInfo(
                $"Resolved types: Item={_itemType?.FullName ?? "?"}, " +
                $"Valuable={_valuableType?.FullName ?? "?"}, " +
                $"Enemy={_enemyType?.FullName ?? "?"}");
        }

        private static Type ResolveOne(string[] candidates)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsExternalAssembly(asm)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var name in candidates)
                {
                    var hit = types.FirstOrDefault(t => t != null && t.Name == name && typeof(UnityEngine.Object).IsAssignableFrom(t));
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        /// <summary>Enumerates every loaded asset of the given category's type.</summary>
        public static IEnumerable<UnityEngine.Object> Discover(BlacklistCategory cat)
        {
            var t = TypeFor(cat);
            if (t == null) return Array.Empty<UnityEngine.Object>();
            return Resources.FindObjectsOfTypeAll(t).Where(o => o != null);
        }

        /// <summary>
        /// Walks every reachable MonoBehaviour (including DDOL and inactive) plus all
        /// static fields on game-assembly types, removing blacklisted entries from any
        /// List, array, or Dictionary that holds Item/Valuable/Enemy assets.
        /// </summary>
        public static void Apply()
        {
            ApplyInternal(dump: false);
        }

        /// <summary>
        /// Same as Apply but also logs every (owner, field, type, count) tuple visited.
        /// Use when investigating a "still spawning" report.
        /// </summary>
        public static void Dump()
        {
            ApplyInternal(dump: true);
        }

        private static void ApplyInternal(bool dump)
        {
            try
            {
                Resolve();
                if (Plugin.Store == null) return;
                if (_itemType == null && _valuableType == null && _enemyType == null) return;

                int filteredFields = 0;
                int removedTotal = 0;
                int visitedFields = 0;

                // 1) Instance fields on every MonoBehaviour in the active scene + DDOL.
                // FindObjectsOfType(true) includes inactive but excludes prefab assets,
                // which is exactly what we want and roughly an order of magnitude
                // cheaper than Resources.FindObjectsOfTypeAll.
                var behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
                foreach (var mb in behaviours)
                {
                    if (mb == null) continue;
                    var t = mb.GetType();
                    var matched = GetMatchedInstanceFields(t);
                    if (matched.Length == 0) continue;
                    foreach (var hit in matched)
                    {
                        var value = hit.Field.GetValue(mb);
                        visitedFields++;
                        if (value == null) continue;
                        int beforeCount = ContainerCount(value, hit.Kind);
                        int removed = StripBlacklisted(ref value, hit.Cat, hit.Elem, hit.Kind);
                        if (removed > 0) { filteredFields++; removedTotal += removed; hit.Field.SetValue(mb, value); }
                        if (dump)
                            Plugin.Log?.LogInfo($"[dump] inst {t.FullName}.{hit.Field.Name} : {hit.Field.FieldType.Name} count={beforeCount} removed={removed} cat={hit.Cat}");
                    }
                }

                // 2) Static fields on game-assembly types (cached).
                if (_staticFieldCache == null) BuildStaticFieldCache();
                foreach (var hit in _staticFieldCache)
                {
                    var value = hit.Field.GetValue(null);
                    visitedFields++;
                    if (value == null) continue;
                    int beforeCount = ContainerCount(value, hit.Kind);
                    int removed = StripBlacklisted(ref value, hit.Cat, hit.Elem, hit.Kind);
                    if (removed > 0) { filteredFields++; removedTotal += removed; hit.Field.SetValue(null, value); }
                    if (dump)
                        Plugin.Log?.LogInfo($"[dump] static {hit.Field.DeclaringType?.FullName}.{hit.Field.Name} : {hit.Field.FieldType.Name} count={beforeCount} removed={removed} cat={hit.Cat}");
                }

                if (dump || removedTotal > 0)
                    Plugin.Log?.LogInfo($"Filter pass: visited {visitedFields} field(s), filtered {removedTotal} entries across {filteredFields} field(s).");
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"Apply failed: {e}");
            }
        }

        private static void BuildStaticFieldCache()
        {
            _staticFieldCache = new List<FieldHit>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsExternalAssembly(asm)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    FieldInfo[] fields;
                    try { fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                    catch { continue; }
                    foreach (var f in fields)
                    {
                        var (cat, elem, kind) = MatchField(f.FieldType);
                        if (cat == null) continue;
                        _staticFieldCache.Add(new FieldHit { Field = f, Cat = cat.Value, Elem = elem, Kind = kind });
                    }
                }
            }
            Plugin.Log?.LogInfo($"Static field cache: {_staticFieldCache.Count} hit(s).");
        }

        private static FieldHit[] GetMatchedInstanceFields(Type t)
        {
            if (_instanceFieldCache.TryGetValue(t, out var cached)) return cached;
            // Filter out external types up front — saves walking their fields.
            if (IsExternalAssembly(t.Assembly))
            {
                _instanceFieldCache[t] = _emptyHits;
                return _emptyHits;
            }
            FieldInfo[] fields;
            try { fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
            catch { _instanceFieldCache[t] = _emptyHits; return _emptyHits; }
            List<FieldHit> hits = null;
            foreach (var f in fields)
            {
                var (cat, elem, kind) = MatchField(f.FieldType);
                if (cat == null) continue;
                (hits ?? (hits = new List<FieldHit>())).Add(new FieldHit { Field = f, Cat = cat.Value, Elem = elem, Kind = kind });
            }
            var arr = hits == null ? _emptyHits : hits.ToArray();
            _instanceFieldCache[t] = arr;
            return arr;
        }

        private enum ContainerKind { None, Array, List, Dictionary }

        private static (BlacklistCategory? cat, Type elem, ContainerKind kind) MatchField(Type fieldType)
        {
            if (fieldType.IsArray)
            {
                var elem = fieldType.GetElementType();
                var cat = ClassifyElement(elem);
                return cat == null ? (null, null, ContainerKind.None) : (cat, elem, ContainerKind.Array);
            }
            if (fieldType.IsGenericType)
            {
                var def = fieldType.GetGenericTypeDefinition();
                var args = fieldType.GetGenericArguments();
                if (def == typeof(List<>))
                {
                    var elem = args[0];
                    var cat = ClassifyElement(elem);
                    return cat == null ? (null, null, ContainerKind.None) : (cat, elem, ContainerKind.List);
                }
                if (def == typeof(Dictionary<,>))
                {
                    // Match either key or value being a target type. For removal, we care
                    // about both: build a dictionary entry only counts if EITHER the key
                    // or the value is a UnityEngine.Object whose name is blacklisted.
                    var keyCat = ClassifyElement(args[0]);
                    var valCat = ClassifyElement(args[1]);
                    if (keyCat != null) return (keyCat, args[0], ContainerKind.Dictionary);
                    if (valCat != null) return (valCat, args[1], ContainerKind.Dictionary);
                }
            }
            return (null, null, ContainerKind.None);
        }

        private static BlacklistCategory? ClassifyElement(Type elem)
        {
            if (elem == null) return null;
            if (_itemType != null && _itemType.IsAssignableFrom(elem)) return BlacklistCategory.Item;
            if (_valuableType != null && _valuableType.IsAssignableFrom(elem)) return BlacklistCategory.Valuable;
            if (_enemyType != null && _enemyType.IsAssignableFrom(elem)) return BlacklistCategory.Enemy;
            return null;
        }

        private static int ContainerCount(object value, ContainerKind kind)
        {
            if (value == null) return 0;
            if (kind == ContainerKind.Dictionary && value is IDictionary d) return d.Count;
            if (value is ICollection c) return c.Count;
            return 0;
        }

        private static int StripBlacklisted(ref object value, BlacklistCategory cat, Type elem, ContainerKind kind)
        {
            if (value == null) return 0;
            switch (kind)
            {
                case ContainerKind.Array:
                {
                    var arr = (Array)value;
                    var kept = new List<object>(arr.Length);
                    int removed = 0;
                    foreach (var item in arr)
                    {
                        if (item is UnityEngine.Object uo && Plugin.Store.IsBlacklisted(cat, uo.name)) { removed++; continue; }
                        kept.Add(item);
                    }
                    if (removed > 0)
                    {
                        var newArr = Array.CreateInstance(elem, kept.Count);
                        for (int i = 0; i < kept.Count; i++) newArr.SetValue(kept[i], i);
                        value = newArr;
                    }
                    return removed;
                }
                case ContainerKind.List:
                {
                    var list = (IList)value;
                    int removed = 0;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i] is UnityEngine.Object uo && Plugin.Store.IsBlacklisted(cat, uo.name))
                        {
                            list.RemoveAt(i);
                            removed++;
                        }
                    }
                    return removed;
                }
                case ContainerKind.Dictionary:
                {
                    var dict = (IDictionary)value;
                    var keysToRemove = new List<object>();
                    foreach (DictionaryEntry e in dict)
                    {
                        if (e.Key is UnityEngine.Object ko && Plugin.Store.IsBlacklisted(cat, ko.name)) { keysToRemove.Add(e.Key); continue; }
                        if (e.Value is UnityEngine.Object vo && Plugin.Store.IsBlacklisted(cat, vo.name)) { keysToRemove.Add(e.Key); continue; }
                        // Heuristic: keys are often strings matching the asset name.
                        if (e.Key is string ks && Plugin.Store.IsBlacklisted(cat, ks)) { keysToRemove.Add(e.Key); }
                    }
                    foreach (var k in keysToRemove) dict.Remove(k);
                    return keysToRemove.Count;
                }
                default:
                    return 0;
            }
        }

        private static bool IsExternalAssembly(Assembly asm)
        {
            var name = asm.GetName().Name;
            if (string.IsNullOrEmpty(name)) return true;
            if (name == "REPOBlacklist") return true;
            if (name.StartsWith("UnityEngine") || name.StartsWith("Unity.")) return true;
            if (name.StartsWith("System") || name == "mscorlib" || name == "netstandard") return true;
            if (name.StartsWith("BepInEx") || name == "0Harmony" || name.StartsWith("HarmonyX") || name == "Mono.Cecil") return true;
            if (name.StartsWith("Microsoft.")) return true;
            return false;
        }
    }
}
