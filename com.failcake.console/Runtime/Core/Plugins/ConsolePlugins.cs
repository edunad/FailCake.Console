#region

using System;
using System.Collections.Generic;

#endregion

namespace FailCake.Console
{
    public abstract class ConsolePlugin
    {
        public string name;
        public int priority;

        public void Load() {
            this.name = this.GetType().Name;
            this.OnLoad();
        }

        protected abstract void OnLoad();
        protected virtual void OnUnload() { }
    }


    public static class ConsolePlugins
    {
        #region STATIC

        private static readonly List<ConsolePlugin> LOADED = new List<ConsolePlugin>();
        private static readonly List<KeyValuePair<Type, int>> PENDING = new List<KeyValuePair<Type, int>>();
        private static readonly HashSet<Type> SEEN = new HashSet<Type>();

        #endregion

        public static IReadOnlyList<ConsolePlugin> GetAll() {
            return ConsolePlugins.LOADED;
        }

        public static T Get<T>() where T : ConsolePlugin {
            foreach (ConsolePlugin plugin in ConsolePlugins.LOADED)
                if (plugin is T typed)
                    return typed;

            return null;
        }

        internal static void Add(Type type, int priority) {
            if (type == null || type.IsAbstract || !typeof(ConsolePlugin).IsAssignableFrom(type)) return;
            if (!ConsolePlugins.SEEN.Add(type)) return;

            ConsolePlugins.PENDING.Add(new KeyValuePair<Type, int>(type, priority));
        }

        internal static void LoadPending() {
            if (ConsolePlugins.PENDING.Count == 0) return;

            ConsolePlugins.PENDING.Sort(ConsolePlugins.ComparePending);
            foreach (KeyValuePair<Type, int> kvp in ConsolePlugins.PENDING)
            {
                ConsolePlugin plugin = (ConsolePlugin)Activator.CreateInstance(kvp.Key, true);
                plugin.priority = kvp.Value;
                plugin.Load();

                ConsolePlugins.LOADED.Add(plugin);
            }

            ConsolePlugins.PENDING.Clear();
        }

        private static int ComparePending(KeyValuePair<Type, int> a, KeyValuePair<Type, int> b) {
            return a.Value.CompareTo(b.Value);
        }
    }
}