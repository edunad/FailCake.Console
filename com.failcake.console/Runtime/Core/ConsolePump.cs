#region

using System;
#if UNITY_SERVER
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif
using System.Text;
using System.Threading;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.Console {
	public class ConsolePump : MonoBehaviour {
		#region STATIC

		private static bool CONFIG_WRITTEN;

		#if UNITY_SERVER
		private const string PROMPT = "> ";
		private const string PROMPT_HINT = "type a command";
		private const string ANSI_ENTER = "\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H";
		private const string ANSI_EXIT = "\x1b[?2026l\x1b[0m\x1b[?25h\x1b[?1049l";
		private const string ANSI_HIDE_CURSOR = "\x1b[?25l";
		private const string ANSI_SHOW_CURSOR = "\x1b[?25h";
		private const string ANSI_BEGIN_FRAME = "\x1b[?2026h";
		private const string ANSI_END_FRAME = "\x1b[?2026l";
		private const string ANSI_CLEAR_TAIL = "\x1b[0K";
		private const string ANSI_RESET = "\x1b[0m";
		private const string ANSI_BORDER = "\x1b[38;2;82;82;82m";
		private const string ANSI_HEADER = "\x1b[38;2;225;225;225m";
		private const string ANSI_MUTED = "\x1b[38;2;120;120;120m";
		private const int MAX_HISTORY = 64;
		private const int MAX_INPUT_CHARACTERS = 4096;
		private const int MAX_PENDING_KEY_EVENTS = 1024;
		private const int MAX_KEY_EVENTS_PER_FRAME = 256;
		private const int MAX_PENDING_COMMANDS = 256;
		private const int MAX_COMMANDS_PER_FRAME = 32;
		private const int MAX_LOG_CHARACTERS = 16384;
		private const int MAX_RENDERED_LOG_LINES = 8192;
		private const int MIN_TERMINAL_WIDTH = 40;
		private const int MIN_TERMINAL_HEIGHT = 10;
		private const int TERMINAL_POLL_MS = 25;
		private const int TERMINAL_STATUS_POLL_MS = 250;
		private const int TERMINAL_FAILURE_GRACE_MS = 1500;
		private const int TERMINAL_FAILURE_RETRY_MS = 100;

		#if UNITY_STANDALONE_WIN
		private const int STD_INPUT_HANDLE = -10;
		private const int STD_OUTPUT_HANDLE = -11;
		private const short KEY_EVENT = 0x0001;
		private const short MOUSE_EVENT = 0x0002;
		private const uint ENABLE_MOUSE_INPUT = 0x0010;
		private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
		private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
		private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
		private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
		private const uint MOUSE_WHEELED = 0x0004;
		private const uint RIGHT_ALT_PRESSED = 0x0001;
		private const uint LEFT_ALT_PRESSED = 0x0002;
		private const uint RIGHT_CTRL_PRESSED = 0x0004;
		private const uint LEFT_CTRL_PRESSED = 0x0008;
		private const uint SHIFT_PRESSED = 0x0010;
		private const int MOUSE_SCROLL_ROWS = 3;
		private const int WHEEL_DELTA = 120;
		#endif

		private static readonly Color COMMAND_COLOR = new Color(0.45F, 0.72F, 0.78F);
		private static ConsolePump ACTIVE_PUMP;

		private readonly struct RenderedLogLine {
			public readonly string text;
			public readonly Color? color;

			public RenderedLogLine(string text, Color? color) {
				this.text = text;
				this.color = color;
			}
		}

		#if UNITY_STANDALONE_WIN
		private struct NativeConsoleMode {
			public IntPtr handle;
			public uint original;
			public bool restore;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct Coordinate {
			public short x;
			public short y;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct KeyEventRecord {
			public int keyDown;
			public ushort repeatCount;
			public ushort virtualKeyCode;
			public ushort virtualScanCode;
			public char unicodeChar;
			public uint controlKeyState;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MouseEventRecord {
			public Coordinate mousePosition;
			public uint buttonState;
			public uint controlKeyState;
			public uint eventFlags;
		}

		[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 20)]
		private struct InputRecord {
			[FieldOffset(0)]
			public short eventType;
			[FieldOffset(4)]
			public KeyEventRecord keyEvent;
			[FieldOffset(4)]
			public MouseEventRecord mouseEvent;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr GetStdHandle(int handle);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetConsoleMode(IntPtr handle, uint mode);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetNumberOfConsoleInputEvents(IntPtr handle, out uint events);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "ReadConsoleInputW", SetLastError = true)]
		private static extern bool ReadConsoleInput(IntPtr handle, [Out] InputRecord[] records, uint length, out uint read);
		#endif
		#endif

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetRuntime() {
			#if UNITY_SERVER
			System.Console.CancelKeyPress -= ConsolePump.OnCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit -= ConsolePump.OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException -= ConsolePump.OnUnhandledException;
			if (ConsolePump.ACTIVE_PUMP) ConsolePump.ACTIVE_PUMP.StopPump();
			ConsolePump.ACTIVE_PUMP = null;
			#endif

			Application.quitting -= ConsolePump.WriteConfig;
			Application.quitting -= ConsolePump.Shutdown;
			ConsolePump.CONFIG_WRITTEN = false;

			Console.SetLogIntercept(false);
			ConsolePlugins.UnloadAll();
			ConsoleOutput.Reset();
			ConsoleRegistry.Reset();
			Console.ResetHandlers();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init() {
			Console.Scan();
			Console.SetLogIntercept(!Application.isBatchMode);

			Application.quitting += ConsolePump.WriteConfig;
			Application.quitting += ConsolePump.Shutdown;

			GameObject go = new GameObject("Console.Pump");
			Object.DontDestroyOnLoad(go);
			go.AddComponent<ConsolePump>();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void BootCfg() {
			ConsoleCfg.BootExec();
		}

		private static void Shutdown() {
			Application.quitting -= ConsolePump.WriteConfig;
			Application.quitting -= ConsolePump.Shutdown;
			#if UNITY_SERVER
			System.Console.CancelKeyPress -= ConsolePump.OnCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit -= ConsolePump.OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException -= ConsolePump.OnUnhandledException;

			if (ConsolePump.ACTIVE_PUMP) ConsolePump.ACTIVE_PUMP.StopPump();
			#endif

			Console.SetLogIntercept(false);
			ConsolePlugins.UnloadAll();
			ConsoleRegistry.ClearRemoteStubs();
			Console.ResetHandlers();
		}

		private static void WriteConfig() {
			if (ConsolePump.CONFIG_WRITTEN) return;
			try {
				ConsoleCfg.WriteConfig();
				ConsolePump.CONFIG_WRITTEN = true;
			} catch (Exception exception) {
				try {
					System.Console.Error.WriteLine($"Failed to write archived console variables: {exception.Message}");
				} catch {
				}
			}
		}

		#if UNITY_SERVER
		private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args) {
			ConsolePump pump = ConsolePump.ACTIVE_PUMP;
			if (ReferenceEquals(pump, null)) return;
			if (pump._quitRequested) {
				pump.StopPump();
				return;
			}

			args.Cancel = true;
			pump._quitRequested = true;
		}

		private static void OnProcessExit(object sender, EventArgs args) {
			ConsolePump pump = ConsolePump.ACTIVE_PUMP;
			if (ReferenceEquals(pump, null)) return;
			pump.StopPump();
		}

		private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) {
			ConsolePump pump = ConsolePump.ACTIVE_PUMP;
			if (ReferenceEquals(pump, null)) return;
			pump.StopPump();
		}

		private static long GetTimestampMilliseconds() {
			return System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency;
		}

		private static bool WritesUnityLogToStdout() {
			string[] arguments = Environment.GetCommandLineArgs();
			for (int i = 0; i < arguments.Length; i++) {
				if (arguments[i].Equals("-logFile", StringComparison.OrdinalIgnoreCase))
					return i + 1 < arguments.Length && arguments[i + 1] == "-";
				if (arguments[i].StartsWith("-logFile=", StringComparison.OrdinalIgnoreCase))
					return arguments[i].Substring("-logFile=".Length) == "-";
			}

			return false;
		}

		private static string Sanitize(string text, bool preserveNewLines) {
			if (string.IsNullOrEmpty(text)) return string.Empty;

			StringBuilder sanitized = new StringBuilder(text.Length);
			for (int i = 0; i < text.Length; i++) {
				char character = text[i];
				if (character == '<' && ConsolePump.TrySkipRichTextTag(text, i, out int tagEnd)) {
					i = tagEnd;
					continue;
				}

				if (character == '\r') continue;
				if (character == '\n') {
					sanitized.Append(preserveNewLines ? '\n' : ' ');
					continue;
				}

				if (character == '\t') {
					sanitized.Append("    ");
					continue;
				}

				if (character == '\x1b' || character == '\u009b' || char.IsControl(character)) continue;
				sanitized.Append(character);
			}

			return sanitized.ToString();
		}

		private static string SanitizeTerminal(string text, bool preserveNewLines) {
			return ConsolePump.MakeSingleCell(ConsolePump.Sanitize(text, preserveNewLines), preserveNewLines);
		}

		private static string MakeSingleCell(string text, bool preserveNewLines = false) {
			if (string.IsNullOrEmpty(text)) return string.Empty;

			StringBuilder normalized = null;
			for (int i = 0; i < text.Length; i++) {
				char character = text[i];
				if ((character >= ' ' && character <= '~') || (preserveNewLines && character == '\n')) continue;
				if (normalized == null) normalized = new StringBuilder(text);
				normalized[i] = '?';
			}

			return normalized == null ? text : normalized.ToString();
		}

		private static string LimitLog(string text, bool newest) {
			if (string.IsNullOrEmpty(text) || text.Length <= ConsolePump.MAX_LOG_CHARACTERS) return text;
			return newest
				? text.Substring(text.Length - ConsolePump.MAX_LOG_CHARACTERS)
				: text.Substring(0, ConsolePump.MAX_LOG_CHARACTERS - 3) + "...";
		}

		private static bool TrySkipRichTextTag(string text, int start, out int end) {
			end = text.IndexOf('>', start + 1);
			if (end < 0 || end - start > 64) return false;

			int nameStart = start + 1;
			if (nameStart < end && text[nameStart] == '/') nameStart++;
			if (nameStart >= end) return false;
			if (text[nameStart] == '#') return true;

			int nameEnd = nameStart;
			while (nameEnd < end && text[nameEnd] != '=' && !char.IsWhiteSpace(text[nameEnd])) nameEnd++;
			string name = text.Substring(nameStart, nameEnd - nameStart).ToLowerInvariant();

			switch (name) {
			case "align":
			case "alpha":
			case "b":
			case "br":
			case "color":
			case "font":
			case "i":
			case "link":
			case "mark":
			case "material":
			case "nobr":
			case "noparse":
			case "s":
			case "size":
			case "sprite":
			case "style":
			case "sub":
			case "sup":
			case "u":
				return true;
			default:
				return false;
			}
		}

		private static string Truncate(string text, int width) {
			if (width <= 0) return string.Empty;
			if (text.Length <= width) return text;
			if (width == 1) return ".";
			return text.Substring(0, width - 1) + ".";
		}

		private static string Pad(string text, int width) {
			text = ConsolePump.Truncate(text, width);
			return text.Length < width ? text + new string(' ', width - text.Length) : text;
		}

		private static void AppendTerminalPosition(StringBuilder frame, int row, int column) {
			frame.Append("\x1b[").Append(row + 1).Append(';').Append(column + 1).Append('H');
		}

		private static void AppendTerminalRow(StringBuilder frame, int row, string text) {
			ConsolePump.AppendTerminalPosition(frame, row, 0);
			frame.Append(text).Append(ConsolePump.ANSI_CLEAR_TAIL);
		}

		private static void AppendLineColor(StringBuilder frame, Color? color) {
			if (!color.HasValue) {
				frame.Append(ConsolePump.ANSI_HEADER);
				return;
			}

			Color value = color.Value;
			int red = (int)Math.Round(Math.Clamp(value.r, 0F, 1F) * 255F);
			int green = (int)Math.Round(Math.Clamp(value.g, 0F, 1F) * 255F);
			int blue = (int)Math.Round(Math.Clamp(value.b, 0F, 1F) * 255F);
			frame.Append("\x1b[38;2;").Append(red).Append(';').Append(green).Append(';').Append(blue).Append('m');
		}

		private static string BuildBorder(int width, char left, char fill, char right, string label = null, string rightLabel = null) {
			if (width <= 0) return string.Empty;
			if (width == 1) return left.ToString();
			if (width == 2) return new string(new[] { left, right });

			char[] border = new string(fill, width).ToCharArray();
			border[0] = left;
			border[width - 1] = right;

			if (!string.IsNullOrEmpty(label)) {
				label = ConsolePump.Truncate(label, Math.Max(0, width - 4));
				for (int i = 0; i < label.Length; i++) border[i + 2] = label[i];
			}

			if (!string.IsNullOrEmpty(rightLabel)) {
				rightLabel = ConsolePump.Truncate(rightLabel, Math.Max(0, width - 4));
				int start = Math.Max(2, width - rightLabel.Length - 2);
				for (int i = 0; i < rightLabel.Length && start + i < width - 1; i++) border[start + i] = rightLabel[i];
			}

			return new string(border);
		}
		#endif

		#endregion

		#region PRIVATE FIELDS

		#if UNITY_SERVER
		private readonly ConcurrentQueue<ConsoleKeyInfo> _keyQueue = new ConcurrentQueue<ConsoleKeyInfo>();
		private readonly ConcurrentQueue<string> _stdinQueue = new ConcurrentQueue<string>();
		private readonly object _terminalWriteLock = new object();
		private readonly List<string> _history = new List<string>();
		private readonly List<ConsoleOutput.Line> _terminalLines = new List<ConsoleOutput.Line>(ConsoleOutput.MAX_LINES);
		private readonly List<RenderedLogLine> _renderedLogLines = new List<RenderedLogLine>();
		private readonly List<RenderedLogLine> _messageLines = new List<RenderedLogLine>();
		private readonly StringBuilder _frame = new StringBuilder();
		private Thread _stdinThread;
		private volatile bool _running;
		private volatile bool _stdinFailed;
		private volatile bool _quitRequested;
		private bool _unityLogsToStdout;
		private bool _plainOutputAvailable = true;
		private bool _terminalDirty;
		private bool _logRowsDirty = true;
		private bool _logRowsCompact;
		private bool _followLogs = true;
		private string _inputBuffer = string.Empty;
		private string _serverName = "Starting server...";
		private int _inputCursor;
		private int _inputViewStart;
		private int _historyCursor;
		private int _stdinGeneration;
		private int _stopping;
		private int _playerCount;
		private int _publicCapacity;
		private Vector2Int _terminalSize;
		private int _logRowsWidth;
		private int _logTop;
		private int _maxLogTop;
		private long _nextStatusRefresh;
		private long _terminalFailureStarted;
		private long _nextTerminalRetry;
		private TextWriter _terminalWriter;
		private Encoding _originalOutputEncoding;
		#if UNITY_STANDALONE_WIN
		private NativeConsoleMode _inputConsoleMode;
		private NativeConsoleMode _outputConsoleMode;
		#endif
		private bool? _originalTreatControlCAsInput;
		#endif

		#endregion

		#if UNITY_SERVER
		private void Awake() {
			if (ConsolePump.ACTIVE_PUMP) {
				Object.Destroy(this);
				return;
			}

			ConsolePump.ACTIVE_PUMP = this;
			this._unityLogsToStdout = ConsolePump.WritesUnityLogToStdout();
			this.TryStartTerminal();
			Console.SetLogIntercept(!this._unityLogsToStdout);

			ConsoleOutput.OnLinesAdded += this.OnLinesAdded;
			ConsoleOutput.OnCleared += this.OnCleared;

			IReadOnlyList<ConsoleOutput.Line> lines = ConsoleOutput.GetLines();
			for (int i = 0; i < lines.Count; i++) this._terminalLines.Add(lines[i]);

			this._running = true;
			Interlocked.Increment(ref this._stdinGeneration);
			this._stdinThread = new Thread(this.StdinLoop) { IsBackground = true, Name = "FailCake.Console" };
			this._stdinThread.Start();

			System.Console.CancelKeyPress += ConsolePump.OnCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit += ConsolePump.OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException += ConsolePump.OnUnhandledException;

			if (this._terminalWriter != null) {
				this._terminalDirty = true;
				this.RefreshTerminalStatus(true);
				this.RenderTerminal();
			}
		}
		#endif

		private void Update() {
			ConsoleOutput.Flush();
			#if UNITY_SERVER
			if (this._quitRequested) {
				try {
					this.StopPump();
					ConsolePump.WriteConfig();
				} finally {
					Application.Quit();
				}
				return;
			}

			for (int i = 0; i < ConsolePump.MAX_KEY_EVENTS_PER_FRAME && this._keyQueue.TryDequeue(out ConsoleKeyInfo key); i++)
				this.HandleKey(key);
			for (int i = 0; i < ConsolePump.MAX_COMMANDS_PER_FRAME && this._stdinQueue.TryDequeue(out string line); i++)
				Console.Execute(line, ConsoleContext.ServerConsole());
			ConsoleOutput.Flush();

			if (this._terminalWriter == null) return;

			if (this._stdinFailed) {
				this.HandleTerminalFailure(true);
				if (this._terminalWriter == null) return;
			}

			this.RefreshTerminalStatus(false);
			if (this._terminalDirty) this.RenderTerminal();
			#endif
		}

		private void OnDestroy() {
			#if UNITY_SERVER
			if (ConsolePump.ACTIVE_PUMP != this) return;
			#endif

			ConsolePump.WriteConfig();
			ConsolePump.Shutdown();
		}

		#region PRIVATE METHODS

		#if UNITY_SERVER
		private void StdinLoop() {
			int generation = Volatile.Read(ref this._stdinGeneration);
			if (this._terminalWriter != null) {
				#if UNITY_STANDALONE_WIN
				if (this._inputConsoleMode.restore) {
					if (this.TryWindowsStdinLoop(generation)) return;
					lock (this._terminalWriteLock) {
						if (Volatile.Read(ref this._stopping) != 0) return;
						this.RestoreConsoleMode(ref this._inputConsoleMode);
					}
				}
				#endif

				while (this._running && generation == Volatile.Read(ref this._stdinGeneration)) {
					if (this._keyQueue.Count >= ConsolePump.MAX_PENDING_KEY_EVENTS) {
						Thread.Sleep(ConsolePump.TERMINAL_POLL_MS);
						continue;
					}

					bool keyAvailable;
					try {
						keyAvailable = System.Console.KeyAvailable;
					} catch {
						this._stdinFailed = true;
						break;
					}

					if (!keyAvailable) {
						Thread.Sleep(ConsolePump.TERMINAL_POLL_MS);
						continue;
					}

					try {
						this._keyQueue.Enqueue(System.Console.ReadKey(true));
					} catch {
						this._stdinFailed = true;
						break;
					}
				}
				return;
			}

			while (this._running && generation == Volatile.Read(ref this._stdinGeneration)) {
				if (this._stdinQueue.Count >= ConsolePump.MAX_PENDING_COMMANDS) {
					Thread.Sleep(ConsolePump.TERMINAL_POLL_MS);
					continue;
				}

				string line;
				try {
					line = System.Console.ReadLine();
				} catch {
					break;
				}

				if (line == null || !this._running || generation != Volatile.Read(ref this._stdinGeneration)) break;
				if (line.Length > ConsolePump.MAX_INPUT_CHARACTERS) {
					ConsoleOutput.Add($"Console input exceeded {ConsolePump.MAX_INPUT_CHARACTERS} characters and was ignored.");
					continue;
				}
				this._stdinQueue.Enqueue(line);
			}
		}

		#if UNITY_STANDALONE_WIN
		private bool TryWindowsStdinLoop(int generation) {
			try {
				InputRecord[] records = new InputRecord[16];

				while (this._running && generation == Volatile.Read(ref this._stdinGeneration)) {
					if (this._keyQueue.Count >= ConsolePump.MAX_PENDING_KEY_EVENTS) {
						Thread.Sleep(ConsolePump.TERMINAL_POLL_MS);
						continue;
					}

					if (!ConsolePump.GetNumberOfConsoleInputEvents(this._inputConsoleMode.handle, out uint available)) return false;

					if (available == 0) {
						Thread.Sleep(ConsolePump.TERMINAL_POLL_MS);
						continue;
					}

					uint requested = Math.Min(available, (uint)records.Length);
					if (!ConsolePump.ReadConsoleInput(this._inputConsoleMode.handle, records, requested, out uint read)) return false;

					for (int i = 0; i < read; i++) this.HandleWindowsInput(records[i]);
				}

				return true;
			} catch {
				return false;
			}
		}

		private void HandleWindowsInput(in InputRecord record) {
			if (record.eventType == ConsolePump.MOUSE_EVENT) {
				if (record.mouseEvent.eventFlags != ConsolePump.MOUSE_WHEELED) return;

				short delta = unchecked((short)(record.mouseEvent.buttonState >> 16));
				if (delta == 0) return;

				ConsoleKeyInfo wheelKey = new ConsoleKeyInfo('\0', delta > 0 ? ConsoleKey.UpArrow : ConsoleKey.DownArrow, true, false, false);
				int rows = Math.Min(Math.Max(1, Math.Abs(delta) / ConsolePump.WHEEL_DELTA) * ConsolePump.MOUSE_SCROLL_ROWS,
					Math.Max(0, ConsolePump.MAX_PENDING_KEY_EVENTS - this._keyQueue.Count));
				for (int i = 0; i < rows; i++) this._keyQueue.Enqueue(wheelKey);
				return;
			}

			if (record.eventType != ConsolePump.KEY_EVENT || record.keyEvent.keyDown == 0 || record.keyEvent.virtualKeyCode > byte.MaxValue) return;

			uint controlState = record.keyEvent.controlKeyState;
			bool shift = (controlState & ConsolePump.SHIFT_PRESSED) != 0;
			bool alt = (controlState & (ConsolePump.LEFT_ALT_PRESSED | ConsolePump.RIGHT_ALT_PRESSED)) != 0;
			bool control = (controlState & (ConsolePump.LEFT_CTRL_PRESSED | ConsolePump.RIGHT_CTRL_PRESSED)) != 0;
			ConsoleKeyInfo key = new ConsoleKeyInfo(record.keyEvent.unicodeChar, (ConsoleKey)record.keyEvent.virtualKeyCode, shift, alt, control);
			int repeatCount = Math.Min(Math.Max(1, (int)record.keyEvent.repeatCount),
				Math.Max(0, ConsolePump.MAX_PENDING_KEY_EVENTS - this._keyQueue.Count));
			for (int i = 0; i < repeatCount; i++) this._keyQueue.Enqueue(key);
		}
		#endif

		private void HandleKey(ConsoleKeyInfo key) {
			bool controlShortcut = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == ConsoleModifiers.Control;

			switch (key.Key) {
			case ConsoleKey.Enter:
				if (this._inputBuffer.Length > 0) {
					ConsoleOutput.Add(ConsolePump.PROMPT + this._inputBuffer, "COMMAND", ConsolePump.COMMAND_COLOR);
					this._stdinQueue.Enqueue(this._inputBuffer);
					this._history.Add(this._inputBuffer);
					if (this._history.Count > ConsolePump.MAX_HISTORY) this._history.RemoveAt(0);
				}
				this._inputBuffer = string.Empty;
				this._inputCursor = 0;
				this._inputViewStart = 0;
				this._historyCursor = this._history.Count;
				this._followLogs = true;
				this._terminalDirty = true;
				break;

			case ConsoleKey.Tab:
				this.AcceptCompletion();
				break;

			case ConsoleKey.Backspace:
				if (this._inputCursor > 0) {
					this._inputBuffer = this._inputBuffer.Remove(this._inputCursor - 1, 1);
					this._inputCursor--;
					this._terminalDirty = true;
				}
				break;

			case ConsoleKey.Delete:
				if (this._inputCursor < this._inputBuffer.Length) {
					this._inputBuffer = this._inputBuffer.Remove(this._inputCursor, 1);
					this._terminalDirty = true;
				}
				break;

			case ConsoleKey.LeftArrow:
				this.MoveCursor(-1, controlShortcut);
				break;

			case ConsoleKey.RightArrow:
				this.MoveCursor(1, controlShortcut);
				break;

			case ConsoleKey.Home when controlShortcut:
				this._followLogs = false;
				this._logTop = 0;
				this._terminalDirty = true;
				break;

			case ConsoleKey.End when controlShortcut:
				this._followLogs = true;
				this._terminalDirty = true;
				break;

			case ConsoleKey.A when controlShortcut:
			case ConsoleKey.Home:
				this._inputCursor = 0;
				this._terminalDirty = true;
				break;

			case ConsoleKey.E when controlShortcut:
			case ConsoleKey.End:
				this._inputCursor = this._inputBuffer.Length;
				this._terminalDirty = true;
				break;

			case ConsoleKey.UpArrow when (key.Modifiers & ConsoleModifiers.Shift) != 0:
				this.ScrollLogs(-1);
				break;

			case ConsoleKey.DownArrow when (key.Modifiers & ConsoleModifiers.Shift) != 0:
				this.ScrollLogs(1);
				break;

			case ConsoleKey.UpArrow:
				if (this._historyCursor > 0) {
					this._historyCursor--;
					this._inputBuffer = this._history[this._historyCursor];
					this._inputCursor = this._inputBuffer.Length;
					this._inputViewStart = 0;
					this._terminalDirty = true;
				}
				break;

			case ConsoleKey.DownArrow:
				if (this._historyCursor < this._history.Count) {
					this._historyCursor++;
					this._inputBuffer = this._historyCursor < this._history.Count ? this._history[this._historyCursor] : string.Empty;
					this._inputCursor = this._inputBuffer.Length;
					this._inputViewStart = 0;
					this._terminalDirty = true;
				}
				break;

			case ConsoleKey.PageUp:
				this.ScrollLogs(-Math.Max(1, this._terminalSize.y - 7));
				break;

			case ConsoleKey.PageDown:
				this.ScrollLogs(Math.Max(1, this._terminalSize.y - 7));
				break;

			case ConsoleKey.L when controlShortcut:
				ConsoleOutput.Clear();
				break;

			case ConsoleKey.U when controlShortcut:
			case ConsoleKey.Escape:
				this._inputBuffer = string.Empty;
				this._inputCursor = 0;
				this._inputViewStart = 0;
				this._terminalDirty = true;
				break;

			case ConsoleKey.W when controlShortcut:
				this.DeletePreviousWord();
				break;

			default:
				if (this._inputBuffer.Length < ConsolePump.MAX_INPUT_CHARACTERS && !char.IsControl(key.KeyChar) && key.KeyChar != '\0') {
					this._inputBuffer = this._inputBuffer.Insert(this._inputCursor, key.KeyChar.ToString());
					this._inputCursor++;
					this._terminalDirty = true;
				}
				break;
			}
		}

		private void ScrollLogs(int rows) {
			bool compact = this._terminalSize.x < ConsolePump.MIN_TERMINAL_WIDTH || this._terminalSize.y < ConsolePump.MIN_TERMINAL_HEIGHT;
			int logHeight = Math.Max(0, this._terminalSize.y - (compact ? 2 : 6));
			if (compact)
				this.BuildCompactLogLines(this._terminalSize.x);
			else
				this.BuildRenderedLogLines(this._terminalSize.x - 3);
			this._maxLogTop = Math.Max(0, this._renderedLogLines.Count - logHeight);
			this._logTop = this._followLogs ? this._maxLogTop : Math.Clamp(this._logTop, 0, this._maxLogTop);

			if (rows == 0 || this._maxLogTop == 0) return;

			int top = this._followLogs ? this._maxLogTop : this._logTop;
			this._logTop = Math.Clamp(top + rows, 0, this._maxLogTop);
			this._followLogs = this._logTop >= this._maxLogTop;
			this._terminalDirty = true;
		}

		private void OnLinesAdded(IReadOnlyList<ConsoleOutput.Line> lines) {
			if (this._terminalWriter == null) {
				if (!this._plainOutputAvailable) return;

				try {
					for (int i = 0; i < lines.Count; i++) System.Console.WriteLine(ConsolePump.Sanitize(lines[i].text, true));
				} catch {
					this._plainOutputAvailable = false;
				}
				return;
			}

			this._terminalLines.AddRange(lines);
			int excess = this._terminalLines.Count - ConsoleOutput.MAX_LINES;
			if (excess > 0) this._terminalLines.RemoveRange(0, excess);

			this._logRowsDirty = true;
			this._terminalDirty = true;
		}

		private void OnCleared() {
			this._terminalLines.Clear();
			this._renderedLogLines.Clear();
			this._logRowsDirty = true;
			this._followLogs = true;
			this._logTop = 0;
			this._terminalDirty = true;
		}

		private void AcceptCompletion() {
			string completion = this.GetCompletion();
			if (completion == null) return;

			this._inputBuffer = completion + " ";
			this._inputCursor = this._inputBuffer.Length;
			this._terminalDirty = true;
		}

		private void DeletePreviousWord() {
			if (this._inputCursor == 0) return;

			int start = this._inputCursor;
			while (start > 0 && char.IsWhiteSpace(this._inputBuffer[start - 1])) start--;
			while (start > 0 && !char.IsWhiteSpace(this._inputBuffer[start - 1])) start--;

			this._inputBuffer = this._inputBuffer.Remove(start, this._inputCursor - start);
			this._inputCursor = start;
			this._terminalDirty = true;
		}

		private string GetCompletion() {
			if (this._inputCursor != this._inputBuffer.Length || this._inputBuffer.Length == 0) return null;

			for (int i = 0; i < this._inputBuffer.Length; i++)
				if (char.IsWhiteSpace(this._inputBuffer[i]))
					return null;

			string completion = null;
			foreach (ConsoleEntry entry in ConsoleRegistry.GetAll()) {
				if (entry.HasFlag(FCVAR.HIDDEN) || !entry.name.StartsWith(this._inputBuffer, StringComparison.OrdinalIgnoreCase)) continue;
				if (string.Equals(entry.name, this._inputBuffer, StringComparison.OrdinalIgnoreCase)) return entry.name;
				if (completion == null || string.Compare(entry.name, completion, StringComparison.OrdinalIgnoreCase) < 0) completion = entry.name;
			}

			return completion;
		}

		private void MoveCursor(int direction, bool byWord) {
			if (direction < 0) {
				if (this._inputCursor == 0) return;
				this._inputCursor--;

				if (byWord) {
					while (this._inputCursor > 0 && char.IsWhiteSpace(this._inputBuffer[this._inputCursor])) this._inputCursor--;
					while (this._inputCursor > 0 && !char.IsWhiteSpace(this._inputBuffer[this._inputCursor - 1])) this._inputCursor--;
				}
			} else {
				if (this._inputCursor >= this._inputBuffer.Length) return;
				this._inputCursor++;

				if (byWord) {
					while (this._inputCursor < this._inputBuffer.Length && !char.IsWhiteSpace(this._inputBuffer[this._inputCursor])) this._inputCursor++;
					while (this._inputCursor < this._inputBuffer.Length && char.IsWhiteSpace(this._inputBuffer[this._inputCursor])) this._inputCursor++;
				}
			}

			this._terminalDirty = true;
		}

		private void RefreshTerminalStatus(bool force) {
			long now = ConsolePump.GetTimestampMilliseconds();
			if (!force && now < this._nextStatusRefresh) return;
			this._nextStatusRefresh = now + ConsolePump.TERMINAL_STATUS_POLL_MS;

			if (this.TryGetTerminalSize(out int width, out int height) && (width != this._terminalSize.x || height != this._terminalSize.y)) {
				this._terminalSize = new Vector2Int(width, height);
				this._logRowsDirty = true;
				this._terminalDirty = true;
			}

			(string serverName, int playerCount, int publicCapacity) status;
			try {
				status = Console.GetTerminalStatus();
			} catch {
				return;
			}

			string serverName = ConsolePump.SanitizeTerminal(status.serverName, false).Trim();
			if (serverName.Length == 0) serverName = "Dedicated Server";
			int playerCount = Math.Max(0, status.playerCount);
			int publicCapacity = Math.Max(0, status.publicCapacity);
			if (serverName == this._serverName && playerCount == this._playerCount && publicCapacity == this._publicCapacity) return;

			this._serverName = serverName;
			this._playerCount = playerCount;
			this._publicCapacity = publicCapacity;
			this._terminalDirty = true;
		}

		private bool TryStartTerminal() {
			try {
				if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected) return false;
				if (string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase)) return false;
				if (ConsolePump.WritesUnityLogToStdout()) return false;
			} catch {
				return false;
			}

			if (!this.TryGetTerminalSize(out int width, out int height)) return false;
			#if UNITY_STANDALONE_WIN
				if (!this.TryConfigureConsoleMode(ref this._outputConsoleMode, ConsolePump.STD_OUTPUT_HANDLE,
					ConsolePump.ENABLE_PROCESSED_OUTPUT | ConsolePump.ENABLE_VIRTUAL_TERMINAL_PROCESSING)) return false;
				this.TryConfigureConsoleMode(ref this._inputConsoleMode, ConsolePump.STD_INPUT_HANDLE,
					ConsolePump.ENABLE_EXTENDED_FLAGS | ConsolePump.ENABLE_MOUSE_INPUT, ConsolePump.ENABLE_QUICK_EDIT_MODE);
			#endif

			try {
				this._originalOutputEncoding = System.Console.OutputEncoding;
				System.Console.OutputEncoding = new UTF8Encoding(false);
				try {
					this._originalTreatControlCAsInput = System.Console.TreatControlCAsInput;
					System.Console.TreatControlCAsInput = false;
				} catch {
					this._originalTreatControlCAsInput = null;
				}
				this._terminalWriter = System.Console.Out;
				this._terminalWriter.Write(ConsolePump.ANSI_ENTER);
				this._terminalWriter.Flush();
				this._terminalSize = new Vector2Int(width, height);
				return true;
			} catch {
				try {
					this._terminalWriter?.Write(ConsolePump.ANSI_EXIT);
					this._terminalWriter?.Flush();
				} catch {
				}
				this.RestoreTerminalState();
				return false;
			}
		}

		#if UNITY_STANDALONE_WIN
		private bool TryConfigureConsoleMode(ref NativeConsoleMode state, int handle, uint enable, uint disable = 0) {
			state.handle = ConsolePump.GetStdHandle(handle);
			if (state.handle == IntPtr.Zero || state.handle == new IntPtr(-1)) return false;
			if (!ConsolePump.GetConsoleMode(state.handle, out state.original)) return false;

			uint mode = (state.original | enable) & ~disable;
			state.restore = mode == state.original || ConsolePump.SetConsoleMode(state.handle, mode);
			return state.restore;
		}
		#endif

		private bool TryGetTerminalSize(out int width, out int height) {
			width = 0;
			height = 0;

			try {
				int windowWidth = System.Console.WindowWidth;
				int windowHeight = System.Console.WindowHeight;
				if (windowWidth < 2 || windowHeight < 1) return false;
				width = windowWidth - 1;
				height = windowHeight;
				return true;
			} catch {
				return false;
			}
		}

		private void RenderTerminal() {
			if (this._terminalWriter == null) return;
			long now = ConsolePump.GetTimestampMilliseconds();
			if (now < this._nextTerminalRetry) return;

			try {
				if (!this.TryGetTerminalSize(out int width, out int height)) {
					this.HandleTerminalFailure(false);
					return;
				}
				if (width != this._terminalSize.x || height != this._terminalSize.y) {
					this._terminalSize = new Vector2Int(width, height);
					this._logRowsDirty = true;
				}

				this._frame.Clear();
				this._frame.EnsureCapacity(Math.Max(256, width * height * 2));
				this._frame.Append(ConsolePump.ANSI_BEGIN_FRAME).Append(ConsolePump.ANSI_HIDE_CURSOR).Append(ConsolePump.ANSI_RESET);
				if (width >= ConsolePump.MIN_TERMINAL_WIDTH && height >= ConsolePump.MIN_TERMINAL_HEIGHT)
					this.RenderFullTerminal(this._frame, width, height);
				else
					this.RenderCompactTerminal(this._frame, width, height);
				this._frame.Append(ConsolePump.ANSI_END_FRAME);

				lock (this._terminalWriteLock) {
					if (this._terminalWriter == null) return;
					this._terminalWriter.Write(this._frame.ToString());
					this._terminalWriter.Flush();
				}
				this._terminalDirty = false;
				this._terminalFailureStarted = 0;
				this._nextTerminalRetry = 0;
			} catch {
				this.HandleTerminalFailure(false);
			}
		}

		private void HandleTerminalFailure(bool immediate) {
			if (Volatile.Read(ref this._stopping) != 0) return;

			this._terminalDirty = true;
			long now = ConsolePump.GetTimestampMilliseconds();
			if (this._terminalFailureStarted == 0) this._terminalFailureStarted = now;
			this._nextTerminalRetry = now + ConsolePump.TERMINAL_FAILURE_RETRY_MS;
			if (!immediate && now - this._terminalFailureStarted < ConsolePump.TERMINAL_FAILURE_GRACE_MS) return;

			Interlocked.Increment(ref this._stdinGeneration);
			this._running = false;
			if (this._stdinThread != null && this._stdinThread.IsAlive && Thread.CurrentThread != this._stdinThread) this._stdinThread.Join(100);
			this.StopTerminal();

			lock (this._terminalWriteLock) {
				if (Volatile.Read(ref this._stopping) != 0) return;
				this._terminalDirty = false;
				this._stdinFailed = false;
				Console.SetLogIntercept(!this._unityLogsToStdout);
				this._running = true;
				this._stdinThread = new Thread(this.StdinLoop) { IsBackground = true, Name = "FailCake.Console" };
				this._stdinThread.Start();
			}

			if (!this._plainOutputAvailable) return;
			try {
				System.Console.WriteLine("Terminal UI unavailable. Switched to plain console mode.");
			} catch {
				this._plainOutputAvailable = false;
			}
		}

		private void RenderFullTerminal(StringBuilder frame, int width, int height) {
			int innerWidth = width - 2;
			int logTextWidth = width - 3;
			int logHeight = height - 6;
			this.BuildRenderedLogLines(logTextWidth);

			this._maxLogTop = Math.Max(0, this._renderedLogLines.Count - logHeight);
			this._logTop = this._followLogs ? this._maxLogTop : Math.Clamp(this._logTop, 0, this._maxLogTop);

			ConsolePump.AppendTerminalRow(frame, 0, ConsolePump.ANSI_BORDER + ConsolePump.BuildBorder(width, '+', '-', '+'));

			string playerStatus = $"PLAYERS  {this._playerCount}/{this._publicCapacity} ";
			string serverName = ConsolePump.Truncate(" " + this._serverName, Math.Max(1, innerWidth - playerStatus.Length));
			string header = ConsolePump.Pad(serverName, Math.Max(0, innerWidth - playerStatus.Length)) + playerStatus;
			ConsolePump.AppendTerminalRow(frame, 1, ConsolePump.ANSI_BORDER + "|" + ConsolePump.ANSI_HEADER + header + ConsolePump.ANSI_BORDER + "|");

			string scrollStatus = this._followLogs || this._renderedLogLines.Count == 0
				? null
				: $" {this._logTop + 1}-{Math.Min(this._renderedLogLines.Count, this._logTop + logHeight)}/{this._renderedLogLines.Count} ";
			ConsolePump.AppendTerminalRow(frame, 2, ConsolePump.ANSI_BORDER + ConsolePump.BuildBorder(width, '+', '-', '+', " LOGS ", scrollStatus));

			int thumbStart = 0;
			int thumbSize = logHeight;
			if (this._maxLogTop > 0) {
				thumbSize = Math.Max(1, (int)Math.Round((double)logHeight * logHeight / this._renderedLogLines.Count));
				thumbStart = (int)Math.Round((double)this._logTop * (logHeight - thumbSize) / this._maxLogTop);
			}

			for (int i = 0; i < logHeight; i++) {
				int lineIndex = this._logTop + i;
				RenderedLogLine line = lineIndex < this._renderedLogLines.Count
					? this._renderedLogLines[lineIndex]
					: new RenderedLogLine(string.Empty, null);
				char scroll = this._maxLogTop == 0
					? '|'
					: i >= thumbStart && i < thumbStart + thumbSize ? '#' : '|';

				ConsolePump.AppendTerminalPosition(frame, i + 3, 0);
				frame.Append(ConsolePump.ANSI_BORDER).Append('|');
				ConsolePump.AppendLineColor(frame, line.color);
				frame.Append(ConsolePump.Pad(line.text, logTextWidth));
				frame.Append(ConsolePump.ANSI_MUTED).Append(scroll).Append(ConsolePump.ANSI_BORDER).Append('|').Append(ConsolePump.ANSI_CLEAR_TAIL);
			}

			int separatorRow = height - 3;
			int inputRow = height - 2;
			ConsolePump.AppendTerminalRow(frame, separatorRow, ConsolePump.ANSI_BORDER + ConsolePump.BuildBorder(width, '+', '-', '+'));
			this.AppendInputRow(frame, inputRow, width);
			ConsolePump.AppendTerminalRow(frame, height - 1, ConsolePump.ANSI_BORDER + ConsolePump.BuildBorder(width, '+', '-', '+'));

			int inputWidth = Math.Max(1, width - 6);
			this.UpdateInputView(inputWidth);
			ConsolePump.AppendTerminalPosition(frame, inputRow, Math.Min(width - 1, 4 + this._inputCursor - this._inputViewStart));
			frame.Append(ConsolePump.ANSI_SHOW_CURSOR).Append(ConsolePump.ANSI_RESET);
		}

		private void RenderCompactTerminal(StringBuilder frame, int width, int height) {
			string status = ConsolePump.Truncate($"{this._serverName}  {this._playerCount}/{this._publicCapacity}", width);
			ConsolePump.AppendTerminalRow(frame, 0, ConsolePump.ANSI_HEADER + ConsolePump.Pad(status, width));

			int compactLogHeight = Math.Max(0, height - 2);
			this.BuildCompactLogLines(width);
			this._maxLogTop = Math.Max(0, this._renderedLogLines.Count - compactLogHeight);
			this._logTop = this._followLogs ? this._maxLogTop : Math.Clamp(this._logTop, 0, this._maxLogTop);

			for (int row = 0; row < compactLogHeight; row++) {
				int lineIndex = this._logTop + row;
				RenderedLogLine line = lineIndex < this._renderedLogLines.Count
					? this._renderedLogLines[lineIndex]
					: new RenderedLogLine(string.Empty, null);
				ConsolePump.AppendTerminalPosition(frame, row + 1, 0);
				ConsolePump.AppendLineColor(frame, line.color);
				frame.Append(ConsolePump.Pad(line.text, width)).Append(ConsolePump.ANSI_CLEAR_TAIL);
			}

			int inputRow = Math.Max(0, height - 1);
			int inputWidth = Math.Max(1, width - ConsolePump.PROMPT.Length);
			this.UpdateInputView(inputWidth);
			int visibleLength = Math.Min(inputWidth, Math.Max(0, this._inputBuffer.Length - this._inputViewStart));
			string visible = visibleLength > 0
				? ConsolePump.MakeSingleCell(this._inputBuffer.Substring(this._inputViewStart, visibleLength))
				: string.Empty;
			string input = ConsolePump.Truncate(ConsolePump.PROMPT + visible, width);
			ConsolePump.AppendTerminalRow(frame, inputRow, ConsolePump.ANSI_HEADER + ConsolePump.Pad(input, width));

			ConsolePump.AppendTerminalPosition(frame, inputRow,
				Math.Min(width - 1, ConsolePump.PROMPT.Length + this._inputCursor - this._inputViewStart));
			frame.Append(ConsolePump.ANSI_SHOW_CURSOR).Append(ConsolePump.ANSI_RESET);
		}

		private void AppendInputRow(StringBuilder frame, int row, int width) {
			int inputWidth = Math.Max(1, width - 6);
			this.UpdateInputView(inputWidth);

			int visibleLength = Math.Min(inputWidth, Math.Max(0, this._inputBuffer.Length - this._inputViewStart));
			string visible = visibleLength > 0
				? ConsolePump.MakeSingleCell(this._inputBuffer.Substring(this._inputViewStart, visibleLength))
				: string.Empty;
			string completion = this.GetCompletion();
			string hint = this._inputBuffer.Length == 0 ? ConsolePump.PROMPT_HINT : string.Empty;

			if (completion != null && completion.Length > this._inputBuffer.Length) hint = completion.Substring(this._inputBuffer.Length);
			hint = ConsolePump.MakeSingleCell(hint);
			int hintWidth = Math.Max(0, inputWidth - visible.Length);
			hint = ConsolePump.Truncate(hint, hintWidth);

			ConsolePump.AppendTerminalPosition(frame, row, 0);
			frame.Append(ConsolePump.ANSI_BORDER).Append('|').Append(' ');
			frame.Append(ConsolePump.ANSI_MUTED).Append(ConsolePump.PROMPT);
			frame.Append(ConsolePump.ANSI_HEADER).Append(visible);
			frame.Append(ConsolePump.ANSI_MUTED).Append(hint);
			frame.Append(new string(' ', Math.Max(0, inputWidth - visible.Length - hint.Length)));
			frame.Append(' ').Append(ConsolePump.ANSI_BORDER).Append('|').Append(ConsolePump.ANSI_CLEAR_TAIL);
		}

		private void BuildRenderedLogLines(int width) {
			if (!this._logRowsDirty && !this._logRowsCompact && this._logRowsWidth == width) return;

			this._renderedLogLines.Clear();
			this._logRowsWidth = width;
			this._logRowsCompact = false;
			this._logRowsDirty = false;

			int categoryWidth = Math.Clamp(width / 5, 8, 14);
			for (int i = this._terminalLines.Count - 1; i >= 0 && this._renderedLogLines.Count < ConsolePump.MAX_RENDERED_LOG_LINES; i--) {
				this._messageLines.Clear();
				ConsoleOutput.Line line = this._terminalLines[i];
				string timestamp = ConsolePump.Pad(ConsolePump.SanitizeTerminal(line.timestamp, false), 8);
				string category = ConsolePump.Pad(ConsolePump.SanitizeTerminal(line.category, false), categoryWidth);
				string prefix = " " + timestamp + " " + category + " ";
				string continuationPrefix = new string(' ', prefix.Length);
				int messageWidth = Math.Max(1, width - prefix.Length);
				string message = ConsolePump.SanitizeTerminal(ConsolePump.LimitLog(line.text, false), true);
				string[] physicalLines = message.Split('\n');
				bool first = true;

				for (int j = 0; j < physicalLines.Length; j++) {
					string physicalLine = physicalLines[j];
					if (physicalLine.Length == 0) {
						this._messageLines.Add(new RenderedLogLine(first ? prefix : continuationPrefix, line.color));
						first = false;
						continue;
					}

					for (int offset = 0; offset < physicalLine.Length; offset += messageWidth) {
						int length = Math.Min(messageWidth, physicalLine.Length - offset);
						string rowPrefix = first ? prefix : continuationPrefix;
						this._messageLines.Add(new RenderedLogLine(rowPrefix + physicalLine.Substring(offset, length), line.color));
						first = false;
					}
				}

				for (int j = this._messageLines.Count - 1; j >= 0 && this._renderedLogLines.Count < ConsolePump.MAX_RENDERED_LOG_LINES; j--)
					this._renderedLogLines.Add(this._messageLines[j]);
			}

			this._renderedLogLines.Reverse();
		}

		private void BuildCompactLogLines(int width) {
			if (!this._logRowsDirty && this._logRowsCompact && this._logRowsWidth == width) return;

			this._renderedLogLines.Clear();
			this._logRowsWidth = width;
			this._logRowsCompact = true;
			this._logRowsDirty = false;

			for (int i = this._terminalLines.Count - 1; i >= 0 && this._renderedLogLines.Count < ConsolePump.MAX_RENDERED_LOG_LINES; i--) {
				this._messageLines.Clear();
				ConsoleOutput.Line line = this._terminalLines[i];
				string category = ConsolePump.SanitizeTerminal(line.category, false);
				string prefix = category.Length > 0 && width >= 16 ? ConsolePump.Truncate(category, 10) + " " : string.Empty;
				string continuationPrefix = new string(' ', prefix.Length);
				int messageWidth = Math.Max(1, width - prefix.Length);
				string[] physicalLines = ConsolePump.SanitizeTerminal(ConsolePump.LimitLog(line.text, true), true).Split('\n');
				bool first = true;

				for (int j = 0; j < physicalLines.Length; j++) {
					if (physicalLines[j].Length == 0) {
						this._messageLines.Add(new RenderedLogLine(first ? prefix : continuationPrefix, line.color));
						first = false;
						continue;
					}

					for (int offset = 0; offset < physicalLines[j].Length; offset += messageWidth) {
						int length = Math.Min(messageWidth, physicalLines[j].Length - offset);
						this._messageLines.Add(new RenderedLogLine((first ? prefix : continuationPrefix) + physicalLines[j].Substring(offset, length), line.color));
						first = false;
					}
				}

				for (int j = this._messageLines.Count - 1; j >= 0 && this._renderedLogLines.Count < ConsolePump.MAX_RENDERED_LOG_LINES; j--)
					this._renderedLogLines.Add(this._messageLines[j]);
			}

			this._renderedLogLines.Reverse();
		}

		private void UpdateInputView(int inputWidth) {
			if (this._inputCursor < this._inputViewStart) this._inputViewStart = this._inputCursor;
			if (this._inputCursor >= this._inputViewStart + inputWidth) this._inputViewStart = this._inputCursor - inputWidth + 1;

			int maxStart = Math.Max(0, this._inputBuffer.Length - inputWidth);
			this._inputViewStart = Math.Clamp(this._inputViewStart, 0, maxStart);
		}

		private void StopPump() {
			lock (this._terminalWriteLock) {
				if (Interlocked.Exchange(ref this._stopping, 1) != 0) return;
				Interlocked.Increment(ref this._stdinGeneration);
				this._running = false;
			}

			ConsoleOutput.OnLinesAdded -= this.OnLinesAdded;
			ConsoleOutput.OnCleared -= this.OnCleared;
			System.Console.CancelKeyPress -= ConsolePump.OnCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit -= ConsolePump.OnProcessExit;
			AppDomain.CurrentDomain.UnhandledException -= ConsolePump.OnUnhandledException;
			Console.SetLogIntercept(false);

			if (this._stdinThread != null && this._stdinThread.IsAlive && Thread.CurrentThread != this._stdinThread)
				this._stdinThread.Join(100);
			this.StopTerminal();
			this._stdinThread = null;
			if (ConsolePump.ACTIVE_PUMP == this) ConsolePump.ACTIVE_PUMP = null;
		}

		private void StopTerminal() {
			lock (this._terminalWriteLock) {
				TextWriter writer = this._terminalWriter;
				if (writer == null) return;
				this._terminalWriter = null;

				try {
					writer.Write(ConsolePump.ANSI_EXIT);
					writer.Flush();
				} catch {
				}

				this.RestoreTerminalState();
			}
		}

		private void RestoreTerminalState() {
			#if UNITY_STANDALONE_WIN
			this.RestoreConsoleMode(ref this._inputConsoleMode);
			this.RestoreConsoleMode(ref this._outputConsoleMode);
			#endif

			if (this._originalTreatControlCAsInput.HasValue) {
				bool original = this._originalTreatControlCAsInput.Value;
				this._originalTreatControlCAsInput = null;

				try {
					System.Console.TreatControlCAsInput = original;
				} catch {
				}
			}

			if (this._originalOutputEncoding != null) {
				try {
					System.Console.OutputEncoding = this._originalOutputEncoding;
				} catch {
				}
			}

			this._originalOutputEncoding = null;
			this._terminalWriter = null;
		}

		#if UNITY_STANDALONE_WIN
		private void RestoreConsoleMode(ref NativeConsoleMode state) {
			if (!state.restore) return;
			state.restore = false;
			ConsolePump.SetConsoleMode(state.handle, state.original);
		}
		#endif
		#endif
		#endregion
	}
}
