// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Tournament.IO;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Components
{
    public partial class TournamentBeatmapManager : Component
    {
        private readonly TournamentStorage storage;
        private readonly ConcurrentDictionary<int, SemaphoreSlim> downloadLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

        public TournamentBeatmapManager(TournamentStorage storage)
        {
            this.storage = storage;
        }

        public bool HasBeatmap(TournamentBeatmap beatmap)
        {
            if (beatmap.OnlineID <= 0)
                return false;

            string osuFilePath = getBeatmapPath(beatmap);

            if (!File.Exists(osuFilePath))
                return false;

            if (string.IsNullOrEmpty(beatmap.MD5Hash))
                return true;

            try
            {
                using FileStream stream = File.OpenRead(osuFilePath);
                return string.Equals(stream.ComputeMD5Hash(), beatmap.MD5Hash, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public WorkingBeatmap GetWorkingBeatmap(TournamentBeatmap beatmap)
        {
            if (!HasBeatmap(beatmap))
                throw new FileNotFoundException($"Beatmap {beatmap.OnlineID} is not available or failed integrity validation.", getBeatmapPath(beatmap));

            return new FlatWorkingBeatmap(getBeatmapPath(beatmap), beatmap.OnlineID);
        }

        public async Task<WorkingBeatmap> GetWorkingBeatmapAsync(
            TournamentBeatmap beatmap,
            bool downloadIfMissing = true,
            CancellationToken cancellationToken = default)
        {
            if (!await EnsureBeatmapAsync(beatmap, downloadIfMissing, cancellationToken).ConfigureAwait(false))
                throw new FileNotFoundException($"Beatmap {beatmap.OnlineID} is not available or failed integrity validation.", getBeatmapPath(beatmap));

            return new FlatWorkingBeatmap(getBeatmapPath(beatmap), beatmap.OnlineID);
        }

        public async Task<bool> EnsureBeatmapAsync(
            TournamentBeatmap beatmap,
            bool downloadIfMissing = true,
            CancellationToken cancellationToken = default)
        {
            if (HasBeatmap(beatmap))
                return true;

            if (!downloadIfMissing)
                return false;

            return await DownloadBeatmapOsuFile(beatmap, false, cancellationToken).ConfigureAwait(false)
                   && HasBeatmap(beatmap);
        }

        public async Task<bool> DownloadBeatmapOsuFile(TournamentBeatmap beatmap, bool forceRedownload, CancellationToken cancellationToken = default)
        {
            if (beatmap.OnlineID <= 0)
                return false;

            SemaphoreSlim downloadLock = downloadLocks.GetOrAdd(beatmap.OnlineID, _ => new SemaphoreSlim(1, 1));
            await downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string mapPath = storage.GetFullPath("map");
                Directory.CreateDirectory(mapPath);

                string osuFilePath = getBeatmapPath(beatmap);

                if (!forceRedownload && HasBeatmap(beatmap))
                    return true;

                using var req = new OsuWebRequest($"https://osu.ppy.sh/osu/{beatmap.OnlineID}");

                await req.PerformAsync(cancellationToken).ConfigureAwait(false);

                if (!req.Completed)
                    return false;

                byte[]? responseData = req.GetResponseData();

                if (responseData == null)
                    return false;

                if (!string.IsNullOrEmpty(beatmap.MD5Hash))
                {
                    using var responseStream = new MemoryStream(responseData);

                    if (!string.Equals(responseStream.ComputeMD5Hash(), beatmap.MD5Hash, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                string temporaryPath = $"{osuFilePath}.{Guid.NewGuid():N}.tmp";

                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, responseData, cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, osuFilePath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                return true;
            }
            finally
            {
                downloadLock.Release();
            }
        }

        private string getBeatmapPath(TournamentBeatmap beatmap) =>
            Path.Combine(storage.GetFullPath("map"), $"{beatmap.OnlineID}.osu");
    }
}
