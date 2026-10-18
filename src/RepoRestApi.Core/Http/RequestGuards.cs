using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RepoRestApi.Core.Models;
namespace RepoRestApi.Core.Http;
public sealed class RateLimiter
{
    private readonly object gate = new object();
    private readonly Queue<long> times = new Queue<long>();
    private readonly int limit;
    public RateLimiter(int limit) { if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit)); this.limit = limit; }
    public bool Allow()
    {
        lock (gate) { long now = Stopwatch.GetTimestamp(); while (times.Count > 0 && now - times.Peek() >= Stopwatch.Frequency) times.Dequeue(); if (times.Count >= limit) return false; times.Enqueue(now); return true; }
    }
}
public sealed class IdempotencyStore
{
    private sealed class Entry { public string Fingerprint = ""; public long Completed; public TaskCompletionSource<ApiResponse> Result = new TaskCompletionSource<ApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously); }
    private readonly object gate = new object();
    private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private long nextCleanup;
    public async Task<ApiResponse> Run(string? key, string payload, Func<Task<ApiResponse>> action)
    {
        if (string.IsNullOrWhiteSpace(key) || key!.Length > 128) throw new ApiException(400, "IDEMPOTENCY_KEY_REQUIRED", "POST requires Idempotency-Key (1-128 characters). Retain it when retrying.");
        string hash; using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        Entry entry; bool owner = false;
        lock (gate)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= nextCleanup)
            {
                foreach (var expired in new List<string>(entries.Keys))
                    if (entries[expired].Result.Task.IsCompleted && now - entries[expired].Completed >= Stopwatch.Frequency * 600L) entries.Remove(expired);
                nextCleanup = now + Stopwatch.Frequency;
            }
            if (entries.TryGetValue(key!, out entry!)) { if (entry.Fingerprint != hash) throw new ApiException(409, "IDEMPOTENCY_CONFLICT", "This key was already used with a different request."); }
            else { if (entries.Count >= 2048) throw new ApiException(503, "IDEMPOTENCY_CAPACITY", "Idempotency storage is full; retry later."); entry = new Entry { Fingerprint = hash }; entries.Add(key!, entry); owner = true; }
        }
        if (owner)
        {
            ApiResponse response;
            try { response = await action().ConfigureAwait(false); }
            catch (ApiException e) { response = ApiResponse.Error(e); }
            catch { response = ApiResponse.Error(new ApiException(500, "ACTION_FAILED", "Execution failed; inspect the game log before retrying with a new key.")); }
            lock (gate)
            {
                entry.Completed = Stopwatch.GetTimestamp();
                entry.Result.TrySetResult(response);
            }
        }
        return await entry.Result.Task.ConfigureAwait(false);
    }
}
