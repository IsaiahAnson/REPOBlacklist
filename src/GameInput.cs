using System.Collections.Generic;

namespace REPOBlacklist
{
    /// <summary>
    /// Holds the game's controls still while the blacklist menu is open, using the game's own
    /// switches (the ones its menus use). Nothing in Unity or the game is patched for this.
    /// </summary>
    internal static class GameInput
    {
        // Each call holds for this long, so the controls come back on their own shortly after the
        // menu closes, and a click on Close or a press of Esc does not also reach the game.
        private const float HoldSeconds = 0.25f;

        // The only game control left working while the menu is open: voice chat.
        private static readonly List<InputKey> AllowedKeys = new List<InputKey> { InputKey.PushToTalk };

        /// <summary>Call every frame while the menu is open.</summary>
        public static void HoldThisFrame()
        {
            var input = InputManager.instance;
            if (input != null)
            {
                input.DisableMovement(HoldSeconds);
                input.DisableAiming(HoldSeconds);
                input.DisableControlsExcept(HoldSeconds, AllowedKeys);
            }

            var cursor = CursorManager.instance;
            if (cursor != null)
            {
                cursor.Unlock(HoldSeconds);
            }
        }
    }
}
