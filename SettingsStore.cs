using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DevOpsPulse;
public static class SettingsStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevOpsPulse");
    private static readonly string SettingsPath = Path.Combine(Folder, "settings.json");
    private static readonly string TokenPath = Path.Combine(Folder, "credential.dat");
    public static AppSettings Load()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static string LoadToken(AppSettings settings)
    {
        if (!settings.RememberToken || !File.Exists(TokenPath)) return "";
        try { return Encoding.UTF8.GetString(Protect(File.ReadAllBytes(TokenPath), false)); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return ""; }
    }
    public static void Save(AppSettings settings, string token)
    {
        Directory.CreateDirectory(Folder);
        if (settings.RememberToken)
        {
            var bytes = Encoding.UTF8.GetBytes(token);
            try { File.WriteAllBytes(TokenPath, Protect(bytes, true)); }
            finally { Array.Clear(bytes); }
        }
        else if (File.Exists(TokenPath)) File.Delete(TokenPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static void Forget()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
        Save(Load() with { RememberToken = false }, "");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    internal static byte[] Protect(byte[] data, bool encrypt)
    {
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var ok = encrypt ? CryptProtectData(ref input, "DevOps Pulse", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }
}
