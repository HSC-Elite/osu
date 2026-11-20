// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Online.Spectator
{
    /// <summary>
    /// Receives a single user's frames while their player loads, then forwards them in arrival order.
    /// All operations must take place on the update thread.
    /// </summary>
    public sealed class SpectatorFrameBuffer : IDisposable
    {
        private readonly ISpectatorDataSource source;
        private readonly int userId;
        private readonly Queue<FrameDataBundle> pending = new Queue<FrameDataBundle>();
        private Action<FrameDataBundle>? consumer;
        private bool disposed;

        public SpectatorFrameBuffer(ISpectatorDataSource source, int userId)
        {
            this.source = source;
            this.userId = userId;
            source.OnNewFrames += onNewFrames;
        }

        public void Attach(Action<FrameDataBundle> consumer)
        {
            if (disposed)
                return;

            if (this.consumer != null)
                throw new InvalidOperationException("A frame consumer is already attached.");

            while (!disposed && pending.Count > 0)
                consumer(pending.Dequeue());

            if (!disposed)
                this.consumer = consumer;
        }

        private void onNewFrames(int incomingUserId, FrameDataBundle bundle)
        {
            if (disposed || incomingUserId != userId)
                return;

            if (consumer == null)
                pending.Enqueue(bundle);
            else
                consumer(bundle);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            source.OnNewFrames -= onNewFrames;
            consumer = null;
            pending.Clear();
        }
    }
}
