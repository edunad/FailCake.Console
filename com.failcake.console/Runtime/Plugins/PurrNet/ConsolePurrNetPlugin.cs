#region

using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.Console.Plugins
{
    [ConsolePlugin]
    public sealed class ConsolePurrNetPlugin : ConsolePlugin
    {
        protected override void OnLoad() {
            // SETUP -----
            Console.IsMultiplayer = () => NetworkManager.main && (NetworkManager.main.isServer || NetworkManager.main.isClient);
            Console.OnSVCommand = (command, userData) => {
                if (!Console.IsMultiplayer()) return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ServerLocal(userData)));
                if (!ConsolePurrNetBridge.BRIDGE || !ConsolePurrNetBridge.BRIDGE.isSpawned) return (false, "Not connected to server.");

                ConsolePurrNetBridge.BRIDGE.ExecConServer(command.GetCommandString());
                return (true, null);
            };

            Console.OnCLCommand = (command, userData) => (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ClientLocal(userData)));
            // ----------------

            // EVENTS --------
            NetworkManager.onAnyServerConnectionState += ConsolePurrNetPlugin.OnServerConnectionState;
            // ----------------
        }

        protected override void OnUnload() {
            // EVENTS --------
            NetworkManager.onAnyServerConnectionState -= ConsolePurrNetPlugin.OnServerConnectionState;
            // ----------------
        }

        #region PRIVATE

        private static void OnServerConnectionState(ConnectionState state) {
            if (state != ConnectionState.Connected || ConsolePurrNetBridge.BRIDGE) return;

            NetworkManager manager = NetworkManager.main;
            if (!manager) return;

            GameObject go = new GameObject("Console.PurrNetBridge");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ConsolePurrNetBridge>();

            manager.Spawn(go);
        }

        #endregion
    }
}