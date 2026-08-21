#region

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class ConsoleCfg
    {
        public static string ROOT_OVERRIDE;

        public static string GetRoot() {
            if (ConsoleCfg.ROOT_OVERRIDE != null) return ConsoleCfg.ROOT_OVERRIDE;

            #if UNITY_SERVER
            return Path.Combine(Application.dataPath, "..", "cfg");
            #else
            return Path.Combine(Application.persistentDataPath, "cfg");
            #endif
        }

        public static void BootExec() {
            ConsoleCfg.Exec(Console.IS_SERVER_PROCESS ? "server.cfg" : "config.cfg", false);
            ConsoleCfg.Exec("autoexec.cfg", false);
        }

        public static bool Exec(string file, bool logMissing = true) {
            if (string.IsNullOrEmpty(file)) return false;

            string path = ConsoleCfg.ResolvePath(file);
            if (!File.Exists(path))
            {
                if (logMissing) ConsoleOutput.Add($"exec: couldn't exec {file}");
                return false;
            }

            ConsoleOutput.Add($"executing {file}");

            ConsoleContext context = ConsoleContext.Config(Console.IS_SERVER_PROCESS);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;

                ConsoleDispatcher.Execute(line, context);
            }

            return true;
        }

        public static void WriteConfig() {
            string root = ConsoleCfg.GetRoot();
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "config.cfg");

            IEnumerable<string> lines = ConsoleRegistry.GetAll()
                .OfType<ConsoleVar>()
                .Where(ConsoleCfg.IsArchivable)
                .Select(ConsoleCfg.FormatLine);

            File.WriteAllLines(path, lines);
            ConsoleOutput.Add($"host_writeconfig: wrote {path}");
        }

        public static string ResolvePath(string file) {
            string name = file.EndsWith(".cfg") ? file : file + ".cfg";
            return Path.Combine(ConsoleCfg.GetRoot(), name);
        }

        #region PRIVATE METHODS

        private static bool IsArchivable(ConsoleVar cv) {
            return cv.HasFlag(FCVAR.ARCHIVE) && !cv.HasFlag(FCVAR.HIDDEN) && !cv.HasFlag(FCVAR.PROTECTED) && !cv.HasFlag(FCVAR.REPLICATED);
        }

        private static string FormatLine(ConsoleVar cv) {
            return $"\"{cv.name}\" \"{cv.GetString()}\"";
        }

        #endregion
    }
}