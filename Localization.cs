using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private enum UiLanguage { Auto, Korean, English, Japanese, SimplifiedChinese, TraditionalChinese }
    private readonly ConfigEntry<UiLanguage> languageSetting;
    private UiLanguage detectedLanguage = UiLanguage.English;
    private string displayedLanguage = "";

    private bool ModeAvailable(SortMode mode) => mode switch
    {
        SortMode.Custom => customOrder.Value.Split(',').Any(value =>
            TagKeys.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase) ||
            GroupNames.Contains(value.Trim()) || LegacySeatKeys.Contains(value.Trim()) ||
            tagLabels.Any(label => label.Value.Length > 0 && label.Value == value.Trim())),
        SortMode.Manual => manualOrder.Value.Split(',').Any(value => int.TryParse(value, out var id) && id > 0),
        _ => true
    };

    private void DetectLanguage(IntPtr panel)
    {
        if (languageSetting.Value != UiLanguage.Auto)
            detectedLanguage = languageSetting.Value;
        else if (panel != IntPtr.Zero)
        {
            var root = Call(panel, "get_UIObject", optional: true);
            if (root != IntPtr.Zero)
            {
                // Inspect native UI text before adding our own labels.
                var text = string.Join(" ", VisibleDetailNodes(root)
                    .Where(node => !IsOurLabel(node))
                    .Select(node => TextValue(Call(node, "get_text", optional: true))));
                detectedLanguage = text.Any(c => c >= 0xac00 && c <= 0xd7a3) ? UiLanguage.Korean
                    : text.Any(c => c >= 0x3040 && c <= 0x30ff) ? UiLanguage.Japanese
                    : text.Contains("選擇") || text.Contains("確認") || text.Contains("檔案") ? UiLanguage.TraditionalChinese
                    : text.Any(c => c >= 0x4e00 && c <= 0x9fff) ? UiLanguage.SimplifiedChinese
                    : Application.systemLanguage switch
                    {
                        SystemLanguage.Korean => UiLanguage.Korean,
                        SystemLanguage.Japanese => UiLanguage.Japanese,
                        SystemLanguage.ChineseTraditional => UiLanguage.TraditionalChinese,
                        SystemLanguage.ChineseSimplified => UiLanguage.SimplifiedChinese,
                        _ => UiLanguage.English
                    };
                if ((text.Contains("Select") || text.Contains("Profile") || text.Contains("Confirm")) &&
                    !text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u4e00' and <= '\u9fff' or >= '\uac00' and <= '\ud7a3'))
                    detectedLanguage = UiLanguage.English;
            }
        }
        var language = detectedLanguage.ToString();
        if (displayedLanguage == language) return;
        Detach();
        displayedLanguage = language;
    }

    private bool IsOurLabel(IntPtr node)
    {
        for (var parent = node; parent != IntPtr.Zero; parent = Call(parent, "get_parent", optional: true))
            if (parent == container) return true;
        return false;
    }

    private string Localized(int index) => (detectedLanguage switch
    {
        UiLanguage.Korean => KoreanLabels,
        UiLanguage.Japanese => JapaneseLabels,
        UiLanguage.SimplifiedChinese => ChineseLabels,
        UiLanguage.TraditionalChinese => TraditionalLabels,
        _ => EnglishLabels
    })[index];

    private static readonly string[] KoreanLabels = { "딜러", "서포터", "카드특화", "탱커", "랜덤", "정렬", "자리", "역할", "사용자", "수동", "기본" };
    private static readonly string[] EnglishLabels = { "Dealer", "Support", "Card", "Tank", "Random", "Sort", "Seat", "Role", "Custom", "Manual", "Default" };
    private static readonly string[] JapaneseLabels = { "火力", "支援", "カード", "タンク", "ランダム", "並び", "席順", "役割", "カスタム", "手動", "標準" };
    private static readonly string[] ChineseLabels = { "输出", "辅助", "卡牌", "坦克", "随机", "排序", "座位", "定位", "自定义", "手动", "默认" };
    private static readonly string[] TraditionalLabels = { "輸出", "輔助", "卡牌", "坦克", "隨機", "排序", "座位", "定位", "自訂", "手動", "預設" };
}
