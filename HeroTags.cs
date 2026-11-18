using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralPartyHeroGroups;

internal enum HeroTag
{
    Player1, Player2, Player3, Player4, Damage, Support, Cards, Tank
}

internal sealed class HeroTags
{
    private readonly Dictionary<int, HashSet<HeroTag>> tagsByHero = new();

    public HeroTags(IEnumerable<(int HeroId, HeroTag Tag)> tags)
    {
        foreach (var (heroId, tag) in tags)
        {
            if (!tagsByHero.TryGetValue(heroId, out var heroTags))
                tagsByHero.Add(heroId, heroTags = new HashSet<HeroTag>());
            heroTags.Add(tag);
        }
    }

    public int[] Sort(IEnumerable<int> gameOrder, IReadOnlyList<HeroTag> priorities)
    {
        var roster = gameOrder.ToArray();
        var colors = ColorSlots(roster, priorities);
        return roster.OrderBy(id => colors.TryGetValue(id, out var slot) ? slot : Priority(id, priorities)).ToArray();
    }

    public Dictionary<int, int> ColorSlots(IReadOnlyList<int> roster, IReadOnlyList<HeroTag> priorities)
    {
        var coloredTags = priorities.Take(4).ToArray();
        var counts = coloredTags.Select(tag => roster.Count(id =>
            tagsByHero.TryGetValue(id, out var tags) && tags.Contains(tag))).ToArray();
        var slots = new Dictionary<int, int>();
        foreach (var id in roster)
        {
            if (!tagsByHero.TryGetValue(id, out var tags)) continue;
            var best = -1;
            for (var slot = 0; slot < coloredTags.Length; slot++)
                if (tags.Contains(coloredTags[slot]) && (best < 0 || counts[slot] < counts[best]))
                    best = slot;
            if (best >= 0) slots.Add(id, best);
        }
        return slots;
    }

    public int? Pick(HeroTag? tag, IEnumerable<int> selectableHeroes)
    {
        var candidates = selectableHeroes.Distinct()
            .Where(heroId => tagsByHero.TryGetValue(heroId, out var tags)
                && (!tag.HasValue || tags.Contains(tag.Value)))
            .ToArray();
        return candidates.Length == 0 ? null : candidates[Random.Shared.Next(candidates.Length)];
    }

    private int Priority(int heroId, IReadOnlyList<HeroTag> priorities)
    {
        if (tagsByHero.TryGetValue(heroId, out var tags))
        {
            for (var index = 0; index < priorities.Count; index++)
                if (tags.Contains(priorities[index])) return index;
        }
        return priorities.Count;
    }
}
