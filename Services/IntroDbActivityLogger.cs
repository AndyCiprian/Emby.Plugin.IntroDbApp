using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntroDbApp.Services
{
    public class IntroDbLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Title { get; set; } = "";
        public string Action { get; set; } = "";
        public string Level { get; set; } = "info"; // "success", "info", "warn", "error"
        public string Details { get; set; } = "";
    }

    public static class IntroDbActivityLogger
    {
        private static readonly ConcurrentQueue<IntroDbLogEntry> _logs = new ConcurrentQueue<IntroDbLogEntry>();
        private const int MaxEntries = 100;

        public static void Log(string title, string action, string level = "info", string details = "")
        {
            var entry = new IntroDbLogEntry
            {
                Timestamp = DateTime.Now,
                Title = title,
                Action = action,
                Level = level,
                Details = details
            };

            _logs.Enqueue(entry);
            while (_logs.Count > MaxEntries && _logs.TryDequeue(out _)) { }
        }

        public static List<IntroDbLogEntry> GetRecentLogs()
        {
            return _logs.Reverse().ToList();
        }

        public static void Clear()
        {
            while (_logs.TryDequeue(out _)) { }
        }
    }
}