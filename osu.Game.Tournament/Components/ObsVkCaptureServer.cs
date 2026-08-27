// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using osu.Framework.Logging;

namespace osu.Game.Tournament.Components
{
    internal sealed class ObsVkCaptureServer : IDisposable
    {
        public const string TestedRevision = "obs-vkcapture v1.5.6 (a9ea91f)";

        private const string socket_name = "\0/com/obsproject/vkcapture";
        private const int client_data_size = 128;
        private const int texture_data_size = 128;
        private const int control_data_size = 32;
        private const byte client_data_type = 10;
        private const byte texture_data_type = 11;
        private const int sol_socket = 1;
        private const int so_peercred = 17;
        private const int scm_rights = 1;
        private const int msg_ctrunc = 8;

        private readonly Socket listener;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object stateLock = new object();
        private readonly Dictionary<int, ClientState> clients = new Dictionary<int, ClientState>();
        private readonly Thread acceptThread;

        public ObsVkCaptureServer()
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("obs-vkcapture is supported only on Linux.");

            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socket_name));
            listener.Listen(16);

            acceptThread = new Thread(acceptLoop)
            {
                IsBackground = true,
                Name = "obs-vkcapture listener"
            };
            acceptThread.Start();
        }

        public bool IsCapturing(int clientIndex)
        {
            lock (stateLock)
                return clients.TryGetValue(clientIndex, out var state) && state.Connected;
        }

        public bool TryAcquirePendingTexture(int clientIndex, out DmaBufCaptureFrame frame)
        {
            lock (stateLock)
            {
                if (clients.TryGetValue(clientIndex, out var state) && state.PendingFrame != null)
                {
                    frame = state.PendingFrame;
                    state.PendingFrame = null;
                    return true;
                }
            }

            frame = null!;
            return false;
        }

        private void acceptLoop()
        {
            while (!cancellation.IsCancellationRequested)
            {
                Socket? socket = null;

                try
                {
                    socket = listener.Accept();
                    Socket clientSocket = socket;
                    var worker = new Thread(() => handleClient(clientSocket))
                    {
                        IsBackground = true,
                        Name = "obs-vkcapture client"
                    };
                    worker.Start();
                    socket = null;
                }
                catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException e) when (cancellation.IsCancellationRequested)
                {
                    Logger.Log($"obs-vkcapture listener stopped: {e.Message}", level: LogLevel.Debug);
                    return;
                }
                catch (Exception e)
                {
                    Logger.Error(e, "obs-vkcapture listener failed to accept a client");
                }
                finally
                {
                    socket?.Dispose();
                }
            }
        }

        private void handleClient(Socket socket)
        {
            int clientIndex = -1;
            long connectionId = 0;

            try
            {
                var clientData = new byte[client_data_size];
                receiveExactly(socket, clientData);

                if (clientData[0] != client_data_type)
                    throw new InvalidDataException("obs-vkcapture client sent an unknown handshake.");

                if (!tryGetClientIndex(socket, out clientIndex))
                {
                    Logger.Log("Ignoring obs-vkcapture connection that is not a tournament spectator client.", level: LogLevel.Debug);
                    return;
                }

                connectionId = markConnected(clientIndex);
                sendControl(socket);

                while (!cancellation.IsCancellationRequested)
                {
                    if (!tryReceiveTexture(socket, out var frame))
                        break;

                    publishFrame(clientIndex, connectionId, frame);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                Logger.Error(e, "obs-vkcapture client failed");
            }
            finally
            {
                socket.Dispose();

                if (clientIndex >= 0)
                    markDisconnected(clientIndex, connectionId);
            }
        }

        private long markConnected(int clientIndex)
        {
            lock (stateLock)
            {
                if (!clients.TryGetValue(clientIndex, out var state))
                    clients[clientIndex] = state = new ClientState();

                state.PendingFrame?.Dispose();
                state.PendingFrame = null;
                state.Connected = true;
                return ++state.ConnectionId;
            }
        }

        private void markDisconnected(int clientIndex, long connectionId)
        {
            lock (stateLock)
            {
                if (!clients.TryGetValue(clientIndex, out var state) || state.ConnectionId != connectionId)
                    return;

                state.PendingFrame?.Dispose();
                state.PendingFrame = null;
                state.Connected = false;
            }
        }

        private void publishFrame(int clientIndex, long connectionId, DmaBufCaptureFrame frame)
        {
            lock (stateLock)
            {
                if (!clients.TryGetValue(clientIndex, out var state) || state.ConnectionId != connectionId)
                {
                    frame.Dispose();
                    return;
                }

                state.PendingFrame?.Dispose();
                state.PendingFrame = frame;
            }
        }

        private static void sendControl(Socket socket)
        {
            var control = new byte[control_data_size];
            control[0] = 1;
            sendExactly(socket, control);
        }

        private static bool tryGetClientIndex(Socket socket, out int clientIndex)
        {
            clientIndex = -1;

            if (!tryGetPeerCredentials(socket, out var credentials) || credentials.Uid != geteuid())
                return false;

            try
            {
                string[] arguments = File.ReadAllText($"/proc/{credentials.Pid}/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < arguments.Length - 1; i++)
                {
                    if (!arguments[i].Equals("-spectateclient", StringComparison.OrdinalIgnoreCase)
                        || !int.TryParse(arguments[i + 1], out int parsedIndex)
                        || parsedIndex is < 0 or > 7)
                        continue;

                    clientIndex = parsedIndex;
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return false;
        }

        private static void receiveExactly(Socket socket, byte[] buffer)
        {
            int received = 0;

            while (received < buffer.Length)
            {
                int count = socket.Receive(buffer, received, buffer.Length - received, SocketFlags.None);

                if (count == 0)
                    throw new EndOfStreamException();

                received += count;
            }
        }

        private static void sendExactly(Socket socket, byte[] buffer)
        {
            int sent = 0;

            while (sent < buffer.Length)
                sent += socket.Send(buffer, sent, buffer.Length - sent, SocketFlags.None);
        }

        private static unsafe bool tryReceiveTexture(Socket socket, out DmaBufCaptureFrame frame)
        {
            var packet = new byte[texture_data_size];
            Span<int> fileDescriptors = stackalloc int[4];
            int descriptorCount = 0;
            int received;

            fixed (byte* packetPointer = packet)
            {
                var iovec = new IOVector
                {
                    Base = packetPointer,
                    Length = (nuint)packet.Length,
                };
                byte* control = stackalloc byte[32];
                var message = new MessageHeader
                {
                    Iov = &iovec,
                    IovLength = 1,
                    Control = control,
                    ControlLength = 32,
                };

                nint result = recvmsg(checked((int)socket.Handle), &message, 0);

                if (result == 0)
                {
                    frame = null!;
                    return false;
                }

                if (result < 0)
                    throw new SocketException(Marshal.GetLastWin32Error());

                if ((message.Flags & msg_ctrunc) != 0)
                    throw new InvalidDataException("obs-vkcapture texture file descriptors were truncated.");

                received = checked((int)result);

                if (message.ControlLength >= (nuint)sizeof(ControlHeader))
                {
                    var header = (ControlHeader*)control;

                    if (header->Level == sol_socket && header->Type == scm_rights && header->Length >= (nuint)sizeof(ControlHeader))
                    {
                        descriptorCount = Math.Min(4, checked((int)((header->Length - (nuint)sizeof(ControlHeader)) / sizeof(int))));
                        new ReadOnlySpan<int>(control + sizeof(ControlHeader), descriptorCount).CopyTo(fileDescriptors);
                    }
                }
            }

            while (received < packet.Length)
            {
                int count = socket.Receive(packet, received, packet.Length - received, SocketFlags.None);

                if (count == 0)
                {
                    closeDescriptors(fileDescriptors.Slice(0, descriptorCount));
                    frame = null!;
                    return false;
                }

                received += count;
            }

            if (packet[0] != texture_data_type)
            {
                closeDescriptors(fileDescriptors.Slice(0, descriptorCount));
                throw new InvalidDataException($"obs-vkcapture sent packet type {packet[0]} where texture packet type {texture_data_type} was expected.");
            }

            if (packet[1] is < 1 or > 4)
            {
                closeDescriptors(fileDescriptors.Slice(0, descriptorCount));
                throw new NotSupportedException($"obs-vkcapture exported an unsupported {packet[1]}-plane DMA-BUF texture. {describeTexture(packet, descriptorCount)}");
            }

            if (descriptorCount != packet[1])
            {
                closeDescriptors(fileDescriptors.Slice(0, descriptorCount));
                throw new InvalidDataException($"obs-vkcapture exported a {packet[1]}-plane DMA-BUF texture but passed {descriptorCount} file descriptors.");
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(2, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(6, 4));
            uint fourcc = unchecked((uint)BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(10, 4)));
            ulong modifier = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(46, 8));
            bool flipped = packet[58] != 0;
            var planes = new DmaBufCapturePlane[descriptorCount];

            for (int i = 0; i < descriptorCount; i++)
            {
                int stride = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(14 + i * 4, 4));
                int offset = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(30 + i * 4, 4));
                planes[i] = new DmaBufCapturePlane(stride, offset, new SafeFileHandle((IntPtr)fileDescriptors[i], ownsHandle: true));
            }

            frame = new DmaBufCaptureFrame(width, height, fourcc, modifier, flipped, planes);
            return true;
        }

        private static string describeTexture(byte[] packet, int descriptorCount)
        {
            int width = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(2, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(6, 4));
            uint fourcc = unchecked((uint)BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(10, 4)));
            ulong modifier = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(46, 8));
            int planeCount = Math.Min(packet[1], (byte)4);
            var planes = new List<string>(planeCount);

            for (int i = 0; i < planeCount; i++)
            {
                int stride = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(14 + i * 4, 4));
                int offset = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(30 + i * 4, 4));
                planes.Add($"{i}: stride={stride}, offset={offset}");
            }

            string fourccText = new string(new[]
            {
                (char)(fourcc & 0xff),
                (char)((fourcc >> 8) & 0xff),
                (char)((fourcc >> 16) & 0xff),
                (char)((fourcc >> 24) & 0xff),
            });

            return $"Descriptor: {width}x{height}, fourcc={fourccText} (0x{fourcc:x8}), modifier=0x{modifier:x16}, planes=[{string.Join("; ", planes)}], receivedFileDescriptors={descriptorCount}.";
        }

        private static unsafe bool tryGetPeerCredentials(Socket socket, out PeerCredentials credentials)
        {
            uint length = (uint)sizeof(PeerCredentials);
            return getsockopt(checked((int)socket.Handle), sol_socket, so_peercred, out credentials, ref length) == 0 && length == sizeof(PeerCredentials);
        }

        private static void closeDescriptors(ReadOnlySpan<int> descriptors)
        {
            foreach (int descriptor in descriptors)
            {
                if (descriptor >= 0)
                    close(descriptor);
            }
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Dispose();

            if (acceptThread.IsAlive)
                acceptThread.Join(1000);

            lock (stateLock)
            {
                foreach (var state in clients.Values)
                    state.PendingFrame?.Dispose();

                clients.Clear();
            }

            cancellation.Dispose();
        }

        private sealed class ClientState
        {
            public long ConnectionId;
            public bool Connected;
            public DmaBufCaptureFrame? PendingFrame;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct IOVector
        {
            public void* Base;
            public nuint Length;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct MessageHeader
        {
            public void* Name;
            public nuint NameLength;
            public IOVector* Iov;
            public nuint IovLength;
            public void* Control;
            public nuint ControlLength;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ControlHeader
        {
            public nuint Length;
            public int Level;
            public int Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PeerCredentials
        {
            public int Pid;
            public int Uid;
            public int Gid;
        }

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern unsafe nint recvmsg(int socket, MessageHeader* message, int flags);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern int getsockopt(int socket, int level, int name, out PeerCredentials value, ref uint length);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        private static extern int geteuid();

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        private static extern int close(int fileDescriptor);
    }

    internal sealed class DmaBufCaptureFrame : IDisposable
    {
        public int Width { get; }
        public int Height { get; }
        public uint Fourcc { get; }
        public ulong Modifier { get; }
        public bool Flipped { get; }
        public IReadOnlyList<DmaBufCapturePlane> Planes { get; }

        public DmaBufCaptureFrame(int width, int height, uint fourcc, ulong modifier, bool flipped, IReadOnlyList<DmaBufCapturePlane> planes)
        {
            Width = width;
            Height = height;
            Fourcc = fourcc;
            Modifier = modifier;
            Flipped = flipped;
            Planes = planes;
        }

        public void Dispose()
        {
            foreach (var plane in Planes)
                plane.Dispose();
        }
    }

    internal sealed class DmaBufCapturePlane : IDisposable
    {
        public int Stride { get; }
        public int Offset { get; }
        public SafeFileHandle FileDescriptor { get; }

        public DmaBufCapturePlane(int stride, int offset, SafeFileHandle fileDescriptor)
        {
            Stride = stride;
            Offset = offset;
            FileDescriptor = fileDescriptor;
        }

        public void Dispose() => FileDescriptor.Dispose();
    }
}
