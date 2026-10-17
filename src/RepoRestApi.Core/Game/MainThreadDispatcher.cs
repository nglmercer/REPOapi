using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using RepoRestApi.Core.Http;
using RepoRestApi.Core.Models;
namespace RepoRestApi.Core.Game;
// No Unity references: Update is the sole consumer. A timed-out queued action can never execute.
public sealed class MainThreadDispatcher : IDisposable
{
    private sealed class Work
    {
        public Func<ApiResponse> Action = null!;
        public readonly TaskCompletionSource<ApiResponse> Completion = new TaskCompletionSource<ApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Deadline;
        public int State; // 0 queued, 1 executing, 2 completed/cancelled; protected by gate
    }
    private readonly object gate = new object();
    private readonly Queue<Work> queue = new Queue<Work>();
    private readonly int capacity;
    private bool stopped;
    public MainThreadDispatcher(int capacity) { this.capacity = capacity; }
    public async Task<ApiResponse> Execute(Func<ApiResponse> action, int timeoutMs)
    {
        var work = new Work { Action = action, Deadline = Stopwatch.GetTimestamp() + timeoutMs * Stopwatch.Frequency / 1000 };
        lock (gate)
        {
            if (stopped) throw new ApiException(503, "SHUTTING_DOWN", "The dispatcher is stopping.");
            // Remove expired work so an unpumped queue cannot retain dead requests indefinitely.
            var count = queue.Count;
            while (count-- > 0) { var old = queue.Dequeue(); if (old.State == 0) queue.Enqueue(old); }
            if (queue.Count >= capacity) throw new ApiException(503, "QUEUE_FULL", "The game command queue is full.");
            queue.Enqueue(work);
        }
        using (var timer = new System.Threading.Timer(_ =>
        {
            lock (gate) { if (work.State != 0) return; work.State = 2; work.Completion.TrySetException(new ApiException(504, "COMMAND_TIMEOUT", "Command expired before execution; no action was performed.")); }
        }, null, timeoutMs, System.Threading.Timeout.Infinite))
            return await work.Completion.Task.ConfigureAwait(false);
    }
    public void Pump(int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            Work work;
            lock (gate)
            {
                if (stopped || queue.Count == 0) return;
                work = queue.Dequeue();
                if (work.State != 0) continue;
                if (Stopwatch.GetTimestamp() >= work.Deadline) { work.State = 2; work.Completion.TrySetException(new ApiException(504, "COMMAND_TIMEOUT", "Command expired before execution; no action was performed.")); continue; }
                work.State = 1;
            }
            try { work.Completion.TrySetResult(work.Action()); }
            catch (Exception e) { work.Completion.TrySetException(e); }
            finally { lock (gate) work.State = 2; }
        }
    }
    public void Dispose()
    {
        lock (gate) { stopped = true; while (queue.Count > 0) { var w = queue.Dequeue(); w.State = 2; w.Completion.TrySetException(new ApiException(503, "SHUTTING_DOWN", "The game is shutting down.")); } }
    }
}
