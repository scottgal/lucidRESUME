using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace lucidRESUME.Services;

/// <summary>
/// Stores API keys in macOS Keychain, Windows Credential Manager, or the Linux
/// Secret Service. There is deliberately no plaintext fallback.
/// </summary>
public sealed class PlatformSecretStore : ISecretStore
{
    private const string ServiceName = "lucidRESUME";
    private readonly string? _secretToolPath;

    public PlatformSecretStore()
    {
        _secretToolPath = OperatingSystem.IsLinux()
            ? new[] { "/usr/bin/secret-tool", "/bin/secret-tool" }.FirstOrDefault(File.Exists)
            : null;
    }

    public bool IsAvailable => OperatingSystem.IsWindows()
                               || OperatingSystem.IsMacOS()
                               || _secretToolPath is not null;

    public string BackendName => OperatingSystem.IsWindows()
        ? "Windows Credential Manager"
        : OperatingSystem.IsMacOS()
            ? "macOS Keychain"
            : _secretToolPath is not null
                ? "Linux Secret Service"
                : "unavailable";

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        if (OperatingSystem.IsWindows()) return ReadWindows(key);
        if (OperatingSystem.IsMacOS()) return ReadMac(key);
        if (_secretToolPath is not null)
        {
            var result = await RunAsync(_secretToolPath,
                ["lookup", "service", ServiceName, "account", key], null, ct);
            return result.ExitCode == 1 ? null : EnsureSuccess(result, "read").StandardOutput.TrimEnd('\r', '\n');
        }
        throw Unavailable();
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        ValidateKey(key);
        if (string.IsNullOrEmpty(value))
        {
            await DeleteAsync(key, ct);
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            WriteWindows(key, value);
            return;
        }
        if (OperatingSystem.IsMacOS()) { WriteMac(key, value); return; }
        if (_secretToolPath is not null)
        {
            var result = await RunAsync(_secretToolPath,
                ["store", "--label", $"{ServiceName} {key}", "service", ServiceName, "account", key],
                value, ct);
            EnsureSuccess(result, "write");
            return;
        }
        throw Unavailable();
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        if (OperatingSystem.IsWindows())
        {
            if (!CredDelete(Target(key), CredentialType.Generic, 0) && Marshal.GetLastWin32Error() != 1168)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not delete the credential.");
            return;
        }
        if (OperatingSystem.IsMacOS()) { DeleteMac(key); return; }
        if (_secretToolPath is not null)
        {
            var result = await RunAsync(_secretToolPath,
                ["clear", "service", ServiceName, "account", key], null, ct);
            if (result.ExitCode != 1) EnsureSuccess(result, "delete");
            return;
        }
        throw Unavailable();
    }

    private static async Task<ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string? standardInput, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"Could not start secret-store command '{executable}'.");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), ct);
            process.StandardInput.Close();
        }
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static ProcessResult EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"The OS credential store could not {operation} the secret (exit {result.ExitCode}).");
        return result;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(character => char.IsControl(character)))
            throw new ArgumentException("Secret names must be non-empty, printable, and at most 100 characters.", nameof(key));
    }

    private static PlatformNotSupportedException Unavailable() => new(
        "No supported OS credential store is available. Install secret-tool/libsecret on Linux.");

    private static string Target(string key) => $"{ServiceName}:{key}";

    private static string? ReadMac(string key)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            out var length, out var data, out var item);
        if (status == ErrSecItemNotFound) return null;
        EnsureMacSuccess(status, "read");
        try
        {
            var bytes = new byte[length];
            if (length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally
        {
            if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static void WriteMac(string key, string value)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var secret = Encoding.UTF8.GetBytes(value);
        IntPtr item = IntPtr.Zero;
        IntPtr existingData = IntPtr.Zero;
        try
        {
            var find = SecKeychainFindGenericPassword(IntPtr.Zero,
                (uint)service.Length, service, (uint)account.Length, account,
                out _, out existingData, out item);
            int status;
            if (find == 0)
            {
                status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret);
            }
            else if (find == ErrSecItemNotFound)
            {
                status = SecKeychainAddGenericPassword(IntPtr.Zero,
                    (uint)service.Length, service, (uint)account.Length, account,
                    (uint)secret.Length, secret, out item);
            }
            else
            {
                status = find;
            }
            EnsureMacSuccess(status, "write");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            if (existingData != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, existingData);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static void DeleteMac(string key)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            out _, out var data, out var item);
        if (status == ErrSecItemNotFound) return;
        EnsureMacSuccess(status, "find");
        try { EnsureMacSuccess(SecKeychainItemDelete(item), "delete"); }
        finally
        {
            if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static void EnsureMacSuccess(int status, string operation)
    {
        if (status != 0)
            throw new InvalidOperationException(
                $"macOS Keychain could not {operation} the secret (OSStatus {status}).");
    }

    private static string? ReadWindows(string key)
    {
        if (!CredRead(Target(key), CredentialType.Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new Win32Exception(error, "Could not read the credential.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return "";
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void WriteWindows(string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 2560)
            throw new ArgumentException("Credential values cannot exceed 2560 bytes.", nameof(value));
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredentialType.Generic,
                TargetName = Target(key),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistence.LocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write the credential.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeHGlobal(blob);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private const int ErrSecItemNotFound = -25300;

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength,
        out IntPtr passwordData, out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, uint passwordLength,
        byte[] passwordData, out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    private enum CredentialType : uint { Generic = 1 }
    private enum CredentialPersistence : uint { LocalMachine = 2 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public CredentialType Type;
        public string TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CredentialPersistence Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, CredentialType type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, CredentialType type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
