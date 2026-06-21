using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public enum CommunityDataSource
{
    GitCode,
    GitHub
}

public static class CommunityToolService
{
    private const string UpstreamOwner = "luolangaga";
    private const string UpstreamRepo = "tubatoolsPlugin";
    private const string PluginsPath = "plugins";
    private const string GitCodeOwner = "gcw_uDDNaqJw";
    private const string GitCodeRepo = "tubatoolsPlugin";
    private const string GitCodeRawBase = $"https://gitcode.com/{GitCodeOwner}/{GitCodeRepo}/-/raw/main";
    private const string GitCodeApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}";
    private const string GitHubApiBase = $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}";

    public static CommunityDataSource CurrentSource { get; set; } = CommunityDataSource.GitCode;

    private static string ApiBase => CurrentSource == CommunityDataSource.GitCode ? GitCodeApiBase : GitHubApiBase;

    private static readonly HttpClient _apiClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static CommunityToolService()
    {
        _apiClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Community");
    }

    private static List<CommunityTool>? _cache;
    private static DateTimeOffset _cacheTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public static void InvalidateCache()
    {
        _cache = null;
        _cacheTime = DateTimeOffset.MinValue;
    }

    public static async Task<List<CommunityTool>> GetPluginsAsync(int page = 1, int perPage = 30, CancellationToken ct = default)
    {
        if (_cache is not null && (DateTimeOffset.UtcNow - _cacheTime) < CacheDuration)
            return _cache;

        var tools = new List<CommunityTool>();

        try
        {
            if (CurrentSource == CommunityDataSource.GitCode)
                tools = await GetPluginsFromGitCodeAsync(ct);
            else
                tools = await GetPluginsFromGitHubAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _cache = tools;
            _cacheTime = DateTimeOffset.UtcNow;
            throw new InvalidOperationException($"加载社区工具失败：{ex.Message}", ex);
        }

        _cache = tools;
        _cacheTime = DateTimeOffset.UtcNow;
        return tools;
    }

    private static async Task<List<CommunityTool>> GetPluginsFromGitCodeAsync(CancellationToken ct)
    {
        var tools = new List<CommunityTool>();

        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{GitCodeApiBase}/git/trees/main?recursive=1", ct);
        var doc = JsonDocument.Parse(json);
        var tree = doc.RootElement.GetProperty("tree");

        var pluginEntries = new List<(string Category, string ToolDir, string Sha)>();
        var shaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in tree.EnumerateArray())
        {
            var type = item.GetProperty("type").GetString() ?? "";
            var path = item.GetProperty("path").GetString() ?? "";
            var sha = item.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() ?? "" : "";

            if (!string.IsNullOrWhiteSpace(sha) && !string.IsNullOrWhiteSpace(path))
                shaMap[path] = sha;

            if (type != "blob" || !path.EndsWith("plugin.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.StartsWith(PluginsPath + "/", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = path.Split('/');
            if (parts.Length < 3) continue;

            var category = parts[1];
            var toolDir = parts[2];
            pluginEntries.Add((category, toolDir, sha));
        }

        foreach (var (category, toolDir, sha) in pluginEntries)
        {
            var pluginJson = await DownloadBlobAsync(sha, ct);
            if (pluginJson is null) continue;

            var tool = ParsePluginJson(pluginJson, category, toolDir, shaMap);
            if (tool is not null) tools.Add(tool);
        }

        return tools;
    }

    private static async Task<List<CommunityTool>> GetPluginsFromGitHubAsync(CancellationToken ct)
    {
        var tools = new List<CommunityTool>();

        var treeSha = await GetLatestTreeShaAsync(ct);
        if (treeSha is null) return tools;

        var pluginsTreeSha = await GetSubTreeShaAsync(treeSha, PluginsPath, ct);
        if (pluginsTreeSha is null) return tools;

        var categoryEntries = await EnumerateTreeAsync(pluginsTreeSha, ct);

        foreach (var catEntry in categoryEntries)
        {
            if (catEntry.Type != "tree") continue;

            var toolEntries = await EnumerateTreeAsync(catEntry.Sha, ct);
            foreach (var toolEntry in toolEntries)
            {
                if (toolEntry.Type != "tree") continue;

                var pluginJsonSha = await FindFileInTreeAsync(toolEntry.Sha, "plugin.json", ct);
                if (pluginJsonSha is null) continue;

                var pluginJson = await DownloadBlobAsync(pluginJsonSha, ct);
                if (pluginJson is null) continue;

                var tool = ParsePluginJson(pluginJson, catEntry.Path, toolEntry.Path);
                if (tool is not null) tools.Add(tool);
            }
        }

        return tools;
    }

    public static async Task<List<string>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var tools = await GetPluginsAsync(ct: ct);
        return tools.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
    }

    public static async Task<List<CommunityTool>> GetPluginsByCategoryAsync(string category, int page = 1, int perPage = 30, CancellationToken ct = default)
    {
        var all = await GetPluginsAsync(ct: ct);
        var filtered = all.Where(t => t.Category == category).ToList();
        return filtered.Skip((page - 1) * perPage).Take(perPage).ToList();
    }

    public static async Task<List<CommunityTool>> SearchPluginsAsync(string query, CancellationToken ct = default)
    {
        var all = await GetPluginsAsync(ct: ct);
        var q = query.Trim().ToLowerInvariant();
        return all.Where(t =>
            t.Name.ToLowerInvariant().Contains(q) ||
            (t.Description?.ToLowerInvariant().Contains(q) == true) ||
            t.Tags.Any(tag => tag.ToLowerInvariant().Contains(q)) ||
            t.Category.ToLowerInvariant().Contains(q)
        ).ToList();
    }

    public static CommunityToolInstallStatus CheckInstallStatus(CommunityTool tool)
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (toolsRoot is null) return CommunityToolInstallStatus.NotInstalled;

        var toolDir = Path.Combine(toolsRoot, tool.Category, tool.Id);
        if (!Directory.Exists(toolDir)) return CommunityToolInstallStatus.NotInstalled;

        return Directory.EnumerateFileSystemEntries(toolDir, "*", SearchOption.AllDirectories).Any()
            ? CommunityToolInstallStatus.Installed
            : CommunityToolInstallStatus.NotInstalled;
    }

    public static string? GetLocalPath(CommunityTool tool)
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (toolsRoot is null) return null;

        var toolDir = Path.Combine(toolsRoot, tool.Category, tool.Id);
        if (!Directory.Exists(toolDir)) return null;

        var launchTarget = tool.LaunchTarget;
        if (!string.IsNullOrWhiteSpace(launchTarget))
        {
            var directPath = Path.Combine(toolDir, launchTarget);
            if (File.Exists(directPath)) return directPath;

            var found = Directory.GetFiles(toolDir, launchTarget, SearchOption.AllDirectories);
            if (found.Length > 0) return found[0];
        }

        var exes = Directory.GetFiles(toolDir, "*.exe", SearchOption.AllDirectories);
        return exes.Length > 0 ? exes[0] : null;
    }

    public static async Task<string> InstallPluginAsync(CommunityTool tool, IProgress<ToolDownloadProgress>? progress, CancellationToken ct = default)
    {
        return await InstallPluginAsync(tool, null, progress, ct);
    }

    public static async Task<string> InstallPluginAsync(CommunityTool tool, string? overrideSourceUrl, IProgress<ToolDownloadProgress>? progress, CancellationToken ct = default)
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (toolsRoot is null) throw new InvalidOperationException("无法找到工具目录");

        var categoryDir = Path.Combine(toolsRoot, tool.Category);
        Directory.CreateDirectory(categoryDir);
        var toolDir = Path.Combine(categoryDir, tool.Id);

        if (Directory.Exists(toolDir))
        {
            try { Directory.Delete(toolDir, true); } catch { }
        }
        Directory.CreateDirectory(toolDir);

        var downloadSource = !string.IsNullOrWhiteSpace(tool.DownloadUrl) ? tool.DownloadUrl : "";
        var communityFile = !string.IsNullOrWhiteSpace(tool.File) ? tool.File : "";

        if (!string.IsNullOrWhiteSpace(overrideSourceUrl))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"TubaCommunity_{tool.Id}");
            var fileName = communityFile;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                try { fileName = new Uri(overrideSourceUrl).Segments.Last(); }
                catch { fileName = "download"; }
            }
            var archivePath = await ToolDownloaderService.DownloadToFileAsync(
                overrideSourceUrl, tempDir, fileName, progress, ct);
            await ToolDownloaderService.ExtractArchiveAsync(archivePath, toolDir, ct);
        }
        else if (!string.IsNullOrWhiteSpace(communityFile) && string.IsNullOrWhiteSpace(downloadSource))
        {
            var repoFileUrl = $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/{tool.RepoPath}/{communityFile}";
            var bestUrl = await ResolveCommunityFileUrlAsync(repoFileUrl, ct);
            var tempDir = Path.Combine(Path.GetTempPath(), $"TubaCommunity_{tool.Id}");
            var archivePath = await ToolDownloaderService.DownloadToFileAsync(
                bestUrl, tempDir, communityFile, progress, ct);
            await ToolDownloaderService.ExtractArchiveAsync(archivePath, toolDir, ct);
        }
        else if (ToolDownloaderService.IsGitCodeDir(downloadSource))
        {
            var result = await ToolDownloaderService.SyncToolFromGitCodeDirAsync(
                downloadSource[3..], toolDir, null,
                progress: new Progress<GitCodeDirProgress>(p =>
                    progress?.Report(new ToolDownloadProgress(0, 0, p.Percentage, 0, null))),
                ct);
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage ?? "下载失败");
        }
        else if (!string.IsNullOrWhiteSpace(downloadSource))
        {
            var downloadInfo = await ToolDownloaderService.ResolveDownloadUrlAsync(
                downloadSource, tool.DownloadFilter, ct);

            var tempDir = Path.Combine(Path.GetTempPath(), $"TubaCommunity_{tool.Id}");
            var archivePath = await ToolDownloaderService.DownloadToFileAsync(
                downloadInfo!.DownloadUrl, tempDir, downloadInfo.FileName, progress, ct);

            if (downloadInfo.IsArchive)
            {
                await ToolDownloaderService.ExtractArchiveAsync(archivePath, toolDir, ct);
            }
            else
            {
                var destPath = Path.Combine(toolDir, downloadInfo.FileName);
                File.Move(archivePath, destPath, true);
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
        else
        {
            throw new InvalidOperationException("该工具没有提供下载源");
        }

        ToolCatalog.InvalidateTagsCache();
        return toolDir;
    }

    private static async Task<string> ResolveCommunityFileUrlAsync(string rawUrl, CancellationToken ct)
    {
        var prefix = $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/";
        string gitCodeUrl;
        if (rawUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = rawUrl[prefix.Length..];
            gitCodeUrl = BuildGitCodeRawUrl(relativePath);
        }
        else
        {
            gitCodeUrl = rawUrl;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Community");
            using var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, gitCodeUrl), ct);
            if (resp.IsSuccessStatusCode) return gitCodeUrl;
        }
        catch { }

        return rawUrl;
    }

    private static string BuildGitCodeRawUrl(string relativePath)
    {
        var segments = relativePath.Split('/');
        var encoded = segments.Select(s => Uri.EscapeDataString(s));
        return $"{GitCodeRawBase}/{string.Join('/', encoded)}";
    }

    public static List<(string Name, string Url)> GetAllDownloadUrls(CommunityTool tool)
    {
        var urls = new List<(string Name, string Url)>();

        var communityFile = tool.File;
        if (!string.IsNullOrWhiteSpace(communityFile) && !string.IsNullOrWhiteSpace(tool.RepoPath))
        {
            if (!string.IsNullOrWhiteSpace(tool.FileSha))
                urls.Add(("GitCode 镜像",
                    $"https://raw.gitcode.com/{GitCodeOwner}/{GitCodeRepo}/blobs/{tool.FileSha}/{Uri.EscapeDataString(communityFile)}"));
            else
                urls.Add(("GitCode 镜像",
                    BuildGitCodeRawUrl($"{tool.RepoPath}/{communityFile}")));

            urls.Add(("GitHub 直连",
                $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/{tool.RepoPath}/{communityFile}"));
        }
        else if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
        {
            urls.Add(("默认源", tool.DownloadUrl));
        }

        return urls;
    }

    public static void LaunchPlugin(CommunityTool tool)
    {
        var localPath = GetLocalPath(tool);
        if (localPath is null) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = localPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(localPath)
        });

        LaunchHistoryService.RecordLaunch(localPath);
    }

    public const long MaxUploadSizeBytes = 50 * 1024 * 1024;

    public static async Task<string> SubmitPluginAsync(
        string name, string description, string category, string tags,
        string? zipFilePath, string launchTarget,
        string publisher, string homepage, string version,
        IProgress<string>? progress, string? iconFilePath = null, CancellationToken ct = default)
    {
        var token = GitHubAuthService.GetToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("请先登录 GitHub");

        var user = await GitHubAuthService.GetCurrentUserAsync(ct);
        if (user is null)
            throw new InvalidOperationException("无法获取用户信息");

        var toolId = GenerateToolId(name);
        var tagList = tags.Split(',', '，', ';', '；')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var pluginObj = new Dictionary<string, object?>
        {
            ["id"] = toolId,
            ["name"] = name,
            ["version"] = string.IsNullOrWhiteSpace(version) ? "1.0" : version,
            ["description"] = description,
            ["category"] = category,
            ["publisher"] = string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            ["tags"] = tagList,
            ["launchTarget"] = string.IsNullOrWhiteSpace(launchTarget) ? null : launchTarget,
            ["author"] = user.Login,
            ["submittedAt"] = DateTimeOffset.UtcNow.ToString("o"),
            ["homepage"] = string.IsNullOrWhiteSpace(homepage) ? null : homepage
        };

        if (!string.IsNullOrWhiteSpace(zipFilePath))
        {
            pluginObj["file"] = Path.GetFileName(zipFilePath);
        }

        if (!string.IsNullOrWhiteSpace(iconFilePath))
        {
            pluginObj["icon"] = Path.GetFileName(iconFilePath);
        }

        var jsonText = JsonSerializer.Serialize(pluginObj, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        progress?.Report("正在 Fork 仓库...");

        var forkOwner = await EnsureForkAsync(token, ct);

        progress?.Report("正在创建分支...");

        var branchName = $"plugin/{toolId}";
        var mainSha = await GetRefShaAsync(forkOwner, UpstreamRepo, "heads/main", token, ct);
        if (mainSha is null)
            throw new InvalidOperationException("无法获取主分支信息");

        var branchExists = await CheckRefExistsAsync(forkOwner, UpstreamRepo, $"heads/{branchName}", token, ct);
        if (branchExists)
            branchName = $"plugin/{toolId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        await CreateRefAsync(forkOwner, UpstreamRepo, $"refs/heads/{branchName}", mainSha, token, ct);

        progress?.Report("正在上传文件...");

        if (!string.IsNullOrWhiteSpace(zipFilePath) && File.Exists(zipFilePath))
        {
            var zipFileName = Path.GetFileName(zipFilePath);
            var zipRepoPath = $"{PluginsPath}/{category}/{toolId}/{zipFileName}";
            await CreateBinaryFileAsync(forkOwner, UpstreamRepo, zipRepoPath, branchName, zipFilePath, token, ct);
        }

        if (!string.IsNullOrWhiteSpace(iconFilePath) && File.Exists(iconFilePath))
        {
            var iconFileName = Path.GetFileName(iconFilePath);
            var iconRepoPath = $"{PluginsPath}/{category}/{toolId}/{iconFileName}";
            await CreateBinaryFileAsync(forkOwner, UpstreamRepo, iconRepoPath, branchName, iconFilePath, token, ct);
        }

        progress?.Report("正在提交插件信息...");

        var pluginRepoPath = $"{PluginsPath}/{category}/{toolId}/plugin.json";
        await CreateFileAsync(forkOwner, UpstreamRepo, pluginRepoPath, branchName, jsonText, token, ct);

        progress?.Report("正在创建 Pull Request...");

        var prUrl = await CreatePullRequestAsync(
            branchName, forkOwner, toolId, name, description, category, user.Login, token, ct);

        progress?.Report("提交成功！");

        InvalidateCache();
        return prUrl;
    }

    public static string GenerateToolId(string name)
    {
        var id = name.ToLowerInvariant();
        id = System.Text.RegularExpressions.Regex.Replace(id, @"[^a-z0-9\u4e00-\u9fff\-]", "-");
        id = System.Text.RegularExpressions.Regex.Replace(id, @"-+", "-");
        id = id.Trim('-');
        if (id.Length > 50) id = id[..50];
        return string.IsNullOrWhiteSpace(id) ? $"tool-{Guid.NewGuid():N}"[..16] : id;
    }

    private static async Task<string> EnsureForkAsync(string token, CancellationToken ct)
    {
        var user = await GitHubAuthService.GetCurrentUserAsync(ct);
        var forkOwner = user?.Login ?? throw new InvalidOperationException("无法获取用户名");

        using var client = GitHubAuthService.CreateAuthenticatedClient();

        try
        {
            var checkResp = await client.GetAsync(
                $"https://api.github.com/repos/{forkOwner}/{UpstreamRepo}", ct);
            if (checkResp.IsSuccessStatusCode) return forkOwner;
        }
        catch { }

        var forkResp = await client.PostAsync(
            $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}/forks",
            new StringContent("{}", Encoding.UTF8, "application/json"), ct);

        if (!forkResp.IsSuccessStatusCode)
        {
            var errBody = await forkResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Fork 失败：{(int)forkResp.StatusCode} {forkResp.StatusCode}\n{errBody}");
        }

        await Task.Delay(3000, ct);
        return forkOwner;
    }

    private static async Task<string?> GetLatestTreeShaAsync(CancellationToken ct)
    {
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/ref/heads/main", ct);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
    }

    private static async Task<string?> GetSubTreeShaAsync(string treeSha, string path, CancellationToken ct)
    {
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/trees/{treeSha}", ct);
        var doc = JsonDocument.Parse(json);
        var tree = doc.RootElement.GetProperty("tree");

        foreach (var item in tree.EnumerateArray())
        {
            var itemPath = item.GetProperty("path").GetString();
            if (string.Equals(itemPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (item.GetProperty("type").GetString() == "tree")
                    return item.GetProperty("sha").GetString();
                return null;
            }
        }
        return null;
    }

    private static async Task<List<(string Path, string Sha, string Type)>> EnumerateTreeAsync(string treeSha, CancellationToken ct)
    {
        var result = new List<(string Path, string Sha, string Type)>();
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/trees/{treeSha}", ct);
        var doc = JsonDocument.Parse(json);
        var tree = doc.RootElement.GetProperty("tree");

        foreach (var item in tree.EnumerateArray())
        {
            var path = item.GetProperty("path").GetString() ?? "";
            var sha = item.GetProperty("sha").GetString() ?? "";
            var type = item.GetProperty("type").GetString() ?? "";
            result.Add((path, sha, type));
        }
        return result;
    }

    private static async Task<string?> FindFileInTreeAsync(string treeSha, string fileName, CancellationToken ct)
    {
        var entries = await EnumerateTreeAsync(treeSha, ct);
        var entry = entries.FirstOrDefault(e => e.Path.Equals(fileName, StringComparison.OrdinalIgnoreCase) && e.Type == "blob");
        return entry.Sha;
    }

    private static async Task<string?> DownloadBlobAsync(string sha, CancellationToken ct)
    {
        using var client = CreateApiClient();
        var json = await client.GetStringAsync($"{ApiBase}/git/blobs/{sha}", ct);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("content", out var contentEl))
        {
            var base64 = contentEl.GetString() ?? "";
            base64 = base64.Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        return null;
    }

    private static CommunityTool? ParsePluginJson(string json, string category, string toolDir,
        Dictionary<string, string>? shaMap = null)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : toolDir;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : toolDir;
            var cat = root.TryGetProperty("category", out var catEl) ? catEl.GetString() : category;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagsEl))
            {
                foreach (var tag in tagsEl.EnumerateArray())
                {
                    var t = tag.GetString();
                    if (!string.IsNullOrWhiteSpace(t)) tags.Add(t);
                }
            }

            var archVariants = new List<CommunityArchVariant>();
            if (root.TryGetProperty("archVariants", out var archEl))
            {
                foreach (var av in archEl.EnumerateArray())
                {
                    archVariants.Add(new CommunityArchVariant
                    {
                        File = av.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "",
                        Arch = av.TryGetProperty("arch", out var a) ? a.GetString() ?? "" : ""
                    });
                }
            }

            var tool = new CommunityTool
            {
                Id = id,
                Name = name,
                Version = root.TryGetProperty("version", out var v) ? v.GetString() : null,
                Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                Category = cat ?? category,
                Publisher = root.TryGetProperty("publisher", out var p) ? p.GetString() : null,
                Tags = tags,
                Icon = root.TryGetProperty("icon", out var ic) ? ic.GetString() : null,
                DownloadUrl = root.TryGetProperty("downloadUrl", out var du) ? du.GetString() : null,
                DownloadFilter = root.TryGetProperty("downloadFilter", out var df) ? df.GetString() : null,
                LaunchTarget = root.TryGetProperty("launchTarget", out var lt) ? lt.GetString() : null,
                ArchVariants = archVariants.Count > 0 ? archVariants : null,
                Author = root.TryGetProperty("author", out var au) ? au.GetString() : null,
                SubmittedAt = root.TryGetProperty("submittedAt", out var sa) ? DateTimeOffset.TryParse(sa.GetString(), out var dt) ? dt : null : null,
                Homepage = root.TryGetProperty("homepage", out var hp) ? hp.GetString() : null,
                File = root.TryGetProperty("file", out var fl) ? fl.GetString() : null,
                RepoPath = $"{PluginsPath}/{category}/{toolDir}"
            };

            if (shaMap is not null && !string.IsNullOrWhiteSpace(tool.File) && !string.IsNullOrWhiteSpace(tool.RepoPath))
            {
                var fileKey = $"{tool.RepoPath}/{tool.File}";
                if (shaMap.TryGetValue(fileKey, out var fileSha))
                    tool.FileSha = fileSha;
            }

            if (!string.IsNullOrWhiteSpace(tool.Icon) && !string.IsNullOrWhiteSpace(tool.RepoPath))
            {
                if (shaMap is not null)
                {
                    var iconKey = $"{tool.RepoPath}/{tool.Icon}";
                    if (shaMap.TryGetValue(iconKey, out var iconSha))
                        tool.IconPath = $"https://raw.gitcode.com/{GitCodeOwner}/{GitCodeRepo}/blobs/{iconSha}/{Uri.EscapeDataString(tool.Icon)}";
                    else
                        tool.IconPath = BuildGitCodeRawUrl($"{tool.RepoPath}/{tool.Icon}");
                }
                else
                {
                    tool.IconPath = $"https://raw.githubusercontent.com/{UpstreamOwner}/{UpstreamRepo}/main/{tool.RepoPath}/{tool.Icon}";
                }
            }

            return tool;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetRefShaAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        try
        {
            var json = await client.GetStringAsync(
                $"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
        }
        catch { return null; }
    }

    private static async Task<bool> CheckRefExistsAsync(string owner, string repo, string refPath, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        try
        {
            var resp = await client.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}/git/ref/{refPath}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task CreateRefAsync(string owner, string repo, string refName, string sha, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var body = JsonSerializer.Serialize(new { @ref = refName, sha });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/refs", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"创建分支失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task CreateFileAsync(string owner, string repo, string path, string branch, string content, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var body = JsonSerializer.Serialize(new
        {
            message = $"feat: add plugin - {path.Split('/')[^2]}",
            content = base64Content,
            branch
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PutAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/{path}", httpContent, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"提交文件失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task CreateBinaryFileAsync(string owner, string repo, string path, string branch, string localFilePath, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var bytes = await File.ReadAllBytesAsync(localFilePath, ct);
        var base64Content = Convert.ToBase64String(bytes);
        var fileName = Path.GetFileName(localFilePath);
        var body = JsonSerializer.Serialize(new
        {
            message = $"feat: upload {fileName}",
            content = base64Content,
            branch
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PutAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/{path}", httpContent, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"上传 {fileName} 失败：{(int)resp.StatusCode}\n{err}");
        }
    }

    private static async Task<string> CreatePullRequestAsync(
        string branch, string forkOwner, string toolId, string toolName,
        string description, string category, string author, string token, CancellationToken ct)
    {
        using var client = GitHubAuthService.CreateAuthenticatedClient();
        var body = JsonSerializer.Serialize(new
        {
            title = $"[社区工具] {toolName}",
            head = $"{forkOwner}:{branch}",
            @base = "main",
            body = $"## 新增社区工具\n\n" +
                   $"- **名称**：{toolName}\n" +
                   $"- **分类**：{category}\n" +
                   $"- **描述**：{description}\n" +
                   $"- **提交者**：@{author}\n"
        });
        var httpContent = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(
            $"https://api.github.com/repos/{UpstreamOwner}/{UpstreamRepo}/pulls", httpContent, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"创建 PR 失败：{(int)resp.StatusCode}\n{err}");
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(respJson);
        return doc.RootElement.GetProperty("html_url").GetString() ?? "";
    }

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-Community");
        return client;
    }
}
