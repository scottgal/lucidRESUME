using System.Runtime.InteropServices;

namespace lucidRESUME.Services;

/// <summary>Gives unbundled development launches the same Dock icon as the release bundle.</summary>
internal static class MacApplicationIcon
{
    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "lucidresume.png");
        var file = SendString(GetClass("NSString"), Selector("stringWithUTF8String:"), path);
        var image = SendPointer(Send(GetClass("NSImage"), Selector("alloc")), Selector("initWithContentsOfFile:"), file);
        if (image == IntPtr.Zero) return;
        SendPointer(Send(GetClass("NSApplication"), Selector("sharedApplication")), Selector("setApplicationIconImage:"), image);
        Send(image, Selector("release"));
    }

    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjectiveC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjectiveC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr target, IntPtr selector);
    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPointer(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendString(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
}
