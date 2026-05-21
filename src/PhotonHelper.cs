using System;
using System.Reflection;

namespace GTagCameraMod
{
    // Talks to Gorilla Tag's Photon networking layer via reflection, so we don't
    // need to ship a compile-time reference to PhotonUnityNetworking.dll.
    internal static class PhotonHelper
    {
        private static Type? _photonNetworkType;
        private static bool _searched;

        private static Type? PhotonNetworkType
        {
            get
            {
                if (_searched) return _photonNetworkType;
                _searched = true;

                // PUN 2 ships PhotonNetwork in the Photon.Pun namespace
                _photonNetworkType =
                    Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking", throwOnError: false)
                    ?? Type.GetType("Photon.Pun.PhotonNetwork", throwOnError: false);

                if (_photonNetworkType == null)
                {
                    Plugin.Log.LogWarning(
                        "PhotonHelper: could not locate Photon.Pun.PhotonNetwork. " +
                        "Disconnect button will not work in this build of Gorilla Tag.");
                }
                else
                {
                    Plugin.Log.LogInfo($"PhotonHelper: bound to {_photonNetworkType.AssemblyQualifiedName}");
                }
                return _photonNetworkType;
            }
        }

        public static bool InRoom
        {
            get
            {
                var t = PhotonNetworkType;
                if (t == null) return false;
                var prop = t.GetProperty("InRoom", BindingFlags.Public | BindingFlags.Static);
                if (prop == null) return false;
                var value = prop.GetValue(null);
                return value is bool b && b;
            }
        }

        // Leaves the current Photon room. Returns true if the call was dispatched
        // (the actual disconnect happens asynchronously inside Photon).
        public static bool LeaveRoom()
        {
            var t = PhotonNetworkType;
            if (t == null) return false;

            // Prefer the no-arg overload: PhotonNetwork.LeaveRoom()
            var method = t.GetMethod(
                "LeaveRoom",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            // Fallback: PhotonNetwork.LeaveRoom(bool becomeInactive)
            object?[]? args = null;
            if (method == null)
            {
                method = t.GetMethod(
                    "LeaveRoom",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(bool) },
                    modifiers: null);
                args = new object?[] { false };
            }

            if (method == null)
            {
                Plugin.Log.LogWarning("PhotonHelper: no LeaveRoom() method found.");
                return false;
            }

            try
            {
                var result = method.Invoke(null, args);
                Plugin.Log.LogInfo($"PhotonNetwork.LeaveRoom() => {result}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"PhotonNetwork.LeaveRoom() threw: {e.GetBaseException().Message}");
                return false;
            }
        }
    }
}
