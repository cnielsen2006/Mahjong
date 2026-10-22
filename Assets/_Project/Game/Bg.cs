using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mahjong.Game
{
    /// <summary>
    /// Runs heavy work off the main thread. WebGL has no threads, so there the jobs are queued and App.Update runs one per
    /// frame (after the loading overlay has had a chance to draw) and hands back the same Task the callers poll.
    /// </summary>
    public static class Bg
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool Threaded = false;
        static readonly Queue<Action> queue = new Queue<Action>();

        public static Task<T> Run<T>(Func<T> job)
        {
            var tcs = new TaskCompletionSource<T>();
            queue.Enqueue(() =>
            {
                try { tcs.SetResult(job()); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        /// <summary>Runs the next queued job. Call once per frame.</summary>
        public static void Pump() { if (queue.Count > 0) queue.Dequeue()(); }
#else
        public const bool Threaded = true;
        public static Task<T> Run<T>(Func<T> job) { return Task.Run(job); }
        public static void Pump() { }
#endif
    }
}
