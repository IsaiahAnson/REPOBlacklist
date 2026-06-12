using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace REPOBlacklist
{
    public enum BlacklistCategory { Item, Valuable, Enemy }

    /// <summary>
    /// Persistent store backed by a single JSON file. Keys are asset names (the Unity
    /// Object.name of the underlying ScriptableObject), grouped by category.
    /// Mutations write through to disk immediately so the in-game UI and the runtime
    /// filter never disagree.
    /// </summary>
    public class BlacklistStore
    {
        [Serializable]
        private class Dto
        {
            public List<string> items = new List<string>();
            public List<string> valuables = new List<string>();
            public List<string> enemies = new List<string>();
        }

        private readonly string _path;
        private readonly HashSet<string> _items = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _valuables = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _enemies = new HashSet<string>(StringComparer.Ordinal);

        public event Action Changed;

        public BlacklistStore(string path)
        {
            _path = path;
            Load();
        }

        public bool IsBlacklisted(BlacklistCategory cat, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Set(cat).Contains(name);
        }

        public IReadOnlyCollection<string> Snapshot(BlacklistCategory cat) => Set(cat);

        public void Set(BlacklistCategory cat, string name, bool blacklisted)
        {
            if (string.IsNullOrEmpty(name)) return;
            var set = Set(cat);
            bool changed = blacklisted ? set.Add(name) : set.Remove(name);
            if (changed)
            {
                Save();
                Changed?.Invoke();
            }
        }

        private HashSet<string> Set(BlacklistCategory cat)
        {
            switch (cat)
            {
                case BlacklistCategory.Item: return _items;
                case BlacklistCategory.Valuable: return _valuables;
                case BlacklistCategory.Enemy: return _enemies;
                default: throw new ArgumentOutOfRangeException(nameof(cat));
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var json = File.ReadAllText(_path);
                var dto = JsonUtility.FromJson<Dto>(json) ?? new Dto();
                _items.Clear(); foreach (var n in dto.items ?? new List<string>()) _items.Add(n);
                _valuables.Clear(); foreach (var n in dto.valuables ?? new List<string>()) _valuables.Add(n);
                _enemies.Clear(); foreach (var n in dto.enemies ?? new List<string>()) _enemies.Add(n);
            }
            catch (Exception e)
            {
                Debug.LogError($"[REPOBlacklist] Failed to load {_path}: {e}");
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var dto = new Dto
                {
                    items = new List<string>(_items),
                    valuables = new List<string>(_valuables),
                    enemies = new List<string>(_enemies),
                };
                dto.items.Sort(StringComparer.Ordinal);
                dto.valuables.Sort(StringComparer.Ordinal);
                dto.enemies.Sort(StringComparer.Ordinal);
                File.WriteAllText(_path, JsonUtility.ToJson(dto, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[REPOBlacklist] Failed to save {_path}: {e}");
            }
        }
    }
}
