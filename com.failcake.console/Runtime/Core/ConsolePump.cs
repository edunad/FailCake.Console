#region

using System.Collections.Concurrent;
using System.Collections.Generic;
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

        #endregion

        private void Awake() {
            if (!Application.isBatchMode) return;

            // EVENTS ---
            ConsoleOutput.OnLinesAdded += this.OnLinesAdded;
            // ----------

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
            while (this._running)
            {
                string line;
                try
                {
                    line = System.Console.ReadLine();
                }
                catch
                {
                    break;
                }

                if (line == null) break;
                this._stdinQueue.Enqueue(line);
            }
        }

        private void OnLinesAdded(IReadOnlyList<ConsoleOutput.Line> lines) {
            foreach (ConsoleOutput.Line line in lines) System.Console.WriteLine(line.text);
        }

        #endregion
    }
}