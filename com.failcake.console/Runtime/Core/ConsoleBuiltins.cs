#region

using System.Linq;
using System.Text;

#endregion

namespace FailCake.Console
{
    public static class ConsoleBuiltins
    {
        #region STATIC

        [CVar("sv_cheats", "0", FCVAR.REPLICATED | FCVAR.ADMIN, "Allow cheat commands on the server")]
        public static int SV_CHEATS;

        #endregion

        [Command("find", "Find commands/cvars")]
        private static void Find(string partial = "") {
            string lower = partial.ToLowerInvariant();
            StringBuilder sb = new StringBuilder();
            foreach (ConsoleEntry entry in ConsoleRegistry.GetAll().Where(ConsoleBuiltins.IsVisible))
            {
                if (lower.Length > 0 && !entry.name.ToLowerInvariant().Contains(lower)) continue;
                sb.Append(ConsoleBuiltins.FormatEntryLine(entry)).Append('\n');
            }

            string text = sb.ToString().TrimEnd('\n');
            ConsoleOutput.Add(text.Length == 0 ? $"find: no matches for \"{partial}\"" : text);
        }

        [Command("cvarlist", "List all cvars with current values")]
        private static void CvarList() {
            string text = string.Join("\n", ConsoleRegistry.GetAll()
                .OfType<ConsoleVar>()
                .Where(ConsoleBuiltins.IsVisible)
                .Select(ConsoleBuiltins.FormatEntry));
            ConsoleOutput.Add(text);
        }

        [Command("echo", "Print text to the console")]
        private static void Echo(CCommand args) {
            if (args.argc > 1) ConsoleOutput.Add(args.argS);
        }

        [Command("clear", "Clear the console")]
        private static void Clear() {
            ConsoleOutput.Clear();
        }

        [Command("exec", "Execute a cfg file")]
        private static void Exec(string file) {
            ConsoleCfg.Exec(file);
        }

        [Command("host_writeconfig", "Save archived cvars to config.cfg")]
        private static void HostWriteConfig() {
            ConsoleCfg.WriteConfig();
        }

        #region PRIVATE

        private static bool IsVisible(ConsoleEntry entry) {
            return !entry.HasFlag(FCVAR.HIDDEN) && !(entry is ConsoleStubEntry);
        }

        private static string FormatEntry(ConsoleEntry entry) {
            return entry is ConsoleVar cv ? cv.ToDisplayString() : $"{entry.name} - {entry.help}";
        }

        private static string FormatEntryLine(ConsoleEntry entry) {
            string kind = entry is ConsoleVar ? "cvar " : "cmd  ";
            return $"{kind}{entry.name} - {entry.help}";
        }

        #endregion
    }
}