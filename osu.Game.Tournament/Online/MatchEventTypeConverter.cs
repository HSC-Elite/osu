// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Game.Tournament.Online.Requests.Responses;

namespace osu.Game.Tournament.Online
{
    public class MatchEventTypeConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(MatchEventType);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            string? enumText = reader.Value?.ToString();

            if (string.IsNullOrEmpty(enumText))
                return MatchEventType.Unknown;

            string formattedText = enumText.Replace("-", string.Empty);

            return Enum.TryParse(typeof(MatchEventType), formattedText, true, out object? parsedEnum)
                ? parsedEnum
                : MatchEventType.Unknown;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not MatchEventType enumValue)
                return;

            string jsonValue = enumValue.ToString()
                                         .Replace("MatchCreated", "match-created")
                                         .Replace("HostChanged", "host-changed")
                                         .Replace("MatchDisbanded", "match-disbanded")
                                         .Replace("PlayerJoined", "player-joined")
                                         .Replace("PlayerKicked", "player-kicked")
                                         .Replace("PlayerLeft", "player-left")
                                         .Replace("Other", "other");

            writer.WriteValue(jsonValue);
        }
    }
}
