using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal static class Program
{
    private const string Title = "Astral Party Character Select Setup";
    private const string GameExe = "AstralParty_INT.exe";
    private const string PluginDll = "AstralParty_CharacterSelect.dll";
    private const string BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip";
    private const string BepInExHash = "F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A";

    [STAThread]
    private static int Main(string[] args)
    {
        var unattended = args is ["--game-dir", _, "--quiet"];
        try
        {
            ApplicationConfiguration.Initialize();
            var game = args.Length switch
            {
                0 => PickFolder(),
                2 when args[0] == "--game-dir" => args[1],
                3 when unattended => args[1],
                _ => throw new ArgumentException("Usage: Setup.exe [--game-dir <folder> [--quiet]]")
            };
            if (game == null) return 0;
            game = Path.GetFullPath(game);
            if (!IsGameFolder(game)) throw new IOException("AstralParty_INT.exe와 GameAssembly.dll이 있는 게임 폴더를 선택하세요.");
            RejectRunningGame(game);

            var bepinex = Path.Combine(game, "BepInEx");
            var existing = File.Exists(Path.Combine(bepinex, "core", "BepInEx.Unity.IL2CPP.dll")) &&
                File.Exists(Path.Combine(bepinex, "core", "BepInEx.Core.dll")) &&
                File.Exists(Path.Combine(game, "doorstop_config.ini"));
            if (!existing && (Directory.Exists(Path.Combine(bepinex, "core")) ||
                File.Exists(Path.Combine(game, "winhttp.dll")) || File.Exists(Path.Combine(game, "doorstop_config.ini"))))
                throw new IOException("기존 로더 파일이 있지만 BepInEx 6 IL2CPP 설치로 확인되지 않았습니다. 기존 설치를 확인하세요.");

            var summary = existing ? "기존 BepInEx를 사용하고 Character Select DLL을 설치합니다."
                : "공식 BepInEx 6 IL2CPP Windows x64 빌드 788과 Character Select DLL을 설치합니다.";
            if (!unattended && MessageBox.Show(summary + "\n\n" + game, Title,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return 0;

            Install(game, existing);
            if (!unattended) MessageBox.Show("설치가 완료됐습니다. Steam에서 게임을 실행하세요.", Title);
            return 0;
        }
        catch (Exception error)
        {
            if (!unattended) MessageBox.Show(error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static string? PickFolder()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "아스트랄 파티 게임 폴더",
            UseDescriptionForTitle = true,
            SelectedPath = FindSteamGame() ?? ""
        };
        return picker.ShowDialog() == DialogResult.OK ? picker.SelectedPath : null;
    }

    private static string? FindSteamGame()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrWhiteSpace(steam)) return null;
        var libraries = new List<string> { steam };
        var folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(folders))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(folders), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (var library in libraries)
        {
            var root = Path.Combine(library, "steamapps", "common", "Astral Party");
            if (!Directory.Exists(root)) continue;
            if (IsGameFolder(root)) return root;
            var match = Directory.EnumerateDirectories(root).FirstOrDefault(IsGameFolder);
            if (match != null) return match;
        }
        return null;
    }

    private static bool IsGameFolder(string path) =>
        File.Exists(Path.Combine(path, GameExe)) && File.Exists(Path.Combine(path, "GameAssembly.dll"));

    private static void RejectRunningGame(string game)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)))
        {
            using (process)
            {
                if (!process.HasExited && string.Equals(process.MainModule?.FileName,
                    Path.Combine(game, GameExe), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("게임을 종료한 다음 설치를 다시 실행하세요.");
            }
        }
    }

    private static void Install(string game, bool existing)
    {
        using var plugin = Assembly.GetExecutingAssembly().GetManifestResourceStream("CharacterSelect.Plugin.dll")
            ?? throw new IOException("설치 파일에 플러그인 DLL이 없습니다.");
        var plugins = Path.Combine(game, "BepInEx", "plugins");
        var matches = Directory.Exists(plugins)
            ? Directory.GetFiles(plugins, PluginDll, SearchOption.AllDirectories)
            : [];
        if (matches.Length > 1)
            throw new IOException("Character Select DLL이 여러 곳에 있습니다. 한 사본만 남긴 뒤 다시 설치하세요.");
        var target = matches.SingleOrDefault() ?? Path.Combine(plugins, "AstralParty_CharacterSelect", PluginDll);
        var created = new List<string>();
        string? archive = null;
        string? staged = null;
        try
        {
            if (!existing)
            {
                archive = Path.GetTempFileName();
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                using (var download = http.GetStreamAsync(BepInExUrl).GetAwaiter().GetResult())
                using (var file = File.Create(archive))
                    download.CopyTo(file);
                using (var file = File.OpenRead(archive))
                    if (!Convert.ToHexString(SHA256.HashData(file)).Equals(BepInExHash, StringComparison.Ordinal))
                        throw new IOException("BepInEx 다운로드의 SHA-256이 예상과 다릅니다.");
            }

            RejectRunningGame(game);
            if (archive != null) InstallBepInEx(archive, game, created);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            staged = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = File.Create(staged)) plugin.CopyTo(file);
            File.Move(staged, target, overwrite: true);
        }
        catch
        {
            foreach (var path in created.AsEnumerable().Reverse()) File.Delete(path);
            throw;
        }
        finally
        {
            if (archive != null) File.Delete(archive);
            if (staged != null) File.Delete(staged);
        }
    }

    private static void InstallBepInEx(string archive, string game, List<string> created)
    {
        using var zip = ZipFile.OpenRead(archive);
        var root = Path.TrimEndingDirectorySeparator(game) + Path.DirectorySeparatorChar;
        var files = zip.Entries.Where(entry => entry.Name.Length > 0)
            .Select(entry => (Entry: entry, Path: Path.GetFullPath(Path.Combine(root, entry.FullName)))).ToArray();
        foreach (var file in files)
            if (!file.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || File.Exists(file.Path))
                throw new IOException("BepInEx 설치 파일이 기존 파일과 충돌합니다. " + file.Entry.FullName);
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            using var output = new FileStream(file.Path, FileMode.CreateNew);
            created.Add(file.Path);
            using var input = file.Entry.Open();
            input.CopyTo(output);
        }
    }
}
