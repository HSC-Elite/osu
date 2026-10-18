// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input.StateChanges;
using osu.Framework.Utils;
using osu.Game.Replays;
using osuTK;
using static osu.Game.Input.Handlers.ReplayInputHandler;

namespace osu.Game.Rulesets.Osu.Replays
{
    /// <summary>
    /// Feeds recorded cursor and button states into a local player without entering replay playback.
    /// </summary>
    internal sealed class OsuReplayInputDriver
    {
        private readonly OsuReplayFrame[] frames;
        private readonly OsuInputManager input;
        private readonly Func<Vector2, Vector2> toScreenSpace;
        private int currentFrame = -1;
        private double lastTime = double.NegativeInfinity;

        public OsuReplayInputDriver(Replay replay, OsuInputManager input, Func<Vector2, Vector2> toScreenSpace)
        {
            frames = replay.Frames.Cast<OsuReplayFrame>().ToArray();
            if (frames.Length == 0 || frames.Any(f => !double.IsFinite(f.Time))
                                   || frames.Zip(frames.Skip(1), (a, b) => a.Time > b.Time).Any(outOfOrder => outOfOrder))
                throw new ArgumentException("Test player input requires non-empty, ordered osu! replay frames.", nameof(replay));

            this.input = input;
            this.toScreenSpace = toScreenSpace;
        }

        public void Update(double time)
        {
            bool seeking = double.IsNegativeInfinity(lastTime) || time < lastTime;
            if (seeking)
            {
                Release();
                currentFrame = -1;
            }

            // Consume every crossed button transition, including short taps between updates.
            while (currentFrame + 1 < frames.Length && frames[currentFrame + 1].Time <= time)
            {
                currentFrame++;
                if (!seeking)
                    apply(frames[currentFrame].Position, frames[currentFrame].Actions);
            }

            var frame = frames[Math.Max(0, currentFrame)];
            var position = frame.Position;
            if (currentFrame >= 0 && currentFrame + 1 < frames.Length)
            {
                var next = frames[currentFrame + 1];
                position = Interpolation.ValueAt(time, frame.Position, next.Position, frame.Time, next.Time);
            }

            apply(position, currentFrame < 0 || time > frames[^1].Time ? new List<OsuAction>() : frame.Actions);
            lastTime = time;
        }

        public void Release() => new ReplayState<OsuAction> { PressedActions = new List<OsuAction>() }.Apply(input.CurrentState, input);

        private void apply(Vector2 position, List<OsuAction> actions)
        {
            new MousePositionAbsoluteInput { Position = toScreenSpace(position) }.Apply(input.CurrentState, input);
            new ReplayState<OsuAction> { PressedActions = actions }.Apply(input.CurrentState, input);
        }
    }
}
