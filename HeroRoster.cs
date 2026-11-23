using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private readonly HashSet<int> scannedHeroNames = new();
    private const string RosterSection = "캐릭터 목록";
    private static readonly ConfigDefinition InitialTagsMarker = new("일반", "DefaultTagsPending");
    private static readonly HashSet<int> PotentialDependentHeroes = new() { 104, 113, 122 };
    private static readonly Lazy<HashSet<int>> DefaultTaggedHeroes = new(() => DefaultGroups
        .SelectMany(group => group.Split(',')).Select(int.Parse).ToHashSet());
    private readonly Dictionary<int, bool> initialTagEligibility = new();
    private bool initialTagsPending;
    private static readonly PropertyInfo? OrphanedEntries = typeof(ConfigFile).GetProperty(
        "OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private Dictionary<ConfigDefinition, string>? RosterEntries =>
        OrphanedEntries?.GetValue(settingsFile) as Dictionary<ConfigDefinition, string>;

    private void PrepareInitialTags()
    {
        if (RosterEntries is not { } entries) return;
        initialTagsPending = entries.ContainsKey(InitialTagsMarker) ||
            (entries.Count == 0 && settingsFile.Count == 0);
        if (initialTagsPending) entries[InitialTagsMarker] = "true";
    }

    private bool TryApplyInitialTags(IntPtr panel, IntPtr list, Dictionary<int, IntPtr> cards, bool virtualList)
    {
        if (!initialTagsPending || RosterEntries is not { } entries) return false;
        if (groups.Any(group => group.Value.Length != 0))
        {
            initialTagsPending = false;
            entries.Remove(InitialTagsMarker);
            settingsFile.Save();
            return false;
        }
        if (ReadMatchMapId(panel) is < 82000 or >= 84000) return false;

        var order = cards.Keys.ToArray();
        var owned = order.Where(id => DefaultTaggedHeroes.Value.Contains(id) &&
            BoolValue(ReadField(cards[id], "_IsHas"))).ToHashSet();
        if (owned.Count == 0) return false;
        var count = IntValue(Call(list, "get_numChildren"));
        for (var child = 0; child < count; child++)
        {
            var item = child;
            if (virtualList)
            {
                var index = child;
                void* argument = &index;
                item = IntValue(Call(list, "ChildIndexToItemIndex", &argument));
            }
            if (item < 0 || item >= order.Length) continue;
            var id = order[item];
            if (!owned.Contains(id) || initialTagEligibility.ContainsKey(id)) continue;
            var row = ChildAt(list, child);
            if (ClassName(row) != "UIRoomHero_Button_SelectHero_2" || !MatchesHero(row, id))
                continue;
            var nodes = VisibleDetailNodes(row);
            var levels = nodes.Select(node => TextValue(Call(node, "get_text", optional: true)).Trim())
                .Where(value => value.Length == 1 && value[0] is >= '0' and <= '6').Distinct().ToArray();
            if (levels.Length != 1) continue;
            initialTagEligibility[id] = levels[0] == "6" &&
                (!PotentialDependentHeroes.Contains(id) || HasUnlockedPotential(cards[id]));
        }
        if (owned.Any(id => !initialTagEligibility.ContainsKey(id))) return false;

        var saveOnChange = settingsFile.SaveOnConfigSet;
        settingsFile.SaveOnConfigSet = false;
        try
        {
            for (var index = 0; index < groups.Length; index++)
                groups[index].Value = string.Join(", ", DefaultGroups[index].Split(',').Select(int.Parse)
                    .Where(id => owned.Contains(id) && initialTagEligibility.GetValueOrDefault(id)));
            entries.Remove(InitialTagsMarker);
            settingsFile.Save();
            initialTagsPending = false;
            initialTagEligibility.Clear();
        }
        finally { settingsFile.SaveOnConfigSet = saveOnChange; }
        return true;
    }

    private static bool HasUnlockedPotential(IntPtr card)
    {
        foreach (var getter in new[] { "get_IsPotentialUnlocked", "get_IsPotentialActivated", "get_IsHeroPotentialActive" })
        {
            var value = Call(card, getter, optional: true);
            if (ClassName(value) == "Boolean") return BoolValue(value);
        }
        return false;
    }

    internal void CompactRosterConfig(ConfigFile config)
    {
        if (!ReferenceEquals(config, settingsFile)) return;
        try
        {
            var content = File.ReadAllText(config.ConfigFilePath);
            var compact = Regex.Replace(content, @"(?m)^\[캐릭터 목록\]\r?\n[^\[]*", section =>
                Regex.Replace(section.Value, @"(?m)^[\t ]*\r?\n(?=(?:[0-9]+|Source)\s*=)", ""));
            compact = Regex.Replace(compact, @"(?m)^\[(?:태그 이름|캐릭터 ID)\]\r?\n[^\[]*", section =>
            {
                var first = true;
                return Regex.Replace(section.Value,
                    @"(?m)^## (?:게임에 표시할 이름입니다|캐릭터 ID를 쉼표로 구분해 입력합니다)\.\r?\n# Setting type: String\r?\n", block =>
                    {
                        if (!first) return "";
                        first = false;
                        return block.Value;
                    });
            });
            compact = Regex.Replace(compact, @"(?m)^\[Map \d+\]\r?\n(?:(?!^\[)[\s\S])*", match =>
            {
                var newline = match.Value.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var section = Regex.Replace(match.Value,
                    @"(?m)^#(?:# (?:Preferred character IDs, in order\.|Character IDs to exclude from conditional selection\.|"
                    + Regex.Escape(MapPreferredDescription) + "|" + Regex.Escape(MapExcludedDescription)
                    + @")| Setting type: String| Default value:[^\r\n]*)\r?\n", "");
                return Regex.Replace(section, @"(?m)^(Preferred|Excluded)(?=\s*=)", entry =>
                    "## " + (entry.Value == "Preferred" ? MapPreferredDescription : MapExcludedDescription)
                    + newline + entry.Value);
            });
            var lineEnding = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            compact = Regex.Replace(compact, @"(?:\r?\n[\t ]*)+(?=^\[[^\r\n]+\]\r?$)",
                lineEnding + lineEnding + lineEnding, RegexOptions.Multiline);
            compact = Regex.Replace(compact,
                @"(?m)^# (?:Setting type:|Default value:|Acceptable values:|Acceptable value range:)[^\r\n]*\r?\n", "");
            compact = Regex.Replace(compact,
                @"(?m)(^\[(?:태그 이름|캐릭터 ID)\]\r?\n(?:(?!^\[)[\s\S])*)", section =>
                    Regex.Replace(section.Value, @"(?m)^[\t ]*\r?\n(?=[^#\[\r\n]+ =)", ""));
            compact = AddConfigNameComments(compact);
            if (content != compact) File.WriteAllText(config.ConfigFilePath, compact);
        }
        catch (IOException error) { logger.LogWarning(error.Message); }
        catch (UnauthorizedAccessException error) { logger.LogWarning(error.Message); }
    }

    private static string AddConfigNameComments(string content)
    {
        const string mapPrefix = "# 맵 이름 · ";
        const string heroPrefix = "# 캐릭터 · ";
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var heroNames = new Dictionary<int, string>();
        foreach (var (name, id) in DetailNames)
            heroNames.TryAdd(id, name);

        var roster = Regex.Match(content, @"(?m)^\[캐릭터 목록\]\r?\n(?:(?!^\[)[\s\S])*");
        foreach (Match entry in Regex.Matches(roster.Value, @"(?m)^([0-9]+)[\t ]*=[\t ]*([^\r\n]+)"))
        {
            if (!int.TryParse(entry.Groups[1].Value, out var id)) continue;
            var name = entry.Groups[2].Value.Trim();
            var alias = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (alias >= 0 && name.EndsWith(")", StringComparison.Ordinal)) name = name[(alias + 2)..^1];
            if (name.Length != 0) heroNames[id] = name;
        }

        content = Regex.Replace(content, @"(?m)^(?:# 맵 이름 · |# 캐릭터 · )[^\r\n]*\r?\n", "");
        return Regex.Replace(content, @"(?m)^\[([^\r\n]+)\]\r?\n(?:(?!^\[)[\s\S])*", section =>
        {
            var name = section.Groups[1].Value;
            var block = section.Value;
            var map = name.StartsWith("Map ", StringComparison.Ordinal) && int.TryParse(name[4..], out _);
            if (map)
            {
                var id = int.Parse(name[4..], CultureInfo.InvariantCulture);
                var title = LibraryMapNames.FirstOrDefault(pair => pair.Value == id).Key;
                if (title != null)
                {
                    var headerEnd = block.IndexOf('\n') + 1;
                    block = block.Insert(headerEnd, mapPrefix + title + newline);
                }
            }

            return Regex.Replace(block, @"(?m)^([^#=\[\r\n]+?)[\t ]*=[\t ]*([^\r\n]*)", entry =>
            {
                var key = entry.Groups[1].Value.Trim();
                if (!(map && key is "Preferred" or "Excluded") && name != "캐릭터 ID" &&
                    !(name == "정렬" && key == "ManualOrder") &&
                    !(name == "조건 선택" && key == "EmergencySupports")) return entry.Value;
                var names = ParseMapHeroIds(entry.Groups[2].Value)
                    .Where(heroNames.ContainsKey).Select(id => $"{id} {heroNames[id]}").ToArray();
                return names.Length == 0 ? entry.Value :
                    heroPrefix + string.Join(" / ", names) + newline + entry.Value;
            });
        });
    }

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
