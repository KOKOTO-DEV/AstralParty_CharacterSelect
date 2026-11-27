using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private readonly ConfigEntry<bool> conditionalEnabled;
    private readonly ConfigEntry<bool> emergencyEnabled;
    private readonly ConfigEntry<int> emergencyMinimum;
    private readonly ConfigEntry<string> emergencyRoles;
    private readonly ConfigEntry<string> emergencyCandidates;
    private readonly ConfigEntry<string> combinationOrder;
    private readonly ConfigEntry<string> disabledCombinations;
    private IntPtr conditionalPanel;
    private IntPtr gameLogicClass;
    private int pendingSelection;
    private bool emergencyChecked;
    private bool sawMoreThanFiveSeconds;
    private bool conditionalArmed;
    private bool warnedMissingSelectionTimer;
    private bool selectionAcknowledged;
    private int lastRecommendationAttempt;
    private float selectionDeadline;
    private int[] previousTeam = Array.Empty<int>();
    private IntPtr recommendationLabel;
    private string recommendationReason = "";
    private float recommendationWidth;

    private void SetRecommendationReason(string reason)
    {
        recommendationReason = reason;
        if (recommendationLabel == IntPtr.Zero) return;
        WriteRecommendationText(reason);
        if (FloatValue(Call(recommendationLabel, "get_textWidth")) <= recommendationWidth) return;
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(reason);
        for (var index = boundaries.Length - 1; index >= 0; index--)
        {
            WriteRecommendationText(reason[..boundaries[index]].TrimEnd() + "…");
            if (FloatValue(Call(recommendationLabel, "get_textWidth")) <= recommendationWidth) return;
        }
        WriteRecommendationText("");
    }

    private void WriteRecommendationText(string text)
    {
        void* value = (void*)IL2CPP.ManagedStringToIl2Cpp(text);
        Exact(recommendationLabel, "set_text", "System.String", &value);
    }
    private string ConditionalLabel() => detectedLanguage switch
    {
        UiLanguage.Korean => "조건 선택",
        UiLanguage.Japanese => "条件選択",
        UiLanguage.SimplifiedChinese => "条件选择",
        UiLanguage.TraditionalChinese => "條件選擇",
        _ => "Team Pick"
    };

    private void UpdateConditionalSelection(IntPtr panel, IntPtr list, int[] order,
        Dictionary<int, IntPtr> rows, HeroTags tags, bool virtualList, bool listMatches, bool requested)
    {
        if (!listMatches) return;
        var root = Call(panel, "get_UIObject");
        var confirm = ReadField(root, "btn_SureHero");
        if (!BoolValue(Call(confirm, "get_onStage")) || !BoolValue(Call(confirm, "get_touchable"))) return;
        var checkEmergency = CrossedEmergencyThreshold(panel);
        if (!requested && !checkEmergency && !conditionalArmed) return;
        var room = ReadCurrentRoom();
        var self = room == IntPtr.Zero ? IntPtr.Zero : Call(room, "GetSelfInfo");
        if (self == IntPtr.Zero) return;
        var own = ReadOwnChoice(room, self);
        if (!own.HasValue) return;
        var slot = IntValue(Call(self, "get_Slot"));
        if (slot is < 0 or > 3) return;
        var roomChoices = ReadRoomChoices(room, self);
        var team = roomChoices.Concat(order.Where(id => id != own.Value && IsChosen(panel, id)))
            .Distinct().OrderBy(id => id).ToArray();

        var emergencyOnly = !requested && !conditionalArmed;
        if (emergencyOnly && own.Value != 0) return;
        if (!requested && conditionalArmed)
        {
            if (own.Value == pendingSelection) selectionAcknowledged = true;
            if (!selectionAcknowledged)
            {
                if (Time.unscaledTime >= selectionDeadline) conditionalArmed = false;
                return;
            }
            if (own.Value != pendingSelection)
            {
                conditionalArmed = false;
                SetRecommendationReason("");
                return;
            }
        }
        var teamChanged = !team.SequenceEqual(previousTeam);
        if (requested && conditionalArmed && !selectionAcknowledged &&
            own.Value != pendingSelection && Time.unscaledTime < selectionDeadline) return;
        var advance = requested && conditionalArmed && !teamChanged &&
            own.Value == pendingSelection;
        if (requested)
        {
            if (!advance) lastRecommendationAttempt = 0;
            conditionalArmed = true;
            pendingSelection = own.Value;
            selectionAcknowledged = true;
        }
        if (!requested && !checkEmergency) return;
        previousTeam = team;
        var candidates = order.Where(id => tags.HasTag(id) &&
            (id == own.Value || IsAvailable(panel, rows[id], id))).ToArray();
        var (preferred, excluded) = ReadMatchMapRules(panel);
        candidates = candidates.Where(id => !excluded.Contains(id)).ToArray();
        var emergencyPick = checkEmergency && team.Length > 0 ? PickForBalance(team, candidates, tags, own.Value) : 0;
        var pick = emergencyPick;
        var reason = emergencyPick != 0 ? EmergencySettingsLabel() : "";
        if (pick == 0 && requested)
        {
            var eligible = order.Where(id => tags.HasTag(id) && !excluded.Contains(id) &&
                (id == own.Value || IsEligibleForPick(panel, rows[id], id))).ToArray();
            var ranked = RankForTeam(team, eligible, preferred, tags, (HeroTag)slot, out var matches)
                .OrderBy(id => team.Contains(id)).ToArray();
            if (recommendationDiagnostics.Value)
            {
                logger.LogInfo($"Team Pick P{slot + 1}: team [{string.Join(", ", team)}], " +
                    $"room choices [{string.Join(", ", roomChoices)}], " +
                    $"available tagged heroes [{string.Join(", ", candidates)}], " +
                    $"map exclusions [{string.Join(", ", excluded)}], " +
                    $"synergy candidates [{string.Join(", ", ranked.Where(matches.ContainsKey))}], " +
                    $"map candidates [{string.Join(", ", ranked.Where(preferred.Contains))}], " +
                    $"seat candidates [{string.Join(", ", ranked.Where(id => tags.HasTag(id, (HeroTag)slot)))}].");
            }
            var currentRank = advance ? Array.IndexOf(ranked,
                lastRecommendationAttempt != 0 ? lastRecommendationAttempt : own.Value) : -1;
            var nextRank = ranked.Length == 0 ? -1 : (currentRank + 1) % ranked.Length;
            pick = nextRank < 0 ? 0 : ranked[nextRank];
            if (pick != 0)
            {
                var rankLabel = EditorText($"{nextRank + 1}/{ranked.Length} 순위",
                    $"Rank {nextRank + 1}/{ranked.Length}", $"順位 {nextRank + 1}/{ranked.Length}",
                    $"排名 {nextRank + 1}/{ranked.Length}", $"排名 {nextRank + 1}/{ranked.Length}");
                reason = rankLabel + " · " + (matches.TryGetValue(pick, out var name) ? name
                    : preferred.Contains(pick)
                        ? EditorText("맵 추천", "Map recommendation", "マップ推薦", "地图推荐", "地圖推薦")
                        : EditorText("자리 추천", "Seat recommendation", "席順推薦", "座位推荐", "座位推薦"));
            }
        }
        if (pick == 0)
        {
            if (requested) SetRecommendationReason(EditorText("해당 조건 없음", "No matching rule", "該当する条件なし", "没有符合的条件", "沒有符合的條件"));
            return;
        }
        if (emergencyPick == 0) lastRecommendationAttempt = pick;
        if (pick == own.Value)
        {
            if (requested) SetRecommendationReason(reason);
            return;
        }
        if (emergencyPick == 0)
        {
            if (team.Contains(pick))
            {
                SelectRow(panel, list, Array.IndexOf(order, pick), pick, virtualList);
                SetRecommendationReason(reason);
                return;
            }
        }
        if (!SelectRow(panel, list, Array.IndexOf(order, pick), pick, virtualList)) return;
        SetRecommendationReason(reason);
        if (emergencyPick != 0)
        {
            conditionalArmed = false;
            lastRecommendationAttempt = 0;
        }
        pendingSelection = pick;
        selectionAcknowledged = false;
        selectionDeadline = Time.unscaledTime + 2f;
    }

    private bool CrossedEmergencyThreshold(IntPtr panel)
    {
        if (emergencyChecked || !emergencyEnabled.Value ||
            ReadSelectionSeconds(panel) is not float remaining) return false;
        if (remaining > 5f)
        {
            sawMoreThanFiveSeconds = true;
            return false;
        }
        emergencyChecked = true;
        return sawMoreThanFiveSeconds && remaining > 0f;
    }

    private float? ReadSelectionSeconds(IntPtr panel)
    {
        var timer = ReadField(panel, "timeLimiter", optional: true);
        if (timer == IntPtr.Zero) return null;
        var remaining = Call(timer, "GetTimeRemaining", optional: true);
        if (ClassName(remaining) == "Single") return FloatValue(remaining);
        if (ClassName(remaining) == "Double")
            return (float)*(double*)IL2CPP.il2cpp_object_unbox(remaining);
        if (!warnedMissingSelectionTimer)
        {
            warnedMissingSelectionTimer = true;
            logger.LogWarning("Emergency support: selection timer does not expose GetTimeRemaining.");
        }
        return null;
    }

    private int PickForBalance(int[] team, int[] candidates, HeroTags tags, int own)
    {
        var roles = ReadEmergencyRoles();
        var missing = roles.ToDictionary(role => role, role => emergencyMinimum.Value -
            team.Count(id => tags.HasTag(id, role)));
        int Unfilled(int hero) => roles.Sum(role => Math.Max(0,
            missing[role] - (tags.HasTag(hero, role) ? 1 : 0)));

        var current = Unfilled(own);
        if (current == 0) return 0;
        var allowed = ParseMapHeroIds(emergencyCandidates.Value).ToHashSet();
        var ranked = candidates.Where(id => id != own && !team.Contains(id) && allowed.Contains(id))
            .Select(id => (Id: id, Missing: Unfilled(id)))
            .Where(candidate => candidate.Missing < current)
            .OrderBy(candidate => candidate.Missing).ToArray();
        if (ranked.Length == 0) return 0;
        return ranked[System.Random.Shared.Next(ranked.Count(candidate => candidate.Missing == ranked[0].Missing))].Id;
    }

    private HeroTag[] ReadEmergencyRoles()
    {
        var keys = emergencyRoles.Value.Split(',', StringSplitOptions.TrimEntries);
        return RoleOrder.Take(4).Where(tag => keys.Contains(TagKeys[(int)tag], StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private int[] RankForTeam(int[] team, int[] candidates, int[] preferred, HeroTags tags, HeroTag seat,
        out Dictionary<int, string> reasons)
    {
        var disabled = DisabledSets();
        var fixedSeats = FixedSeatSets();
        var matches = new Dictionary<int, int>();
        reasons = new Dictionary<int, string>();
        var names = ReadCombinationNames();
        foreach (var combination in ReadCombinations().DistinctBy(CombinationKey))
        {
            var key = CombinationKey(combination);
            if (disabled.Contains(key)) continue;
            foreach (var id in combination)
            {
                if (!candidates.Contains(id) ||
                    (fixedSeats.Contains(key) && !tags.HasTag(id, seat))) continue;
                if (!combination.Any(member => member != id && team.Contains(member))) continue;
                matches[id] = matches.GetValueOrDefault(id) + 1;
                if (!reasons.ContainsKey(id))
                    reasons[id] = names.TryGetValue(key, out var name) && ShowCombinationName(key, name)
                        ? name : string.Join(" + ", combination.Select(ShortCombinationHeroName));
            }
        }

        var mapRanks = preferred.Select((id, index) => (id, index))
            .DistinctBy(entry => entry.id).ToDictionary(entry => entry.id, entry => entry.index);
        var preferredSeatRole = seat is HeroTag.Player1 or HeroTag.Player2 ? HeroTag.Support : HeroTag.Damage;
        return candidates.Where(id => matches.ContainsKey(id) || mapRanks.ContainsKey(id) || tags.HasTag(id, seat))
            .OrderByDescending(id => matches.GetValueOrDefault(id))
            .ThenBy(id => mapRanks.GetValueOrDefault(id, int.MaxValue))
            .ThenByDescending(id => tags.HasTag(id, seat))
            .ThenByDescending(id => tags.HasTag(id, seat) && tags.HasTag(id, preferredSeatRole))
            .ToArray();
    }

    private static bool IsChosen(IntPtr panel, int id)
    {
        void* argument = &id;
        return BoolValue(Call(panel, "GetHeroHasChoice", &argument));
    }

    private int? ReadOwnChoice(IntPtr room, IntPtr self)
    {
        var bar = ReadChoiceBar(room, self);
        return !bar.HasValue ? null : bar.Value == IntPtr.Zero ? 0 : IntValue(Call(bar.Value, "get_HeroId"));
    }

    private static IntPtr? ReadChoiceBar(IntPtr room, IntPtr player)
    {
        var boxedId = Call(player, "get_Id");
        if (ClassName(boxedId) != "Int64") return null;
        var id = *(long*)IL2CPP.il2cpp_object_unbox(boxedId);
        void* argument = &id;
        return Exact(room, "GetHeroBarById", "System.Int64", &argument);
    }

    private static int[] ReadRoomChoices(IntPtr room, IntPtr self)
    {
        var ownBar = ReadChoiceBar(room, self);
        return ReadRoomChoiceBars(room).Where(bar => bar != ownBar)
            .Select(bar => Call(bar, "get_HeroId", optional: true))
            .Where(hero => ClassName(hero) == "Int32").Select(IntValue).Where(id => id > 0).ToArray();
    }

    private static IntPtr[] ReadRoomChoiceBars(IntPtr room)
    {
        var box = Call(room, "get_Box", optional: true);
        if (box == IntPtr.Zero) return Array.Empty<IntPtr>();
        var values = Call(box, "get_Values", optional: true);
        if (values == IntPtr.Zero) values = box;
        var count = Call(values, "get_Count", optional: true);
        if (ClassName(count) != "Int32" || IntValue(count) is < 1 or > 4)
            return Array.Empty<IntPtr>();
        var item = FindMethod(IL2CPP.il2cpp_object_get_class(values), "get_Item", "System.Int32");
        // Collection interfaces return a boxed enumerator for value-type implementations.
        var enumerator = item == IntPtr.Zero
            ? Call(values, "System.Collections.IEnumerable.GetEnumerator", optional: true)
            : IntPtr.Zero;
        if (item == IntPtr.Zero && enumerator == IntPtr.Zero) return Array.Empty<IntPtr>();
        var enumeratorType = enumerator == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_object_get_class(enumerator);
        var moveNext = enumeratorType == IntPtr.Zero ? IntPtr.Zero : FindMethod(enumeratorType, "MoveNext", "");
        var current = enumeratorType == IntPtr.Zero ? IntPtr.Zero :
            FindMethod(enumeratorType, "System.Collections.IEnumerator.get_Current", "");
        if (item == IntPtr.Zero && (moveNext == IntPtr.Zero || current == IntPtr.Zero)) return Array.Empty<IntPtr>();
        var iterator = enumeratorType != IntPtr.Zero && IL2CPP.il2cpp_class_is_valuetype(enumeratorType)
            ? IL2CPP.il2cpp_object_unbox(enumerator) : enumerator;
        var result = new List<IntPtr>(4);
        for (var index = 0; index < IntValue(count); index++)
        {
            IntPtr bar;
            if (item != IntPtr.Zero)
            {
                var position = index;
                void* argument = &position;
                bar = Invoke(item, values, &argument);
            }
            else
            {
                var next = Invoke(moveNext, iterator, null);
                if (ClassName(next) != "Boolean" || !BoolValue(next)) break;
                bar = Invoke(current, iterator, null);
            }
            result.Add(bar);
        }
        return result.ToArray();
    }

    private IntPtr ReadCurrentRoom()
    {
        if (gameLogicClass == IntPtr.Zero)
        {
            var assemblies = Assemblies(IL2CPP.il2cpp_domain_get(), out var count);
            for (nuint index = 0; index < count && gameLogicClass == IntPtr.Zero; index++)
                gameLogicClass = IL2CPP.il2cpp_class_from_name(IL2CPP.il2cpp_assembly_get_image(assemblies[index]),
                    "GameLogic", "GameLogicManager");
            if (gameLogicClass == IntPtr.Zero) return IntPtr.Zero;
        }
        var field = IL2CPP.il2cpp_class_get_field_from_name(gameLogicClass, "_inst");
        if (field == IntPtr.Zero) return IntPtr.Zero;
        IntPtr manager = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(field, &manager);
        if (manager == IntPtr.Zero) return IntPtr.Zero;
        var roomLogic = ReadField(manager, "<room>k__BackingField");
        if (roomLogic == IntPtr.Zero) return IntPtr.Zero;
        var controller = ReadField(roomLogic, "roomController");
        return controller == IntPtr.Zero ? IntPtr.Zero : ReadField(controller, "_LocalRoom");
    }
}
