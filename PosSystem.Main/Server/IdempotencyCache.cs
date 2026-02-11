using System;
using System.Collections.Concurrent;

namespace PosSystem.Main.Server
{
    internal static class IdempotencyCache
    {
        private sealed class Entry
        {
            public DateTimeOffset CreatedAt { get; init; }
            public bool Completed { get; set; }
            public int StatusCode { get; set; }
            public object? Body { get; set; }
        }

        private static readonly ConcurrentDictionary<string, Entry> Cache = new();

        public static bool TryGetCompleted(string key, out int statusCode, out object? body)
        {
            statusCode = 0;
            body = null;

            if (!Cache.TryGetValue(key, out var entry) || !entry.Completed) return false;

            statusCode = entry.StatusCode;
            body = entry.Body;
            return true;
        }

        public static bool TryBegin(string key)
        {
            var entry = new Entry
            {
                CreatedAt = DateTimeOffset.UtcNow,
                Completed = false,
                StatusCode = 0,
                Body = null
            };

            return Cache.TryAdd(key, entry);
        }

        public static void Complete(string key, int statusCode, object? body)
        {
            Cache.AddOrUpdate(key,
                _ => new Entry
                {
                    CreatedAt = DateTimeOffset.UtcNow,
                    Completed = true,
                    StatusCode = statusCode,
                    Body = body
                },
                (_, existing) =>
                {
                    existing.Completed = true;
                    existing.StatusCode = statusCode;
                    existing.Body = body;
                    return existing;
                });
        }

        public static void Abandon(string key)
        {
            Cache.TryRemove(key, out _);
        }

        public static void Cleanup(TimeSpan ttl)
        {
            var threshold = DateTimeOffset.UtcNow - ttl;
            foreach (var pair in Cache)
            {
                if (pair.Value.CreatedAt < threshold)
                {
                    Cache.TryRemove(pair.Key, out _);
                }
            }
        }
    }
}
