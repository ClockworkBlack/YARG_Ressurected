// Profile menus that used to need the touch screen, as lists a controller or a guitar can work:
// up / down (strum) moves, green picks, red goes back.
//
//   - Rename: the name is put together a letter at a time from a list.
//   - Buttons: what each button does, while playing and in the menus. Pick an action, then press
//     the button that should do it.
//
// (The touch screen's own "Edit binds" screen is still there for sliders such as press points.)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Input;
using YARG.Localization;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Scores;

namespace YARG.Menu.ProfileList
{
    public static class ProfileControllerMenus
    {
        // ---------------------------------------------------------------- rename

        private const int MAX_NAME_LENGTH = 24;
        private const string LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>Shows the letter list. <paramref name="done"/> runs when the list is left, saved or not.</summary>
        public static void ShowRename(YargProfile profile, Action done, string typed = null, int select = 0)
        {
            typed ??= string.Empty;
            var dialog = DialogManager.Instance.ShowList(
                "Name: " + (typed.Length > 0 ? typed : "") + "_\n<size=55%><line-height=60%>\n" +
                "Pick letters one at a time, then Save. (Was: " + profile.Name + ")</size>");

            int rows = 0;
            dialog.AddListButton(typed.Trim().Length > 0 ? "Save this name" : "Save (pick some letters first)", () => After(() =>
            {
                string name = typed.Trim();
                if (name.Length > 0)
                {
                    // The same two steps as typing a name on the touch screen
                    profile.Name = name;
                    ScoreContainer.RecordPlayerInfo(profile.Id, profile.Name);
                }
                done?.Invoke();
                return UniTask.CompletedTask;
            }));
            rows++;
            int deleteRow = rows++;
            dialog.AddListButton("Delete the last letter", () => After(() =>
            {
                ShowRename(profile, done, typed.Length > 0 ? typed.Substring(0, typed.Length - 1) : typed, deleteRow);
                return UniTask.CompletedTask;
            }));
            int spaceRow = rows++;
            dialog.AddListButton("Space", () => After(() =>
            {
                bool room = typed.Length > 0 && typed.Length < MAX_NAME_LENGTH && !typed.EndsWith(" ");
                ShowRename(profile, done, room ? typed + " " : typed, spaceRow);
                return UniTask.CompletedTask;
            }));

            foreach (char c in LETTERS)
            {
                int row = rows++;
                // A capital to start a word, small letters after it
                bool capital = typed.Length == 0 || typed.EndsWith(" ");
                char letter = capital ? c : char.ToLowerInvariant(c);
                dialog.AddListButton(letter.ToString(), () => After(() =>
                {
                    ShowRename(profile, done, typed.Length < MAX_NAME_LENGTH ? typed + letter : typed, row);
                    return UniTask.CompletedTask;
                }));
            }

            dialog.AddListButton("Cancel (keep " + profile.Name + ")", () => After(() =>
            {
                done?.Invoke();
                return UniTask.CompletedTask;
            }));

            dialog.SelectListIndex(select);
        }

        // ---------------------------------------------------------------- buttons

        private const double CAPTURE_SECONDS = 8;

        /// <summary>
        /// Shows what each button of the player's controllers does, and lets it be changed.
        /// </summary>
        /// <param name="menu">False: the buttons while playing. True: the buttons in the menus.</param>
        public static void ShowButtons(YargPlayer player, Action done, bool menu = false, int select = 0)
        {
            if (player?.Bindings == null)
            {
                done?.Invoke();
                return;
            }

            var profile = player.Profile;
            var collection = menu ? player.Bindings.MenuBindings : player.Bindings[profile.GameMode];
            var dialog = DialogManager.Instance.ShowList(profile.Name + "\n<size=55%><line-height=60%>\n" +
                (menu ? "Buttons in the menus" : "Buttons while playing") +
                ". Pick one to change it.</size>");

            int rows = 0;
            dialog.AddListButton(menu ? "Show the buttons while playing instead" : "Show the buttons in the menus instead",
                () => After(() =>
                {
                    ShowButtons(player, done, !menu, 0);
                    return UniTask.CompletedTask;
                }));
            rows++;

            foreach (var binding in collection)
            {
                if (binding is not ButtonBinding button)
                {
                    // Whammy, tilt and the like are set by moving a slider: touch screen only
                    continue;
                }

                int row = rows++;
                dialog.AddListButton(ActionName(profile, button) + ":  " + ControlNames(button), () => After(() =>
                {
                    ShowOneButton(player, done, menu, button, row);
                    return UniTask.CompletedTask;
                }));
            }

            dialog.AddListButton("Back", () => After(() =>
            {
                done?.Invoke();
                return UniTask.CompletedTask;
            }));

            dialog.SelectListIndex(select);
        }

        private static void ShowOneButton(YargPlayer player, Action done, bool menu, ButtonBinding button, int row)
        {
            string action = ActionName(player.Profile, button);
            var dialog = DialogManager.Instance.ShowList(action + "\n<size=55%><line-height=60%>\nNow: " +
                ControlNames(button) + "</size>");

            dialog.AddListButton("Press a button to use (in place of what it is now)", () => After(async () =>
            {
                await Capture(player, button, action, true);
                ShowButtons(player, done, menu, row);
            }));
            dialog.AddListButton("Add a second button (keeps what it is now)", () => After(async () =>
            {
                await Capture(player, button, action, false);
                ShowButtons(player, done, menu, row);
            }));
            if (button.Bindings.Count > 0 && !(menu && IsNeededInMenus(button)))
            {
                dialog.AddListButton("Take its buttons away", () => After(() =>
                {
                    button.ClearAllBindings();
                    ShowButtons(player, done, menu, row);
                    return UniTask.CompletedTask;
                }));
            }
            dialog.AddListButton("Back", () => After(() =>
            {
                ShowButtons(player, done, menu, row);
                return UniTask.CompletedTask;
            }));
        }

        // Without these four a controller can no longer work the menus at all, this list included
        private static bool IsNeededInMenus(ButtonBinding button)
        {
            var action = (YARG.Core.Input.MenuAction) button.Action;
            return action is YARG.Core.Input.MenuAction.Green or YARG.Core.Input.MenuAction.Red
                or YARG.Core.Input.MenuAction.Up or YARG.Core.Input.MenuAction.Down;
        }

        /// <summary>
        /// Waits for a button on one of the player's controllers and gives it to the action. The
        /// player's own buttons do nothing in the menus meanwhile (the press would also pick
        /// something), so there is no button to cancel with: doing nothing for a few seconds cancels.
        /// </summary>
        private static async UniTask Capture(YargPlayer player, ButtonBinding button, string action, bool replace)
        {
            DialogManager.Instance.ShowMessage("Press a button",
                "Press the button on your controller for:\n\n<b>" + action + "</b>\n\n" +
                "Waiting " + (int) CAPTURE_SECONDS + " seconds. Press nothing to leave it as it is.");

            bool wereEnabled = player.InputsEnabled;
            player.DisableInputs();
            InputControl picked = null;
            using (var timeout = new CancellationTokenSource())
            {
                // (The timer is put away before the token it cancels)
                using var timer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(CAPTURE_SECONDS), DelayType.UnscaledDeltaTime);
                try
                {
                    // (Comes back with whatever was pressed, or nothing when the time is up)
                    var controls = new List<InputControl>(
                        await InputControlBindingHelper.Instance.GetControl(player, timeout.Token, button));
                    // A D-pad or strum bar also moves an axis; the button is the one to keep
                    picked = controls.FirstOrDefault(c => c is ButtonControl) ?? controls.FirstOrDefault();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Waiting for a button failed");
                }
            }

            if (picked != null)
            {
                if (replace)
                {
                    button.ClearAllBindings();
                }
                if (!button.ContainsControl(picked))
                {
                    button.AddControl(picked);
                }
            }

            // Let go of the button before it means something in the menus again
            await UniTask.Delay(TimeSpan.FromSeconds(0.4), ignoreTimeScale: true);
            if (wereEnabled)
            {
                player.EnableInputs();
            }

            DialogManager.Instance.ClearDialog();
            await UniTask.Yield();
        }

        private static string ActionName(YargProfile profile, ControlBinding binding)
        {
            string key = profile.LeftyFlip ? binding.NameLefty : binding.Name;
            try
            {
                return Localize.Key("Bindings", key);
            }
            catch (Exception)
            {
                return key;
            }
        }

        private static string ControlNames(ButtonBinding button)
        {
            if (button.Bindings.Count == 0)
            {
                return "(no button)";
            }

            return string.Join(", ", button.Bindings.Select(b =>
                string.IsNullOrEmpty(b.Control.displayName) ? b.Control.name : b.Control.displayName));
        }

        // Runs once the list that was just chosen from has closed, so a new dialog can open
        private static async void After(Func<UniTask> action)
        {
            try
            {
                await UniTask.Yield();
                await DialogManager.Instance.WaitUntilCurrentClosed();
                await action();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Profile menu action failed");
            }
        }
    }
}
