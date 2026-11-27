using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private readonly IntPtr[] lowPveBorders = new IntPtr[4];
    private string lastPveReading = "";
    private bool selectionButtonsVisible;
    private bool selectionLoading;

    private void SetSelectionButtonsVisible(bool visible)
    {
        if (selectionButtonsVisible == visible) return;
        selectionButtonsVisible = visible;
        byte enabled = visible ? (byte)1 : (byte)0;
        void* flag = &enabled;
        var count = IntValue(Call(container, "get_numChildren"));
        for (var index = 0; index < count; index++)
        {
            var child = ChildAt(container, index);
            if (!lowPveBorders.Contains(child))
                Exact(child, "set_visible", "System.Boolean", &flag);
        }
    }

    private void AddLowPveBorders(IntPtr host, float height)
    {
        lastPveReading = "";
        selectionButtonsVisible = true;
        for (var slot = 0; slot < lowPveBorders.Length; slot++)
        {
            var left = 0.045f + slot * 0.234f;
            const float horizontalOutset = 0.002f;
            const float verticalOutset = 0.003f;
            var start = TransformPoint(host, "GlobalToLocal",
                new Vector2(Screen.width * (left - horizontalOutset), 0f));
            var end = TransformPoint(host, "GlobalToLocal",
                new Vector2(Screen.width * (left + 0.209f + horizontalOutset),
                    Screen.height * (0.662f + verticalOutset)));
            var marker = AddMapTileShape(host, start.x, start.y, end.x - start.x, end.y - start.y,
                Color.clear, height * 0.27f, new Color(1f, 0.83f, 0.06f), height * 0.5f);
            byte off = 0;
            void* flag = &off;
            Exact(marker, "set_touchable", "System.Boolean", &flag);
            Exact(marker, "set_visible", "System.Boolean", &flag);
            lowPveBorders[slot] = marker;
        }
    }

    private bool UpdateLowPveWarning(IntPtr root)
    {
        if (lowPveBorders[0] == IntPtr.Zero) return false;
        var levels = ReadVisiblePveLevels(root, out var loading);
        if (loading) return true;
        // The loading banner can be a sibling of RoomHeroPanel.
        var stage = root;
        for (var parent = Call(stage, "get_parent", optional: true); parent != IntPtr.Zero;
            parent = Call(stage, "get_parent", optional: true))
            stage = parent;
        if (stage != root)
        {
            ReadVisiblePveLevels(stage, out loading, loadingOnly: true);
            if (loading) return true;
        }
        if (recommendationDiagnostics.Value)
        {
            var reading = string.Join(", ", levels.Select(level => level >= 0 ? level.ToString() : "?"));
            if (reading != lastPveReading) logger.LogInfo($"PvE badges P1–P4: [{reading}]");
            lastPveReading = reading;
        }
        var alpha = 0.25f + 0.75f * (0.5f + 0.5f * MathF.Sin(Time.unscaledTime * 2.5f * 2f * MathF.PI));
        void* opacity = &alpha;
        for (var slot = 0; slot < lowPveBorders.Length; slot++)
        {
            var marker = lowPveBorders[slot];
            byte visible = levels[slot] is >= 0 and < 6 ? (byte)1 : (byte)0;
            void* flag = &visible;
            Exact(marker, "set_visible", "System.Boolean", &flag);
            if (visible == 0) continue;
            Exact(marker, "set_alpha", "System.Single", &opacity);
        }
        return false;
    }

    private static int[] ReadVisiblePveLevels(IntPtr root, out bool loading, bool loadingOnly = false)
    {
        var levels = new[] { -1, -1, -1, -1 };
        loading = false;
        var queue = new Queue<(IntPtr Node, int Depth)>();
        queue.Enqueue((root, 0));
        var examined = 0;
        while (queue.TryDequeue(out var entry) && examined++ < 1500)
        {
            var node = entry.Node;
            if (node != root && (!BoolValue(Call(node, "get_visible")) || !BoolValue(Call(node, "get_onStage")))) continue;

            var value = TextValue(Call(node, "get_text", optional: true)).Trim();
            if (value.StartsWith("데이터 로딩 중", StringComparison.Ordinal) ||
                value.StartsWith("Loading data", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("データ読み込み中", StringComparison.Ordinal) ||
                value.StartsWith("データロード中", StringComparison.Ordinal) ||
                value.StartsWith("数据加载中", StringComparison.Ordinal) ||
                value.StartsWith("資料載入中", StringComparison.Ordinal))
            {
                loading = true;
                return levels;
            }
            // Loading shows a whole-number percentage below each player profile.
            if (value.EndsWith('%') && int.TryParse(value.AsSpan(0, value.Length - 1), out var progress)
                && progress is >= 0 and <= 100)
            {
                var position = TransformPoint(node, "LocalToGlobal", Vector2.zero);
                if (position.y / Screen.height is >= 0.70f and <= 0.87f)
                {
                    loading = true;
                    return levels;
                }
            }
            if (!loadingOnly && value.Length == 1 && value[0] is >= '0' and <= '9')
            {
                var point = TransformPoint(node, "LocalToGlobal", Vector2.zero);
                var x = point.x / Screen.width;
                var y = point.y / Screen.height;
                // The preview badge is lower than the locked-in badge and above the profile card.
                if (y is >= 0.265f and <= 0.575f)
                {
                    for (var slot = 0; slot < levels.Length; slot++)
                    {
                        var badgeRight = 0.241f + slot * 0.234f;
                        if (x < badgeRight - 0.034f || x > badgeRight + 0.018f) continue;
                        var level = value[0] - '0';
                        if (levels[slot] == -1)
                        {
                            levels[slot] = level;
                        }
                        else if (levels[slot] != level) levels[slot] = -2;
                    }
                }
            }

            if (entry.Depth >= 10 || ClassName(node) == "GList") continue;
            var count = Call(node, "get_numChildren", optional: true);
            if (ClassName(count) != "Int32") continue;
            for (var index = 0; index < IntValue(count); index++)
            {
                var child = ChildAt(node, index);
                if (child != IntPtr.Zero) queue.Enqueue((child, entry.Depth + 1));
            }
        }
        return levels;
    }
}
