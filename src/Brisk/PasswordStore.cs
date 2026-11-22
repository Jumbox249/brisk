using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Brisk;

sealed record SavedPassword(string Site, string Url, string Username, byte[] Encrypted, DateTime Created, bool Never);

/// <summary>
/// Reads the passwords the browser engine has saved. They live in the profile's "Login Data"
/// SQLite file, encrypted with AES-GCM under a key that Windows (DPAPI) protects for the signed-in
/// user. The engine keeps the file locked for writing while it runs, so removal goes through the
/// engine itself (see BrowserForm.DeletePasswordAsync); this class only reads.
/// SQLite comes from Windows (winsqlite3.dll), so this adds nothing to the download.
/// </summary>
static partial class PasswordStore
{
    static string ProfileDir => Path.Combine(Settings.DataDir, "Profile", "EBWebView");
    static string LoginData => Path.Combine(ProfileDir, "Default", "Login Data");

    public static List<SavedPassword> Load()
    {
        var list = new List<SavedPassword>();
        if (!File.Exists(LoginData)) return list;
        // Read a snapshot: the engine holds a lock on the live file.
        var copy = Path.Combine(Path.GetTempPath(), $"brisk-logins-{Environment.ProcessId}.db");
        try
        {
            using (var src = new FileStream(LoginData, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(copy))
                src.CopyTo(dst);
            ReadRows(copy, list);
        }
        catch (IOException) { }
        finally { try { File.Delete(copy); } catch (IOException) { } }
        return list
            .OrderBy(p => p.Never)
            .ThenBy(p => p.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static unsafe void ReadRows(string path, List<SavedPassword> list)
    {
        if (sqlite3_open_v2(path, out var db, 1 /* READONLY */, IntPtr.Zero) != 0) { sqlite3_close(db); return; }
        try
        {
            const string sql = "SELECT origin_url, signon_realm, username_value, password_value, date_created, blacklisted_by_user FROM logins";
            if (sqlite3_prepare_v2(db, sql, -1, out var stmt, IntPtr.Zero) != 0) return;
            try
            {
                while (sqlite3_step(stmt) == 100 /* ROW */)
                {
                    var origin = Text(stmt, 0);
                    var realm = Text(stmt, 1);
                    var user = Text(stmt, 2);
                    int n = sqlite3_column_bytes(stmt, 3);
                    var blob = n > 0 ? new ReadOnlySpan<byte>((void*)sqlite3_column_blob(stmt, 3), n).ToArray() : [];
                    long created = sqlite3_column_int64(stmt, 4);
                    bool never = sqlite3_column_int64(stmt, 5) != 0;
                    var url = origin.Length > 0 ? origin : realm;
                    var site = Uri.TryCreate(url, UriKind.Absolute, out var u) ? Settings.SiteKey(u) : url;
                    list.Add(new SavedPassword(site, url, user, blob, FromChromeTime(created), never));
                }
            }
            finally { sqlite3_finalize(stmt); }
        }
        finally { sqlite3_close(db); }
    }

    static unsafe string Text(IntPtr stmt, int col)
    {
        var p = sqlite3_column_text(stmt, col);
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p, sqlite3_column_bytes(stmt, col));
    }

    /// <summary>The engine stores times as microseconds since 1601-01-01 UTC.</summary>
    static DateTime FromChromeTime(long micros) =>
        micros <= 0 ? DateTime.MinValue : new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(micros * 10);

    static byte[]? _key;

    /// <summary>Decrypts a saved password, or returns null if it can't be read.</summary>
    public static string? Reveal(SavedPassword p)
    {
        var data = p.Encrypted;
        if (data.Length == 0) return "";
        try
        {
            if (data.Length > 3 + 12 + 16 && data[0] == 'v' && data[1] == '1' && data[2] is (byte)'0' or (byte)'1')
            {
                var key = _key ??= ReadKey();
                if (key is null) return null;
                var nonce = data.AsSpan(3, 12);
                var cipher = data.AsSpan(15, data.Length - 15 - 16);
                var tag = data.AsSpan(data.Length - 16);
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            // Very old entries were protected with DPAPI directly.
            return Unprotect(data) is { } raw ? Encoding.UTF8.GetString(raw) : null;
        }
        catch (CryptographicException) { return null; }
    }

    static byte[]? ReadKey()
    {
        try
        {
            var state = JsonNode.Parse(File.ReadAllText(Path.Combine(ProfileDir, "Local State")));
            var b64 = state?["os_crypt"]?["encrypted_key"]?.GetValue<string>();
            if (b64 is null) return null;
            var blob = Convert.FromBase64String(b64);
            if (blob.Length < 5 || Encoding.ASCII.GetString(blob, 0, 5) != "DPAPI") return null;
            return Unprotect(blob[5..]);
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // ---------- Windows APIs ----------

    [StructLayout(LayoutKind.Sequential)]
    struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DATA_BLOB input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB output);

    [LibraryImport("kernel32.dll")] private static partial IntPtr LocalFree(IntPtr p);

    static unsafe byte[]? Unprotect(byte[] data)
    {
        fixed (byte* p = data)
        {
            var input = new DATA_BLOB { cbData = data.Length, pbData = (IntPtr)p };
            var output = new DATA_BLOB();
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output)) return null;
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally { LocalFree(output.pbData); }
        }
    }

    const string Sqlite = "winsqlite3.dll";
    [LibraryImport(Sqlite, StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_open_v2(string path, out IntPtr db, int flags, IntPtr vfs);
    [LibraryImport(Sqlite)] private static partial int sqlite3_close(IntPtr db);
    [LibraryImport(Sqlite, StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_prepare_v2(IntPtr db, string sql, int len, out IntPtr stmt, IntPtr tail);
    [LibraryImport(Sqlite)] private static partial int sqlite3_step(IntPtr stmt);
    [LibraryImport(Sqlite)] private static partial int sqlite3_finalize(IntPtr stmt);
    [LibraryImport(Sqlite)] private static partial IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [LibraryImport(Sqlite)] private static partial IntPtr sqlite3_column_blob(IntPtr stmt, int col);
    [LibraryImport(Sqlite)] private static partial int sqlite3_column_bytes(IntPtr stmt, int col);
    [LibraryImport(Sqlite)] private static partial long sqlite3_column_int64(IntPtr stmt, int col);
}
