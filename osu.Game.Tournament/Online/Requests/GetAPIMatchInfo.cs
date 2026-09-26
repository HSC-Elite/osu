// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.IO.Network;
using osu.Game.Online.API;
using osu.Game.Tournament.Online.Requests.Responses;

namespace osu.Game.Tournament.Online.Requests
{
    public class GetAPIMatchInfo : APIRequest<APIMatchInfo>
    {
        public int MatchID;
        public long? AfterEvent;

        public GetAPIMatchInfo(int matchID)
        {
            MatchID = matchID;
        }

        protected override WebRequest CreateWebRequest()
        {
            var request = base.CreateWebRequest();

            if (AfterEvent.HasValue)
                request.AddParameter("after", AfterEvent.ToString());

            return request;
        }

        protected override string Target => $"matches/{MatchID}";
    }
}
