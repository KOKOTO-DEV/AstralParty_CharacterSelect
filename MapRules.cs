using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private static int ReadMatchMapId(IntPtr panel)
    {
        var room = ReadField(panel, "roomInfo", optional: true);
        if (room == IntPtr.Zero) return 0;
        var value = Call(room, "get_MapId", optional: true);
        return ClassName(value) == "Int32" ? IntValue(value) : 0;
    }

    private (int[] Preferred, HashSet<int> Excluded) ReadMatchMapRules(IntPtr panel)
    {
        var mapId = ReadMatchMapId(panel);
        if (mapId <= 0) return (Array.Empty<int>(), new HashSet<int>());
        var (preferred, excluded) = BindMapRules(mapId);
        return (ParseMapHeroIds(preferred.Value), ParseMapHeroIds(excluded.Value).ToHashSet());
    }

    private static int[] ParseMapHeroIds(string value) => value.Split(',')
        .Select(token => int.TryParse(token, out var id) ? id : 0)
        .Where(id => id > 0).Distinct().ToArray();
}
