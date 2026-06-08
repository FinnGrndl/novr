using System.Collections.Generic;
using System.Linq;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.Native;

public class NativeSinglePlayerMissionPanel : MonoBehaviour
{
    private const int PageSize = 16;

    private static readonly Color BackgroundColor = new(0.025f, 0.035f, 0.045f, 0.92f);
    private static readonly Color PanelColor = new(0.05f, 0.06f, 0.065f, 0.94f);
    private static readonly Color ButtonColor = new(0.24f, 0.29f, 0.31f, 0.96f);
    private static readonly Color ButtonSelectedColor = new(0.44f, 0.49f, 0.50f, 1f);
    private static readonly Color ButtonHoverColor = new(0.34f, 0.40f, 0.42f, 1f);
    private static readonly Color ButtonPressedColor = new(0.16f, 0.20f, 0.22f, 1f);
    private static readonly Color BackButtonColor = new(0.62f, 0.12f, 0.14f, 0.96f);
    private static readonly Color StartButtonColor = new(0.12f, 0.34f, 0.20f, 0.96f);

    private readonly List<MissionEntry> _missions = new();
    private readonly List<Button> _missionButtons = new();
    private readonly List<Text> _missionButtonTexts = new();

    private NativeGameActionAdapter? _actions;
    private RectTransform? _container;
    private Font? _font;
    private Text? _titleText;
    private Text? _descriptionText;
    private Text? _tagsText;
    private Text? _pageText;
    private Button? _startButton;
    private int _selectedIndex;
    private int _page;
    private bool _loaded;

    public void Initialize(NativeGameActionAdapter actions, RectTransform root)
    {
        _actions = actions;
        _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        BuildLayout(root);
    }

    public void SetVisible(bool visible)
    {
        if (_container == null) return;

        if (visible && !_loaded)
        {
            LoadMissions();
        }

        if (_container.gameObject.activeSelf != visible)
        {
            _container.gameObject.SetActive(visible);
        }
    }

    private void BuildLayout(RectTransform root)
    {
        _container = CreateContainer("Native Single Player Missions", root, root.sizeDelta);
        CreateImage("Background", _container, BackgroundColor, Vector2.zero, _container.sizeDelta);
        CreateText("Header", _container, "SINGLE PLAYER MISSIONS", new Vector2(0f, 395f), new Vector2(1000f, 32f), 22, TextAnchor.MiddleCenter, Color.white);

        var listPanel = CreatePanel("Mission List Panel", _container, PanelColor, new Vector2(-315f, -10f), new Vector2(720f, 760f));
        CreateText("List Header", listPanel, "SELECT MISSION", new Vector2(0f, 340f), new Vector2(680f, 30f), 18, TextAnchor.MiddleCenter, Color.white);

        for (var index = 0; index < PageSize; index++)
        {
            var rowIndex = index;
            var row = CreateMenuButton(
                $"Mission Row {index}",
                listPanel,
                new Vector2(0f, 290f - index * 38f),
                new Vector2(670f, 32f),
                ButtonColor,
                () => SelectMission(_page * PageSize + rowIndex),
                13,
                TextAnchor.MiddleLeft);
            _missionButtons.Add(row);
            _missionButtonTexts.Add(row.GetComponentInChildren<Text>());
        }

        CreateMenuButton("Prev Page", listPanel, new Vector2(-210f, -335f), new Vector2(150f, 32f), ButtonColor, PreviousPage, 14);
        _pageText = CreateText("Page", listPanel, "", new Vector2(0f, -335f), new Vector2(180f, 32f), 14, TextAnchor.MiddleCenter, Color.white);
        CreateMenuButton("Next Page", listPanel, new Vector2(210f, -335f), new Vector2(150f, 32f), ButtonColor, NextPage, 14);

        var detailsPanel = CreatePanel("Mission Details Panel", _container, PanelColor, new Vector2(425f, -10f), new Vector2(620f, 760f));
        _titleText = CreateText("Mission Title", detailsPanel, "", new Vector2(0f, 310f), new Vector2(560f, 42f), 21, TextAnchor.MiddleCenter, Color.white);
        _tagsText = CreateText("Mission Tags", detailsPanel, "", new Vector2(0f, 262f), new Vector2(560f, 28f), 13, TextAnchor.MiddleCenter, new Color(0.82f, 0.86f, 0.72f, 1f));
        _descriptionText = CreateText("Mission Description", detailsPanel, "", new Vector2(0f, 40f), new Vector2(540f, 390f), 15, TextAnchor.UpperLeft, new Color(0.84f, 0.88f, 0.90f, 1f));

        CreateMenuButton("BACK", _container, new Vector2(-690f, -405f), new Vector2(170f, 40f), BackButtonColor, BackToMainMenu, 15);
        _startButton = CreateMenuButton("START MISSION", _container, new Vector2(570f, -405f), new Vector2(240f, 40f), StartButtonColor, StartSelectedMission, 15);

        _container.gameObject.SetActive(false);
    }

    private void LoadMissions()
    {
        _loaded = true;
        _missions.Clear();
        _page = 0;
        _selectedIndex = 0;

        try
        {
            MissionGroup.Init();
            var entries = MissionSaveLoad
                .QuickLoadMany(MissionGroup.All.GetMissions())
                .Where(entry => HasTag(entry.mission, MissionTag.SinglePlayer))
                .Select(entry => new MissionEntry(entry.key, entry.mission));
            _missions.AddRange(entries);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[NOVR] Failed to load native single player mission list: {exception}");
        }

        RefreshList();
        SelectMission(_missions.Count > 0 ? 0 : -1);
    }

    private static bool HasTag(MissionQuickLoad mission, MissionTag tag)
    {
        var tags = mission.missionSettings.Tags;
        return tags != null && tags.Any(existing => existing.Equals(tag));
    }

    private void RefreshList()
    {
        for (var index = 0; index < PageSize; index++)
        {
            var missionIndex = _page * PageSize + index;
            var hasMission = missionIndex >= 0 && missionIndex < _missions.Count;
            var button = _missionButtons[index];
            var text = _missionButtonTexts[index];
            button.gameObject.SetActive(hasMission);
            if (!hasMission) continue;

            var mission = _missions[missionIndex];
            text.text = $"  {mission.Key.Name}";
            SetButtonColor(button, missionIndex == _selectedIndex ? ButtonSelectedColor : ButtonColor);
        }

        if (_pageText != null)
        {
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(_missions.Count / (float)PageSize));
            _pageText.text = $"{_page + 1} / {totalPages}";
        }
    }

    private void SelectMission(int index)
    {
        if (index < 0 || index >= _missions.Count)
        {
            if (_titleText != null) _titleText.text = "";
            if (_tagsText != null) _tagsText.text = "";
            if (_descriptionText != null) _descriptionText.text = "";
            if (_startButton != null) _startButton.interactable = false;
            return;
        }

        _selectedIndex = index;
        var mission = _missions[_selectedIndex];
        if (_titleText != null) _titleText.text = mission.Key.Name;
        if (_tagsText != null) _tagsText.text = string.Join("   ", mission.Mission.missionSettings.Tags.Select(tag => tag.Tag));
        if (_descriptionText != null) _descriptionText.text = mission.Mission.missionSettings.description ?? "";
        if (_startButton != null) _startButton.interactable = true;
        RefreshList();
    }

    private void PreviousPage()
    {
        if (_page <= 0) return;

        _page--;
        SelectMission(Mathf.Clamp(_selectedIndex, _page * PageSize, Mathf.Min(_missions.Count - 1, (_page + 1) * PageSize - 1)));
        RefreshList();
    }

    private void NextPage()
    {
        if ((_page + 1) * PageSize >= _missions.Count) return;

        _page++;
        SelectMission(_page * PageSize);
        RefreshList();
    }

    private void BackToMainMenu()
    {
        _actions?.TryInvokeCurrentMenuButton("Back", "< BACK", "BACK", "MenuExit_Button");
    }

    private void StartSelectedMission()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _missions.Count) return;

        var missionKey = _missions[_selectedIndex].Key;
        if (!missionKey.TryLoad(out var mission, out var error))
        {
            if (_descriptionText != null) _descriptionText.text = error;
            Debug.LogWarning($"[NOVR] Native single player failed to load mission '{missionKey}': {error}");
            return;
        }

        MissionManager.SetMission(mission, checkIfSame: false);
        NetworkManagerNuclearOption.i.StartHost(new HostOptions(SocketType.Offline, GameState.SinglePlayer, mission.MapKey));
    }

    private RectTransform CreateContainer(string name, RectTransform parent, Vector2 size)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        LayerHelper.SetLayerRecursive(gameObject.transform, LayerHelper.GetVrUiLayer());

        var rectTransform = gameObject.AddComponent<RectTransform>();
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = Vector2.zero;
        return rectTransform;
    }

    private RectTransform CreatePanel(string name, RectTransform parent, Color color, Vector2 anchoredPosition, Vector2 size)
    {
        return CreateImage(name, parent, color, anchoredPosition, size);
    }

    private RectTransform CreateImage(string name, RectTransform parent, Color color, Vector2 anchoredPosition, Vector2 size)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        LayerHelper.SetLayerRecursive(gameObject.transform, LayerHelper.GetVrUiLayer());

        var rectTransform = gameObject.AddComponent<RectTransform>();
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var image = gameObject.AddComponent<Image>();
        image.color = color;
        return rectTransform;
    }

    private Text CreateText(string name, RectTransform parent, string text, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        LayerHelper.SetLayerRecursive(gameObject.transform, LayerHelper.GetVrUiLayer());

        var rectTransform = gameObject.AddComponent<RectTransform>();
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var textComponent = gameObject.AddComponent<Text>();
        textComponent.text = text;
        textComponent.font = _font;
        textComponent.fontSize = fontSize;
        textComponent.alignment = alignment;
        textComponent.color = color;
        textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
        textComponent.verticalOverflow = VerticalWrapMode.Truncate;
        return textComponent;
    }

    private Button CreateMenuButton(string label, RectTransform parent, Vector2 anchoredPosition, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 15, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var rectTransform = CreateImage(label, parent, color, anchoredPosition, size);
        var button = rectTransform.gameObject.AddComponent<Button>();
        button.targetGraphic = rectTransform.GetComponent<Image>();
        button.onClick.AddListener(onClick);

        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonPressedColor;
        colors.selectedColor = ButtonHoverColor;
        colors.disabledColor = new Color(0.16f, 0.18f, 0.19f, 0.55f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        CreateText($"{label} Text", rectTransform, label, Vector2.zero, size, fontSize, alignment, Color.white);
        return button;
    }

    private static void SetButtonColor(Button button, Color color)
    {
        var image = button.targetGraphic;
        if (image != null)
        {
            image.color = color;
        }

        var colors = button.colors;
        colors.normalColor = color;
        button.colors = colors;
    }

    private readonly struct MissionEntry
    {
        public MissionEntry(MissionKey key, MissionQuickLoad mission)
        {
            Key = key;
            Mission = mission;
        }

        public MissionKey Key { get; }
        public MissionQuickLoad Mission { get; }
    }
}
