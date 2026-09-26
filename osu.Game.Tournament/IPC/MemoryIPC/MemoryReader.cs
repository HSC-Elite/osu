// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace osu.Game.Tournament.IPC.MemoryIPC
{
    public class MemoryReader : IDisposable
    {
        public IntPtr ProcessHandle { get; private set; }

        public Process? Process { get; private set; }

        public bool IsAttached => Process != null && !Process.HasExited;

        public bool AttachToProcessByTitleName(string titleName)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            Process? p = WindowsAPI.GetProcessByWindowTitle(titleName, false);
            return p != null && AttachToProcess(p);
        }

        public bool AttachToProcessByProcessCommandLine(string processName, Func<string, bool> matches)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(processName);
            ArgumentNullException.ThrowIfNull(matches);

            if (OperatingSystem.IsWindows())
                return attachToWindowsProcessByCommandLine(processName, matches);

            if (OperatingSystem.IsLinux())
                return attachToLinuxProcessByCommandLine(processName, matches);

            return false;
        }

        private bool attachToLinuxProcessByCommandLine(string processName, Func<string, bool> matches)
        {
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (!processNamesMatch(process.ProcessName, processName))
                        continue;

                    string commandLine = File.ReadAllText($"/proc/{process.Id}/cmdline").Replace('\0', ' ').TrimEnd();
                    if (!matches(commandLine))
                        continue;

                    if (AttachToProcess(process))
                        return true;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                finally
                {
                    if (!ReferenceEquals(process, this.Process))
                        process.Dispose();
                }
            }

            return false;
        }

        [SupportedOSPlatform("windows")]
        private bool attachToWindowsProcessByCommandLine(string processName, Func<string, bool> matches)
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process");

            foreach (ManagementObject processInfo in searcher.Get())
            {
                if (processInfo["ProcessId"] is not uint processId
                    || processInfo["Name"] is not string name
                    || !processNamesMatch(name, processName)
                    || processInfo["CommandLine"] is not string commandLine
                    || !matches(commandLine))
                    continue;

                Process? process = null;

                try
                {
                    process = Process.GetProcessById((int)processId);

                    if (AttachToProcess(process))
                        return true;
                }
                catch (ArgumentException)
                {
                }
                finally
                {
                    if (process != null && !ReferenceEquals(process, this.Process))
                        process.Dispose();
                }
            }

            return false;
        }

        private static bool processNamesMatch(string processName, string expectedProcessName)
        {
            return Path.GetFileNameWithoutExtension(processName).Equals(Path.GetFileNameWithoutExtension(expectedProcessName), StringComparison.OrdinalIgnoreCase);
        }

        public virtual bool AttachToProcess(Process process)
        {
            this.Process = process;

            if (OperatingSystem.IsWindows())
            {
                ProcessHandle = WindowsAPI.OpenProcess(WindowsAPI.ProcessAccessFlags.VMRead | WindowsAPI.ProcessAccessFlags.QueryInformation, false, process.Id);
                return ProcessHandle != IntPtr.Zero;
            }

            if (OperatingSystem.IsLinux())
            {
                ProcessHandle = new IntPtr(process.Id);
                return !process.HasExited;
            }

            throw new PlatformNotSupportedException("Memory reading is supported only on Windows and Linux.");
        }

        #region Basic Method

        public int ReadInt32(IntPtr address)
        {
            ThrowIfNotAttached();

            Span<byte> buffer = stackalloc byte[4];
            readProcessMemory(ProcessHandle, address, buffer, out _);
            return BitConverter.ToInt32(buffer);
        }

        public long ReadInt64(IntPtr address)
        {
            ThrowIfNotAttached();

            Span<byte> buffer = stackalloc byte[8];
            readProcessMemory(ProcessHandle, address, buffer, out _);
            return BitConverter.ToInt64(buffer);
        }

        public short ReadShort(IntPtr address)
        {
            ThrowIfNotAttached();

            Span<byte> buffer = stackalloc byte[2];
            readProcessMemory(ProcessHandle, address, buffer, out _);
            return BitConverter.ToInt16(buffer);
        }

        public float ReadFloat(IntPtr address)
        {
            ThrowIfNotAttached();

            Span<byte> buffer = stackalloc byte[4];
            readProcessMemory(ProcessHandle, address, buffer, out _);
            return BitConverter.ToSingle(buffer);
        }

        public double ReadDouble(IntPtr address)
        {
            ThrowIfNotAttached();

            Span<byte> buffer = stackalloc byte[8];
            readProcessMemory(ProcessHandle, address, buffer, out _);
            return BitConverter.ToDouble(buffer);
        }

        public byte[] ReadBytes(IntPtr address, int length)
        {
            ThrowIfNotAttached();

            byte[] buffer = new byte[length];
            readProcessMemory(ProcessHandle, address, buffer, out _);
            return buffer;
        }

        public void ReadBytes(IntPtr address, Span<byte> buffer)
        {
            ThrowIfNotAttached();

            readProcessMemory(ProcessHandle, address, buffer, out _);
        }

        public string ReadString(IntPtr address, int length)
        {
            ThrowIfNotAttached();

            byte[] bytes = ReadBytes(address, length);

            int nullIndex = Array.IndexOf(bytes, (byte)0);
            int strLength = (nullIndex >= 0) ? nullIndex : length;

            return System.Text.Encoding.UTF8.GetString(bytes, 0, strLength);
        }

        public T Read<T>(IntPtr address) where T : struct
        {
            ThrowIfNotAttached();

            int byteSize = Marshal.SizeOf(typeof(T));

            byte[] buffer = new byte[byteSize];

            readProcessMemory(ProcessHandle, address, buffer, out _);

            return byteArrayToStructure<T>(buffer);
        }

        public IntPtr GetModuleBase(string moduleName)
        {
            ThrowIfNotAttached();

            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines($"/proc/{Process!.Id}/maps"))
                {
                    string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 6 || !Path.GetFileName(fields[5]).Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] addressRange = fields[0].Split('-');
                    if (addressRange.Length == 2 && long.TryParse(addressRange[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long baseAddress))
                        return new IntPtr(baseAddress);
                }

                return IntPtr.Zero;
            }

            foreach (ProcessModule mod in Process!.Modules)
            {
                if (mod.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    return mod.BaseAddress;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (OperatingSystem.IsWindows() && ProcessHandle != IntPtr.Zero)
                WindowsAPI.CloseHandle(ProcessHandle);
        }

        #endregion

        #region Conversion

        // https://stackoverflow.com/a/50672487

        private static T byteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

            try
            {
                return (T)(Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T)) ?? throw new InvalidOperationException());
            }
            finally
            {
                handle.Free();
            }
        }

        // maybe useless unless we write memory :)
        private static byte[] structureToByteArray(object obj)
        {
            int length = Marshal.SizeOf(obj);

            byte[] array = new byte[length];

            IntPtr pointer = Marshal.AllocHGlobal(length);

            Marshal.StructureToPtr(obj, pointer, true);
            Marshal.Copy(pointer, array, 0, length);
            Marshal.FreeHGlobal(pointer);

            return array;
        }

        #endregion

        #region Pattern Scan

        public IntPtr? ResolveFromPatternInfo(PatternInfo pattern, IEnumerable<MemoryRegion>? regions = null)
        {
            IntPtr? baseAddress = (regions != null)
                ? FindPattern(regions, pattern.Pattern)
                : FindPattern(pattern.Pattern);

            if (baseAddress == null)
                return null;

            return baseAddress + pattern.Offset;
        }

        public IntPtr?[] ResolveFromPatternInfos(PatternInfo[] patterns, IEnumerable<MemoryRegion>? regions = null)
        {
            byte?[][] rawPatterns = new byte?[patterns.Length][];

            for (int i = 0; i < patterns.Length; i++)
                rawPatterns[i] = patterns[i].Pattern;

            IntPtr?[] baseAddresses = (regions != null)
                ? FindPatterns(regions, ProcessHandle, rawPatterns)
                : FindPatterns(ProcessHandle, rawPatterns);

            for (int i = 0; i < baseAddresses.Length; i++)
            {
                if (baseAddresses[i] != null)
                    baseAddresses[i] = baseAddresses[i]!.Value + patterns[i].Offset;
            }

            return baseAddresses;
        }

        public static IntPtr? FindPattern(IntPtr processHandle, byte?[] pattern)
        {
            var regions = QueryMemoryRegions(processHandle);
            return FindPattern(regions, processHandle, pattern);
        }

        public static IntPtr? FindPattern(IEnumerable<MemoryRegion> regions, IntPtr processHandle, byte?[] pattern)
        {
            return FindPatterns(regions, processHandle, new[] { pattern })[0];
        }

        public static IntPtr?[] FindPatterns(IntPtr processHandle, byte?[][] patterns)
        {
            var regions = QueryMemoryRegions(processHandle);
            return FindPatterns(regions, processHandle, patterns);
        }

        public static IntPtr?[] FindPatterns(IEnumerable<MemoryRegion> regions, IntPtr processHandle, byte?[][] patterns)
        {
            const int buffer_size = 64 * 1024;

            IntPtr?[] results = new IntPtr?[patterns.Length];

            if (patterns.Length == 0)
                return results;

            int maxPatternLength = 0;

            foreach (byte?[] pattern in patterns)
            {
                if (pattern.Length == 0)
                    throw new ArgumentException("Pattern cannot be empty.", nameof(patterns));

                maxPatternLength = Math.Max(maxPatternLength, pattern.Length);
            }

            // 保留该块到下一块
            // 设置为 patternLength - 1 后总能让新块的第一个字节开始进行匹配
            int headSize = maxPatternLength - 1;
            int remainingPatterns = patterns.Length;

            byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer_size + headSize);

            try
            {
                foreach (var region in regions)
                {
                    IntPtr regionStart = region.BaseAddress;
                    int regionSize = region.RegionSize.ToInt32();

                    int copiedTail = 0;

                    for (int offset = 0; offset < regionSize; offset += buffer_size)
                    {
                        // 在跨块的时候复制保留的数据到数组前
                        if (copiedTail > 0)
                            Array.Copy(sharedBuffer, buffer_size, sharedBuffer, 0, copiedTail);

                        int readSize = Math.Min(buffer_size + headSize - copiedTail, regionSize - offset);

                        IntPtr bytesRead;

                        if (!readProcessMemory(processHandle, regionStart + offset, sharedBuffer.AsSpan(copiedTail, readSize), out bytesRead)
                            || bytesRead <= 0)
                        {
                            copiedTail = 0;
                            continue;
                        }

                        int totalSize = bytesRead.ToInt32() + copiedTail;

                        // 滑动窗口查找
                        for (int i = 0; i < totalSize && remainingPatterns > 0; i++)
                        {
                            for (int p = 0; p < patterns.Length; p++)
                            {
                                if (results[p] != null)
                                    continue;

                                byte?[] pattern = patterns[p];

                                if (i > totalSize - pattern.Length || !matchesPattern(sharedBuffer, i, pattern))
                                    continue;

                                results[p] = new IntPtr(regionStart + offset + i - copiedTail);
                                remainingPatterns--;

                                if (remainingPatterns == 0)
                                {
                                    return results;
                                }
                            }
                        }

                        // 实际逻辑上 bytesRead 不会小于 headSize
                        if (headSize > 0 && totalSize >= headSize)
                        {
                            Array.Copy(sharedBuffer, totalSize - headSize, sharedBuffer, buffer_size, headSize);
                            copiedTail = headSize;
                        }
                        else
                        {
                            copiedTail = 0;
                        }
                    }
                }

                return results;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }
        }

        private static bool matchesPattern(byte[] buffer, int offset, byte?[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != null && buffer[offset + i] != pattern[i])
                    return false;
            }

            return true;
        }

        public IntPtr? FindPattern(byte?[] pattern) => FindPattern(ProcessHandle, pattern);
        public IntPtr? FindPattern(IEnumerable<MemoryRegion> regions, byte?[] pattern) => FindPattern(regions, ProcessHandle, pattern);

        public static List<MemoryRegion> QueryMemoryRegions(IntPtr processHandle)
        {
            List<MemoryRegion> regions = new List<MemoryRegion>();

            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines($"/proc/{processHandle.ToInt32()}/maps"))
                {
                    string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 2 || fields[1].Length < 2 || fields[1][0] != 'r' || fields[1][1] != 'w')
                        continue;

                    string[] addressRange = fields[0].Split('-');
                    if (addressRange.Length != 2
                        || !long.TryParse(addressRange[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long startAddress)
                        || !long.TryParse(addressRange[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long endAddress))
                        continue;

                    regions.Add(new MemoryRegion
                    {
                        BaseAddress = new IntPtr(startAddress),
                        RegionSize = new IntPtr(endAddress - startAddress),
                    });
                }

                return regions;
            }

            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Memory reading is supported only on Windows and Linux.");

            IntPtr address = IntPtr.Zero;

            while (true)
            {
                WindowsAPI.MEMORY_BASIC_INFORMATION memInfo;
                int result = WindowsAPI.VirtualQueryEx(processHandle, address, out memInfo, (uint)Marshal.SizeOf(typeof(WindowsAPI.MEMORY_BASIC_INFORMATION)));
                if (result == 0)
                    break;

                bool isCommitted = (memInfo.State & 0x1000) != 0; // MEM_COMMIT
                bool isReadable =
                    (memInfo.Protect & 0x04) != 0 || // PAGE_READWRITE
                    (memInfo.Protect & 0x40) != 0; // PAGE_EXECUTE_READWRITE

                if (isCommitted && isReadable)
                {
                    regions.Add(new MemoryRegion
                    {
                        BaseAddress = address,
                        RegionSize = memInfo.RegionSize,
                    });
                }

                address = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize);
            }

            return regions;
        }

        private static unsafe bool readProcessMemory(IntPtr processHandle, IntPtr address, Span<byte> buffer, out IntPtr bytesRead)
        {
            if (OperatingSystem.IsWindows())
                return WindowsAPI.ReadProcessMemory(processHandle, address, buffer, buffer.Length, out bytesRead);

            if (OperatingSystem.IsLinux())
            {
                if (buffer.Length == 0)
                {
                    bytesRead = IntPtr.Zero;
                    return true;
                }

                fixed (byte* bufferPointer = buffer)
                {
                    IOVector localVector = new IOVector { Base = bufferPointer, Length = (nuint)buffer.Length };
                    IOVector remoteVector = new IOVector { Base = address.ToPointer(), Length = (nuint)buffer.Length };
                    nint result = process_vm_readv(processHandle.ToInt32(), &localVector, 1, &remoteVector, 1, 0);
                    bytesRead = new IntPtr(result);
                    return result == buffer.Length;
                }
            }

            throw new PlatformNotSupportedException("Memory reading is supported only on Windows and Linux.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct IOVector
        {
            public void* Base;
            public nuint Length;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe nint process_vm_readv(int processId, IOVector* localVectors, nuint localVectorCount, IOVector* remoteVectors, nuint remoteVectorCount, nuint flags);

        #endregion

        protected void ThrowIfNotAttached()
        {
            if (!IsAttached)
                throw new InvalidOperationException("Process is not attached or has exited.");
        }
    }

    public class MemoryRegion
    {
        public IntPtr BaseAddress;
        public IntPtr RegionSize;
    }

    public class PatternInfo
    {
        public byte?[] Pattern;
        public int Offset;

        public PatternInfo(string pattern, int offset = 0)
        {
            Pattern = ParsePattern(pattern);
            Offset = offset;
        }

        public PatternInfo(byte?[] pattern, int offset = 0)
        {
            Pattern = pattern;
            Offset = offset;
        }

        /// <summary>
        /// 用于将形如 "89 ?? ?? ?? 8B ?? ??" 的字符串解析为 pattern 数组
        /// </summary>
        public static byte?[] ParsePattern(string pattern)
        {
            string[] tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<byte?> result = new List<byte?>();

            foreach (string token in tokens)
            {
                if (token == "??" || token == "?")
                    result.Add(null);
                else
                    result.Add(Convert.ToByte(token, 16));
            }

            return result.ToArray();
        }
    }
}
