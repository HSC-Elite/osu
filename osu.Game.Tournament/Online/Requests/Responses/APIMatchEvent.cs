// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Game.Tournament.Online;

namespace osu.Game.Tournament.Online.Requests.Responses
{
    public class APIMatchEvent
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("detail")]
        public MatchEventDetail Detail { get; set; } = null!;

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("user_id")]
        public int? UserID { get; set; }

        [JsonProperty("game")]
        public APIMatchGame? Game { get; set; }
    }

    public class MatchEventDetail
    {
        [JsonProperty("type")]
        public MatchEventType Type { get; set; }

        [JsonProperty("text")]
        public string? Text { get; set; }
    }

    [JsonConverter(typeof(MatchEventTypeConverter))]
    public enum MatchEventType
    {
        HostChanged,
        MatchCreated,
        MatchDisbanded,
        Other,
        PlayerJoined,
        PlayerKicked,
        PlayerLeft,
        Unknown,
    }
}
