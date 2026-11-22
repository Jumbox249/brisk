using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace Brisk;

/// <summary>
/// Builds the scriptlet resources that uBlock Origin's filter rules (`##+js(...)`) refer to, the
/// same way Brave does: uBO ships its scriptlets as JavaScript modules, which are imported and
/// turned into adblock-rust resources. Nothing from uBO is bundled with Brisk; the modules are
/// downloaded from uBO's latest release and evaluated inside a hidden WebView2 — the browser
/// sandbox every web page runs in — with all network access refused.
/// </summary>
static partial class ScriptletConverter
{
    const string Repo = "gorhill/uBlock";
    const string Entry = "src/js/resources/scriptlets.js";
    const string Host = "ubo-modules.brisk";
    static readonly string SourceDir = Path.Combine(Settings.DataDir, "adblock", "ubo-src");

    [GeneratedRegex(@"(?:from|import)\s+'(\.{1,2}/[^']+)'")]
    private static partial Regex RelativeImport();

    /// <summary>Returns the resources JSON, or null if anything fails (the old resources stay in use).</summary>
    public static async Task<string?> ConvertAsync(CoreWebView2Environment env, IntPtr parentWindow)
    {
        try
        {
            await DownloadModulesAsync();
            return await EvaluateAsync(env, parentWindow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or
                                         JsonException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Mirrors the scriptlet modules of uBO's latest release, following relative imports.</summary>
    static async Task DownloadModulesAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Brisk/0.1");
        using var release = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        var tag = release.RootElement.GetProperty("tag_name").GetString()!;

        if (Directory.Exists(SourceDir)) Directory.Delete(SourceDir, recursive: true);
        var pending = new Queue<string>([Entry]);
        var seen = new HashSet<string> { Entry };
        while (pending.Count > 0)
        {
            var rel = pending.Dequeue();
            var text = await http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/{tag}/{rel}");
            var path = Path.Combine(SourceDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text);

            var dir = rel[..rel.LastIndexOf('/')];
            foreach (Match m in RelativeImport().Matches(text))
            {
                var next = Normalize(dir + "/" + m.Groups[1].Value);
                if (next.StartsWith("src/", StringComparison.Ordinal) && seen.Add(next)) pending.Enqueue(next);
            }
        }

        // The page that imports the modules and reports the converted resources.
        await File.WriteAllTextAsync(Path.Combine(SourceDir, "convert.html"), $$"""
            <!doctype html><meta charset="utf-8">
            <script type="module">
            const send = (o) => chrome.webview.postMessage(JSON.stringify(o));
            try {
              const { builtinScriptlets } = await import('./{{Entry}}');
              const b64 = (s) => {
                const bytes = new TextEncoder().encode(s);
                let bin = '';
                for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
                return btoa(bin);
              };
              send({ resources: builtinScriptlets.map((s) => ({
                name: s.name,
                aliases: s.aliases ?? [],
                kind: { mime: 'application/javascript' },
                content: b64(s.fn.toString()),
                dependencies: s.dependencies ?? [],
              })) });
            } catch (e) { send({ error: String(e) }); }
            </script>
            """);
    }

    static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (part != "." && part.Length > 0) parts.Add(part);
        }
        return string.Join('/', parts);
    }

    static async Task<string?> EvaluateAsync(CoreWebView2Environment env, IntPtr parentWindow)
    {
        var controller = await env.CreateCoreWebView2ControllerAsync(parentWindow);
        try
        {
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, SourceDir, CoreWebView2HostResourceAccessKind.Deny);

            // No network: only the mirrored module files may load.
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (!e.Request.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase))
                    e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
            };

            var result = new TaskCompletionSource<string?>();
            core.WebMessageReceived += (_, e) =>
            {
                using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                result.TrySetResult(doc.RootElement.TryGetProperty("resources", out var r) && r.GetArrayLength() > 0
                    ? r.GetRawText()
                    : null);
            };
            core.Navigate($"https://{Host}/convert.html");
            var done = await Task.WhenAny(result.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            return done == result.Task ? result.Task.Result : null;
        }
        finally
        {
            controller.Close();
        }
    }
}
