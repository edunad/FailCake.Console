#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class ConsoleOutput
    {
        #region STATIC

        public const int MAX_LINES = 1024;

        private static readonly ConcurrentQueue<Line> PENDING = new ConcurrentQueue<Line>();
        private static readonly List<Line> LINES = new List<Line>(ConsoleOutput.MAX_LINES);

        [ThreadStatic]
        internal static List<string> CAPTURE;

        public static event Action<IReadOnlyList<Line>> OnLinesAdded;
        public static event Action OnCleared;

        #endregion

        public readonly struct Line
        {
            public readonly string timestamp;
            public readonly string category;
            public readonly string text;
            public readonly Color? color;

            public Line(string text, string category, Color? color) {
                this.timestamp = DateTime.Now.ToString("HH:mm:ss");
                this.category = string.IsNullOrEmpty(category) ? "" : category;
                this.text = text;
                this.color = color;
            }
        }

        public static void Add(string text, string category = "ENGINE", Color? color = null) {
            if (string.IsNullOrEmpty(text)) return;
            ConsoleOutput.PENDING.Enqueue(new Line(text, category, color));
            if (ConsoleOutput.CAPTURE != null) ConsoleOutput.CAPTURE.Add(text);
        }

        public static void Flush() {
            if (ConsoleOutput.PENDING.IsEmpty) return;

            List<Line> batch = new List<Line>();
            while (ConsoleOutput.PENDING.TryDequeue(out Line line))
            {
                ConsoleOutput.LINES.Add(line);
                batch.Add(line);
            }

            while (ConsoleOutput.LINES.Count > ConsoleOutput.MAX_LINES) ConsoleOutput.LINES.RemoveAt(0);
            if (batch.Count > 0 && ConsoleOutput.OnLinesAdded != null) ConsoleOutput.OnLinesAdded(batch);
        }

        public static IReadOnlyList<Line> GetLines() {
            return ConsoleOutput.LINES;
        }

        public static void Clear() {
            ConsoleOutput.LINES.Clear();
            while (ConsoleOutput.PENDING.TryDequeue(out Line _)) { }

            ConsoleOutput.OnCleared?.Invoke();
        }
    }
}