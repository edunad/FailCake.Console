#region

using System;

#endregion

namespace FailCake.Console
{
    public enum ConSource
    {
        Local,
        ServerConsole,
        Remote
    }

    public readonly struct ConsoleContext
    {
        public readonly ConSource source;
        public readonly bool echo;
        public readonly object userData;

        public ConsoleContext(ConSource source, bool echo, object userData = null) {
            this.source = source;
            this.echo = echo;
            this.userData = userData;
        }

        public static ConsoleContext Local(object userData = null) {
            return new ConsoleContext(ConSource.Local, true, userData);
        }

        public static ConsoleContext ServerConsole() {
            return new ConsoleContext(ConSource.ServerConsole, false);
        }

        public static ConsoleContext Remote(object userData) {
            if (userData == null) throw new ArgumentNullException(nameof(userData));
            return new ConsoleContext(ConSource.Remote, false, userData);
        }

        public static ConsoleContext Config(bool onServer) {
            return new ConsoleContext(onServer ? ConSource.ServerConsole : ConSource.Local, false);
        }

        public static ConsoleContext ClientLocal(object userData = null) {
            return new ConsoleContext(ConSource.Local, true, userData);
        }

        public static ConsoleContext ServerLocal(object userData = null) {
            return new ConsoleContext(ConSource.ServerConsole, false, userData);
        }
    }
}
