using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private readonly HashSet<int> scannedHeroNames = new();
    private const string RosterSection = "캐릭터 목록";
    private static readonly PropertyInfo? OrphanedEntries = typeof(ConfigFile).GetProperty(
        "OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private Dictionary<ConfigDefinition, string>? RosterEntries =>
        OrphanedEntries?.GetValue(settingsFile) as Dictionary<ConfigDefinition, string>;

    private int[] ManualHeroOrder(IEnumerable<int> originalOrder)
    {
        var roster = originalOrder.ToArray();
        var remaining = roster.ToHashSet();
        var result = new List<int>(roster.Length);
        foreach (var value in manualOrder.Value.Split(',', StringSplitOptions.TrimEntries))
            if (int.TryParse(value, out var id) && remaining.Remove(id))
                result.Add(id);
        result.AddRange(roster.Where(remaining.Contains));
        return result.ToArray();
    }

    private void UpdateHeroNames(IntPtr list, int[] order, bool virtualList)
    {
        if (scannedHeroNames.Count >= order.Length) return;
        var entries = RosterEntries;
        if (entries == null) return;
        var changed = false;
        try
        {
            var count = IntValue(Call(list, "get_numChildren"));
            for (var child = 0; child < count; child++)
            {
                var item = child;
                if (virtualList)
                {
                    var childIndex = child;
                    void* argument = &childIndex;
                    item = IntValue(Call(list, "ChildIndexToItemIndex", &argument));
                }
                if (item < 0 || item >= order.Length) continue;
                var id = order[item];
                if (scannedHeroNames.Contains(id)) continue;
                var row = ChildAt(list, child);
                if (ClassName(row) != "UIRoomHero_Button_SelectHero_2" || !MatchesHero(row, id))
                    continue;
                var name = ReadHeroName(row);
                if (name.Length == 0) continue;
                var key = RosterKey(id);
                if (!entries.TryGetValue(key, out var existing) ||
                    (existing != name && !existing.StartsWith(name + " (", StringComparison.Ordinal)))
                {
                    entries[key] = name;
                    changed = true;
                }
                scannedHeroNames.Add(id);
            }
        }
        finally
        {
            if (changed || entries.TryAdd(new ConfigDefinition(RosterSection, "Source"),
                "게임의 캐릭터 선택창에서 갱신됩니다.")) SaveRoster(entries);
        }
    }

    private static ConfigDefinition RosterKey(int id) =>
        new(RosterSection, id.ToString(CultureInfo.InvariantCulture));

    private void SaveRoster(Dictionary<ConfigDefinition, string> entries)
    {
        entries.TryAdd(new ConfigDefinition(RosterSection, "Source"), "게임의 캐릭터 선택창에서 갱신됩니다.");
        settingsFile.Save();
    }

    private void RememberFullHeroName(int id, string fullName)
    {
        var entries = RosterEntries;
        var key = RosterKey(id);
        if (entries == null || fullName.Length == 0 || !entries.TryGetValue(key, out var existing))
            return;
        var separator = existing.IndexOf(" (", StringComparison.Ordinal);
        var shortName = separator < 0 ? existing : existing[..separator];
        var updated = shortName == fullName ? shortName : shortName + " (" + fullName + ")";
        if (updated == existing) return;
        entries[key] = updated;
        SaveRoster(entries);
    }

    private static string ReadHeroName(IntPtr row)
    {
        foreach (var node in VisibleDetailNodes(row))
        {
            if (ClassName(node) != "UIRoomHero_Com_CharacterName") continue;
            foreach (var child in VisibleDetailNodes(node))
            {
                var text = TextValue(Call(child, "get_text", optional: true));
                if (text.Length > 0 && !int.TryParse(text, out _)) return text;
            }
        }
        return "";
    }
}
