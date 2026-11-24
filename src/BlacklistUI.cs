using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace REPOBlacklist
{
    /// <summary>
    /// The in-game menu, opened with Plugin.MenuToggleKey (default F3) and closed with the same
    /// key, Esc or the Close button. While it is open the game's own controls are held still
    /// (see GameInput), so the mouse and keyboard only reach the menu.
    /// </summary>
    public class BlacklistUI : MonoBehaviour
    {
        private struct Row
        {
            public string Name;
            public string Label;
        }

        private bool _open;
        private Rect _window = new Rect(60, 60, 560, 660);
        private BlacklistCategory _tab = BlacklistCategory.Item;
        private string _filter = "";
        private Vector2 _scroll;

        private readonly Dictionary<BlacklistCategory, List<Row>> _rows = new Dictionary<BlacklistCategory, List<Row>>();

        private GUIStyle _windowStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _noteStyle;
        private bool _stylesReady;
        private bool _wasCursorVisible;
        private bool _isHost = true;
        private BlacklistCategory? _resetArmed;

        private void Update()
        {
            if (Plugin.Instance == null) return;

            var input = UnityInput.Current;
            if (input.GetKeyDown(Plugin.Instance.MenuToggleKey.Value))
            {
                if (_open) Close(); else Open();
            }
            else if (_open && input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }

            // The game must not move, look around, grab or open its own menus from the same input.
            if (_open) GameInput.HoldThisFrame();
        }

        private void OnDisable()
        {
            // Never leave the player frozen.
            if (_open) Close();
        }

        private void Open()
        {
            _open = true;
            _resetArmed = null;
            _wasCursorVisible = Cursor.visible;
            Refresh();
        }

        private void Close()
        {
            _open = false;
            _resetArmed = null;
            // The game locks the cursor again by itself once GameInput stops holding it.
            Cursor.visible = _wasCursorVisible;
        }

        private void Refresh()
        {
            foreach (BlacklistCategory cat in Enum.GetValues(typeof(BlacklistCategory)))
            {
                var found = BlacklistFilter.Discover(cat);
                var seen = new HashSet<string>(found, StringComparer.Ordinal);
                var rows = new List<Row>(found.Count);
                foreach (var name in found) rows.Add(new Row { Name = name, Label = name });

                // Entries from the file that the game does not have loaded stay visible, so they can be unticked.
                foreach (var name in Plugin.Store.Snapshot(cat))
                {
                    if (!seen.Contains(name)) rows.Add(new Row { Name = name, Label = name + "   (not found right now)" });
                }

                rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                _rows[cat] = rows;
            }

            try
            {
                _isHost = SemiFunc.IsMasterClientOrSingleplayer();
            }
            catch (Exception)
            {
                _isHost = true;
            }
        }

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();

            // REPO locks and hides the cursor during play. Keep it free and visible while the menu is open.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;

            _window = GUI.Window(GetInstanceID() ^ 0x1B71A57, _window, DrawWindow, "REPO Blacklist", _windowStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _windowStyle = new GUIStyle(GUI.skin.window);
            _toggleStyle = new GUIStyle(GUI.skin.toggle) { wordWrap = true };
            _noteStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
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
            if (GUILayout.Button("Close", GUILayout.Width(60))) Close();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(50));
            _filter = GUILayout.TextField(_filter ?? "");
            if (GUILayout.Button("X", GUILayout.Width(24))) _filter = "";
            GUILayout.EndHorizontal();

            if (!_rows.TryGetValue(_tab, out var rows) || rows == null || rows.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Nothing found for {_tab} yet.\nStart or join a game, then press Refresh.", _noteStyle);
                GUILayout.FlexibleSpace();
                DrawFooter();
                GUI.DragWindow(new Rect(0, 0, 10000, 20));
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{Plugin.Store.Count(_tab)} / {rows.Count} blacklisted");
            GUILayout.FlexibleSpace();
            string resetLabel = _resetArmed == _tab ? "Confirm clear?" : $"Clear {_tab}";
            if (GUILayout.Button(resetLabel, GUILayout.Width(140)))
            {
                if (_resetArmed == _tab)
                {
                    Plugin.Store.Clear(_tab);
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
            foreach (var row in rows)
            {
                if (filter.Length > 0 && row.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool was = Plugin.Store.IsBlacklisted(_tab, row.Name);
                bool now = GUILayout.Toggle(was, row.Label, _toggleStyle);
                if (now != was) Plugin.Store.Set(_tab, row.Name, now);
            }
            GUILayout.EndScrollView();

            DrawFooter();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawFooter()
        {
            string note = _isHost
                ? "Ticked things are left out from the next shop or level on."
                : "You are not the host here. Your list is used when you host.";
            if (BlacklistFilter.AnyHookFailed) note += "\nWorking in this game version: " + BlacklistFilter.HookSummary + ".";
            GUILayout.Label(note, _noteStyle);
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
