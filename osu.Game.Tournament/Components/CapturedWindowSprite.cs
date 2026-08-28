// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Veldrid;
using osu.Framework.Graphics.Veldrid.Textures;
using osu.Framework.Graphics.OpenGL.Textures;
using osu.Framework.Logging;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.Models;
using osuTK;
using SixLabors.ImageSharp.PixelFormats;
using Vortice.Direct3D11;
using FillMode = osu.Framework.Graphics.FillMode;
using static osu.Game.Tournament.WindowsAPI;

namespace osu.Game.Tournament.Components
{
    public partial class CapturedWindowSprite : CompositeDrawable
    {
        private Sprite sprite = null!;
        private readonly string targetWindowTitle;
        private ICaptureSource? capture;
        private D3D11ExternalTexture? externalTexture;
        private Texture? cpuTexture;
        private Texture? dmaBufTexture;
        private readonly int? linuxClientIndex;
        private IntPtr targetHwnd;
        private bool d3d11Available;
        private Thread? windowWatcherThread;
        private readonly ManualResetEventSlim watcherStop = new ManualResetEventSlim();
        private bool watcherStopDisposed;
        private volatile bool watcherRunning;
        private IntPtr watchedHwnd;
        private volatile bool watchedAlive;
        private Texture? pendingSpriteTexture;
        private volatile bool spriteTextureAssignmentsStopped;

        private bool isWindowsLive = false;

        public CapturedWindowSprite(string windowTitle, int? linuxClientIndex = null)
        {
            Masking = true;
            AlwaysPresent = true;
            targetWindowTitle = windowTitle;
            this.linuxClientIndex = linuxClientIndex;
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(TournamentConfigManager config)
        {
            sprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fit,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };

            Name = $"WindowCapture<{targetWindowTitle}>";

            AddInternal(sprite);

            config.BindWith(TournamentConfig.CaptureFrameRate, FrameRate);

            if (OperatingSystem.IsWindows())
            {
                d3d11Available = D3D11Interop.TryGetD3D11Device(renderer, out var device, out _, out _);

                if (d3d11Available)
                    capture = new WgcCaptureSource(new WgcCapture(device!));
                else
                    capture = new BitBltCaptureSource();

                watcherRunning = true;
                windowWatcherThread = new Thread(watchWindowLoop)
                {
                    IsBackground = true,
                    Name = $"WindowWatcher<{targetWindowTitle}>"
                };
                windowWatcherThread.Start();
            }
            else if (OperatingSystem.IsLinux() && linuxClientIndex != null)
            {
                capture = new ObsVkCaptureSource(linuxClientIndex.Value);
                capture.StartForWindow(IntPtr.Zero);
            }
        }

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        public BindableInt FrameRate { get; } = new BindableInt(60)
        {
            MinValue = 30,
            MaxValue = 360,
            Default = 60,
        };

        private bool captureErrorReported;

        protected override void Update()
        {
            base.Update();

            if (capture == null)
                return;

            if (OperatingSystem.IsLinux())
            {
                if (capture.IsRunning)
                    this.FadeIn(100);
                else
                    this.FadeOut(100);

                return;
            }

            if (targetHwnd == IntPtr.Zero || !IsWindow(targetHwnd) || !isWindowsLive)
            {
                if (capture.IsRunning)
                    capture.Stop();

                targetHwnd = watchedHwnd;

                if (targetHwnd != IntPtr.Zero && watchedAlive)
                {
                    try
                    {
                        capture.StartForWindow(targetHwnd);
                        isWindowsLive = true;
                        captureErrorReported = false;
                    }
                    catch (Exception e)
                    {
                        if (!captureErrorReported)
                        {
                            Logger.Error(e, $"{targetWindowTitle} Capture Error");
                            captureErrorReported = true;
                        }

                        isWindowsLive = false;
                    }
                }
                else
                {
                    isWindowsLive = false;
                }
            }

            if (!isWindowsLive)
            {
                this.FadeOut(100);
                return;
            }

            this.FadeIn(100);
        }

        private void consumePendingFrame(CaptureFrame frame, IRenderer renderer)
        {
            if (!frame.IsValid)
                return;

            bool resourceOwnershipTransferred = false;
            Texture? textureToApply = null;

            try
            {
                resourceOwnershipTransferred = capture?.ApplyFrame(frame, renderer, ref externalTexture, ref cpuTexture, ref dmaBufTexture, out textureToApply) == true;

                sprite.Scale = new Vector2(1, frame.DmaBuf?.Flipped == true ? -1 : 1);

                if (textureToApply != null)
                    queueSpriteTexture(textureToApply);
            }
            finally
            {
                frame.ReleaseResources(discardUpload: !resourceOwnershipTransferred);
            }
        }

        private void queueSpriteTexture(Texture texture)
        {
            if (spriteTextureAssignmentsStopped)
            {
                texture.Dispose();
                return;
            }

            var previousTexture = Interlocked.Exchange(ref pendingSpriteTexture, texture);

            if (previousTexture != null && previousTexture != texture)
                previousTexture.Dispose();

            if (spriteTextureAssignmentsStopped)
            {
                Interlocked.Exchange(ref pendingSpriteTexture, null)?.Dispose();
                return;
            }

            Scheduler.AddOnce(applyPendingSpriteTexture);
        }

        private void applyPendingSpriteTexture()
        {
            var texture = Interlocked.Exchange(ref pendingSpriteTexture, null);

            if (texture == null)
                return;

            if (spriteTextureAssignmentsStopped)
            {
                texture.Dispose();
                return;
            }

            sprite.Texture = texture;
        }

        protected override DrawNode CreateDrawNode() => new CaptureDrawNode(this);

        private sealed class CaptureDrawNode : CompositeDrawableDrawNode
        {
            private readonly Stopwatch stopwatch = Stopwatch.StartNew();
            private double elapsedMs;

            public CaptureDrawNode(CapturedWindowSprite source)
                : base(source)
            {
            }

            protected override void Draw(IRenderer renderer)
            {
                var source = (CapturedWindowSprite)Source;
                double interval = 1000.0 / source.FrameRate.Value;
                elapsedMs += stopwatch.Elapsed.TotalMilliseconds;
                stopwatch.Restart();

                var capture = source.capture;

                if (!source.spriteTextureAssignmentsStopped && capture != null && elapsedMs >= interval)
                {
                    if (capture.TryAcquireLatestFrame(out var frame))
                        source.consumePendingFrame(frame, renderer);

                    elapsedMs = Math.Min(elapsedMs - interval, interval);
                }

                base.Draw(renderer);
            }
        }

        private void watchWindowLoop()
        {
            while (watcherRunning)
            {
                try
                {
                    IntPtr hwnd = watchedHwnd;

                    if (hwnd != IntPtr.Zero && !IsWindow(hwnd))
                    {
                        watchedHwnd = IntPtr.Zero;
                        watchedAlive = false;
                    }

                    if (watchedHwnd == IntPtr.Zero)
                    {
                        hwnd = FindWindowByPartialTitle(targetWindowTitle);
                        watchedHwnd = hwnd;
                        watchedAlive = hwnd != IntPtr.Zero;
                    }
                    else
                    {
                        watchedAlive = true;
                    }
                }
                catch
                {
                    watchedHwnd = IntPtr.Zero;
                    watchedAlive = false;
                }

                if (watcherStop.Wait(500))
                    break;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            var captureToDispose = capture;
            capture = null;
            spriteTextureAssignmentsStopped = true;

            Interlocked.Exchange(ref pendingSpriteTexture, null)?.Dispose();

            base.Dispose(isDisposing);
            captureToDispose?.Dispose();
            externalTexture?.Dispose();
            cpuTexture?.Dispose();
            dmaBufTexture?.Dispose();

            watcherRunning = false;

            if (watcherStopDisposed)
                return;

            watcherStop.Set();

            if (windowWatcherThread == null || windowWatcherThread.Join(1000))
            {
                watcherStop.Dispose();
                watcherStopDisposed = true;
            }
        }

        private interface ICaptureSource : IDisposable
        {
            bool IsRunning { get; }
            void StartForWindow(IntPtr hwnd);
            void Stop();
            bool TryAcquireLatestFrame(out CaptureFrame frame);
            bool ApplyFrame(CaptureFrame frame, IRenderer renderer, ref D3D11ExternalTexture? externalTexture, ref Texture? cpuTexture, ref Texture? dmaBufTexture, out Texture? textureToApply);
        }

        private readonly struct CaptureFrame
        {
            public static readonly CaptureFrame EMPTY = new CaptureFrame(CaptureFrameKind.None, null, null, null, 0, 0);

            public CaptureFrameKind Kind { get; }
            public ID3D11Texture2D? D3D11Texture { get; }
            public ITextureUpload? Upload { get; }
            public DmaBufCaptureFrame? DmaBuf { get; }
            public int Width { get; }
            public int Height { get; }

            public bool IsValid => Kind != CaptureFrameKind.None;

            private CaptureFrame(CaptureFrameKind kind, ID3D11Texture2D? texture, ITextureUpload? upload, DmaBufCaptureFrame? dmaBuf, int width, int height)
            {
                Kind = kind;
                D3D11Texture = texture;
                Upload = upload;
                DmaBuf = dmaBuf;
                Width = width;
                Height = height;
            }

            public static CaptureFrame FromD3D11(ID3D11Texture2D texture, int width, int height)
                => new CaptureFrame(CaptureFrameKind.D3D11Texture, texture, null, null, width, height);

            public static CaptureFrame FromUpload(ITextureUpload upload, int width, int height)
                => new CaptureFrame(CaptureFrameKind.CpuUpload, null, upload, null, width, height);

            public static CaptureFrame FromDmaBuf(DmaBufCaptureFrame dmaBuf)
                => new CaptureFrame(CaptureFrameKind.DmaBuf, null, null, dmaBuf, dmaBuf.Width, dmaBuf.Height);

            public void ReleaseResources(bool discardUpload)
            {
                D3D11Texture?.Release();

                if (discardUpload)
                    Upload?.Dispose();

                DmaBuf?.Dispose();
            }
        }

        private enum CaptureFrameKind
        {
            None,
            D3D11Texture,
            CpuUpload,
            DmaBuf
        }

        private sealed class WgcCaptureSource : ICaptureSource
        {
            private readonly WgcCapture capture;

            public WgcCaptureSource(WgcCapture capture)
            {
                this.capture = capture;
            }

            public bool IsRunning => capture.IsRunning;

            public void StartForWindow(IntPtr hwnd) => capture.StartForWindow(hwnd);

            public void Stop() => capture.Stop();

            public bool TryAcquireLatestFrame(out CaptureFrame frame)
            {
                if (capture.TryAcquireLatestTexture(out var texture, out int width, out int height))
                {
                    frame = CaptureFrame.FromD3D11(texture, width, height);
                    return true;
                }

                frame = CaptureFrame.EMPTY;
                return false;
            }

            public bool ApplyFrame(CaptureFrame frame, IRenderer renderer, ref D3D11ExternalTexture? externalTexture, ref Texture? cpuTexture, ref Texture? dmaBufTexture, out Texture? textureToApply)
            {
                textureToApply = null;

                if (frame.Kind != CaptureFrameKind.D3D11Texture || frame.D3D11Texture == null)
                    return false;

                if (externalTexture == null || externalTexture.Width != frame.Width || externalTexture.Height != frame.Height)
                {
                    externalTexture = new D3D11ExternalTexture(renderer, frame.Width, frame.Height);
                    textureToApply = externalTexture;
                }

                externalTexture.UpdateFrom(frame.D3D11Texture);
                return false;
            }

            public void Dispose() => capture.Dispose();
        }

        private sealed class ObsVkCaptureSource : ICaptureSource
        {
            private static readonly Lazy<ObsVkCaptureServer> server = new Lazy<ObsVkCaptureServer>(() => new ObsVkCaptureServer());

            private readonly int clientIndex;

            public ObsVkCaptureSource(int clientIndex)
            {
                this.clientIndex = clientIndex;
                _ = server.Value;
            }

            public bool IsRunning => server.Value.IsCapturing(clientIndex);

            public void StartForWindow(IntPtr hwnd)
            {
            }

            public void Stop()
            {
            }

            public bool TryAcquireLatestFrame(out CaptureFrame frame)
            {
                if (server.Value.TryAcquirePendingTexture(clientIndex, out var dmaBuf))
                {
                    frame = CaptureFrame.FromDmaBuf(dmaBuf);
                    return true;
                }

                frame = CaptureFrame.EMPTY;
                return false;
            }

            public bool ApplyFrame(CaptureFrame frame, IRenderer renderer, ref D3D11ExternalTexture? externalTexture, ref Texture? cpuTexture, ref Texture? dmaBufTexture, out Texture? textureToApply)
            {
                textureToApply = null;

                if (frame.Kind != CaptureFrameKind.DmaBuf || frame.DmaBuf == null || frame.DmaBuf.Planes.Count == 0)
                    return false;

                var planes = new DmaBufExternalTexturePlane[frame.DmaBuf.Planes.Count];

                for (int i = 0; i < planes.Length; i++)
                {
                    var plane = frame.DmaBuf.Planes[i];

                    if (plane.FileDescriptor.IsInvalid)
                        return false;

                    planes[i] = new DmaBufExternalTexturePlane(plane.Stride, plane.Offset, checked((int)plane.FileDescriptor.DangerousGetHandle()));
                }

                if (!DmaBufExternalTexture.TryCreate(renderer, frame.Width, frame.Height, frame.DmaBuf.Fourcc, frame.DmaBuf.Modifier, planes, out var texture, out var failure))
                {
                    Logger.Log($"obs-vkcapture DMA-BUF import failed: {failure}", level: LogLevel.Error);
                    return false;
                }

                dmaBufTexture?.Dispose();
                dmaBufTexture = texture;
                textureToApply = texture;
                return false;
            }

            public void Dispose()
            {
            }
        }

        private sealed class BitBltCaptureSource : ICaptureSource
        {
            private System.Drawing.Bitmap? bitmapPool;
            private System.Drawing.Graphics? graphicsPool;
            private byte[]? rawBufferPool;
            private int poolWidth;
            private int poolHeight;
            private int poolStride;
            private IntPtr hwnd;

            public bool IsRunning { get; private set; }

            public void StartForWindow(IntPtr hwnd)
            {
                this.hwnd = hwnd;
                IsRunning = true;
            }

            public void Stop()
            {
                IsRunning = false;
                hwnd = IntPtr.Zero;
            }

            public bool TryAcquireLatestFrame(out CaptureFrame frame)
            {
                frame = CaptureFrame.EMPTY;

                if (!IsRunning)
                    return false;

                if (hwnd == IntPtr.Zero)
                    return false;

                if (!GetWindowRect(hwnd, out RECT rect))
                    return false;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                if (width <= 0 || height <= 0)
                    return false;

                if (bitmapPool == null || graphicsPool == null || poolWidth != width || poolHeight != height)
                {
                    graphicsPool?.Dispose();
                    bitmapPool?.Dispose();

                    bitmapPool = new System.Drawing.Bitmap(width, height, PixelFormat.Format24bppRgb);
                    graphicsPool = System.Drawing.Graphics.FromImage(bitmapPool);

                    var tmpData = bitmapPool.LockBits(
                        new Rectangle(0, 0, width, height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);
                    poolStride = Math.Abs(tmpData.Stride);
                    bitmapPool.UnlockBits(tmpData);

                    rawBufferPool = new byte[poolStride * height];

                    poolWidth = width;
                    poolHeight = height;
                }

                ArrayPoolTextureUpload? upload = null;

                try
                {
                    copyWindowToBitmap(hwnd, graphicsPool, width, height);
                    copyBitmapToRawBuffer(bitmapPool, rawBufferPool!);

                    upload = new ArrayPoolTextureUpload(width, height);
                    convertRgr24ToRgba32(rawBufferPool!, width, height, poolStride, upload.RawData);

                    frame = CaptureFrame.FromUpload(upload, width, height);
                    upload = null;
                    return true;
                }
                catch
                {
                    upload?.Dispose();
                    return false;
                }
            }

            private static void copyWindowToBitmap(IntPtr hwnd, System.Drawing.Graphics graphics, int width, int height)
            {
                IntPtr hdcDest = IntPtr.Zero;
                IntPtr hdcSrc = IntPtr.Zero;

                try
                {
                    hdcDest = graphics.GetHdc();
                    hdcSrc = GetWindowDC(hwnd);

                    if (hdcSrc == IntPtr.Zero || !BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, 0x00CC0020))
                        throw new InvalidOperationException("Failed to capture window contents.");
                }
                finally
                {
                    if (hdcDest != IntPtr.Zero)
                        graphics.ReleaseHdc(hdcDest);

                    if (hdcSrc != IntPtr.Zero)
                        ReleaseDC(hwnd, hdcSrc);
                }
            }

            private static void copyBitmapToRawBuffer(System.Drawing.Bitmap bitmap, byte[] rawBuffer)
            {
                BitmapData? bmpData = null;

                try
                {
                    bmpData = bitmap.LockBits(
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);

                    Marshal.Copy(bmpData.Scan0, rawBuffer, 0, rawBuffer.Length);
                }
                finally
                {
                    if (bmpData != null)
                        bitmap.UnlockBits(bmpData);
                }
            }

            private static void convertRgr24ToRgba32(byte[] src, int width, int height, int stride, Span<Rgba32> dst)
            {
                int dstIdx = 0;

                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;

                    for (int x = 0; x < width; x++)
                    {
                        int i = rowStart + x * 3;
                        byte b = src[i + 0];
                        byte g = src[i + 1];
                        byte r = src[i + 2];

                        dst[dstIdx++] = new Rgba32(r, g, b, 255);
                    }
                }
            }

            public bool ApplyFrame(CaptureFrame frame, IRenderer renderer, ref D3D11ExternalTexture? externalTexture, ref Texture? cpuTexture, ref Texture? dmaBufTexture, out Texture? textureToApply)
            {
                textureToApply = null;

                if (frame.Kind != CaptureFrameKind.CpuUpload || frame.Upload == null)
                    return false;

                if (cpuTexture == null || cpuTexture.Width != frame.Width || cpuTexture.Height != frame.Height)
                {
                    cpuTexture = renderer.CreateTexture(frame.Width, frame.Height);
                    textureToApply = cpuTexture;
                }

                cpuTexture.SetData(frame.Upload);
                return true;
            }

            public void Dispose()
            {
                graphicsPool?.Dispose();
                bitmapPool?.Dispose();
            }
        }
    }
}
