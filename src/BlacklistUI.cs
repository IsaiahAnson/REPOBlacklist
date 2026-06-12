using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace REPOBlacklist
{
    /// <summary>
    /// IMGUI overlay menu, toggled by Plugin.MenuToggleKey (default F3).
    /// While open, suppresses legacy mouse Input via InputBlocker so the camera
    /// doesn't follow the cursor and clicks don't leak through to the game.
    /// </summary>
    public class BlacklistUI : MonoBehaviour
    {
        private bool _open;
        private Rect _window = new Rect(60, 60, 560, 660);
        private BlacklistCategory _tab = BlacklistCategory.Item;
        private string _filter = "";
        private Vector2 _scroll;

        private readonly Dictionary<BlacklistCategory, List<UnityEngine.Object>> _cache =
            new Dictionary<BlacklistCategory, List<UnityEngine.Object>>();

        private GUIStyle _windowStyle;
        private GUIStyle _toggleStyle;
        private bool _stylesReady;
        private bool _wasCursorVisible;
        private CursorLockMode _wasCursorLock;
        private BlacklistCategory? _resetArmed;

        private void Update()
        {
            if (Plugin.Instance != null && Input.GetKeyDown(Plugin.Instance.MenuToggleKey.Value))
                Toggle();
        }

        private void LateUpdate()
        {
            // Re-assert cursor unlocked every frame. Some games re-lock in their own
            // Update; we win because LateUpdate runs after Update.
            if (_open)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void Toggle()
        {
            _open = !_open;
            InputBlocker.BlockInput = _open;
            _resetArmed = null;
            if (_open)
            {
                Refresh();
                _wasCursorVisible = Cursor.visible;
                _wasCursorLock = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                Cursor.visible = _wasCursorVisible;
                Cursor.lockState = _wasCursorLock;
                // Closing means the user is done editing — push changes through now so
                // they take effect on the next shop/level cycle without waiting for
                // the periodic safety-net pass.
                BlacklistFilter.Apply();
            }
        }

        private void Refresh()
        {
            foreach (BlacklistCategory cat in Enum.GetValues(typeof(BlacklistCategory)))
            {
                _cache[cat] = BlacklistFilter.Discover(cat)
                    .Where(o => !string.IsNullOrEmpty(o.name))
                    .GroupBy(o => o.name)
                    .Select(g => g.First())
                    .OrderBy(o => o.name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();
            _window = GUI.Window(GetInstanceID() ^ 0x1B71A57, _window, DrawWindow, "REPO Blacklist", _windowStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _windowStyle = new GUIStyle(GUI.skin.window);
            _toggleStyle = new GUIStyle(GUI.skin.toggle) { wordWrap = true };
            _stylesReady = true;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            DrawTabButton("Items", BlacklistCategory.Item);
            DrawTabButton("Valuables", BlacklistCategory.Valuable);
            DrawTabButton("Enemies", BlacklistCategory.Enemy);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", GUILayout.Width(72))) Refresh();
            if (GUILayout.Button("Dump", GUILayout.Width(60))) BlacklistFilter.Dump();
            if (GUILayout.Button("Close", GUILayout.Width(60))) Toggle();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(50));
            _filter = GUILayout.TextField(_filter ?? "");
            if (GUILayout.Button("X", GUILayout.Width(24))) _filter = "";
            GUILayout.EndHorizontal();

            if (!_cache.TryGetValue(_tab, out var list) || list == null || list.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(BlacklistFilter.TypeFor(_tab) == null
                    ? $"Could not resolve the {_tab} type in any loaded assembly.\nThe game may not be loaded yet, or REPO renamed it."
                    : $"No {_tab} assets currently loaded.\nEnter a level or open the shop, then hit Refresh.");
                GUILayout.FlexibleSpace();
                GUI.DragWindow(new Rect(0, 0, 10000, 20));
                return;
            }

            int blacklisted = list.Count(o => Plugin.Store.IsBlacklisted(_tab, o.name));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{blacklisted} / {list.Count} blacklisted");
            GUILayout.FlexibleSpace();
            string resetLabel = _resetArmed == _tab ? "Confirm clear?" : $"Clear {_tab}";
            if (GUILayout.Button(resetLabel, GUILayout.Width(140)))
            {
                if (_resetArmed == _tab)
                {
                    foreach (var name in Plugin.Store.Snapshot(_tab).ToList())
                        Plugin.Store.Set(_tab, name, false);
                    _resetArmed = null;
                }
                else
                {
                    _resetArmed = _tab;
                }
            }
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            string filter = (_filter ?? "").Trim();
            foreach (var asset in list)
            {
                if (filter.Length > 0 && asset.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool was = Plugin.Store.IsBlacklisted(_tab, asset.name);
                bool now = GUILayout.Toggle(was, asset.name, _toggleStyle);
                if (now != was) Plugin.Store.Set(_tab, asset.name, now);
            }
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawTabButton(string label, BlacklistCategory cat)
        {
            var prev = GUI.backgroundColor;
            if (_tab == cat) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button(label, GUILayout.Width(90))) { _tab = cat; _resetArmed = null; }
            GUI.backgroundColor = prev;
        }
    }
}
