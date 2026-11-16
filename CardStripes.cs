using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private static readonly Color[] SeatColors =
    {
        new(0.91f, 0.29f, 0.31f), new(0.53f, 0.82f, 0.26f),
        new(0.33f, 0.60f, 1f), new(0.96f, 0.67f, 0.27f)
    };

    private readonly Dictionary<IntPtr, (IntPtr Graph, nint Handle, int Slot)> cardStripes = new();

    private void UpdateCardStripes(IntPtr list, int[] order, Dictionary<int, int> colors, bool virtualList)
    {
        var used = new HashSet<IntPtr>();
        var fill = Color.clear;
        var line = Color.clear;
        var lineSize = 0f;
        void** draw = stackalloc void*[3] { &lineSize, &line, &fill };
        var children = IntValue(Call(list, "get_numChildren"));
        for (var child = 0; child < children; child++)
        {
            var row = ChildAt(list, child);
            if (ClassName(row) != "UIRoomHero_Button_SelectHero_2") continue;
            var item = child;
            if (virtualList)
            {
                var index = child;
                void* argument = &index;
                item = IntValue(Call(list, "ChildIndexToItemIndex", &argument));
            }
            if (item < 0 || item >= order.Length || !colors.TryGetValue(order[item], out var slot)
                || !MatchesHero(row, order[item])) continue;
            used.Add(row);
            if (!cardStripes.TryGetValue(row, out var stripe))
            {
                var graph = Create(graphClass);
                var handle = IL2CPP.il2cpp_gchandle_new(graph, false);
                try
                {
                    var width = FloatValue(Call(row, "get_width"));
                    var height = FloatValue(Call(row, "get_height"));
                    var cornerInset = width * 0.15f;
                    // The roster cell includes the badge extending past the portrait's left edge.
                    var nameplateCenter = width * 0.55f;
                    const float stripeHeight = 5f;
                    var stripeWidth = width - 2 * cornerInset;
                    FloatPair(graph, "SetSize", stripeWidth, stripeHeight);
                    FloatPair(graph, "SetXY", nameplateCenter - stripeWidth / 2, Math.Max(0, height - stripeHeight));
                    byte touchable = 0;
                    void* argument = &touchable;
                    Exact(graph, "set_touchable", "System.Boolean", &argument);
                    AddChild(row, graph);
                    stripe = (graph, handle, -1);
                    cardStripes.Add(row, stripe);
                }
                catch
                {
                    IL2CPP.il2cpp_gchandle_free(handle);
                    throw;
                }
            }
            if (stripe.Slot == slot) continue;
            fill = SeatColors[slot];
            Exact(Call(stripe.Graph, "get_shape"), "DrawRect",
                "System.Single,UnityEngine.Color,UnityEngine.Color", draw);
            cardStripes[row] = (stripe.Graph, stripe.Handle, slot);
        }
        foreach (var row in new List<IntPtr>(cardStripes.Keys))
            if (!used.Contains(row)) RemoveCardStripe(row);
    }

    private void ClearCardStripes()
    {
        foreach (var row in new List<IntPtr>(cardStripes.Keys)) RemoveCardStripe(row);
    }

    private void RemoveCardStripe(IntPtr row)
    {
        var stripe = cardStripes[row];
        cardStripes.Remove(row);
        try
        {
            if (Call(stripe.Graph, "get_parent") != IntPtr.Zero) Call(stripe.Graph, "Dispose");
        }
        catch (Exception error) { logger.LogWarning(error.Message); }
        finally { IL2CPP.il2cpp_gchandle_free(stripe.Handle); }
    }
}
