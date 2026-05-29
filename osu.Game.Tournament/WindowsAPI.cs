// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace osu.Game.Tournament
{
    [SupportedOSPlatform("windows")]
    public static class WindowsAPI
    {
        internal static Bitmap CaptureWindowFromBitbit(IntPtr hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT rect))
                throw new InvalidOperationException("Failed to get window bounds.");

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Window bounds are empty.");

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            try
            {
                using (System.Drawing.Graphics gfxBmp = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr hdcBitmap = IntPtr.Zero;
                    IntPtr hdcWindow = IntPtr.Zero;

                    try
                    {
                        hdcBitmap = gfxBmp.GetHdc();
                        hdcWindow = GetWindowDC(hWnd);

                        if (hdcWindow == IntPtr.Zero || !BitBlt(hdcBitmap, 0, 0, width, height, hdcWindow, 0, 0, 0x00CC0020)) // SRCCOPY
                            throw new InvalidOperationException("Failed to capture window contents.");
                    }
                    finally
                    {
                        if (hdcWindow != IntPtr.Zero)
                            ReleaseDC(hWnd, hdcWindow);

                        if (hdcBitmap != IntPtr.Zero)
                            gfxBmp.ReleaseHdc(hdcBitmap);
                    }
                }

                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
                                           IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hWnd);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        internal static IntPtr FindWindowByPartialTitle(string partialTitle)
        {
            IntPtr result = FindWindow(null, partialTitle);

            if (result != IntPtr.Zero)
                return result;

            StringBuilder sb = new StringBuilder(256);

            EnumWindows((hWnd, lParam) =>
            {
                sb.Clear();
                GetWindowText(hWnd, sb, sb.Capacity);

                if (contains(sb, partialTitle))
                {
                    result = hWnd;
                    return false; // 停止遍历
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static bool contains(StringBuilder source, string value)
        {
            if (value.Length == 0)
                return true;

            for (int i = 0; i <= source.Length - value.Length; i++)
            {
                int j = 0;

                for (; j < value.Length; j++)
                {
                    if (source[i + j] != value[j])
                        break;
                }

                if (j == value.Length)
                    return true;
            }

            return false;
        }

        [Flags]
        internal enum ProcessAccessFlags : uint
        {
            VMRead = 0x0010,
            VM_WRITE = 0x0020,
            QueryInformation = 0x0400,
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        internal static unsafe bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, Span<byte> lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead)
        {
            if (lpBuffer.Length == 0)
            {
                lpNumberOfBytesRead = IntPtr.Zero;
                return true;
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(dwSize, lpBuffer.Length);

            fixed (byte* bufferPtr = lpBuffer)
                return ReadProcessMemory(hProcess, lpBaseAddress, bufferPtr, dwSize, out lpNumberOfBytesRead);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern unsafe bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte* lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        internal static extern bool CloseHandle(IntPtr hObject);

        [Flags]
        internal enum AllocationProtect : uint
        {
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        internal static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        internal static Process? GetProcessByWindowTitle(string windowTitle, bool partial = true)
        {
            IntPtr hWnd = partial ? FindWindowByPartialTitle(windowTitle) : FindWindow(null, windowTitle);
            if (hWnd == IntPtr.Zero)
                return null;

            GetWindowThreadProcessId(hWnd, out int processId);
            if (processId == 0)
                return null;

            try
            {
                return Process.GetProcessById(processId);
            }
            catch
            {
                return null;
            }
        }
    }
}
