using System.IO.Compression;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace FetchDependencies;

public partial class FetchDependencies
{
    private const string VersionUrlGlobal = "https://www.iinact.com/updater/version";
    private const string VersionUrlChinese = "https://cdn.diemoe.net/files/ACT.DieMoe/Packs/FFXIV_ACT_Plugin/chinese.version";
    private const string PluginUrlGlobal = "https://www.iinact.com/updater/download";
    private const string PluginUrlChinese = "https://cdn.diemoe.net/files/ACT.DieMoe/Packs/FFXIV_ACT_Plugin/chinese.zip";
    private const string OpcodesDotJsoncGlobal = "https://raw.githubusercontent.com/OverlayPlugin/OverlayPlugin/main/OverlayPlugin.Core/resources/opcodes.jsonc";
    private const string OpcodesDotJsoncChinese = "https://assets.diemoe.net/OverlayPlugin/OverlayPlugin.Core/resources/opcodes.jsonc";

    // "build_version=20241022.001"
    [GeneratedRegex(@"build_version\s*=\s*([0-9.]+)", RegexOptions.Multiline)]
    private static partial Regex DieMoeBuildVersionRegex();

    private Version PluginVersion { get; }
    private string DependenciesDir { get; }
    private bool IsChinese { get; }
    private HttpClient HttpClient { get; }
    private IPluginLog PluginLog { get; }

    public static string RemoteDieMoeBuildVersion = "";

    public FetchDependencies(Version version, string assemblyDir, bool isChinese, HttpClient httpClient, IPluginLog pluginLog)
    {
        PluginVersion = version;
        DependenciesDir = assemblyDir;
        IsChinese = isChinese;
        HttpClient = httpClient;
        PluginLog = pluginLog;
    }

    public void GetFfxivPlugin()
    {
        var pluginZipPath = Path.Combine(DependenciesDir, "FFXIV_ACT_Plugin.zip");
        var pluginPath = Path.Combine(DependenciesDir, "FFXIV_ACT_Plugin.dll");
        var deucalionPath = Path.Combine(DependenciesDir, "deucalion-1.1.0.distrib.dll");
        
        if (!NeedsUpdate(pluginPath))
            return;

        if (!File.Exists(pluginZipPath))
            DownloadFile(IsChinese ? PluginUrlChinese : PluginUrlGlobal, pluginZipPath);
        try
        {
            ZipFile.ExtractToDirectory(pluginZipPath, DependenciesDir, true);
        }
        catch (InvalidDataException)
        {
            File.Delete(pluginZipPath);
            DownloadFile(IsChinese ? PluginUrlChinese : PluginUrlGlobal, pluginZipPath);
            ZipFile.ExtractToDirectory(pluginZipPath, DependenciesDir, true);
        }
        File.Delete(pluginZipPath);

        foreach (var deucalionDll in Directory.GetFiles(DependenciesDir, "deucalion*.dll"))
            File.Delete(deucalionDll);

        var patcher = new Patcher(PluginVersion, DependenciesDir, PluginLog);
        patcher.MainPlugin();
        patcher.LogFilePlugin();
        patcher.MemoryPlugin();
        patcher.MachinaFFXIV();
    }

    private bool NeedsUpdate(string dllPath)
    {
        if (!File.Exists(dllPath)) return true;
        try
        {
            using var plugin = new TargetAssembly(dllPath);

            if (!plugin.ApiVersionMatches())
                return true;
            
            using var cancelAfterDelay = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var remoteVersionString = HttpClient
                                      .GetStringAsync(IsChinese ? VersionUrlChinese : VersionUrlGlobal,
                                                      cancelAfterDelay.Token).Result;
            if (IsChinese) {
                var match = DieMoeBuildVersionRegex().Match(remoteVersionString);
                var buildVersion = match.Success ? match.Groups[1].Value : string.Empty;
                if (buildVersion.IsNullOrEmpty())
                {
                    PluginLog.Error($"Failed to parse DieMoe Plugin version string: {remoteVersionString}");
                    return false;
                }

                RemoteDieMoeBuildVersion = buildVersion;
                var localVersion = plugin.GetDieMoeBuildVersion();
                return buildVersion != localVersion;
            }

            var remoteVersion = new Version(remoteVersionString);
            return remoteVersion > plugin.Version;
        }
        catch
        {
            return false;
        }
    }

    private void DownloadFile(string url, string path)
    {
        using var cancelAfterDelay = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var downloadStream = HttpClient
                                   .GetStreamAsync(url,
                                                   cancelAfterDelay.Token).Result;
        using var zipFileStream = new FileStream(path, FileMode.Create);
        downloadStream.CopyTo(zipFileStream);
        zipFileStream.Close();
    }

    public string FetchOpcodesDotJsonc()
    {
        var url = IsChinese ? OpcodesDotJsoncChinese : OpcodesDotJsoncGlobal;
        using var cancelAfterDelay = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return HttpClient.GetStringAsync(url, cancelAfterDelay.Token).Result;
    }
}
