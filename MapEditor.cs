using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private const int MapColumns = 10;
    private const int MapGridRows = 4;
    private const int MapPageSize = MapColumns * MapGridRows;
    private int editorMapId;
    private int mapPage;
    private bool mapEditorOpen;
    private IntPtr mapRows;
    private IntPtr mapHoverLabel;
    private int mapHoveredHero;
    private int[] mapPageHeroes = Array.Empty<int>();
    private float mapTileWidth;
    private float mapTileHeight;
    private float mapGridWidth;
    private const float MapGridHeader = 30f;
    private const float MapGridPadding = 8f;

    private const string MapPreferredDescription = "추천할 캐릭터 ID를 우선순위대로 쉼표로 구분합니다.";
    private const string MapExcludedDescription = "조건 선택에서 제외할 캐릭터 ID를 쉼표로 구분합니다.";

    private (ConfigEntry<string> Preferred, ConfigEntry<string> Excluded) BindMapRules(int id)
    {
        var section = "Map " + id;
        var (preferred, excluded) = id switch
        {
            82007 => ("122, 118, 120", ""),
            82008 => ("122, 118, 120", "107"),
            82010 => ("122, 118", "110"),
            82012 => ("118, 122, 104", "107"),
            82013 => ("119, 118, 110", ""),
            82014 => ("104, 122, 304, 124, 126, 110", ""),
            82015 => ("112, 123, 304, 110, 101, 120", ""),
            82016 => ("306, 305, 102, 122", ""),
            82017 => ("117, 119, 120, 122, 306", "107"),
            83003 => ("112", ""),
            _ => ("", "")
        };
        return (settingsFile.Bind(section, "Preferred", preferred, MapPreferredDescription),
            settingsFile.Bind(section, "Excluded", excluded, MapExcludedDescription));
    }

    private bool ObserveMapEditor(bool clicked)
    {
        if (rootClass == IntPtr.Zero) return false;
        var getter = FindMethod(rootClass, "get_inst", "");
        if (getter == IntPtr.Zero) return false;
        var root = Invoke(getter, IntPtr.Zero, null);
        if (root == IntPtr.Zero) return false;
        var count = IntValue(Call(root, "get_numChildren"));
        for (var index = count - 1; index >= 0; index--)
        {
            var window = ChildAt(root, index);
            if (ClassName(window) != "NewGameLibraryWindow") continue;
            var pane = Call(window, "get_contentPane", optional: true);
            if (pane == IntPtr.Zero) return false;
            var map = ReadField(pane, "com_Map", optional: true);
            if (map == IntPtr.Zero || !BoolValue(Call(map, "get_visible"))) return false;
            var detail = ReadField(map, "Com_main", optional: true);
            if (detail == IntPtr.Zero || !BoolValue(Call(detail, "get_visible")) ||
                !BoolValue(Call(detail, "get_onStage"))) return false;
            var identity = ReadLibraryMap(detail);
            if (!identity.HasValue) return false;
            UpdateMapEditor(pane, detail, identity.Value.Id, identity.Value.Name, clicked);
            return true;
        }
        return false;
    }

    private void UpdateMapEditor(IntPtr pane, IntPtr detail, int mapId, string mapName, bool clicked)
    {
        const float width = 230f;
        if (activeRoot != detail || editorMapId != mapId)
        {
            Detach();
            var type = IL2CPP.il2cpp_object_get_class(detail);
            while (type != IntPtr.Zero && ClassNameOfComponent(type) != "GComponent")
                type = IL2CPP.il2cpp_class_get_parent(type);
            if (type == IntPtr.Zero) return;
            var host = Create(type);
            var handle = IL2CPP.il2cpp_gchandle_new(host, false);
            try
            {
                FloatPair(host, "SetSize", width, TabStep);
                var position = PositionIn(pane, detail);
                var paneWidth = FloatValue(Call(pane, "get_width"));
                FloatPair(host, "SetXY", Math.Clamp(position.x + FloatValue(Call(detail, "get_width")) - width - 16,
                    0, Math.Max(0, paneWidth - width)), Math.Max(0, position.y + 12f));
                AddDetailButton(host, IntPtr.Zero, width, 0, $"{MapEditorLabel()} · {mapName}", false, symmetric: true);
                AddChild(pane, host);
                container = host;
                containerHandle = handle;
            }
            finally { if (container != host) IL2CPP.il2cpp_gchandle_free(handle); }
            activeRoot = detail;
            editorMapId = mapId;
            mapRows = IntPtr.Zero;
            mapEditorOpen = false;
            mapPage = 0;
        }
        if (mapEditorOpen && PollMapPortraits()) PaintMapRows();
        if (mapEditorOpen && mapRows != IntPtr.Zero) UpdateMapHover();
        if (!clicked) return;
        var mouse = Input.mousePosition;
        var point = TransformPoint(container, "GlobalToLocal", new Vector2(mouse.x, Screen.height - mouse.y));
        if (point.x >= 0 && point.x < width && point.y >= 0 && point.y < TabHeight)
        {
            mapEditorOpen = !mapEditorOpen;
            PaintMapRows();
            return;
        }
        if (!mapEditorOpen || mapRows == IntPtr.Zero) return;
        var grid = TransformPoint(mapRows, "GlobalToLocal", new Vector2(mouse.x, Screen.height - mouse.y));
        if (grid.x < 0 || grid.x >= mapGridWidth || grid.y < 0) return;
        if (grid.y < MapGridHeader)
        {
            if (grid.x >= mapGridWidth - 44)
            {
                mapEditorOpen = false;
                PaintMapRows();
                return;
            }
            if (grid.x < mapGridWidth - 120) return;
            var pages = Math.Max(1, (MapEditorHeroIds().Length + MapPageSize - 1) / MapPageSize);
            mapPage = (mapPage + 1) % pages;
            PaintMapRows();
            return;
        }
        var tileX = grid.x - MapGridPadding;
        var tileY = grid.y - MapGridHeader;
        if (tileX < 0 || tileX >= mapTileWidth * MapColumns ||
            tileY < 0 || tileY >= mapTileHeight * MapGridRows) return;
        if (tileX % mapTileWidth >= mapTileWidth - 4 || tileY % mapTileHeight >= mapTileHeight - 4) return;
        var tile = (int)(tileY / mapTileHeight) * MapColumns + (int)(tileX / mapTileWidth);
        if (tile >= mapPageHeroes.Length) return;
        var hero = mapPageHeroes[tile];
        var (preferred, excluded) = BindMapRules(mapId);
        var priorities = ParseMapHeroIds(preferred.Value).ToList();
        var exclusions = ParseMapHeroIds(excluded.Value).ToList();
        if (exclusions.Remove(hero)) { }
        else if (priorities.Remove(hero)) exclusions.Add(hero);
        else priorities.Add(hero);
        preferred.Value = string.Join(", ", priorities);
        excluded.Value = string.Join(", ", exclusions);
        logger.LogDebug($"Map rules saved for map {mapId}.");
        PaintMapRows();
    }

    private static readonly Dictionary<string, int> LibraryMapNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["유원지"] = 81003, ["교차로 역"] = 81004, ["사쿠라 타운"] = 81005,
        ["팬텀 시티"] = 81006, ["드림호 - 위층"] = 81007, ["정글 어드벤처"] = 81011,
        ["아스트랄 드림호"] = 82007, ["영혼 축제"] = 82008, ["선물 광장"] = 82009,
        ["워터 타운"] = 82010, ["마법 학교"] = 82012, ["용궁 유원지"] = 82013,
        ["유령 골목"] = 82014, ["원림 중정"] = 82015, ["이변 도서관"] = 82016,
        ["가을 환상 숲"] = 82017, ["예선 운동장"] = 83001, ["본선 운동장"] = 83002,
        ["결승 경기장"] = 83003, ["Autumnwood Mirage"] = 82017, ["Anomalous Library"] = 82016,
        ["秋幻の森"] = 82017, ["龍星の中庭"] = 82015
    };

    private static (int Id, string Name)? ReadLibraryMap(IntPtr detail)
    {
        var labels = VisibleDetailNodes(detail)
            .Select(node => TextValue(Call(node, "get_text", optional: true)))
            .Where(text => text.Length != 0).ToArray();
        foreach (var label in labels)
            if (LibraryMapNames.TryGetValue(label, out var knownId)) return (knownId, label);

        var value = Call(detail, "get_MapId", optional: true);
        if (value == IntPtr.Zero) value = ReadField(detail, "_mapId", optional: true);
        if (ClassName(value) != "Int32" || IntValue(value) <= 0) return null;

        var name = TextValue(Call(detail, "get_MapName", optional: true));
        if (name.Length == 0)
            name = LibraryMapNames.FirstOrDefault(pair => pair.Value == IntValue(value)).Key ?? "";
        return name.Length == 0 ? null : (IntValue(value), name);
    }

    private int[] MapEditorHeroIds()
    {
        IEnumerable<int> ids = DetailNames.Values;
        if (RosterEntries is { } entries)
            ids = ids.Concat(entries.Keys.Where(key => key.Section == RosterSection)
                .Select(key => int.TryParse(key.Key, out var id) ? id : 0));
        return ids.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
    }

    private void PaintMapRows()
    {
        if (mapRows != IntPtr.Zero) Call(mapRows, "Dispose");
        mapRows = IntPtr.Zero;
        mapHoverLabel = IntPtr.Zero;
        mapHoveredHero = 0;
        if (!mapEditorOpen) return;
        var start = TransformPoint(container, "GlobalToLocal", new Vector2(Screen.width * 0.411f, Screen.height * 0.568f));
        var end = TransformPoint(container, "GlobalToLocal", new Vector2(Screen.width * 0.885f, Screen.height * 0.939f));
        mapGridWidth = end.x - start.x;
        var height = end.y - start.y;
        if (mapGridWidth <= 100 || height <= MapGridHeader) return;
        mapTileWidth = (mapGridWidth - 2 * MapGridPadding) / MapColumns;
        mapTileHeight = (height - MapGridHeader - MapGridPadding) / MapGridRows;
        var ids = MapEditorHeroIds();
        RequestMapPortraits(ids);
        mapPageHeroes = ids.Skip(mapPage * MapPageSize).Take(MapPageSize).ToArray();
        var rows = Create(IL2CPP.il2cpp_object_get_class(container));
        AddChild(container, rows);
        mapRows = rows;
        FloatPair(rows, "SetXY", start.x, start.y);
        FloatPair(rows, "SetSize", mapGridWidth, height);
        AddMapTileShape(rows, 0, 0, mapGridWidth, height,
            new Color(0.98f, 0.96f, 0.91f), 2f, new Color(0.38f, 0.19f, 0.47f), radius: 12f);
        mapHoverLabel = AddMapBadgeText(rows, "", MapGridPadding, 2, 17, Color.black);
        FloatPair(mapHoverLabel, "SetSize", Math.Max(0, mapGridWidth - 136), 26);
        var (preferred, excluded) = BindMapRules(editorMapId);
        var priorities = ParseMapHeroIds(preferred.Value);
        var exclusions = ParseMapHeroIds(excluded.Value);
        for (var index = 0; index < mapPageHeroes.Length; index++)
        {
            var id = mapPageHeroes[index];
            var rank = Array.IndexOf(priorities, id);
            var isExcluded = exclusions.Contains(id);
            var x = MapGridPadding + (index % MapColumns) * mapTileWidth;
            var y = MapGridHeader + (index / MapColumns) * mapTileHeight;
            var name = CombinationHeroName(id);
            var open = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (open >= 0 && name.EndsWith(")", StringComparison.Ordinal)) name = name[(open + 2)..^1];
            var tileWidth = mapTileWidth - 4;
            var tileHeight = mapTileHeight - 4;
            var hasPortrait = mapPortraits.ContainsKey(id);
            AddDetailButton(rows, IntPtr.Zero, tileWidth, y, hasPortrait ? "" : name,
                rank >= 0, isExcluded, x, tileHeight, symmetric: true);
            AddMapPortrait(rows, id, x, y, tileWidth, tileHeight, isExcluded);
            if (isExcluded) AddMapDisabledOverlay(rows, x, y, tileWidth, tileHeight);
            AddMapTileShape(rows, x, y, tileWidth, tileHeight, Color.clear, rank >= 0 ? 3f : 2f,
                rank >= 0 ? new Color(0.60f, 0.92f, 0.12f) : new Color(0.13f, 0.15f, 0.20f));
            if (rank >= 0)
            {
                var badgeX = x + tileWidth - 28;
                var badgeY = y - 4;
                var outline = new Color(0.20f, 0.12f, 0.07f);
                AddMapBadgeText(rows, "★", badgeX - 1, badgeY, 27, outline);
                AddMapBadgeText(rows, "★", badgeX + 1, badgeY, 27, outline);
                AddMapBadgeText(rows, "★", badgeX, badgeY - 1, 27, outline);
                AddMapBadgeText(rows, "★", badgeX, badgeY + 1, 27, outline);
                AddMapBadgeText(rows, "★", badgeX, badgeY, 27, new Color(1f, 0.78f, 0.17f));
                AddMapBadgeText(rows, (rank + 1).ToString(), badgeX + 1, badgeY + 7, 11, Color.black);
            }
        }
        var pageCount = Math.Max(1, (ids.Length + MapPageSize - 1) / MapPageSize);
        if (pageCount > 1)
            AddDetailButton(rows, IntPtr.Zero, 66, 3, $"{mapPage + 1}/{pageCount} ›",
                false, false, mapGridWidth - 116, 24, symmetric: true);
        AddDetailButton(rows, IntPtr.Zero, 36, 3, "×", false, false, mapGridWidth - 42, 24, symmetric: true);
    }

    private void UpdateMapHover()
    {
        if (mapHoverLabel == IntPtr.Zero) return;
        var mouse = Input.mousePosition;
        var point = TransformPoint(mapRows, "GlobalToLocal", new Vector2(mouse.x, Screen.height - mouse.y));
        var x = point.x - MapGridPadding;
        var y = point.y - MapGridHeader;
        var id = 0;
        if (x >= 0 && x < mapTileWidth * MapColumns && y >= 0 && y < mapTileHeight * MapGridRows &&
            x % mapTileWidth < mapTileWidth - 4 && y % mapTileHeight < mapTileHeight - 4)
        {
            var index = (int)(y / mapTileHeight) * MapColumns + (int)(x / mapTileWidth);
            if (index < mapPageHeroes.Length) id = mapPageHeroes[index];
        }
        if (mapHoveredHero == id) return;
        mapHoveredHero = id;
        void* text = (void*)IL2CPP.ManagedStringToIl2Cpp(id == 0 ? "" : CombinationHeroName(id));
        Exact(mapHoverLabel, "set_text", "System.String", &text);
    }

    private void AddMapDisabledOverlay(IntPtr host, float x, float y, float width, float height)
        => AddMapTileShape(host, x, y, width, height, new Color(0.20f, 0.20f, 0.20f, 0.52f), 0f, Color.clear);

    private IntPtr AddMapTileShape(IntPtr host, float x, float y, float width, float height,
        Color fill, float lineSize, Color line, float radius = 9f)
    {
        var overlay = Create(graphClass);
        AddChild(host, overlay);
        FloatPair(overlay, "SetSize", width, height);
        FloatPair(overlay, "SetXY", x, y);
        void** draw = stackalloc void*[7] { &lineSize, &line, &fill, &radius, &radius, &radius, &radius };
        Exact(Call(overlay, "get_shape"), "DrawRoundRect",
            "System.Single,UnityEngine.Color,UnityEngine.Color,System.Single,System.Single,System.Single,System.Single", draw);
        return overlay;
    }

    private IntPtr FindFairyType(string name)
    {
        var assemblies = Assemblies(IL2CPP.il2cpp_domain_get(), out var count);
        for (nuint index = 0; index < count; index++)
        {
            var type = IL2CPP.il2cpp_class_from_name(IL2CPP.il2cpp_assembly_get_image(assemblies[index]), "FairyGUI", name);
            if (type != IntPtr.Zero) return type;
        }
        return IntPtr.Zero;
    }

    private void AddMapPortrait(IntPtr host, int id, float x, float y, float width, float height, bool isExcluded)
    {
        if (!mapPortraits.TryGetValue(id, out var image)) return;
        if (mapPortraitLoaderClass == IntPtr.Zero) return;
        var loader = Create(mapPortraitLoaderClass);
        AddChild(host, loader);
        FloatPair(loader, "SetSize", width, height);
        FloatPair(loader, "SetXY", x, y);
        var fill = 4;
        void* mode = &fill;
        Exact(loader, "set_fill", "FairyGUI.FillType", &mode);
        byte touchable = 0;
        void* touch = &touchable;
        Exact(loader, "set_touchable", "System.Boolean", &touch);
        void* texture = (void*)image.Texture;
        Exact(loader, "set_texture", "FairyGUI.NTexture", &texture);
        if (isExcluded)
        {
            byte grayed = 1;
            void* argument = &grayed;
            Exact(loader, "set_grayed", "System.Boolean", &argument);
        }
    }

    private IntPtr AddMapBadgeText(IntPtr host, string text, float x, float y, int size, Color color)
    {
        var label = Create(textClass);
        AddChild(host, label);
        CenterLabel(label);
        FloatPair(label, "SetSize", 27, 27);
        FloatPair(label, "SetXY", x, y);
        var format = Call(label, "get_textFormat");
        SetIntField(format, "size", size);
        var field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(format), "color");
        if (field != IntPtr.Zero) IL2CPP.il2cpp_field_set_value(format, field, &color);
        void* style = (void*)format;
        Exact(label, "set_textFormat", "FairyGUI.TextFormat", &style);
        void* title = (void*)IL2CPP.ManagedStringToIl2Cpp(text);
        Exact(label, "set_text", "System.String", &title);
        return label;
    }

    private string MapEditorLabel() => detectedLanguage switch
    {
        UiLanguage.Korean => "맵 추천 설정",
        UiLanguage.Japanese => "マップ推薦設定",
        UiLanguage.SimplifiedChinese => "地图推荐设置",
        UiLanguage.TraditionalChinese => "地圖推薦設定",
        _ => "Map Recommendations"
    };

    private static string? ClassNameOfComponent(IntPtr type) =>
        System.Runtime.InteropServices.Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(type));
}
