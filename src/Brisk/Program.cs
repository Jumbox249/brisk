using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Brisk;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--register-default-browser"))
        {
            DefaultBrowser.Register();
            return;
        }

        var urls = args.Select(DefaultBrowser.ToUrl).OfType<string>().ToList();

        // One Brisk per profile: a second launch (say, a link clicked in another app) hands its
        // URLs to the running window over a named pipe and exits.
        var id = InstanceId();
        using var mutex = new Mutex(initiallyOwned: true, $@"Local\Brisk-{id}", out bool first);
        if (!first)
        {
            if (SendToRunningInstance(id, urls)) return;
        }

        ApplicationConfiguration.Initialize();
        var form = new BrowserForm(urls);
        ListenForOtherInstances(id, form);
        Application.Run(form);
    }

    /// <summary>Distinguishes profiles, so separate data folders run separate windows.</summary>
    static string InstanceId() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Settings.DataDir.ToLowerInvariant())))[..16];

    static bool SendToRunningInstance(string id, List<string> urls)
    {
        try
        {
            // The running window may bring itself to the front: this process was started by the user.
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            using var pipe = new NamedPipeClientStream(".", $"Brisk-{id}", PipeDirection.Out);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, Encoding.UTF8);
            writer.Write(string.Join('\n', urls));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static void ListenForOtherInstances(string id, BrowserForm form)
    {
        var thread = new Thread(() =>
        {
            while (!form.IsDisposed)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream($"Brisk-{id}", PipeDirection.In);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var urls = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (form.IsHandleCreated) form.BeginInvoke(() => form.OpenFromOutside(urls));
                }
                catch (IOException) { }
            }
        }) { IsBackground = true, Name = "Brisk instance pipe" };
        thread.Start();
    }
}
