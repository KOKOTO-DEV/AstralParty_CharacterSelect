using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralPartyHeroGroups;

[BepInPlugin(CharacterSelectPlugin.PluginId, "Astral Party Character Select", "1.1.0")]
public sealed class CharacterSelectPlugin : BasePlugin
{
    private Harmony? harmony;
    internal const string PluginId = "astral-party.character-select";
    private static PanelObserver? observer;

    public override void Load()
    {
        var update = AccessTools.Method(typeof(EventSystem), "Update", Type.EmptyTypes)
            ?? throw new MissingMethodException("EventSystem.Update");
        observer = new PanelObserver(Log, Config);
        harmony = new Harmony(PluginId);
        harmony.Patch(update, postfix: new HarmonyMethod(typeof(CharacterSelectPlugin), nameof(AfterUpdate)));
    }

    private static void AfterUpdate() => observer?.Update();

    public override bool Unload()
    {
        observer?.Detach();
        observer = null;
        harmony?.UnpatchSelf();
        return true;
    }
}

internal sealed unsafe partial class PanelObserver
{
    private readonly ManualLogSource logger;
    private readonly ConfigEntry<string>[] groups;
    private readonly ConfigEntry<string>[] tagLabels;
    private readonly ConfigEntry<SortMode> sortMode;
    private readonly ConfigEntry<string> customOrder;
    private readonly ConfigEntry<string> manualOrder;
    private readonly ConfigFile settingsFile;
    private IntPtr sortLabel;
    private SortMode displayedSortMode;
    private IntPtr managerClass;
    private IntPtr observedPanel;
    private IntPtr rootClass;
    private IntPtr graphClass;
    private IntPtr textClass;
    private IntPtr container;
    private nint containerHandle;
    private float buttonWidth;
    private float selectionButtonGap;
    private float buttonHeight;
    private int selectionScreenWidth;
    private int selectionScreenHeight;
    private Rect selectionBounds;
    private int clickFrame = -1;
    private Dictionary<int, int> originalIndex = new();
    private IntPtr previousCards;
    private IntPtr activeRoot;
    private IntPtr activeList;
    private float nextPoll;
    private string? lastError;

    private static readonly string[] GroupNames =
        { "P1", "P2", "P3", "P4", "딜러", "서포터", "카드특화", "탱커" };
    private static readonly string[] LegacySeatKeys = { "1P", "2P", "3P", "4P" };
    private static readonly string[] TagKeys = { "P1", "P2", "P3", "P4", "Dealer", "Support", "Card", "Tank" };
    private const string RandomLabel = "랜덤";
    private static int SelectionButtonCount => GroupNames.Length + 2;
    private static int RandomButtonIndex => GroupNames.Length;
    private static int SortButtonIndex => GroupNames.Length + 1;
    private enum SortMode { Seat, Role, Custom, Manual, Default }
    private static readonly HeroTag[] SeatOrder = Enum.GetValues<HeroTag>();
    private static readonly HeroTag[] RoleOrder = SeatOrder.Skip(4).Concat(SeatOrder.Take(4)).ToArray();

    public PanelObserver(ManualLogSource logger, ConfigFile config)
    {
        this.logger = logger;
        settingsFile = config;
        groups = new ConfigEntry<string>[GroupNames.Length];
        for (var index = 0; index < groups.Length; index++)
            groups[index] = config.Bind("캐릭터 ID",
                index < LegacySeatKeys.Length ? LegacySeatKeys[index] : GroupNames[index], "",
                "캐릭터 ID를 쉼표로 구분해 입력합니다.");
        tagLabels = BindTagLabels(config);
        sortMode = config.Bind("정렬", "Mode", SortMode.Seat,
            "Default = 게임 기본 순서, Seat = 자리 우선, Role = 역할 우선, Custom = 태그 순서 지정, Manual = 캐릭터 순서 지정");
        customOrder = config.Bind("정렬", "CustomOrder", "",
            "태그 키 또는 표시 이름을 쉼표로 구분합니다. 빠진 태그는 기본 순서로 뒤에 붙습니다.");
        manualOrder = config.Bind("정렬", "ManualOrder", "",
            "Mode = Manual일 때 캐릭터 ID를 쉼표로 구분해 입력합니다. 빠진 캐릭터는 게임 순서로 뒤에 붙습니다.");
        languageSetting = config.Bind("일반", "Language", UiLanguage.Auto,
            "Auto follows displayed game text, including Korean patches. Select a language to override.");
    }

    private static ConfigEntry<string>[] BindTagLabels(ConfigFile config)
    {
        return TagKeys.Select((key, index) =>
            config.Bind("태그 이름", key, GroupNames[index], "게임에 표시할 이름입니다.")).ToArray();
    }

    [DllImport("GameAssembly.dll", EntryPoint = "il2cpp_domain_get_assemblies",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern unsafe IntPtr* Assemblies(IntPtr domain, out nuint count);

    public void Update()
    {
        var clicked = Input.GetMouseButtonUp(0) && clickFrame != Time.frameCount;
        if (clicked) clickFrame = Time.frameCount;
        var resized = activeList != IntPtr.Zero &&
            (selectionScreenWidth != Screen.width || selectionScreenHeight != Screen.height);
        if (!clicked && !resized && Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.15f;
        try { Observe(clicked); }
        catch (Exception error)
        {
            if (lastError == error.Message) return;
            lastError = error.Message;
            logger.LogWarning(error.Message);
        }
    }

    private void Observe(bool clicked)
    {
        if (managerClass == IntPtr.Zero)
        {
            var assemblies = Assemblies(IL2CPP.il2cpp_domain_get(), out var count);
            for (nuint index = 0; index < count && managerClass == IntPtr.Zero; index++)
            {
                var image = IL2CPP.il2cpp_assembly_get_image(assemblies[index]);
                managerClass = IL2CPP.il2cpp_class_from_name(image, "UI", "UIManager");
                if (managerClass == IntPtr.Zero) continue;
                rootClass = IL2CPP.il2cpp_class_from_name(image, "FairyGUI", "GRoot");
                graphClass = IL2CPP.il2cpp_class_from_name(image, "FairyGUI", "GGraph");
                textClass = IL2CPP.il2cpp_class_from_name(image, "FairyGUI", "GTextField");
            }
            if (managerClass == IntPtr.Zero) return;
        }

        var field = IL2CPP.il2cpp_class_get_field_from_name(managerClass, "_inst");
        if (field == IntPtr.Zero) throw new MissingFieldException("UIManager._inst");
        IntPtr manager = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(field, &manager);
        var panel = Call(manager, "get_currentPanel");
        DetectLanguage(panel);
        if (!ModeAvailable(sortMode.Value)) sortMode.Value = SortMode.Seat;
        if (ClassName(panel) == "HeroPanel")
        {
            observedPanel = IntPtr.Zero;
            previousCards = IntPtr.Zero;
            ObserveDetails(panel, clicked);
            return;
        }
        if (ClassName(panel) != "RoomHeroPanel")
        {
            scannedHeroNames.Clear();
            Detach();
            observedPanel = IntPtr.Zero;
            previousCards = IntPtr.Zero;
            return;
        }
        var root = Call(panel, "get_UIObject");
        if (root == IntPtr.Zero) return;
        var cards = ReadField(panel, "HeroCardDatas");
        var list = ReadField(root, "list_RendererSelectRole");
        var size = IntValue(Call(cards, "get_Count"));
        if (size <= 0 || size > 1000) return;
        var rows = new Dictionary<int, IntPtr>(size);
        for (var index = 0; index < size; index++)
        {
            var card = CardAt(cards, index);
            var id = IntValue(Call(card, "get_HeroId"));
            if (id <= 0 || !rows.TryAdd(id, card)) return;
        }
        if (panel != observedPanel || cards != previousCards || !originalIndex.Keys.ToHashSet().SetEquals(rows.Keys))
        {
            originalIndex = rows.Keys.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            previousCards = cards;
            observedPanel = panel;
            scannedHeroNames.Clear();
        }
        var tags = new List<(int, HeroTag)>();
        for (var index = 0; index < groups.Length; index++)
            foreach (var token in groups[index].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(token, out var id) && id > 0) tags.Add((id, (HeroTag)index));
        var settings = new HeroTags(tags);
        var priorities = SortPriorities();
        var originalOrder = rows.Keys.OrderBy(id => originalIndex[id]);
        var order = sortMode.Value switch
        {
            SortMode.Default => originalOrder.ToArray(),
            SortMode.Manual => ManualHeroOrder(originalOrder),
            _ => settings.Sort(originalOrder, priorities)
        };
        var virtualList = BoolValue(ReadField(list, "_virtual"));
        var listMatches = virtualList ? IntValue(Call(list, "get_numItems")) == size
            : IntValue(Call(list, "get_numChildren")) >= size;
        if (!order.SequenceEqual(rows.Keys) && listMatches)
        {
            try
            {
                if (virtualList) Reorder(cards, list, rows, order);
                else ReorderVisible(panel, cards, list, rows, order);
            }
            catch (Exception error)
            {
                if (lastError != error.Message) logger.LogWarning("Sort skipped: " + error.Message);
                lastError = error.Message;
                order = rows.Keys.ToArray();
            }
        }
        else if (!listMatches)
            order = rows.Keys.ToArray();
        var bounds = MeasureSelectionBounds(root);
        if (bounds.width <= 0 || bounds.height <= 0) return;
        if (activeRoot != root || activeList != list || SelectionBoundsChanged(bounds))
        {
            Detach();
            AddButtons(root, list, bounds);
            activeRoot = root;
            activeList = list;
        }
        selectionScreenWidth = Screen.width;
        selectionScreenHeight = Screen.height;
        UpdateSortLabel();
        try { UpdateHeroNames(list, order, virtualList); }
        catch (Exception error)
        {
            if (lastError != error.Message) logger.LogWarning("Character names: " + error.Message);
            lastError = error.Message;
        }
        try { UpdateCardStripes(list, order, settings.ColorSlots(order, priorities), virtualList); }
        catch (Exception error)
        {
            if (lastError != error.Message) logger.LogWarning("Card colors: " + error.Message);
            lastError = error.Message;
        }
        if (!clicked) return;
        if (!BoolValue(Call(container, "get_onStage"))) return;
        var mouse = Input.mousePosition;
        var position = new Vector2(mouse.x, Screen.height - mouse.y);
        void* point = &position;
        var boxedPoint = Exact(container, "GlobalToLocal", "UnityEngine.Vector2", &point);
        if (ClassName(boxedPoint) != "Vector2") return;
        var local = *(Vector2*)IL2CPP.il2cpp_object_unbox(boxedPoint);
        if (local.x < 0 || local.x >= buttonWidth * SelectionButtonCount) return;
        var button = (int)(local.x / buttonWidth);
        if (local.y < 0 || local.y >= buttonHeight) return;
        if (local.x - button * buttonWidth >= buttonWidth - selectionButtonGap) return;
        if (button == SortButtonIndex)
        {
            var modes = new[] { SortMode.Default, SortMode.Seat, SortMode.Role, SortMode.Custom, SortMode.Manual }
                .Where(ModeAvailable).ToArray();
            sortMode.Value = modes[(Array.IndexOf(modes, sortMode.Value) + 1) % modes.Length];
            UpdateSortLabel();
            nextPoll = 0;
            return;
        }
        var label = SelectionButtonLabel(button);

        var confirm = ReadField(root, "btn_SureHero");
        if (!BoolValue(Call(confirm, "get_onStage")) || !BoolValue(Call(confirm, "get_touchable")))
            return;
        var candidates = order.Where(id => IsAvailable(panel, rows[id], id)).ToArray();
        var picked = settings.Pick(button == RandomButtonIndex ? null : (HeroTag)button, candidates);
        if (picked.HasValue && (!listMatches ||
            !SelectRow(panel, list, Array.IndexOf(order, picked.Value), picked.Value, virtualList)))
            logger.LogWarning("No safe matching cell for ID " + picked.Value + " (" + label + ").");
    }

    private string SelectionButtonLabel(int index)
    {
        if (index < GroupNames.Length) return TagLabel(index);
        if (index == RandomButtonIndex) return Localized(4);
        return Localized(5) + " · " + Localized(6 + (int)sortMode.Value);
    }

    private string TagLabel(int index)
    {
        var label = tagLabels[index].Value.Trim();
        return label.Length == 0 || label == GroupNames[index]
            ? (index < 4 ? GroupNames[index] : Localized(index - 4)) : label;
    }

    private IReadOnlyList<HeroTag> SortPriorities()
    {
        if (sortMode.Value == SortMode.Seat) return SeatOrder;
        if (sortMode.Value == SortMode.Role) return RoleOrder;
        return customOrder.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name =>
            {
                var index = Array.FindIndex(TagKeys, key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) return index;
                index = Array.IndexOf(GroupNames, name);
                if (index >= 0) return index;
                index = Array.IndexOf(LegacySeatKeys, name);
                if (index >= 0) return index;
                for (var tag = 0; tag < GroupNames.Length; tag++)
                    if (TagLabel(tag) == name) return tag;
                return -1;
            })
            .Where(index => index >= 0)
            .Select(index => (HeroTag)index)
            .Concat(SeatOrder)
            .Distinct()
            .ToArray();
    }

    private void UpdateSortLabel()
    {
        if (sortLabel == IntPtr.Zero || displayedSortMode == sortMode.Value) return;
        void* title = (void*)IL2CPP.ManagedStringToIl2Cpp(SelectionButtonLabel(SortButtonIndex));
        Exact(sortLabel, "set_text", "System.String", &title);
        displayedSortMode = sortMode.Value;
    }

    internal void Detach()
    {
        ClearCardStripes();
        if (container == IntPtr.Zero) return;
        try { Call(container, "Dispose"); }
        catch (Exception error) { logger.LogWarning(error.Message); }
        finally
        {
            IL2CPP.il2cpp_gchandle_free(containerHandle);
            container = IntPtr.Zero;
            activeRoot = IntPtr.Zero;
            activeList = IntPtr.Zero;
            sortLabel = IntPtr.Zero;
            detailHero = 0;
            Array.Clear(detailGraphs);
        }
    }

    private static void ReorderVisible(IntPtr panel, IntPtr cards, IntPtr list,
        Dictionary<int, IntPtr> rows, int[] order)
    {
        var items = ReadField(panel, "allHeroItem");
        var count = IntValue(Call(items, "get_Count"));
        var children = IntValue(Call(list, "get_numChildren"));
        if (count < order.Length || children < order.Length)
            throw new InvalidOperationException($"Character roster has {order.Length} cards, {count} tracked cells, {children} list children.");
        var before = rows.Keys.ToArray();
        var controls = new Dictionary<int, IntPtr>(order.Length);
        for (var index = 0; index < before.Length; index++)
        {
            void* argument = &index;
            var child = Call(list, "GetChildAt", &argument);
            if (child != CardAt(items, index) || ClassName(child) != "UIRoomHero_Button_SelectHero_2")
                throw new InvalidOperationException("Character cell order differs at index " + index + ".");
            if (!MatchesHero(child, before[index]))
                throw new InvalidOperationException("Character cell data differs at index " + index + ".");
            controls.Add(before[index], child);
        }
        var cardSetter = RequireMethod(cards, "set_Item", "System.Int32,GameLogic.HeroCardData");
        var itemSetter = RequireMethod(items, "set_Item", "System.Int32,UI.UIRoomHero_Button_SelectHero_2");
        var move = RequireMethod(list, "SetChildIndex", "FairyGUI.GObject,System.Int32");
        var handles = before.SelectMany(id => new[] { rows[id], controls[id] })
            .Select(value => IL2CPP.il2cpp_gchandle_new(value, false)).ToArray();
        try
        {
            WriteOrder(cards, cardSetter, rows, order);
            WriteOrder(items, itemSetter, controls, order);
            MoveChildren(list, move, controls, order);
        }
        catch
        {
            WriteOrder(cards, cardSetter, rows, before);
            WriteOrder(items, itemSetter, controls, before);
            MoveChildren(list, move, controls, before);
            throw;
        }
        finally
        {
            foreach (var handle in handles) IL2CPP.il2cpp_gchandle_free(handle);
        }
    }

    private static IntPtr RequireMethod(IntPtr target, string name, string signature)
    {
        var method = FindMethod(IL2CPP.il2cpp_object_get_class(target), name, signature);
        return method == IntPtr.Zero ? throw new MissingMethodException(ClassName(target), name) : method;
    }

    private static void MoveChildren(IntPtr list, IntPtr method, Dictionary<int, IntPtr> controls, int[] order)
    {
        void** args = stackalloc void*[2];
        for (var index = 0; index < order.Length; index++)
        {
            args[0] = (void*)controls[order[index]];
            args[1] = &index;
            Invoke(method, list, args);
        }
    }

    private static void Reorder(IntPtr cards, IntPtr list, Dictionary<int, IntPtr> rows, int[] order)
    {
        var type = IL2CPP.il2cpp_object_get_class(cards);
        var setter = FindMethod(type, "set_Item", "System.Int32,GameLogic.HeroCardData");
        if (setter == IntPtr.Zero) throw new MissingMethodException("HeroCardDatas.set_Item");
        var refresh = FindMethod(IL2CPP.il2cpp_object_get_class(list), "RefreshVirtualList", "");
        if (refresh == IntPtr.Zero) throw new MissingMethodException("GList.RefreshVirtualList");
        var before = rows.Keys.ToArray();
        var handles = before.Select(id => IL2CPP.il2cpp_gchandle_new(rows[id], false)).ToArray();
        try
        {
            WriteOrder(cards, setter, rows, order);
            Invoke(refresh, list, null);
        }
        catch
        {
            WriteOrder(cards, setter, rows, before);
            Invoke(refresh, list, null);
            throw;
        }
        finally
        {
            foreach (var handle in handles) IL2CPP.il2cpp_gchandle_free(handle);
        }
    }

    private static void WriteOrder(IntPtr cards, IntPtr setter, Dictionary<int, IntPtr> rows, int[] order)
    {
        void** args = stackalloc void*[2];
        for (var index = 0; index < order.Length; index++)
        {
            args[0] = &index;
            args[1] = (void*)rows[order[index]];
            Invoke(setter, cards, args);
        }
    }

    private static bool IsAvailable(IntPtr panel, IntPtr card, int id)
    {
        if (!BoolValue(ReadField(card, "_IsHas"))) return false;
        var banned = ReadField(panel, "banCharacterIds");
        void* argument = &id;
        return (banned == IntPtr.Zero || !BoolValue(Call(banned, "Contains", &argument)))
            && !BoolValue(Call(panel, "GetHeroHasChoice", &argument));
    }

    private static bool SelectRow(IntPtr panel, IntPtr list, int index, int heroId, bool virtualList)
    {
        if (index < 0) return false;
        if (virtualList)
        {
            byte animate = 0;
            byte first = 0;
            void** scroll = stackalloc void*[3] { &index, &animate, &first };
            Exact(list, "ScrollToView", "System.Int32,System.Boolean,System.Boolean", scroll);
            void* item = &index;
            index = IntValue(Call(list, "ItemIndexToChildIndex", &item));
        }
        if (index < 0 || index >= IntValue(Call(list, "get_numChildren"))) return false;
        void* child = &index;
        var row = Call(list, "GetChildAt", &child);
        if (!virtualList)
        {
            var items = ReadField(panel, "allHeroItem");
            if (index >= IntValue(Call(items, "get_Count")) || row != CardAt(items, index)) return false;
        }
        if (ClassName(row) != "UIRoomHero_Button_SelectHero_2" ||
            !BoolValue(Call(row, "get_visible")) || !BoolValue(Call(row, "get_touchable"))) return false;
        if (!MatchesHero(row, heroId)) return false;
        var listener = Call(row, "get_onClick");
        Call(listener, "Call");
        return listener != IntPtr.Zero;
    }

    private static bool MatchesHero(IntPtr row, int heroId)
    {
        var data = Call(row, "get_data", optional: true);
        return ClassName(data) != "HeroCardData" || IntValue(Call(data, "get_HeroId")) == heroId;
    }

    private bool SelectionBoundsChanged(Rect bounds) =>
        Math.Abs(bounds.x - selectionBounds.x) > 0.25f ||
        Math.Abs(bounds.y - selectionBounds.y) > 0.25f ||
        Math.Abs(bounds.width - selectionBounds.width) > 0.25f ||
        Math.Abs(bounds.height - selectionBounds.height) > 0.25f;

    private static Rect MeasureSelectionBounds(IntPtr root)
    {
        const float halfRowWidth = 0.257f;
        var width = Screen.width;
        var height = Screen.height;
        const float upwardPixels = 5f;
        var start = TransformPoint(root, "GlobalToLocal",
            new Vector2(width * (0.5f - halfRowWidth), height * 0.675f - upwardPixels));
        var end = TransformPoint(root, "GlobalToLocal",
            new Vector2(width * (0.5f + halfRowWidth), height * 0.702f - upwardPixels));
        return Rect.MinMaxRect(start.x, start.y, end.x, end.y);
    }

    private void AddButtons(IntPtr root, IntPtr list, Rect bounds)
    {
        if (rootClass == IntPtr.Zero || graphClass == IntPtr.Zero || textClass == IntPtr.Zero)
            throw new TypeLoadException("FairyGUI controls unavailable.");
        var component = IL2CPP.il2cpp_class_get_parent(IL2CPP.il2cpp_object_get_class(list));
        if (Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(component)) != "GComponent")
            throw new InvalidOperationException("Unexpected FairyGUI list base.");
        var host = Create(component);
        var handle = IL2CPP.il2cpp_gchandle_new(host, false);
        try
        {
            var width = bounds.width / SelectionButtonCount;
            buttonWidth = width;
            var height = bounds.height;
            buttonHeight = height;
            FloatPair(host, "SetSize", width * SelectionButtonCount, height);
            selectionButtonGap = Math.Min(width * 0.10f, height * 0.22f);
            FloatPair(host, "SetXY", bounds.x + selectionButtonGap / 2, bounds.y);
            var faceWidth = width - selectionButtonGap;
            var lineSize = height * 0.055f;
            var line = Color.white;
            var fill = Color.black;
            var radius = Math.Min(faceWidth / 4, height * 0.26f);
            void** draw = stackalloc void*[7] { &lineSize, &line, &fill, &radius, &radius, &radius, &radius };
            for (var index = 0; index < SelectionButtonCount; index++)
            {
                (fill, line) = index == RandomButtonIndex
                    ? (new Color(0.58f, 0.06f, 0.41f), new Color(1f, 0.59f, 0.83f))
                    : index == SortButtonIndex
                        ? (new Color(0.24f, 0.32f, 0.13f), new Color(0.71f, 0.92f, 0.43f))
                        : (new Color(0.15f, 0.13f, 0.25f), new Color(0.76f, 0.69f, 0.93f));
                var graph = Create(graphClass);
                AddChild(host, graph);
                FloatPair(graph, "SetSize", faceWidth, height);
                FloatPair(graph, "SetXY", index * width, 0);
                Exact(Call(graph, "get_shape"), "DrawRoundRect",
                    "System.Single,UnityEngine.Color,UnityEngine.Color,System.Single,System.Single,System.Single,System.Single", draw);

                var label = Create(textClass);
                AddChild(host, label);
                CenterLabel(label);
                FloatPair(label, "SetSize", faceWidth, height);
                FloatPair(label, "SetXY", index * width, 0);
                byte touchable = 0;
                void* touchArg = &touchable;
                Exact(label, "set_touchable", "System.Boolean", &touchArg);
                var format = Call(label, "get_textFormat");
                var titleText = SelectionButtonLabel(index);
                var measuredUnits = Math.Max(1f, titleText.Sum(character => character < 128 ? 0.62f : 1f));
                var fontSize = Math.Min(height * (index == SortButtonIndex ? 0.55f : 0.66f),
                    faceWidth * 0.92f / measuredUnits);
                SetIntField(format, "size", Math.Max(1, (int)fontSize));
                var colorField = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(format), "color");
                if (colorField != IntPtr.Zero && TypeName(IL2CPP.il2cpp_field_get_type(colorField)) == "UnityEngine.Color")
                {
                    var white = Color.white;
                    IL2CPP.il2cpp_field_set_value(format, colorField, &white);
                }
                void* style = (void*)format;
                Exact(label, "set_textFormat", "FairyGUI.TextFormat", &style);
                void* title = (void*)IL2CPP.ManagedStringToIl2Cpp(titleText);
                Exact(label, "set_text", "System.String", &title);
                if (index == SortButtonIndex)
                {
                    sortLabel = label;
                    displayedSortMode = sortMode.Value;
                }
            }
            AddChild(root, host);
            container = host;
            containerHandle = handle;
            selectionBounds = bounds;
        }
        finally
        {
            if (container != host) IL2CPP.il2cpp_gchandle_free(handle);
        }
    }

    private static void SetIntField(IntPtr target, string name, int value)
    {
        if (target == IntPtr.Zero) return;
        var field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(target), name);
        if (field != IntPtr.Zero && IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_field_get_type(field)) == 8)
            IL2CPP.il2cpp_field_set_value(target, field, &value);
    }

    private static void AddChild(IntPtr parent, IntPtr child)
    {
        void* argument = (void*)child;
        Exact(parent, "AddChild", "FairyGUI.GObject", &argument);
    }

    private static IntPtr Create(IntPtr type)
    {
        var constructor = FindMethod(type, ".ctor", "");
        if (constructor == IntPtr.Zero) throw new MissingMethodException("FairyGUI constructor");
        var instance = IL2CPP.il2cpp_object_new(type);
        Invoke(constructor, instance, null);
        return instance;
    }

    private static void FloatPair(IntPtr target, string name, float first, float second)
    {
        void** arguments = stackalloc void*[2] { &first, &second };
        Exact(target, name, "System.Single,System.Single", arguments);
    }

    private static bool BoolValue(IntPtr value) => ClassName(value) == "Boolean"
        ? Marshal.ReadByte(IL2CPP.il2cpp_object_unbox(value)) != 0
        : throw new InvalidOperationException("Expected Boolean from FairyGUI.");

    private static float FloatValue(IntPtr value) => ClassName(value) == "Single"
        ? *(float*)IL2CPP.il2cpp_object_unbox(value)
        : throw new InvalidOperationException("Expected Single from FairyGUI.");

    private static IntPtr Exact(IntPtr value, string name, string signature, void** args)
    {
        if (value == IntPtr.Zero) throw new InvalidOperationException(name + " target unavailable.");
        var method = FindMethod(IL2CPP.il2cpp_object_get_class(value), name, signature);
        if (method == IntPtr.Zero) throw new MissingMethodException(ClassName(value), name + "(" + signature + ")");
        return Invoke(method, value, args);
    }

    private static readonly Dictionary<(IntPtr, string, string), IntPtr> MethodCache = new();

    private static IntPtr FindMethod(IntPtr type, string name, string signature)
    {
        var key = (type, name, signature);
        if (MethodCache.TryGetValue(key, out var cached)) return cached;
        var args = signature.Length == 0 ? Array.Empty<string>() : signature.Split(',');
        for (; type != IntPtr.Zero; type = IL2CPP.il2cpp_class_get_parent(type))
        {
            var iterator = IntPtr.Zero;
            IntPtr candidate;
            while ((candidate = IL2CPP.il2cpp_class_get_methods(type, ref iterator)) != IntPtr.Zero)
            {
                if (IL2CPP.il2cpp_method_get_param_count(candidate) != args.Length ||
                    Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(candidate)) != name) continue;
                var index = 0;
                for (; index < args.Length; index++)
                    if (TypeName(IL2CPP.il2cpp_method_get_param(candidate, (uint)index)) != args[index]) break;
                if (index != args.Length) continue;
                MethodCache[key] = candidate;
                return candidate;
            }
        }
        return IntPtr.Zero;
    }

    private static unsafe IntPtr CardAt(IntPtr cards, int index)
    {
        void* argument = &index;
        return Call(cards, "get_Item", &argument);
    }

    private static int IntValue(IntPtr boxed)
    {
        if (boxed == IntPtr.Zero || ClassName(boxed) != "Int32")
            throw new InvalidOperationException("Expected an Int32 from the selection screen.");
        return Marshal.ReadInt32(IL2CPP.il2cpp_object_unbox(boxed));
    }

    private static IntPtr ReadField(IntPtr instance, string name, bool optional = false)
    {
        var field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(instance), name);
        if (field != IntPtr.Zero) return IL2CPP.il2cpp_field_get_value_object(field, instance);
        if (optional) return IntPtr.Zero;
        throw new MissingFieldException(ClassName(instance), name);
    }

    private static string TypeName(IntPtr type)
    {
        var name = IL2CPP.il2cpp_type_get_name(type);
        try { return Marshal.PtrToStringAnsi(name) ?? "?"; }
        finally { IL2CPP.il2cpp_free(name); }
    }

    private static string? ClassName(IntPtr instance) => instance == IntPtr.Zero ? null :
        Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(IL2CPP.il2cpp_object_get_class(instance)));

    private static unsafe IntPtr Call(IntPtr instance, string name, void** arguments = null, bool optional = false)
    {
        if (instance == IntPtr.Zero) return IntPtr.Zero;
        var type = IL2CPP.il2cpp_object_get_class(instance);
        var method = IntPtr.Zero;
        for (; type != IntPtr.Zero && method == IntPtr.Zero; type = IL2CPP.il2cpp_class_get_parent(type))
        {
            var iterator = IntPtr.Zero;
            IntPtr candidate;
            while ((candidate = IL2CPP.il2cpp_class_get_methods(type, ref iterator)) != IntPtr.Zero)
            {
                if (IL2CPP.il2cpp_method_get_param_count(candidate) != (arguments == null ? 0 : 1)
                    || Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(candidate)) != name)
                    continue;
                if (arguments != null && IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_method_get_param(candidate, 0)) != 8)
                    continue;
                method = candidate;
                break;
            }
        }
        if (method == IntPtr.Zero)
        {
            if (optional) return IntPtr.Zero;
            throw new MissingMethodException(ClassName(instance), name);
        }
        return Invoke(method, instance, arguments);
    }

    private static IntPtr Invoke(IntPtr method, IntPtr instance, void** arguments)
    {
        if (method == IntPtr.Zero) throw new MissingMethodException("Required game method unavailable.");
        IntPtr error = IntPtr.Zero;
        var result = IL2CPP.il2cpp_runtime_invoke(method, instance, arguments, ref error);
        Il2CppException.RaiseExceptionIfNecessary(error);
        return result;
    }
}
