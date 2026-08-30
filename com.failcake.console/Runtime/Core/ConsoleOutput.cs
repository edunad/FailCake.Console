#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

#endregion

namespace FailCake.Console {
	public static class ConsoleOutput {
		#region STATIC

		public const int MAX_LINES = 1024;
		private const int MAX_CAPTURE_CHARACTERS = 16384;
		private const int MAX_PENDING_LINES = 4096;
		private const int MAX_FLUSH_LINES = 1024;
		private const string CAPTURE_TRUNCATED = "[console output truncated]";

		private static readonly ConcurrentQueue<Line> PENDING = new ConcurrentQueue<Line>();
		private static readonly List<Line> LINES = new List<Line>(ConsoleOutput.MAX_LINES);
		private static int PENDING_COUNT;
		private static int OMITTED_LINES;

		[ThreadStatic]
		internal static CaptureBuffer CAPTURE;

		public static event Action<IReadOnlyList<Line>> OnLinesAdded;
		public static event Action OnCleared;

		#endregion

		public readonly struct Line {
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

			public Line(string timestamp, string text, string category, Color? color) {
				this.timestamp = timestamp;
				this.category = string.IsNullOrEmpty(category) ? "" : category;
				this.text = text;
				this.color = color;
			}
		}

		internal sealed class CaptureBuffer {
			#region PRIVATE FIELDS

			private readonly System.Text.StringBuilder _value = new System.Text.StringBuilder(ConsoleOutput.MAX_CAPTURE_CHARACTERS);
			private bool _truncated;

			#endregion

			public void Add(string text) {
				if (this._truncated) return;

				int remaining = ConsoleOutput.MAX_CAPTURE_CHARACTERS - this._value.Length;
				if (this._value.Length > 0) {
					if (remaining == 0) {
						this._truncated = true;
						return;
					}

					this._value.Append('\n');
					remaining--;
				}

				if (text.Length <= remaining) {
					this._value.Append(text);
					return;
				}

				int textLength = Math.Max(0, remaining - ConsoleOutput.CAPTURE_TRUNCATED.Length);
				if (textLength > 0) this._value.Append(text, 0, textLength);
				remaining -= textLength;
				if (remaining > 0) this._value.Append(ConsoleOutput.CAPTURE_TRUNCATED, 0, Math.Min(remaining, ConsoleOutput.CAPTURE_TRUNCATED.Length));
				this._truncated = true;
			}

			public string GetText() {
				return this._value.ToString();
			}
		}

		public static void Add(string text, string category = "CONSOLE", Color? color = null) {
			if (string.IsNullOrEmpty(text)) return;
			ConsoleOutput.PENDING.Enqueue(new Line(text, category, color));
			if (Interlocked.Increment(ref ConsoleOutput.PENDING_COUNT) > ConsoleOutput.MAX_PENDING_LINES &&
				ConsoleOutput.PENDING.TryDequeue(out Line _)) {
				Interlocked.Decrement(ref ConsoleOutput.PENDING_COUNT);
				Interlocked.Increment(ref ConsoleOutput.OMITTED_LINES);
			}
			ConsoleOutput.CAPTURE?.Add(text);
		}

		public static void Flush() {
			if (ConsoleOutput.PENDING.IsEmpty && Volatile.Read(ref ConsoleOutput.OMITTED_LINES) == 0) return;

			List<Line> batch = new List<Line>(ConsoleOutput.MAX_FLUSH_LINES + 1);
			int omitted = Interlocked.Exchange(ref ConsoleOutput.OMITTED_LINES, 0);
			if (omitted > 0) {
				Line omittedLine = new Line($"{omitted} console lines omitted because the display backlog was full.", "CONSOLE", Color.yellow);
				ConsoleOutput.LINES.Add(omittedLine);
				batch.Add(omittedLine);
			}

			for (int i = 0; i < ConsoleOutput.MAX_FLUSH_LINES && ConsoleOutput.PENDING.TryDequeue(out Line line); i++) {
				Interlocked.Decrement(ref ConsoleOutput.PENDING_COUNT);
				ConsoleOutput.LINES.Add(line);
				batch.Add(line);
			}

			int excess = ConsoleOutput.LINES.Count - ConsoleOutput.MAX_LINES;
			if (excess > 0) ConsoleOutput.LINES.RemoveRange(0, excess);
			if (batch.Count > 0 && ConsoleOutput.OnLinesAdded != null) ConsoleOutput.OnLinesAdded(batch);
		}

		public static IReadOnlyList<Line> GetLines() {
			return ConsoleOutput.LINES;
		}

		public static void Clear() {
			ConsoleOutput.LINES.Clear();
			while (ConsoleOutput.PENDING.TryDequeue(out Line _)) Interlocked.Decrement(ref ConsoleOutput.PENDING_COUNT);
			Interlocked.Exchange(ref ConsoleOutput.OMITTED_LINES, 0);

			ConsoleOutput.OnCleared?.Invoke();
		}

		internal static void Reset() {
			ConsoleOutput.LINES.Clear();
			while (ConsoleOutput.PENDING.TryDequeue(out Line _)) Interlocked.Decrement(ref ConsoleOutput.PENDING_COUNT);
			Interlocked.Exchange(ref ConsoleOutput.OMITTED_LINES, 0);
			ConsoleOutput.CAPTURE = null;
			ConsoleOutput.OnLinesAdded = null;
			ConsoleOutput.OnCleared = null;
		}
	}
}
