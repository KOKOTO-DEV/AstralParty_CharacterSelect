using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private const float TabWidth = 96f;
    private const float TabHeight = 34f;
    private const float TabStep = 39f;
    private const float TabInsetFromTitle = 58f;
    private const float TabLift = 52f;
    private readonly IntPtr[] detailGraphs = new IntPtr[8];
    private int detailHero;
    private int loggedDetailHero;
    private int detailMask = -1;

    private void ObserveDetails(IntPtr panel, bool clicked)
    {
        var root = Call(panel, "get_UIObject");
        if (root == IntPtr.Zero || !BoolValue(Call(root, "get_onStage")))
        {
            Detach();
            return;
        }
        var profile = IntPtr.Zero;
        var nameLabel = IntPtr.Zero;
        var hero = 0;
        var width = FloatValue(Call(root, "get_width"));
        foreach (var node in VisibleDetailNodes(root))
        {
            var label = TextValue(Call(node, "get_text", optional: true));
            if (label is "프로필" or "Profile")
            {
                if (profile == IntPtr.Zero || PositionIn(root, node).y < PositionIn(root, profile).y)
                    profile = node;
            }
            if (!DetailNames.TryGetValue(label, out var id)) continue;
            var point = PositionIn(root, node);
            if (point.x < width * 0.40f) continue;
            if (nameLabel == IntPtr.Zero || point.y < PositionIn(root, nameLabel).y)
            {
                hero = id;
                nameLabel = node;
            }
        }
        if (hero == 0 || profile == IntPtr.Zero)
        {
            Detach();
            return;
        }

        if (activeRoot != root || detailHero == 0)
        {
            Detach();
            var profilePoint = PositionIn(root, profile);
            var titlePoint = PositionIn(root, nameLabel);
            var x = Math.Max(0, Math.Min(profilePoint.x, titlePoint.x) - TabWidth - TabInsetFromTitle);
            var y = Math.Clamp(Math.Max(profilePoint.y, titlePoint.y) - TabLift, 0,
                Math.Max(0, FloatValue(Call(root, "get_height")) - TabStep * (groups.Length + 2)));
            CreateDetailTabs(root, nameLabel, x, y);
            activeRoot = root;
            detailMask = -1;
        }
        if (detailHero != hero) CloseCombinationEditor();
        detailHero = hero;
        if (!clicked) RefreshDraftPortraits(root, nameLabel, hero);
        if (loggedDetailHero != hero)
        {
            RememberFullHeroName(hero, TextValue(Call(nameLabel, "get_text", optional: true)));
            loggedDetailHero = hero;
        }

        if (clicked)
        {
            var mouse = Input.mousePosition;
            var local = TransformPoint(container, "GlobalToLocal", new Vector2(mouse.x, Screen.height - mouse.y));
            if (HandleCombinationClick(local, root, nameLabel, hero)) return;
            if (local.x >= 0 && local.x < TabWidth && local.y >= 2 * TabStep && local.y < TabStep * (groups.Length + 2))
            {
                var tab = (int)(local.y / TabStep);
                if (local.y - tab * TabStep < TabHeight)
                {
                    tab -= 2;
                    var ids = ReadGroup(tab);
                    var enabled = !ids.Remove(hero);
                    if (enabled) ids.Add(hero);
                    groups[tab].Value = string.Join(", ", ids.OrderBy(id => id));
                }
            }
        }

        var mask = 0;
        for (var tab = 0; tab < groups.Length; tab++)
            if (ReadGroup(tab).Contains(hero)) mask |= 1 << tab;
        if (mask == detailMask) return;
        for (var tab = 0; tab < groups.Length; tab++)
            PaintTab(detailGraphs[tab], (mask & (1 << tab)) != 0);
        detailMask = mask;
    }

    private HashSet<int> ReadGroup(int tab) => groups[tab].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(token => int.TryParse(token, out var id) ? id : 0).Where(id => id > 0).ToHashSet();

    private static List<IntPtr> VisibleDetailNodes(IntPtr root)
    {
        var visible = new List<IntPtr>();
        var nodes = new Queue<(IntPtr Node, int Depth)>();
        nodes.Enqueue((root, 0));
        var examined = 0;
        while (nodes.TryDequeue(out var entry) && examined++ < 220)
        {
            if (!BoolValue(Call(entry.Node, "get_visible")) ||
                !BoolValue(Call(entry.Node, "get_onStage"))) continue;
            visible.Add(entry.Node);
            if (entry.Depth == 5 || ClassName(entry.Node) == "GList") continue;
            var count = Call(entry.Node, "get_numChildren", optional: true);
            if (count == IntPtr.Zero) continue;
            for (var index = 0; index < Math.Min(IntValue(count), 64); index++)
            {
                var child = ChildAt(entry.Node, index);
                if (child != IntPtr.Zero) nodes.Enqueue((child, entry.Depth + 1));
            }
        }
        return visible;
    }

    private static IntPtr ChildAt(IntPtr node, int index)
    {
        void* arg = &index;
        return Call(node, "GetChildAt", &arg);
    }

    private static string TextValue(IntPtr value) => ClassName(value) == "String"
        ? (IL2CPP.Il2CppStringToManaged(value) ?? "").Trim()
        : "";

    private static Vector2 PositionIn(IntPtr root, IntPtr node) =>
        TransformPoint(root, "GlobalToLocal", TransformPoint(node, "LocalToGlobal", Vector2.zero));

    private static Vector2 TransformPoint(IntPtr node, string method, Vector2 value)
    {
        void* arg = &value;
        var result = Exact(node, method, "UnityEngine.Vector2", &arg);
        if (ClassName(result) != "Vector2") throw new InvalidOperationException("Invalid FairyGUI coordinate.");
        return *(Vector2*)IL2CPP.il2cpp_object_unbox(result);
    }

    private void CreateDetailTabs(IntPtr root, IntPtr nameLabel, float x, float y)
    {
        var type = IL2CPP.il2cpp_object_get_class(root);
        while (type != IntPtr.Zero && Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(type)) != "GComponent")
            type = IL2CPP.il2cpp_class_get_parent(type);
        if (type == IntPtr.Zero || graphClass == IntPtr.Zero || textClass == IntPtr.Zero)
            throw new InvalidOperationException("Character profile tab controls unavailable.");
        var host = Create(type);
        var handle = IL2CPP.il2cpp_gchandle_new(host, false);
        try
        {
            FloatPair(host, "SetSize", TabWidth, TabStep * (groups.Length + 2));
            FloatPair(host, "SetXY", x, y);
            var sourceStyle = Call(nameLabel, "get_textFormat", optional: true);
            for (var slot = 0; slot < groups.Length + 2; slot++)
            {
                var graph = AddDetailButton(host, sourceStyle, TabWidth, slot * TabStep,
                    slot == 0 ? ConditionEditLabel() : slot == 1 ? ConditionSettingsLabel() : TagLabel(slot - 2), false,
                    symmetric: slot < 2, fillOverride: slot == 0 ? new Color(0.54f, 0.79f, 1f)
                        : slot == 1 ? new Color(0.65f, 0.88f, 0.97f) : null);
                if (slot >= 2) detailGraphs[slot - 2] = graph;
            }
            AddChild(root, host);
            container = host;
            containerHandle = handle;
        }
        finally
        {
            if (container != host) IL2CPP.il2cpp_gchandle_free(handle);
        }
    }

    private static void CopyFont(IntPtr source, IntPtr destination)
    {
        if (source == IntPtr.Zero || destination == IntPtr.Zero) return;
        var field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(destination), "font");
        if (field == IntPtr.Zero || IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_field_get_type(field)) != 14) return;
        var font = ReadField(source, "font", optional: true);
        if (font != IntPtr.Zero) IL2CPP.il2cpp_field_set_value(destination, field, (void*)font);
    }

    private static void PaintTab(IntPtr graph, bool enabled, bool excluded = false, bool symmetric = false,
        Color? fillOverride = null)
    {
        var lineSize = 2f;
        var line = new Color(0.13f, 0.15f, 0.20f);
        var fill = fillOverride ?? (excluded ? new Color(0.63f, 0.65f, 0.69f)
            : enabled ? new Color(0.57f, 0.98f, 0.00f)
            : symmetric ? new Color(0.88f, 0.81f, 0.98f) : new Color(1f, 0.31f, 0.67f));
        var leftRadius = 9f;
        var rightRadius = symmetric ? leftRadius : 3f;
        void** draw = stackalloc void*[7] { &lineSize, &line, &fill,
            &leftRadius, &rightRadius, &leftRadius, &rightRadius };
        Exact(Call(graph, "get_shape"), "DrawRoundRect",
            "System.Single,UnityEngine.Color,UnityEngine.Color,System.Single,System.Single,System.Single,System.Single", draw);
    }

    private static void CenterLabel(IntPtr label)
    {
        var fixedSize = 0;
        void* size = &fixedSize;
        Exact(label, "set_autoSize", "FairyGUI.AutoSizeType", &size);
        var centered = 1;
        void* value = &centered;
        Exact(label, "set_align", "FairyGUI.AlignType", &value);
        Exact(label, "set_verticalAlign", "FairyGUI.VertAlignType", &value);
    }

    private static readonly Dictionary<string, int> DetailNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["파루난"] = 101, ["패니"] = 102, ["알라나"] = 103, ["코마치"] = 104,
        ["패드맨"] = 105, ["파파라"] = 106, ["렌"] = 107, ["미미"] = 108,
        ["Z3000"] = 109, ["판다맨"] = 110, ["잉크 섀도우"] = 111, ["루루"] = 112,
        ["펭"] = 113, ["카이세이"] = 114, ["미사키"] = 115, ["나르디스"] = 116,
        ["자스민"] = 117, ["AL"] = 118, ["A.L."] = 118, ["루카"] = 119, ["낸시 루"] = 120,
        ["린"] = 121, ["메가스"] = 122, ["유메"] = 123, ["테루"] = 124,
        ["모제스"] = 125, ["마무시"] = 126, ["보니"] = 127, ["링링"] = 128,
        ["사이크스"] = 129, ["소우리"] = 130, ["최고귀염뽀짝천사"] = 301, ["니디 걸"] = 302,
        ["질 스팅레이"] = 303, ["도로시 헤이즈"] = 304, ["토노 한나"] = 305,
        ["타치바나 셰리"] = 306, ["타치바나 세리"] = 306, ["타치바나 쉐리"] = 306,
        ["Fanny"] = 102, ["Tachibana Sherry"] = 306
    };
}
