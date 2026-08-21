#region

using System;

#endregion

namespace FailCake.Console
{
    [Flags]
    public enum FCVAR : uint
    {
        NONE = 0,
        HIDDEN = 1u << 4,
        PROTECTED = 1u << 5,
        ARCHIVE = 1u << 7,
        REPLICATED = 1u << 13,
        CHEAT = 1u << 14,
        ADMIN = 1u << 26,
        SERVER_CAN_EXECUTE = 1u << 28
    }
}