#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class ConsoleDispatcher
    {
        public static void Execute(string line, ConsoleContext context) {
            if (string.IsNullOrEmpty(line)) return;
            foreach (string sub in ConsoleDispatcher.SplitSemicolons(line))
            {
                string trimmed = sub.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;
                ConsoleDispatcher.ExecuteOne(trimmed, context);
            }
        }

        public static string ExecuteCaptured(string line, ConsoleContext context) {
            List<string> buffer = new List<string>();
            List<string> prev = ConsoleOutput.CAPTURE;

            ConsoleOutput.CAPTURE = buffer;

            try
            {
                ConsoleDispatcher.Execute(line, context);
            }
            finally
            {
                ConsoleOutput.CAPTURE = prev;
            }

            return string.Join("\n", buffer);
        }

        public static void ApplyReplicated(string name, string value) {
            ConsoleVar cv = ConsoleRegistry.FindVar(name);
            cv?.SetValue(value);
        }

        private static void ExecuteOne(string line, ConsoleContext context) {
            CCommand cmd = new CCommand(line);
            if (cmd.argc == 0) return;

            if (context.echo) ConsoleOutput.Add($"] {line}");

            string name = cmd.Arg(0);
            ConsoleEntry entry = ConsoleRegistry.Find(name);

            if (entry is null or ConsoleStubEntry)
            {
                if (context.source == ConSource.Local && !Console.IS_SERVER_PROCESS)
                {
                    if (Console.OnSVCommand == null) throw new UnityException("Missing OnSVCommand");

                    (bool success, string response) = Console.OnSVCommand.Invoke(cmd, context.userData);
                    if (!success && !string.IsNullOrEmpty(response)) ConsoleOutput.Add(response);
                    return;
                }

                ConsoleOutput.Add($"Unknown command \"{name}\"");
                return;
            }

            if (!ConsoleDispatcher.PassGates(entry, context)) return;

            switch (entry)
            {
                case ConsoleVar cv:
                    ConsoleDispatcher.HandleVar(cv, cmd, context);
                    break;
                case ConsoleCommand cc:
                    ConsoleDispatcher.HandleCommand(cc, cmd);
                    break;
            }
        }

        private static bool PassGates(ConsoleEntry entry, ConsoleContext context) {
            if (context.source == ConSource.ServerConsole) return true;

            if (entry.HasFlag(FCVAR.CHEAT) && Console.IsMultiplayer())
            {
                ConsoleVar sv = ConsoleRegistry.FindVar("sv_cheats");
                if (sv == null || !sv.GetBool())
                {
                    ConsoleOutput.Add($"Can't use cheat command {entry.name} in multiplayer, unless the server has sv_cheats set to 1.");
                    return false;
                }
            }

            if (!entry.HasFlag(FCVAR.ADMIN) || Console.IsAdmin(context.userData)) return true;
            ConsoleOutput.Add("You don't have permission to run this command.");

            return false;
        }

        private static void HandleVar(ConsoleVar cv, CCommand cmd, ConsoleContext context) {
            if (cmd.argc == 1)
            {
                ConsoleOutput.Add(cv.ToDisplayString());
                return;
            }

            if (cv.HasFlag(FCVAR.REPLICATED))
            {
                if (context.source == ConSource.Remote && !(Console.IsAdmin?.Invoke(context.userData) ?? false))
                {
                    ConsoleOutput.Add("You don't have permission to change this cvar.");
                    return;
                }

                if (!Console.IS_SERVER_PROCESS && context.source != ConSource.Remote && (Console.IsMultiplayer?.Invoke() ?? false))
                {
                    ConsoleOutput.Add($"Can't change replicated ConsoleVar {cv.name}. Server enforces: \"{cv.GetString()}\"");
                    return;
                }
            }

            string value = cmd.Arg(1);
            try
            {
                cv.SetValue(value);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Add($"Failed to set {cv.name}: {ex.Message}");
            }
        }

        private static void HandleCommand(ConsoleCommand cc, CCommand cmd) {
            try
            {
                (object result, string error) = cc.Invoke(cmd);
                if (error != null)
                {
                    ConsoleOutput.Add(error);
                    return;
                }

                ConsoleDispatcher.PrintResult(result);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                ConsoleOutput.Add($"Error: {msg}");
            }
        }

        private static void PrintResult(object result) {
            switch (result)
            {
                case null:
                    return;
                case string s:
                {
                    if (s.Length > 0) ConsoleOutput.Add(s);
                    return;
                }
                case IEnumerable ie:
                {
                    foreach (object item in ie) ConsoleOutput.Add(item != null ? item.ToString() : string.Empty);
                    return;
                }
                default:
                    ConsoleOutput.Add(result.ToString());
                    break;
            }
        }

        private static IEnumerable<string> SplitSemicolons(string line) {
            bool inQuote = false;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                switch (c)
                {
                    case '"':
                        inQuote = !inQuote;
                        break;
                    case ';' when !inQuote:
                        yield return sb.ToString();
                        sb.Clear();
                        continue;
                }

                sb.Append(c);
            }

            yield return sb.ToString();
        }
    }
}