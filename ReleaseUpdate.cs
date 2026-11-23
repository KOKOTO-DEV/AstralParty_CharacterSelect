using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;

namespace AstralPartyHeroGroups;

internal static class ReleaseUpdate
{
    private const string LatestApi = "https://api.github.com/repos/KOKOTO-DEV/AstralParty_CharacterSelect/releases/latest";
    private const string LatestRelease = "https://github.com/KOKOTO-DEV/AstralParty_CharacterSelect/releases/latest";

    internal static async Task CheckAsync(ManualLogSource log, CancellationToken cancellation)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AstralParty-CharacterSelect/" + CharacterSelectPlugin.PluginVersion);
            using var response = await http.GetAsync(LatestApi, cancellation).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
            if (!release.RootElement.TryGetProperty("tag_name", out var tag) ||
                !Version.TryParse(tag.GetString()?.TrimStart('v', 'V'), out var latest) ||
                latest <= Version.Parse(CharacterSelectPlugin.PluginVersion))
                return;

            await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                log.LogInfo($"Character Select {latest} is available. {LatestRelease}");
                const uint yesNoQuestion = 0x24;
                const uint defaultNo = 0x100;
                const int yes = 6;
                var message = $"Character Select {latest} is available.\nOpen the download page? Close the game before installing.";
                if (MessageBoxW(IntPtr.Zero, message, "Astral Party Character Select", yesNoQuestion | defaultNo) == yes &&
                    !cancellation.IsCancellationRequested)
                    Process.Start(new ProcessStartInfo(LatestRelease) { UseShellExecute = true });
            }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            log.LogDebug($"Character Select update check failed: {error.Message}");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
