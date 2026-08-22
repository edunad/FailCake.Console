#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.Console
{
    public class ConsolePump : MonoBehaviour
    {
        #region STATIC

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init() {
            Console.Scan();
            Console.SetLogIntercept(!Application.isBatchMode);
            Application.quitting += ConsoleCfg.WriteConfig;

            GameObject go = new GameObject("Console.Pump");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ConsolePump>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootCfg() {
            ConsoleCfg.BootExec();
        }

        #endregion

        #region PRIVATE FIELDS

        private Thread _stdinThread;
        private bool _running;
        private readonly ConcurrentQueue<string> _stdinQueue = new ConcurrentQueue<string>();
        private readonly object _terminalLock = new object();
        private string _inputBuffer = "";
        private int _inputCursor;
        private readonly List<string> _history = new List<string>();
        private int _historyCursor;
        private bool _isTerminal;

        private const string PROMPT = "] ";
        private const int MAX_HISTORY = 64;

        #endregion

        private void Awake() {
            if (!Application.isBatchMode) return;

            this._isTerminal = !System.Console.IsOutputRedirected && !System.Console.IsInputRedirected;

            ConsoleOutput.OnLinesAdded += this.OnLinesAdded;

            this._running = true;
            this._stdinThread = new Thread(this.StdinLoop) { IsBackground = true, Name = "FailCake.Console" };
            this._stdinThread.Start();
        }

        private void Update() {
            ConsoleOutput.Flush();
            while (this._stdinQueue.TryDequeue(out string line)) Console.Execute(line, ConsoleContext.ServerConsole());
        }

        private void OnDestroy() {
            this._running = false;
            ConsoleOutput.OnLinesAdded -= this.OnLinesAdded;
        }

        #region PRIVATE METHODS

        private void StdinLoop() {
            if (this._isTerminal)
            {
                try { System.Console.OutputEncoding = Encoding.UTF8; } catch { }

                lock (this._terminalLock) this.RedrawInput();

                while (this._running)
                {
                    ConsoleKeyInfo key;
                    try { key = System.Console.ReadKey(true); }
                    catch { break; }

                    this.HandleKey(key);
                }
            }
            else
            {
                while (this._running)
                {
                    string line;
                    try { line = System.Console.ReadLine(); }
                    catch { break; }

                    if (line == null) break;
                    this._stdinQueue.Enqueue(line);
                }
            }
        }

        private void HandleKey(ConsoleKeyInfo key) {
            lock (this._terminalLock)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        System.Console.WriteLine();
                        if (this._inputBuffer.Length > 0)
                        {
                            this._stdinQueue.Enqueue(this._inputBuffer);
                            this._history.Add(this._inputBuffer);
                            if (this._history.Count > ConsolePump.MAX_HISTORY) this._history.RemoveAt(0);
                        }
                        this._inputBuffer = "";
                        this._inputCursor = 0;
                        this._historyCursor = this._history.Count;
                        this.RedrawInput();
                        break;

                    case ConsoleKey.Backspace:
                        if (this._inputCursor > 0)
                        {
                            this._inputBuffer = this._inputBuffer.Remove(this._inputCursor - 1, 1);
                            this._inputCursor--;
                            this.RedrawInput();
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (this._inputCursor < this._inputBuffer.Length)
                        {
                            this._inputBuffer = this._inputBuffer.Remove(this._inputCursor, 1);
                            this.RedrawInput();
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (this._inputCursor > 0)
                        {
                            this._inputCursor--;
                            this.RedrawInput();
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (this._inputCursor < this._inputBuffer.Length)
                        {
                            this._inputCursor++;
                            this.RedrawInput();
                        }
                        break;

                    case ConsoleKey.Home:
                        this._inputCursor = 0;
                        this.RedrawInput();
                        break;

                    case ConsoleKey.End:
                        this._inputCursor = this._inputBuffer.Length;
                        this.RedrawInput();
                        break;

                    case ConsoleKey.UpArrow:
                        if (this._historyCursor > 0)
                        {
                            this._historyCursor--;
                            this._inputBuffer = this._history[this._historyCursor];
                            this._inputCursor = this._inputBuffer.Length;
                            this.RedrawInput();
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (this._historyCursor < this._history.Count)
                        {
                            this._historyCursor++;
                            this._inputBuffer = this._historyCursor < this._history.Count ? this._history[this._historyCursor] : "";
                            this._inputCursor = this._inputBuffer.Length;
                            this.RedrawInput();
                        }
                        break;

                    case ConsoleKey.Escape:
                        this._inputBuffer = "";
                        this._inputCursor = 0;
                        this.RedrawInput();
                        break;

                    default:
                        if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
                        {
                            this._inputBuffer = this._inputBuffer.Insert(this._inputCursor, key.KeyChar.ToString());
                            this._inputCursor++;
                            this.RedrawInput();
                        }
                        break;
                }
            }
        }

        private void RedrawInput() {
            if (!this._isTerminal) return;

            int width = ConsolePump.GetWidth();
            System.Console.Write("\r");
            System.Console.Write(new string(' ', width - 1));
            System.Console.Write("\r");
            System.Console.Write(ConsolePump.PROMPT + this._inputBuffer);

            int cursorPos = ConsolePump.PROMPT.Length + this._inputCursor;
            if (cursorPos < width - 1)
            {
                try { System.Console.SetCursorPosition(cursorPos, System.Console.CursorTop); } catch { }
            }
        }

        private void OnLinesAdded(IReadOnlyList<ConsoleOutput.Line> lines) {
            lock (this._terminalLock)
            {
                if (this._isTerminal)
                {
                    int width = ConsolePump.GetWidth();
                    System.Console.Write("\r");
                    System.Console.Write(new string(' ', width - 1));
                    System.Console.Write("\r");
                }

                foreach (ConsoleOutput.Line line in lines)
                {
                    string text = ConsolePump.StripRichText(line.text);
                    foreach (string sub in text.Split('\n'))
                        System.Console.WriteLine(sub);
                }

                if (this._isTerminal) this.RedrawInput();
            }
        }

        private static int GetWidth() {
            try { return Math.Max(20, System.Console.WindowWidth); } catch { return 80; }
        }

        private static string StripRichText(string text) {
            if (string.IsNullOrEmpty(text) || !text.Contains('<')) return text;

            StringBuilder sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in text)
            {
                if (c == '<') { inTag = true; continue; }
                if (inTag)
                {
                    if (c == '>') inTag = false;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        #endregion
    }
}
