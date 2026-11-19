using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NEXMC.Services;

/// <summary>
/// 只在本机保存网盘登录 Cookie。使用 Windows DPAPI 加密，换用户或换机器无法解密。
/// 不写入设置文件，也不会上传到任何服务器。
/// </summary>
public sealed class NetdiskCookieStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private readonly string _directory;

    public NetdiskCookieStore()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("NEXMC_NETDISK_AUTH_DIR");
        _directory = !string.IsNullOrWhiteSpace(overrideDirectory)
            ? overrideDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NEXMC", "netdisk-auth");
    }

    public string? Load(NetdiskPlatform platform)
    {
        if (!IsAccountPlatform(platform)) return null;
        var path = PathFor(platform);
        try
        {
            if (!File.Exists(path)) return null;
            var protectedBytes = File.ReadAllBytes(path);
            var cookie = Unprotect(protectedBytes);
            return string.IsNullOrWhiteSpace(cookie) ? null : cookie.Trim();
        }
        catch
        {
            return null;
        }
    }

    public void Save(NetdiskPlatform platform, string cookie)
    {
        if (!IsAccountPlatform(platform)) return;
        if (string.IsNullOrWhiteSpace(cookie)) throw new ArgumentException("Cookie 不能为空。", nameof(cookie));
        Directory.CreateDirectory(_directory);
        var protectedBytes = Protect(cookie.Trim());
        var path = PathFor(platform);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, path, true);
    }

    public void Clear(NetdiskPlatform platform)
    {
        if (!IsAccountPlatform(platform)) return;
        try
        {
            var path = PathFor(platform);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 退出登录失败不应该影响界面。
        }
    }

    public static bool IsAccountPlatform(NetdiskPlatform platform) =>
        platform is NetdiskPlatform.Quark or NetdiskPlatform.Uc;

    private string PathFor(NetdiskPlatform platform) =>
        Path.Combine(_directory, platform.ToString().ToLowerInvariant() + ".bin");

    private static byte[] Protect(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        return Transform(data, protect: true);
    }

    private static string Unprotect(byte[] value)
    {
        var data = Transform(value, protect: false);
        return Encoding.UTF8.GetString(data);
    }

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        var inputPtr = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, inputPtr, data.Length);
            input.Size = data.Length;
            input.Data = inputPtr;

            var ok = protect
                ? CryptProtectData(ref input, "NEXMC netdisk cookie", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok) throw new InvalidOperationException("Windows DPAPI 操作失败：" + Marshal.GetLastWin32Error());

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
