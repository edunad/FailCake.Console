#region

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

#endregion

namespace FailCake.Console
{
    public static class ConsoleRegistry
    {
        #region STATIC

        private static readonly Dictionary<string, ConsoleEntry> ENTRIES = new Dictionary<string, ConsoleEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> REMOTE_STUBS = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<KeyValuePair<string, MethodInfo>> PENDING_CALLBACKS = new List<KeyValuePair<string, MethodInfo>>();
        private static bool _scanned;

        #endregion

        public static void Scan() {
            if (ConsoleRegistry._scanned) return;
            ConsoleRegistry._scanned = true;

            #if UNITY_EDITOR
            ConsoleRegistry.ScanTypeCache();
            #else
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) ConRegistry.ScanAssembly(asm);
            #endif

            ConsoleRegistry.BindPendingCallbacks();
            ConsolePlugins.LoadPending();
        }

        public static void ScanAssembly(Assembly asm) {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                return;
            }

            foreach (Type t in types) ConsoleRegistry.ScanType(t);
        }

        public static ConsoleEntry Find(string name) {
            if (string.IsNullOrEmpty(name)) return null;
            ConsoleRegistry.ENTRIES.TryGetValue(name, out ConsoleEntry entry);
            return entry;
        }

        public static ConsoleVar FindVar(string name) {
            return ConsoleRegistry.Find(name) as ConsoleVar;
        }

        public static ConsoleCommand FindCommand(string name) {
            return ConsoleRegistry.Find(name) as ConsoleCommand;
        }

        public static IEnumerable<ConsoleEntry> GetAll() {
            return ConsoleRegistry.ENTRIES.Values;
        }

        public static void AddRemoteStub(string name, string help, FCVAR flags) {
            if (string.IsNullOrEmpty(name) || ConsoleRegistry.ENTRIES.ContainsKey(name)) return;
            ConsoleRegistry.REMOTE_STUBS.Add(name);
            ConsoleRegistry.ENTRIES[name] = new ConsoleStubEntry(name, help, flags);
        }

        public static void ClearRemoteStubs() {
            foreach (string name in ConsoleRegistry.REMOTE_STUBS) ConsoleRegistry.ENTRIES.Remove(name);
            ConsoleRegistry.REMOTE_STUBS.Clear();
        }

        #region PRIVATE METHODS

        #if UNITY_EDITOR
        private static void ScanTypeCache() {
            foreach (MethodInfo m in TypeCache.GetMethodsWithAttribute<CommandAttribute>())
            {
                CommandAttribute attr = m.GetCustomAttribute<CommandAttribute>();
                if (attr == null) continue;
                ConsoleRegistry.RegisterCommand(attr, m);
            }

            foreach (FieldInfo f in TypeCache.GetFieldsWithAttribute<CVarAttribute>())
            {
                CVarAttribute attr = f.GetCustomAttribute<CVarAttribute>();
                if (attr == null) continue;
                ConsoleRegistry.RegisterCVar(attr, f);
            }

            foreach (MethodInfo m in TypeCache.GetMethodsWithAttribute<OnCVarChangedAttribute>())
            {
                OnCVarChangedAttribute[] attrs = (OnCVarChangedAttribute[])m.GetCustomAttributes(typeof(OnCVarChangedAttribute), false);
                foreach (OnCVarChangedAttribute attr in attrs) ConsoleRegistry.PENDING_CALLBACKS.Add(new KeyValuePair<string, MethodInfo>(attr.name, m));
            }

            foreach (Type t in TypeCache.GetTypesDerivedFrom<ConsolePlugin>()) ConsoleRegistry.RegisterPlugin(t);
        }
        #endif

        private static void ScanType(Type t) {
            ConsoleRegistry.ScanCommands(t);
            ConsoleRegistry.ScanCVars(t);
            ConsoleRegistry.ScanChangeCallbacks(t);
            ConsoleRegistry.RegisterPlugin(t);
        }

        private static void RegisterPlugin(Type t) {
            if (t == null || t.IsAbstract || !typeof(ConsolePlugin).IsAssignableFrom(t)) return;

            ConsolePluginAttribute attr = t.GetCustomAttribute<ConsolePluginAttribute>();
            if (attr == null) return;

            ConsolePlugins.Add(t, attr.priority);
        }

        private static void ScanCommands(Type t) {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                CommandAttribute attr = m.GetCustomAttribute<CommandAttribute>();
                if (attr == null) continue;
                ConsoleRegistry.RegisterCommand(attr, m);
            }
        }

        private static void ScanCVars(Type t) {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                CVarAttribute attr = f.GetCustomAttribute<CVarAttribute>();
                if (attr == null) continue;
                ConsoleRegistry.RegisterCVar(attr, f);
            }
        }

        private static void ScanChangeCallbacks(Type t) {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                OnCVarChangedAttribute[] attrs = (OnCVarChangedAttribute[])m.GetCustomAttributes(typeof(OnCVarChangedAttribute), false);
                if (attrs.Length == 0) continue;
                foreach (OnCVarChangedAttribute attr in attrs) ConsoleRegistry.PENDING_CALLBACKS.Add(new KeyValuePair<string, MethodInfo>(attr.name, m));
            }
        }

        private static void BindPendingCallbacks() {
            foreach (KeyValuePair<string, MethodInfo> kvp in ConsoleRegistry.PENDING_CALLBACKS)
            {
                ConsoleVar cv = ConsoleRegistry.FindVar(kvp.Key);
                if (cv == null) throw new UnityException($"[FailCake.Console] OnCVarChanged target \"{kvp.Key}\" not found");

                Delegate d = Delegate.CreateDelegate(typeof(Action<ConsoleVar, string>), kvp.Value, false);
                if (d != null) cv.Changed += (Action<ConsoleVar, string>)d;
            }

            ConsoleRegistry.PENDING_CALLBACKS.Clear();
        }

        private static void RegisterCommand(CommandAttribute attr, MethodInfo m) {
            ConsoleRegistry.ValidateName(attr.name);
            if (ConsoleRegistry.ENTRIES.ContainsKey(attr.name)) throw new UnityException($"[FailCake.Console] Duplicate command registration: \"{attr.name}\"");
            if (!m.IsStatic) throw new UnityException($"[FailCake.Console] Command \"{attr.name}\" is not static");
            if (m.IsGenericMethod) throw new UnityException($"[FailCake.Console] Command \"{attr.name}\" must not be generic");
            if (ConsoleRegistry.HasInvalidParams(m)) throw new UnityException($"[FailCake.Console] Command \"{attr.name}\" has invalid parameters");

            ConsoleRegistry.ENTRIES[attr.name] = new ConsoleCommand(attr.name, attr.help, attr.flags, m);
        }

        private static void RegisterCVar(CVarAttribute attr, FieldInfo field) {
            ConsoleRegistry.ValidateName(attr.name);
            if (ConsoleRegistry.ENTRIES.ContainsKey(attr.name)) throw new UnityException($"[FailCake.Console] Duplicate cvar registration: \"{attr.name}\"");

            ConsoleVar cv = new ConsoleVar(attr.name, attr.help, attr.flags, attr.defaultValue, attr.hasMin, attr.min, attr.hasMax, attr.max, field);
            ConsoleRegistry.ENTRIES[attr.name] = cv;
            if (field != null) cv.SetValue(attr.defaultValue);
        }

        private static bool HasInvalidParams(MethodInfo m) {
            ParameterInfo[] ps = m.GetParameters();
            bool hasCmdType = false;
            foreach (ParameterInfo p in ps)
            {
                if (p.IsOut || p.ParameterType.IsByRef) return true;
                if (p.ParameterType == typeof(CCommand))
                {
                    hasCmdType = true;
                    continue;
                }

                if (!ConsoleParser.CanParse(p.ParameterType)) return true;
            }

            return hasCmdType && ps.Length != 1;
        }

        private static void ValidateName(string name) {
            if (string.IsNullOrEmpty(name)) throw new UnityException("[FailCake.Console] Command/cvar name cannot be empty");

            foreach (char c in name)
                if (char.IsWhiteSpace(c) || c == '"' || c == ';' || c == '(' || c == ')' ||
                    c == '{' || c == '}' || c == '[' || c == ']' || c == '<' || c == '>')
                    throw new UnityException($"[FailCake.Console] Invalid char '{c}' in name \"{name}\"");
        }

        #endregion
    }
}