#region

using System;
using System.Linq;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class Console
    {
        public static Func<CCommand, object, (bool success, string response)> OnCLCommand = Console.DefaultCLCommand;
        public static Func<CCommand, object, (bool success, string response)> OnSVCommand = Console.DefaultSVCommand;
        public static Func<object, bool> IsAdmin = Console.DefaultIsAdmin;
        public static Func<bool> IsMultiplayer = Console.DefaultIsMultiplayer;

        #if UNITY_SERVER
        public const bool IS_SERVER_PROCESS = true;
        #else
        public const bool IS_SERVER_PROCESS = false;
        #endif

        public static void Scan() {
            ConsoleRegistry.Scan();
        }

        public static void Execute(string line, ConsoleContext context) {
            ConsoleRegistry.Scan();
            ConsoleDispatcher.Execute(line, context);
        }

        public static string ExecuteCaptured(string line, ConsoleContext context) {
            ConsoleRegistry.Scan();
            return ConsoleDispatcher.ExecuteCaptured(line, context);
        }

        public static void Msg(string text) {
            ConsoleOutput.Add(text);
        }

        public static void Warn(string text, string category = "ENGINE") {
            ConsoleOutput.Add(text, category, new Color(0.96F, 0.80F, 0.06F));
        }

        public static void ColorMsg(Color color, string text, string category = "ENGINE") {
            ConsoleOutput.Add(text, category, color);
        }

        public static void Error(string text, string category = "ENGINE") {
            ConsoleOutput.Add(text, category, new Color(0.98F, 0.29F, 0.20F));
        }

        public static ConsoleVar FindVar(string name) {
            ConsoleRegistry.Scan();
            return ConsoleRegistry.FindVar(name);
        }

        public static ConsoleCommand FindCommand(string name) {
            ConsoleRegistry.Scan();
            return ConsoleRegistry.FindCommand(name);
        }

        public static void SetLogIntercept(bool enable) {
            if (enable)
                Application.logMessageReceivedThreaded += Console.OnUnityLog;
            else
                Application.logMessageReceivedThreaded -= Console.OnUnityLog;
        }

        public static void OutputManifest(out string entries, out string replicatedValues) {
            ConsoleRegistry.Scan();

            entries = string.Join("\n", ConsoleRegistry.GetAll()
                .Where(Console.IsManifestVisible)
                .Select(Console.FormatManifestEntry));

            replicatedValues = string.Join("\n", ConsoleRegistry.GetAll()
                .OfType<ConsoleVar>()
                .Where(Console.IsManifestReplicated)
                .Select(Console.FormatManifestValue));
        }

        public static void ApplyManifest(string entries, string replicatedValues) {
            foreach (string line in entries.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('\x01');
                if (parts.Length != 3 || !int.TryParse(parts[1], out int flags)) continue;
                ConsoleRegistry.AddRemoteStub(parts[0], parts[2], (FCVAR)flags);
            }

            foreach (string line in replicatedValues.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('\x01');
                if (parts.Length != 2) continue;
                ConsoleDispatcher.ApplyReplicated(parts[0], parts[1]);
            }
        }

        private static (bool, string) DefaultCLCommand(CCommand command, object userData) {
            return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ClientLocal(userData)));
        }

        private static (bool, string) DefaultSVCommand(CCommand command, object userData) {
            return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ServerLocal(userData)));
        }

        private static bool DefaultIsAdmin(object userData) {
            return !Console.IsMultiplayer();
        }

        private static bool DefaultIsMultiplayer() {
            return false;
        }

        private static bool IsManifestVisible(ConsoleEntry entry) {
            return !entry.HasFlag(FCVAR.HIDDEN);
        }

        private static string FormatManifestEntry(ConsoleEntry entry) {
            return $"{entry.name}\x01{(int)entry.flags}\x01{entry.help}";
        }

        private static bool IsManifestReplicated(ConsoleVar cv) {
            return cv.HasFlag(FCVAR.REPLICATED);
        }

        private static string FormatManifestValue(ConsoleVar cv) {
            return $"{cv.name}\x01{cv.GetString()}";
        }

        private static void OnUnityLog(string condition, string stackTrace, LogType type) {
            if (string.IsNullOrEmpty(condition)) return;

            switch (type)
            {
                case LogType.Warning:
                    Console.Warn(condition);
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    Console.Error(condition);
                    break;
                case LogType.Log:
                default:
                    Console.Msg(condition);
                    break;
            }
        }
    }
}