using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private const float CombinationWidth = 300f;
    private IntPtr combinationEditor;
    private readonly List<Action> combinationButtons = new();
    private const int CombinationPageSize = 8;
    private int combinationPage;
    private bool editingAllCombinations;
    private bool editingEmergencySettings;
    private int editIndex = -1;
    private List<int>? draftCombination;
    private readonly ConfigEntry<string> combinationNames;
    private Dictionary<string, string> defaultCombinationNames = new();
    private readonly ConfigEntry<string> fixedSeatCombinations;
    private string draftName = "";
    private IntPtr combinationNameInput;
    private const float DraftCell = 54f;
    private const float DraftWidth = MapColumns * DraftCell + 12f;
    private int[] draftPageHeroes = Array.Empty<int>();
    private float draftGridTop;
    private float combinationEditorWidth = CombinationWidth;

    private IEnumerable<int[]> ReadCombinations() => combinationOrder.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(entry => entry.Split('+').Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToArray())
        .Where(ids => ids.Length is >= 2 and <= 4);

    private static string CombinationKey(int[] ids) => string.Join("+", ids.OrderBy(id => id));

    private HashSet<string> DisabledSets() => disabledCombinations.Value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    private HashSet<string> FixedSeatSets() => fixedSeatCombinations.Value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    private void SaveFixedSeatSets(HashSet<string> sets) =>
        fixedSeatCombinations.Value = string.Join(";", sets.OrderBy(value => value, StringComparer.Ordinal));

    private string ConditionSettingsLabel() => detectedLanguage switch
    {
        UiLanguage.Korean => "개별 설정",
        UiLanguage.Japanese => "個別設定",
        UiLanguage.SimplifiedChinese => "个别设置",
        UiLanguage.TraditionalChinese => "個別設定",
        _ => "Hero Settings"
    };

    private string ConditionEditLabel() => detectedLanguage switch
    {
        UiLanguage.Korean => "전체 설정",
        UiLanguage.Japanese => "全体設定",
        UiLanguage.SimplifiedChinese => "全局设置",
        UiLanguage.TraditionalChinese => "全域設定",
        _ => "Global Settings"
    };

    private void CloseCombinationEditor()
    {
        if (combinationEditor != IntPtr.Zero) Call(combinationEditor, "Dispose");
        combinationEditor = IntPtr.Zero;
        combinationNameInput = IntPtr.Zero;
        combinationButtons.Clear();
    }

    private bool HandleCombinationClick(Vector2 local, IntPtr root, IntPtr nameLabel, int hero)
    {
        if (local.x >= 0 && local.x < TabWidth &&
            ((local.y >= 0 && local.y < TabHeight) || (local.y >= TabStep && local.y < TabStep + TabHeight)))
        {
            var edit = local.y < TabStep;
            if (combinationEditor != IntPtr.Zero && edit == editingAllCombinations) CloseCombinationEditor();
            else OpenCombinationEditor(root, nameLabel, hero, edit);
            return true;
        }
        if (combinationEditor == IntPtr.Zero) return false;
        var mouse = Input.mousePosition;
        var point = TransformPoint(combinationEditor, "GlobalToLocal", new Vector2(mouse.x, Screen.height - mouse.y));
        if (point.x < 0 || point.x >= combinationEditorWidth || point.y < 0 ||
            point.y >= FloatValue(Call(combinationEditor, "get_height")))
        {
            CloseCombinationEditor();
            return false;
        }
        if (combinationNameInput != IntPtr.Zero)
            draftName = TextValue(Call(combinationNameInput, "get_text"));
        if (draftCombination is { } draft && point.y >= draftGridTop)
        {
            var gridX = point.x - 6;
            var gridY = point.y - draftGridTop;
            if (gridX < 0 || gridX >= MapColumns * DraftCell ||
                gridX % DraftCell >= DraftCell - 4 || gridY % DraftCell >= DraftCell - 4) return true;
            var tile = (int)(gridY / DraftCell) * MapColumns + (int)(gridX / DraftCell);
            if (tile >= draftPageHeroes.Length) return true;
            var id = draftPageHeroes[tile];
            if (!draft.Remove(id))
            {
                if (draft.Count >= 4) return true;
                draft.Add(id);
            }
            DrawCombinationEditor(root, nameLabel, hero);
            return true;
        }
        var index = (int)(point.y / TabStep);
        if (index >= combinationButtons.Count || point.y - index * TabStep >= TabHeight) return true;
        combinationButtons[index]();
        return true;
    }

    private void RefreshDraftPortraits(IntPtr root, IntPtr nameLabel, int hero)
    {
        if (combinationEditor == IntPtr.Zero || draftCombination == null || !PollMapPortraits()) return;
        if (combinationNameInput != IntPtr.Zero)
            draftName = TextValue(Call(combinationNameInput, "get_text"));
        DrawCombinationEditor(root, nameLabel, hero);
    }

    private void OpenCombinationEditor(IntPtr root, IntPtr nameLabel, int hero, bool edit)
    {
        editingAllCombinations = edit;
        editingEmergencySettings = false;
        combinationPage = 0;
        draftCombination = null;
        DrawCombinationEditor(root, nameLabel, hero);
    }

    private void DrawCombinationEditor(IntPtr root, IntPtr nameLabel, int hero)
    {
        CloseCombinationEditor();
        var panel = Create(IL2CPP.il2cpp_object_get_class(container));
        AddChild(container, panel);
        combinationEditor = panel;
        var position = PositionIn(root, container);
        var width = draftCombination == null ? CombinationWidth : DraftWidth;
        combinationEditorWidth = width;
        var x = position.x >= width + 8 ? -width - 8 : TabWidth + 8;
        x = Math.Clamp(x, -position.x, Math.Max(-position.x,
            FloatValue(Call(root, "get_width")) - position.x - width));
        var style = Call(nameLabel, "get_textFormat", optional: true);
        var combinations = ReadCombinations().DistinctBy(CombinationKey).ToList();

        void Redraw() => DrawCombinationEditor(root, nameLabel, hero);
        void Add(string label, bool enabled, Action action, Color? fill = null)
        {
            AddDetailButton(panel, style, width, combinationButtons.Count * TabStep,
                label, enabled, symmetric: true, fillOverride: fill);
            combinationButtons.Add(action);
        }
        void AddPage(int count, int pageSize = CombinationPageSize)
        {
            var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
            if (pages <= 1) return;
            Add($"{combinationPage + 1}/{pages} ›", false, () =>
            {
                combinationPage = (combinationPage + 1) % pages;
                Redraw();
            }, new Color(0.77f, 0.83f, 0.91f));
        }
        void ReturnToList()
        {
            draftCombination = null;
            combinationPage = 0;
            Redraw();
        }

        if (editingEmergencySettings)
        {
            Add(EmergencySettingsLabel() + (emergencyEnabled.Value ? " · ON" : " · OFF"), emergencyEnabled.Value, () =>
            {
                emergencyEnabled.Value = !emergencyEnabled.Value;
                Redraw();
            });
            Add(EditorText("역할당 목표", "Target per role", "役割ごとの目標", "每个定位的目标", "每個定位的目標")
                + $" · {emergencyMinimum.Value} ›", false, () =>
            {
                emergencyMinimum.Value = emergencyMinimum.Value % 4 + 1;
                Redraw();
            });
            var selected = ReadEmergencyRoles().ToHashSet();
            foreach (var role in RoleOrder.Take(4))
            {
                Add(TagLabel((int)role), selected.Contains(role), () =>
                {
                    if (!selected.Remove(role)) selected.Add(role);
                    emergencyRoles.Value = string.Join(", ", RoleOrder.Where(selected.Contains).Select(tag => TagKeys[(int)tag]));
                    Redraw();
                });
            }
            Add(EditorText("돌아가기", "Back", "戻る", "返回", "返回"), false, () =>
            {
                editingEmergencySettings = false;
                Redraw();
            });
        }
        else if (!editingAllCombinations)
        {
            var allowed = ParseMapHeroIds(emergencyCandidates.Value).ToList();
            var included = allowed.Contains(hero);
            Add(EditorText("긴급 추천 후보", "Emergency Pick candidate", "緊急推薦候補", "紧急推荐候选", "緊急推薦候選")
                + (included ? " · ON" : " · OFF"), included, () =>
            {
                if (!allowed.Remove(hero)) allowed.Add(hero);
                emergencyCandidates.Value = string.Join(", ", allowed);
                Redraw();
            });
            var relevant = combinations.Where(set => set.Contains(hero)).ToArray();
            var disabled = DisabledSets();
            foreach (var set in relevant.Skip(combinationPage * CombinationPageSize).Take(CombinationPageSize))
            {
                var key = CombinationKey(set);
                Add(CombinationTitle(set), !disabled.Contains(key), () =>
                {
                    if (!disabled.Remove(key)) disabled.Add(key);
                    SaveDisabledCombinations(disabled);
                    Redraw();
                });
            }
            AddPage(relevant.Length);
        }
        else if (draftCombination is not { } draft)
        {
            Add(EmergencySettingsLabel() + " ›", emergencyEnabled.Value, () =>
            {
                editingEmergencySettings = true;
                Redraw();
            });
            Add(EditorText("+ 조합 추가", "+ Add combination", "+ 組み合わせを追加", "+ 添加组合", "+ 新增組合"), true, () =>
            {
                editIndex = -1;
                draftName = "";
                draftCombination = new List<int> { hero };
                combinationPage = 0;
                Redraw();
            }, new Color(1f, 0.84f, 0.44f));
            var disabled = DisabledSets();
            foreach (var index in Enumerable.Range(0, combinations.Count)
                .Skip(combinationPage * CombinationPageSize).Take(CombinationPageSize))
            {
                var set = combinations[index];
                Add(CombinationTitle(set), !disabled.Contains(CombinationKey(set)), () =>
                {
                    editIndex = index;
                    draftName = ReadCombinationNames().GetValueOrDefault(CombinationKey(set), "");
                    draftCombination = set.ToList();
                    combinationPage = 0;
                    Redraw();
                });
            }
            AddPage(combinations.Count);
        }
        else
        {
            var disabled = DisabledSets();
            var editingExisting = editIndex >= 0 && editIndex < combinations.Count;
            var oldKey = editingExisting ? CombinationKey(combinations[editIndex]) : "";
            var fixedSeats = FixedSeatSets();
            var duplicate = combinations.Where((_, index) => index != editIndex)
                .Any(set => CombinationKey(set) == CombinationKey(draft.ToArray()));
            var canSave = draft.Count >= 2 && !duplicate;
            AddCombinationNameInput(panel, 0, width);
            combinationButtons.Add(() => { });
            Add(draft.Count > 0 ? string.Join(" + ", draft.Select(ShortCombinationHeroName))
                : EditorText("캐릭터 선택", "Select characters", "キャラクターを選択", "选择角色", "選擇角色"), true, () => { });
            Add(duplicate ? EditorText("이미 등록된 조합", "Combination already exists", "登録済みの組み合わせ", "组合已存在", "組合已存在")
                : EditorText("저장", "Save", "保存", "保存", "儲存"), canSave, () =>
            {
                if (!canSave) return;
                var updated = draft.ToArray();
                var newKey = CombinationKey(updated);
                if (editingExisting)
                {
                    combinations[editIndex] = updated;
                    if (disabled.Remove(oldKey)) disabled.Add(newKey);
                    if (fixedSeats.Remove(oldKey)) fixedSeats.Add(newKey);
                }
                else combinations.Add(updated);
                var names = ReadCombinationNames();
                if (editingExisting) names.Remove(oldKey);
                var name = string.Join(" ", draftName.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (name.Length > 0) names[newKey] = name.Length <= 60 ? name : name[..60];
                combinationNames.Value = SerializeCombinationNames(names);
                SaveFixedSeatSets(fixedSeats);
                SaveCombinations(combinations, disabled);
                ReturnToList();
            });
            if (editingExisting)
            {
                var enabled = !disabled.Contains(oldKey);
                var seatFixed = fixedSeats.Contains(oldKey);
                Add(seatFixed ? EditorText("자리 고정", "Fixed seat", "席順固定", "固定座位", "固定座位") : EditorText("자리 무시", "Ignore seat", "席順を無視", "忽略座位", "忽略座位"),
                    seatFixed, () =>
                    {
                        if (!fixedSeats.Remove(oldKey)) fixedSeats.Add(oldKey);
                        SaveFixedSeatSets(fixedSeats);
                        Redraw();
                    });
                Add(enabled ? EditorText("사용", "Enabled", "有効", "启用", "啟用") : EditorText("미사용", "Disabled", "無効", "停用", "停用"), enabled, () =>
                {
                    if (!disabled.Remove(oldKey)) disabled.Add(oldKey);
                    SaveDisabledCombinations(disabled);
                    Redraw();
                });
                Add(EditorText("조합 삭제", "Delete combination", "組み合わせを削除", "删除组合", "刪除組合"), false, () =>
                {
                    combinations.RemoveAt(editIndex);
                    disabled.Remove(oldKey);
                    if (fixedSeats.Remove(oldKey)) SaveFixedSeatSets(fixedSeats);
                    var names = ReadCombinationNames();
                    if (names.Remove(oldKey)) combinationNames.Value = SerializeCombinationNames(names);
                    SaveCombinations(combinations, disabled);
                    ReturnToList();
                });
            }
            Add(EditorText("돌아가기", "Back", "戻る", "返回", "返回"), false, ReturnToList);
            var ids = MapEditorHeroIds();
            AddPage(ids.Length, MapPageSize);
            draftGridTop = combinationButtons.Count * TabStep;
            DrawDraftGrid(panel, draft, ids);
        }

        var height = draftCombination == null ? TabStep * combinationButtons.Count
            : draftGridTop + Math.Max(1, (draftPageHeroes.Length + MapColumns - 1) / MapColumns) * DraftCell + 6;
        var y = Math.Max(-position.y, Math.Min(0, FloatValue(Call(root, "get_height")) - position.y - height));
        FloatPair(panel, "SetXY", x, y);
        FloatPair(panel, "SetSize", CombinationWidth, height);
    }

    private string EmergencySettingsLabel() =>
        EditorText("긴급 추천", "Emergency Pick", "緊急推薦", "紧急推荐", "緊急推薦");

    private string EditorText(string korean, string english, string japanese, string simplified, string traditional) =>
        detectedLanguage switch
        {
            UiLanguage.Korean => korean,
            UiLanguage.Japanese => japanese,
            UiLanguage.SimplifiedChinese => simplified,
            UiLanguage.TraditionalChinese => traditional,
            _ => english
        };

    private string CombinationTitle(int[] ids)
    {
        var members = string.Join(" + ", ids.Select(ShortCombinationHeroName));
        return ReadCombinationNames().TryGetValue(CombinationKey(ids), out var name) &&
            ShowCombinationName(CombinationKey(ids), name)
            ? $"{name} ({members})" : members;
    }

    private bool ShowCombinationName(string key, string name) => name.Length > 0 &&
        (detectedLanguage == UiLanguage.Korean || defaultCombinationNames.GetValueOrDefault(key) != name);

    private Dictionary<string, string> ReadCombinationNames()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(combinationNames.Value) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static readonly JsonSerializerOptions CombinationNameJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string SerializeCombinationNames(Dictionary<string, string> names) =>
        JsonSerializer.Serialize(names, CombinationNameJson);

    private void NormalizeCombinationNames()
    {
        if (combinationNames.DefaultValue is string defaults)
            defaultCombinationNames = JsonSerializer.Deserialize<Dictionary<string, string>>(defaults) ?? new();
        try
        {
            var names = JsonSerializer.Deserialize<Dictionary<string, string>>(combinationNames.Value);
            if (names != null) combinationNames.Value = SerializeCombinationNames(names);
        }
        catch (JsonException) { }
    }

    private string ShortCombinationHeroName(int id)
    {
        var name = CombinationHeroName(id);
        var opening = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (opening >= 0 && name.EndsWith(")", StringComparison.Ordinal)) return name[(opening + 2)..^1];
        if (detectedLanguage == UiLanguage.Korean)
        {
            var known = DetailNames.FirstOrDefault(pair => pair.Value == id).Key;
            if (known != null) return known;
        }
        return name;
    }

    private void DrawDraftGrid(IntPtr panel, List<int> draft, int[] ids)
    {
        draftPageHeroes = ids.Skip(combinationPage * MapPageSize).Take(MapPageSize).ToArray();
        RequestMapPortraits(draftPageHeroes);
        var height = Math.Max(1, (draftPageHeroes.Length + MapColumns - 1) / MapColumns) * DraftCell + 6;
        AddMapTileShape(panel, 0, draftGridTop - 4, DraftWidth, height + 4,
            new Color(0.98f, 0.96f, 0.91f), 2f, new Color(0.38f, 0.19f, 0.47f), 12f);
        for (var index = 0; index < draftPageHeroes.Length; index++)
        {
            var id = draftPageHeroes[index];
            var x = 6 + index % MapColumns * DraftCell;
            var y = draftGridTop + index / MapColumns * DraftCell;
            const float size = DraftCell - 4;
            var selected = draft.Contains(id);
            AddDetailButton(panel, IntPtr.Zero, size, y,
                mapPortraits.ContainsKey(id) ? "" : ShortCombinationHeroName(id), selected,
                x: x, height: size, symmetric: true);
            AddMapPortrait(panel, id, x, y, size, size, false);
            AddMapTileShape(panel, x, y, size, size, Color.clear, selected ? 3f : 2f,
                selected ? new Color(0.60f, 0.92f, 0.12f) : new Color(0.13f, 0.15f, 0.20f));
        }
    }

    private void AddCombinationNameInput(IntPtr panel, float y, float width)
    {
        var type = FindFairyType("GTextInput");
        if (type == IntPtr.Zero) return;
        AddMapTileShape(panel, 0, y, width, TabHeight, Color.white, 2, new Color(0.38f, 0.19f, 0.47f));
        var input = Create(type);
        AddChild(panel, input);
        FloatPair(input, "SetXY", 8, y + 2);
        FloatPair(input, "SetSize", width - 16, TabHeight - 4);
        var format = Call(input, "get_textFormat");
        SetIntField(format, "size", 18);
        void* style = (void*)format;
        Exact(input, "set_textFormat", "FairyGUI.TextFormat", &style);
        byte singleLine = 1;
        void* singleLineArgument = &singleLine;
        Exact(input, "set_singleLine", "System.Boolean", &singleLineArgument);
        void* prompt = (void*)IL2CPP.ManagedStringToIl2Cpp(EditorText("조합 이름", "Combination name", "組み合わせ名", "组合名称", "組合名稱"));
        Exact(input, "set_promptText", "System.String", &prompt);
        void* value = (void*)IL2CPP.ManagedStringToIl2Cpp(draftName);
        Exact(input, "set_text", "System.String", &value);
        combinationNameInput = input;
    }

    private void SaveCombinations(IEnumerable<int[]> combinations, HashSet<string> disabled)
    {
        combinationOrder.Value = string.Join(";", combinations.Select(set => string.Join("+", set)));
        SaveDisabledCombinations(disabled);
    }

    private void SaveDisabledCombinations(HashSet<string> disabled) =>
        disabledCombinations.Value = string.Join(";", disabled.OrderBy(value => value, StringComparer.Ordinal));


    private string CombinationHeroName(int id)
    {
        if (RosterEntries?.TryGetValue(RosterKey(id), out var name) == true) return name;
        if (detectedLanguage == UiLanguage.Korean)
        {
            if (id == 302) return "아메";
            if (id == 301) return "초텐";
            var known = DetailNames.FirstOrDefault(pair => pair.Value == id).Key;
            if (known != null) return known;
        }
        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private IntPtr AddDetailButton(IntPtr host, IntPtr sourceStyle, float width, float y, string title,
        bool enabled, bool excluded = false, float x = 0, float height = TabHeight, float labelInset = 0,
        bool symmetric = false, Color? fillOverride = null)
    {
        var graph = Create(graphClass);
        AddChild(host, graph);
        FloatPair(graph, "SetSize", width, height);
        FloatPair(graph, "SetXY", x, y);
        PaintTab(graph, enabled, excluded, symmetric, fillOverride);

        var label = Create(textClass);
        AddChild(host, label);
        var markupSetter = FindMethod(IL2CPP.il2cpp_object_get_class(label), "set_ubbEnabled", "System.Boolean");
        if (markupSetter != IntPtr.Zero)
        {
            byte markupEnabled = 0;
            void* argument = &markupEnabled;
            Invoke(markupSetter, label, &argument);
        }
        CenterLabel(label);
        FloatPair(label, "SetSize", width, height - labelInset);
        FloatPair(label, "SetXY", x, y + labelInset);
        byte touchable = 0;
        void* touch = &touchable;
        Exact(label, "set_touchable", "System.Boolean", &touch);
        var format = Call(label, "get_textFormat");
        CopyFont(sourceStyle, format);
        var units = Math.Max(1f, title.Sum(character => character < 128 ? 0.62f : 1f));
        SetIntField(format, "size", Math.Max(1, (int)Math.Min(Math.Min(18f, height - labelInset), (width - 12f) / units)));
        void* style = (void*)format;
        Exact(label, "set_textFormat", "FairyGUI.TextFormat", &style);
        void* text = (void*)IL2CPP.ManagedStringToIl2Cpp(title);
        Exact(label, "set_text", "System.String", &text);
        return graph;
    }
}
