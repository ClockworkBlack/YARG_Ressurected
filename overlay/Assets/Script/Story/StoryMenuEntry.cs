// YARG Resurrected - Story Mode: the STORY entry of the main menu.
//
// The main menu lives in a prefab, which this project does not edit. So each time the menu scene
// loads, a copy of one of the menu's own rows (Quickplay, Settings, ...) is made and put at the top
// of the list as "STORY". It then looks, moves and answers touch and controllers exactly like the
// others. Four things have to be fixed on the copy: its text would be put back to the original
// name, it would still run the original row's action, it must be copied from a row that is not
// highlighted, and the list's controller order must include it.
//
// If any of that fails (a later version of the game may build the menu differently), a small
// "STORY" button floats over the main menu instead, so story mode can always be reached.
//
// This also keeps track of whether the main menu is the menu on screen, which the story uses to
// come back after the player list it opened is closed.
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YARG.Core.Logging;
using YARG.Localization;
using YARG.Menu;
using YARG.Menu.Main;
using YARG.Menu.Navigation;

namespace YARG.Story
{
    public sealed class StoryMenuEntry : MonoBehaviour
    {
        private static readonly Color Gold = new Color(1f, 0.831f, 0.227f, 1f);

        /// <summary>Counts each time the main menu comes on screen.</summary>
        public static int MainMenuShows { get; private set; }

        /// <summary>The main menu is the menu on screen (the story may still be covering it).</summary>
        public static bool MainMenuVisible { get; private set; }

        // Both belong to the menu scene and are destroyed with it (Unity then reports them as null)
        private MainMenu      _mainMenu;
        private GameObject    _entry;
        private RectTransform _movedList;

        private bool _injectFailed;
        private int  _nextSearchFrame;
        private bool _openRequested;
        private bool _wasVisible;

        private Canvas     _fallbackCanvas;
        private GameObject _fallbackButton;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex != (int) SceneIndex.Menu)
            {
                return;
            }

            // A fresh menu scene: a fresh main menu to add the entry to. At this point the main
            // menu has never been switched on, so its list will pick the new row up by itself.
            _mainMenu = null;
            _entry = null;
            _injectFailed = false;
            TryInject();
        }

        private void Update()
        {
            try
            {
                // The press is handled a frame later, outside the game's input handling
                if (_openRequested)
                {
                    _openRequested = false;
                    var story = StoryMode.Instance;
                    if (story != null)
                    {
                        story.Open();
                    }
                }

                bool menuScene = InMenusOnly();
                if (menuScene && MenuManager.Instance != null)
                {
                    // Second path, in case the scene event came before the menu was there
                    if (_entry == null && !_injectFailed)
                    {
                        TryInject();
                    }

                    if (_mainMenu == null && Time.frameCount >= _nextSearchFrame)
                    {
                        _nextSearchFrame = Time.frameCount + 30;
                        _mainMenu = FindMainMenu();
                    }
                }

                bool visible = menuScene && _mainMenu != null && _mainMenu.isActiveAndEnabled;
                if (visible && !_wasVisible)
                {
                    MainMenuShows++;
                }

                _wasVisible = visible;
                MainMenuVisible = visible;

                UpdateFallback(visible);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: error in the main menu entry");
                _injectFailed = true;
            }
        }

        private void OnEntryPressed()
        {
            _openRequested = true;
        }

        // ------------------------------------------------------------------------------------
        // The row in the main menu
        // ------------------------------------------------------------------------------------

        private void TryInject()
        {
            GameObject clone = null;
            try
            {
                if (_entry != null || MenuManager.Instance == null)
                {
                    return;
                }

                var mainMenu = FindMainMenu();
                if (mainMenu == null)
                {
                    InjectFailed("the main menu was not found");
                    return;
                }

                _mainMenu = mainMenu;
                var group = mainMenu.GetComponentInChildren<NavigationGroup>(true);
                var list = group != null ? group.transform as RectTransform : null;
                if (list == null)
                {
                    InjectFailed("the main menu's list was not found");
                    return;
                }

                // Copy a row that is not highlighted: the copy keeps the color it starts with as its
                // normal color. Not "Replays", which carries a "Beta" badge.
                NavigatableButton template = null;
                foreach (Transform child in list)
                {
                    if (child.name == "Replays" || !child.gameObject.activeSelf)
                    {
                        continue;
                    }

                    if (child.TryGetComponent<NavigatableButton>(out var button) && !button.Selected)
                    {
                        template = button;
                        break;
                    }
                }

                if (template == null)
                {
                    InjectFailed("no row of the main menu could be copied");
                    return;
                }

                clone = Instantiate(template.gameObject, list, false);
                clone.name = "Story Entry";
                clone.transform.SetSiblingIndex(0);

                // Its translation would put the original name back
                foreach (var localize in clone.GetComponentsInChildren<LocalizeText>(true))
                {
                    DestroyImmediate(localize);
                }

                var text = clone.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text == null)
                {
                    InjectFailed("the copied row has no text");
                    Destroy(clone);
                    return;
                }

                // The row's style is upper case: this shows as STORY
                text.text = "Story";

                var badge = clone.transform.Find("Badge Container");
                if (badge != null)
                {
                    badge.gameObject.SetActive(false);
                }

                // The copy's action still points at the original row's: give it a fresh, empty one
                var entryButton = clone.GetComponent<NavigatableButton>();
                var field = typeof(NavigatableButton).GetField("_onClick", BindingFlags.Instance | BindingFlags.NonPublic);
                if (entryButton == null || field == null)
                {
                    InjectFailed("the copied row's action could not be replaced");
                    Destroy(clone);
                    return;
                }

                field.SetValue(entryButton, new Button.ButtonClickedEvent());
                if (!(field.GetValue(entryButton) is UnityEventBase action) || action.GetPersistentEventCount() != 0)
                {
                    InjectFailed("the copied row still has the original action");
                    Destroy(clone);
                    return;
                }

                entryButton.SetOnClickEvent(OnEntryPressed);

                // Room for one more row: the list moves up a little and the rows sit a little
                // closer, so the last row stays where it was (clear of the bars along the bottom)
                if (_movedList != list)
                {
                    _movedList = list;
                    list.anchoredPosition += new Vector2(0f, 40f);
                    var layout = list.GetComponent<VerticalLayoutGroup>();
                    if (layout != null)
                    {
                        layout.spacing = 20f;
                    }
                }

                // A list that has already been on screen does not see new rows by itself: rebuild
                // it in the order the rows are shown
                if (group.Count > 0)
                {
                    group.ClearNavigatables();
                    foreach (Transform child in list)
                    {
                        if (child.gameObject.activeSelf && child.TryGetComponent<NavigatableBehaviour>(out var row))
                        {
                            group.AddNavigatable(row);
                        }
                    }

                    group.SelectFirst();
                }

                _entry = clone;
                clone = null;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not add STORY to the main menu");
                _injectFailed = true;
                if (clone != null)
                {
                    Destroy(clone);
                }
            }
        }

        private void InjectFailed(string why)
        {
            if (!_injectFailed)
            {
                YargLogger.LogWarning("Story: STORY could not be added to the main menu (" + why +
                    "); showing a separate button instead");
            }

            _injectFailed = true;
        }

        private static MainMenu FindMainMenu()
        {
            var manager = MenuManager.Instance;
            if (manager == null)
            {
                return null;
            }

            var mainMenu = manager.GetComponentInChildren<MainMenu>(true);
            if (mainMenu == null)
            {
                mainMenu = FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);
            }

            return mainMenu;
        }

        // The menus are up and no song, score screen or calibration is
        private static bool InMenusOnly()
        {
            return SceneManager.GetSceneByBuildIndex((int) SceneIndex.Menu).isLoaded &&
                !SceneManager.GetSceneByBuildIndex((int) SceneIndex.Gameplay).isLoaded &&
                !SceneManager.GetSceneByBuildIndex((int) SceneIndex.Score).isLoaded &&
                !SceneManager.GetSceneByBuildIndex((int) SceneIndex.Calibration).isLoaded;
        }

        // ------------------------------------------------------------------------------------
        // The separate button, when the row could not be added
        // ------------------------------------------------------------------------------------

        private void UpdateFallback(bool mainMenuVisible)
        {
            var story = StoryMode.Instance;
            bool wanted = mainMenuVisible && _entry == null && _injectFailed && story != null &&
                !story.IsOpen && !story.GigUnderWay;

            if (wanted && _fallbackCanvas == null)
            {
                CreateFallback();
            }

            if (_fallbackCanvas != null && _fallbackCanvas.enabled != wanted)
            {
                _fallbackCanvas.enabled = wanted;
            }
        }

        // Its own small canvas, like the PHONES bar, but at the top of the screen: the PHONES bar,
        // the button help bar and the Song Setup bar all sit along the bottom
        private void CreateFallback()
        {
            var canvasObject = new GameObject("YARG Story Button");
            canvasObject.transform.SetParent(transform, false);

            _fallbackCanvas = canvasObject.AddComponent<Canvas>();
            _fallbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fallbackCanvas.sortingOrder = 91;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            _fallbackButton = new GameObject("Story Button");
            _fallbackButton.transform.SetParent(canvasObject.transform, false);
            var rect = _fallbackButton.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(280f, 88f);

            var image = _fallbackButton.AddComponent<Image>();
            image.color = new Color(0.06f, 0.05f, 0.08f, 0.85f);

            var button = _fallbackButton.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnEntryPressed);

            var textObject = new GameObject("Label");
            textObject.transform.SetParent(_fallbackButton.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.AddComponent<TextMeshProUGUI>();
            try
            {
                // The main menu's own font, when the loading screen has it
                var loading = LoadingScreen.Instance;
                if (loading != null && loading.LoadingPhrase != null && loading.LoadingPhrase.font != null)
                {
                    text.font = loading.LoadingPhrase.font;
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: menu font not found, using the default one");
            }

            // Set once: the game's font scaler puts sizes back to the first one it sees
            text.fontSize = 44;
            text.fontStyle = FontStyles.Bold;
            text.text = "STORY";
            text.color = Gold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.richText = false;

            _fallbackCanvas.enabled = false;
        }
    }
}
