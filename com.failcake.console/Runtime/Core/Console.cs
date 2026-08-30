#region

using System;
using System.Linq;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class Console
    {
        private static readonly Color RESPONSE_COLOR = new Color(0.58F, 0.58F, 0.58F);
        #if UNITY_SERVER
        private static Func<(string serverName, int playerCount, int publicCapacity)> TERMINAL_STATUS_PROVIDER = Console.GetDefaultTerminalStatus;
        #endif

        public static Func<CCommand, object, (bool success, string response)> OnCLCommand = Console.ExecuteClientLocal;
        public static Func<CCommand, object, (bool success, string response)> OnSVCommand = Console.ExecuteServerLocal;
		#if UNITY_SERVER
		public static Func<object, bool> IsAdmin = Console.IsLocalAdmin;
		#endif
        public static Func<bool> IsMultiplayer = Console.IsOffline;

        #if UNITY_SERVER
        public const bool IS_SERVER_PROCESS = true;
        #else
        public const bool IS_SERVER_PROCESS = false;
        #endif

        public static void Scan() {
            ConsoleRegistry.Scan();
        }

        #if UNITY_SERVER
        public static void SetTerminalStatusProvider(Func<(string serverName, int playerCount, int publicCapacity)> provider) {
            Console.TERMINAL_STATUS_PROVIDER = provider ?? Console.GetDefaultTerminalStatus;
        }

        public static void ResetTerminalStatusProvider() {
            Console.TERMINAL_STATUS_PROVIDER = Console.GetDefaultTerminalStatus;
        }
        #endif

        public static void Execute(string line, ConsoleContext context) {
            ConsoleRegistry.Scan();
            ConsoleDispatcher.Execute(line, context);
        }

        public static string ExecuteCaptured(string line, ConsoleContext context) {
            ConsoleRegistry.Scan();
            return ConsoleDispatcher.ExecuteCaptured(line, context);
        }

        public static void Msg(string text, string category = "ENGINE") {
            ConsoleOutput.Add(text, category);
        }

        public static void Response(string text) {
            ConsoleOutput.Add(text, "CONSOLE", Console.RESPONSE_COLOR);
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
            Application.logMessageReceivedThreaded -= Console.OnUnityLog;
            if (enable) Application.logMessageReceivedThreaded += Console.OnUnityLog;
        }

        public static void ResetNetworkHandlers() {
            Console.OnCLCommand = Console.ExecuteClientLocal;
            Console.OnSVCommand = Console.ExecuteServerLocal;
            Console.IsMultiplayer = Console.IsOffline;
        }

        internal static void ResetHandlers() {
            Console.ResetNetworkHandlers();

			#if UNITY_SERVER
			Console.IsAdmin = Console.IsLocalAdmin;
            Console.ResetTerminalStatusProvider();
            #endif
        }

        #if UNITY_SERVER
        internal static (string serverName, int playerCount, int publicCapacity) GetTerminalStatus() {
            return Console.TERMINAL_STATUS_PROVIDER();
        }
        #endif

        public static void OutputManifest(out string entries, out string replicatedValues) {
            ConsoleRegistry.Scan();

            entries = string.Join("\n", ConsoleRegistry.GetAll().Where(Console.IsManifestVisible).Select(Console.FormatManifestEntry));
            replicatedValues = string.Join("\n", ConsoleRegistry.GetAll().OfType<ConsoleVar>().Where(Console.IsManifestReplicated).Select(Console.FormatManifestValue));
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

        private static bool IsManifestVisible(ConsoleEntry entry) {
            return !entry.HasFlag(FCVAR.HIDDEN) && (entry.HasFlag(FCVAR.SERVER) || entry.HasFlag(FCVAR.REPLICATED));
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

        private static (bool success, string response) ExecuteClientLocal(CCommand command, object userData) {
            return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ClientLocal(userData)));
        }

        private static (bool success, string response) ExecuteServerLocal(CCommand command, object userData) {
            #if !UNITY_SERVER
            return (false, "Not connected to server.");
            #else
            return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ServerLocal(userData)));
            #endif
        }

        private static bool IsOffline() {
            return false;
        }

        #if UNITY_SERVER
		private static bool IsLocalAdmin(object userData) {
			return userData == null && !Console.IsMultiplayer();
		}

        private static (string serverName, int playerCount, int publicCapacity) GetDefaultTerminalStatus() {
            return ("Starting server...", 0, 0);
        }
        #endif

        private static void OnUnityLog(string condition, string stackTrace, LogType type) {
            if (string.IsNullOrEmpty(condition)) return;

            (string text, string category) = Console.ParseLogCategory(condition);
            #if UNITY_SERVER
            if ((type == LogType.Error || type == LogType.Assert || type == LogType.Exception) &&
                !string.IsNullOrWhiteSpace(stackTrace) && !text.Contains(stackTrace))
                text += "\n" + stackTrace.TrimEnd();
            #endif

            switch (type)
            {
                case LogType.Warning:
                    Console.Warn(text, category);
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    Console.Error(text, category);
                    break;
                case LogType.Log:
                default:
                    Console.Msg(text, category);
                    break;
            }
        }

        private static (string text, string category) ParseLogCategory(string condition) {
            int bracketStart = -1;

            if (condition.StartsWith("<color="))
            {
                int tagEnd = condition.IndexOf('>');
                if (tagEnd >= 0 && tagEnd + 1 < condition.Length && condition[tagEnd + 1] == '[') bracketStart = tagEnd + 1;
            }
            else if (condition.StartsWith("[")) bracketStart = 0;

            if (bracketStart < 0) return (condition, "UNITY");

            int bracketEnd = condition.IndexOf(']', bracketStart);
            if (bracketEnd <= bracketStart) return (condition, "UNITY");

            string category = condition.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

            int textStart = bracketEnd + 1;
            if (textStart < condition.Length && condition.IndexOf("</color>", textStart, StringComparison.Ordinal) == textStart) textStart += "</color>".Length;

            string text = condition.Substring(textStart).TrimStart(' ', '\n');
            return (text.Length == 0 ? condition : text, category.ToUpperInvariant());
        }
    }
}
