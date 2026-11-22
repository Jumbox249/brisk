using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Brisk;

/// <summary>
/// Registers Brisk with Windows as a web browser (per user, no admin rights needed), so it shows up
/// under Settings → Apps → Default apps for web links and .html files. Windows only lets the user
/// pick the default, so <see cref="OpenSettings"/> takes them to Brisk's page in Settings.
/// </summary>
static class DefaultBrowser
{
    const string AppName = "Brisk";
    const string ClientKey = @"Software\Clients\StartMenuInternet\Brisk";
    const string UrlProgId = "BriskURL";
    const string HtmlProgId = "BriskHTML";
    static readonly string[] Protocols = ["http", "https"];
    static readonly string[] FileTypes = [".htm", ".html", ".shtml", ".xht", ".xhtml"];

    static string Exe => Environment.ProcessPath!;
    static string OpenCommand => $"\"{Exe}\" \"%1\"";

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void Register()
    {
        using var hkcu = Registry.CurrentUser;

        foreach (var (progId, description) in new[] { (UrlProgId, "Brisk URL"), (HtmlProgId, "Brisk HTML Document") })
        {
            using var cls = hkcu.CreateSubKey($@"Software\Classes\{progId}");
            cls.SetValue("", description);
            if (progId == UrlProgId) cls.SetValue("URL Protocol", "");
            using (var icon = cls.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{Exe}\",0");
            using (var cmd = cls.CreateSubKey(@"shell\open\command")) cmd.SetValue("", OpenCommand);
        }

        using (var client = hkcu.CreateSubKey(ClientKey))
        {
            client.SetValue("", AppName);
            using (var icon = client.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{Exe}\",0");
            using (var cmd = client.CreateSubKey(@"shell\open\command")) cmd.SetValue("", $"\"{Exe}\"");
            using var caps = client.CreateSubKey("Capabilities");
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "A small, fast, private web browser with ad blocking built in.");
            caps.SetValue("ApplicationIcon", $"\"{Exe}\",0");
            using (var urls = caps.CreateSubKey("URLAssociations"))
                foreach (var p in Protocols) urls.SetValue(p, UrlProgId);
            using (var files = caps.CreateSubKey("FileAssociations"))
                foreach (var f in FileTypes) files.SetValue(f, HtmlProgId);
            using var start = caps.CreateSubKey("StartMenu");
            start.SetValue("StartMenuInternet", AppName);
        }

        using (var registered = hkcu.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue(AppName, ClientKey + @"\Capabilities");

        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Removes everything <see cref="Register"/> added (used by the uninstaller).</summary>
    public static void Unregister()
    {
        using var hkcu = Registry.CurrentUser;
        hkcu.DeleteSubKeyTree($@"Software\Classes\{UrlProgId}", throwOnMissingSubKey: false);
        hkcu.DeleteSubKeyTree($@"Software\Classes\{HtmlProgId}", throwOnMissingSubKey: false);
        hkcu.DeleteSubKeyTree(ClientKey, throwOnMissingSubKey: false);
        using (var registered = hkcu.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            registered?.DeleteValue(AppName, throwOnMissingValue: false);
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Re-points the registration at this copy if the registered one no longer exists (Brisk was
    /// moved). Another copy that still exists keeps the registration.
    /// </summary>
    public static void RefreshIfMoved()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{UrlProgId}\shell\open\command");
        if (cmd?.GetValue("") is not string command || command == OpenCommand) return;
        var registeredExe = command.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (registeredExe is not null && !File.Exists(registeredExe)) Register();
    }

    /// <summary>True if the user has chosen Brisk for https links.</summary>
    public static bool IsDefault()
    {
        using var choice = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        return choice?.GetValue("ProgId") as string == UrlProgId;
    }

    /// <summary>Opens Brisk's page under Settings → Apps → Default apps.</summary>
    public static void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={AppName}") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
    }

    /// <summary>Turns a command-line argument (a URL or a local file path) into something to open.</summary>
    public static string? ToUrl(string arg)
    {
        arg = arg.Trim().Trim('"');
        if (arg.Length == 0 || arg.StartsWith('-')) return null;
        if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file") return uri.AbsoluteUri;
        if (File.Exists(arg)) return new Uri(Path.GetFullPath(arg)).AbsoluteUri;
        return null;
    }
}
