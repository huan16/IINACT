using System.IO.Compression;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace FetchDependencies;

public partial class FetchDependencies
{
    private const string VersionUrlGlobal = "https://www.iinact.com/updater/version";
    private const string VersionUrlChinese = "https://cdn.diemoe.net/files/ACT.DieMoe/Packs/FFXIV_ACT_Plugin/chinese.version";
    private const string PluginUrlGlobal = "https://www.iinact.com/updater/download";
    private const string PluginUrlChinese = "https://cdn.diemoe.net/files/ACT.DieMoe/Packs/FFXIV_ACT_Plugin/chinese.zip";
    private const string OpcodesDotJsoncGlobal = "https://raw.githubusercontent.com/OverlayPlugin/OverlayPlugin/main/OverlayPlugin.Core/resources/opcodes.jsonc";
    private const string OpcodesDotJsoncChinese = "https://assets.diemoe.net/OverlayPlugin/OverlayPlugin.Core/resources/opcodes.jsonc";

    // "display_at=2.7.1.9-CN7.01"
    [GeneratedRegex(@"display_at\s*=\s*(\d+\.\d+\.\d+\.\d+)", RegexOptions.Multiline)]
    private static partial Regex ChineseVersionRegex();

    private Version PluginVersion { get; }
    private string DependenciesDir { get; }
    private bool IsChinese { get; }
    private HttpClient HttpClient { get; }
    private IPluginLog PluginLog { get; }

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
                var regex = ChineseVersionRegex();
                var match = regex.Match(remoteVersionString);
                if (match.Success)
                {
                    remoteVersionString = match.Groups[1].Value;
                }
                else
                {
                    PluginLog.Error($"Failed to parse Chinese Plugin version string: {remoteVersionString}");
                    return false;
                }
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
