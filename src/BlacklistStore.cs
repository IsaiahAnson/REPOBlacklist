using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace REPOBlacklist
{
    public enum BlacklistCategory { Item, Valuable, Enemy }

    /// <summary>
    /// The blacklist, kept in one JSON file (BepInEx/config/REPOBlacklist.json). Entries are asset
    /// names grouped by category: the Item and EnemySetup asset names and the valuable prefab
    /// names. Every change is written to disk straight away.
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
        private readonly ManualLogSource _log;
        private readonly HashSet<string> _items = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _valuables = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _enemies = new HashSet<string>(StringComparer.Ordinal);

        public BlacklistStore(string path, ManualLogSource log)
        {
            _path = path;
            _log = log;
            Load();
        }

        public bool IsBlacklisted(BlacklistCategory cat, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Names(cat).Contains(name);
        }

        public int Count(BlacklistCategory cat) => Names(cat).Count;

        public int Total => _items.Count + _valuables.Count + _enemies.Count;

        public IReadOnlyCollection<string> Snapshot(BlacklistCategory cat) => Names(cat);

        public void Set(BlacklistCategory cat, string name, bool blacklisted)
        {
            if (string.IsNullOrEmpty(name)) return;
            var names = Names(cat);
            bool changed = blacklisted ? names.Add(name) : names.Remove(name);
            if (changed) Save();
        }

        /// <summary>Empties one category and writes the file once.</summary>
        public void Clear(BlacklistCategory cat)
        {
            var names = Names(cat);
            if (names.Count == 0) return;
            names.Clear();
            Save();
        }

        private HashSet<string> Names(BlacklistCategory cat)
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
            if (!File.Exists(_path)) return;
            try
            {
                string json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return;

                var dto = JsonUtility.FromJson<Dto>(json);
                if (dto == null) throw new FormatException("no blacklist found in the file");
                Fill(_items, dto.items);
                Fill(_valuables, dto.valuables);
                Fill(_enemies, dto.enemies);
            }
            catch (Exception e)
            {
                // Keep a copy: the next change in the menu writes a fresh file over this one.
                string file = Path.GetFileName(_path);
                string kept = "";
                try
                {
                    File.Copy(_path, _path + ".bak", true);
                    kept = $" A copy was kept as {file}.bak.";
                }
                catch (Exception)
                {
                    // Nothing more to do; the error below still says what happened.
                }
                _items.Clear();
                _valuables.Clear();
                _enemies.Clear();
                _log.LogError($"Could not read {file}, so the blacklist starts empty.{kept} ({e.Message})");
            }
        }

        private static void Fill(HashSet<string> target, List<string> names)
        {
            target.Clear();
            if (names == null) return;
            foreach (var name in names)
            {
                if (!string.IsNullOrEmpty(name)) target.Add(name);
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

                // Write a new file first and swap it in, so a crash mid-write cannot leave half a list.
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(dto, prettyPrint: true));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temp, _path, null);
                    }
                    catch (Exception)
                    {
                        File.Copy(temp, _path, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception e)
            {
                _log.LogError($"Could not save {Path.GetFileName(_path)}: {e.Message}");
            }
        }
    }
}
