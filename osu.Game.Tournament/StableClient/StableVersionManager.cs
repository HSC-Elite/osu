// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;

namespace osu.Game.Tournament.StableClient
{
    public class StableVersionManager
    {
        private const string update_url = "https://osu.ppy.sh/web/check-updates.php?action=check&stream=cuttingedge";

        private readonly IAPIProvider api;
        private static readonly HttpClient http = new HttpClient();

        public StableVersionManager(IAPIProvider api)
        {
            this.api = api;
        }

        public async Task<StableVersionInfo?> GetLatestVersionAsync()
        {
            try
            {
                // 1. 使用原生 API Request 获取准确的版本号
                var changelogReq = new GetChangelogBuildRequest("cuttingedge", string.Empty);

                await api.PerformAsync(changelogReq).ConfigureAwait(false);

                string? rawVersion = changelogReq.Response?.Version;
                if (string.IsNullOrEmpty(rawVersion)) return null;

                // 2. 从 check-updates 获取对应的文件哈希
                string updateJson = await http.GetStringAsync(update_url).ConfigureAwait(false);
                var files = JsonConvert.DeserializeObject<List<UpdateFile>>(updateJson);

                var exe = files?.FirstOrDefault(f => f.Filename == "osu!.exe");
                if (exe == null) return null;

                return new StableVersionInfo
                {
                    Version = $"b{rawVersion}tourney",
                    FileHash = exe.FileHash
                };
            }
            catch (System.Exception e)
            {
                Logger.Error(e, "Failed to fetch latest stable version and hash using API.");
                return null;
            }
        }

        public class StableVersionInfo
        {
            public string Version = string.Empty;
            public string FileHash = string.Empty;
        }

        private class UpdateFile
        {
            [JsonProperty("filename")]
            public string Filename = string.Empty;

            [JsonProperty("file_hash")]
            public string FileHash = string.Empty;
        }
    }
}
