// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;

namespace osu.Game.Tournament.StableClient.Protocol
{
    public class bMessage
    {
        public readonly string SendingClient;
        public readonly string Message;
        public readonly string Target;
        public readonly int SenderId;

        public bool IsPrivate => Target.Length == 0 || Target[0] != '#';

        public bMessage(BinaryReader reader)
        {
            SendingClient = reader.ReadBString();
            Message = reader.ReadBString();
            Target = reader.ReadBString();
            SenderId = reader.ReadInt32();
        }
    }
}
