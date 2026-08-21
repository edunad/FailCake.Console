namespace FailCake.Console
{
    public sealed class ConsoleStubEntry : ConsoleEntry
    {
        internal ConsoleStubEntry(string name, string help, FCVAR flags)
            : base(name, help, flags) { }
    }
}