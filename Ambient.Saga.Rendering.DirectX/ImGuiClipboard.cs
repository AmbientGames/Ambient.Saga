using ImGuiNET;
using System.Runtime.InteropServices;

namespace Ambient.Saga.Rendering.DirectX;

/// <summary>
/// Windows clipboard integration for ImGui using Win32 APIs directly.
/// No STA thread gymnastics - just simple, reliable clipboard access.
/// </summary>
public static unsafe class ImGuiClipboard
{
    // Persistent buffer for clipboard text (ImGui expects pointer to remain valid)
    private static IntPtr _clipboardTextPtr = IntPtr.Zero;
    private static IntPtr _emptyStringPtr = IntPtr.Zero;

    // Keep delegates alive to prevent GC
    private static GetClipboardTextDelegate? _getClipboardTextDelegate;
    private static SetClipboardTextDelegate? _setClipboardTextDelegate;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetClipboardTextDelegate(IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetClipboardTextDelegate(IntPtr userData, IntPtr text);

    #region Win32 Clipboard APIs

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    #endregion

    private static IntPtr GetClipboardText(IntPtr userData)
    {
        // Free previous allocation
        if (_clipboardTextPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_clipboardTextPtr);
            _clipboardTextPtr = IntPtr.Zero;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] OpenClipboard failed for read");
            return _emptyStringPtr; // Return empty string, not null
        }

        try
        {
            IntPtr hGlobal = GetClipboardData(CF_UNICODETEXT);
            if (hGlobal == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] No CF_UNICODETEXT data");
                return _emptyStringPtr;
            }

            IntPtr lpStr = GlobalLock(hGlobal);
            if (lpStr == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] GlobalLock failed");
                return _emptyStringPtr;
            }

            try
            {
                // Read Unicode string from clipboard
                string? text = Marshal.PtrToStringUni(lpStr);
                if (string.IsNullOrEmpty(text))
                    return _emptyStringPtr;

                // Convert to UTF-8 for ImGui
                var bytes = System.Text.Encoding.UTF8.GetBytes(text + '\0');
                _clipboardTextPtr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, _clipboardTextPtr, bytes.Length);

                System.Diagnostics.Debug.WriteLine($"[ImGuiClipboard] Read: '{text}'");
                return _clipboardTextPtr;
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void SetClipboardText(IntPtr userData, IntPtr text)
    {
        if (text == IntPtr.Zero)
            return;

        // ImGui passes UTF-8
        string? str = Marshal.PtrToStringUTF8(text);
        if (string.IsNullOrEmpty(str))
            return;

        if (!OpenClipboard(IntPtr.Zero))
        {
            System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] OpenClipboard failed for write");
            return;
        }

        try
        {
            EmptyClipboard();

            // Allocate global memory for Unicode string
            int charCount = str.Length + 1; // Include null terminator
            int byteCount = charCount * 2;  // UTF-16 = 2 bytes per char
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] GlobalAlloc failed");
                return;
            }

            IntPtr lpStr = GlobalLock(hGlobal);
            if (lpStr == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] GlobalLock failed for write");
                return;
            }

            // Copy string as Unicode
            char[] chars = (str + '\0').ToCharArray();
            Marshal.Copy(chars, 0, lpStr, chars.Length);
            GlobalUnlock(hGlobal);

            SetClipboardData(CF_UNICODETEXT, hGlobal);
            System.Diagnostics.Debug.WriteLine($"[ImGuiClipboard] Wrote: '{str}'");
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void Install()
    {
        // On Windows, ImGui's default clipboard implementation should work.
        // Only install custom handlers if native clipboard doesn't work.
        // Setting Platform_* functions causes an endless polling loop - avoid.
        System.Diagnostics.Debug.WriteLine("[ImGuiClipboard] Using ImGui's native Windows clipboard");
    }

    public static void Shutdown()
    {
        if (_clipboardTextPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_clipboardTextPtr);
            _clipboardTextPtr = IntPtr.Zero;
        }

        if (_emptyStringPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_emptyStringPtr);
            _emptyStringPtr = IntPtr.Zero;
        }

        _getClipboardTextDelegate = null;
        _setClipboardTextDelegate = null;
    }
}
