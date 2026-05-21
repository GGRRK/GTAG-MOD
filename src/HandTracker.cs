using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GTagCameraMod
{
    // Locates Gorilla Tag's GorillaLocomotion.Player singleton via reflection and
    // exposes its leftHandTransform / rightHandTransform. Also manages two
    // invisible "proxy" sphere colliders parented to the hands — these are what
    // actually fire OnTriggerEnter on our menu buttons (the same pattern used by
    // mature open-source GT menu libraries).
    internal static class HandTracker
    {
        public static Transform? LeftHandTransform  { get; private set; }
        public static Transform? RightHandTransform { get; private set; }

        // Public so trigger handlers can filter "is this collider our proxy?"
        public static GameObject? LeftProxy  { get; private set; }
        public static GameObject? RightProxy { get; private set; }

        // Local offset from the GT hand transform to the proxy sphere center.
        // 0, -0.05, 0 places the proxy ~5cm "down" relative to the hand's local
        // orientation, roughly where the gorilla palm sits.
        private static readonly Vector3 ProxyLocalOffset = new(0f, -0.05f, 0f);
        private const float ProxyRadius = 0.025f; // 2.5cm sphere — generous touch

        private static int _lastSearchFrame = -1;
        private const int SearchEveryFrames = 30; // ~once every 0.5s at 60fps

        public static bool TryGetGrip(XRNode node, out bool gripDown)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.TryGetFeatureValue(CommonUsages.gripButton, out gripDown);
        }

        public static bool TryGetPrimary(XRNode node, out bool pressed)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
        }

        public static XRNode? IdentifyProxy(GameObject go)
        {
            if (go == null) return null;
            if (go == LeftProxy)  return XRNode.LeftHand;
            if (go == RightProxy) return XRNode.RightHand;
            return null;
        }

        public static string DescribeState()
        {
            return $"L={(IsAlive(LeftHandTransform) ? LeftHandTransform!.position.ToString("F2") : "null")}, " +
                   $"R={(IsAlive(RightHandTransform) ? RightHandTransform!.position.ToString("F2") : "null")}, " +
                   $"LProxy={(LeftProxy != null ? "ok" : "null")}, " +
                   $"RProxy={(RightProxy != null ? "ok" : "null")}";
        }

        // Called every frame from the plugin. Locates GT hands if not yet found,
        // and ensures proxy sphere colliders exist parented to them.
        public static void Tick()
        {
            EnsureHandsFound();
            EnsureProxies();
        }

        private static bool IsAlive(Transform? t) => t != null && t.gameObject != null;
        private static bool IsAlive(GameObject? g) => g != null;

        // ---- GT player lookup ----

        private static void EnsureHandsFound()
        {
            if (IsAlive(LeftHandTransform) && IsAlive(RightHandTransform)) return;
            if (_lastSearchFrame != -1 && Time.frameCount - _lastSearchFrame < SearchEveryFrames) return;
            _lastSearchFrame = Time.frameCount;

            Type? playerType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    playerType = asm.GetType("GorillaLocomotion.Player", throwOnError: false);
                    if (playerType != null) break;
                    playerType = asm.GetType("GorillaLocomotion.GTPlayer", throwOnError: false);
                    if (playerType != null) break;
                }
                catch { /* dynamic asm; ignore */ }
            }

            if (playerType == null)
            {
                Plugin.Log.LogWarning("HandTracker: GT player class not loaded yet.");
                return;
            }

            object? instance = null;
            var ip = playerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (ip != null) instance = ip.GetValue(null);
            if (instance == null)
            {
                var iff = playerType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (iff != null) instance = iff.GetValue(null);
            }
            if (instance == null)
            {
                Plugin.Log.LogWarning("HandTracker: GT Player.Instance still null.");
                return;
            }

            LeftHandTransform  = FindMember(playerType, instance, new[] { "leftHandTransform",  "leftHandFollower",  "leftControllerTransform" });
            RightHandTransform = FindMember(playerType, instance, new[] { "rightHandTransform", "rightHandFollower", "rightControllerTransform" });

            Plugin.Log.LogInfo($"HandTracker: bound to {playerType.FullName}. left={(IsAlive(LeftHandTransform) ? "ok" : "missing")}, right={(IsAlive(RightHandTransform) ? "ok" : "missing")}");
        }

        private static Transform? FindMember(Type t, object instance, string[] names)
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var n in names)
            {
                var fi = t.GetField(n, f);
                if (fi != null && fi.GetValue(instance) is Transform tf) return tf;
                var pi = t.GetProperty(n, f);
                if (pi != null && pi.CanRead && pi.GetValue(instance) is Transform tp) return tp;
            }
            return null;
        }

        // ---- Proxy sphere management ----

        private static void EnsureProxies()
        {
            if (LeftProxy == null && IsAlive(LeftHandTransform))
            {
                LeftProxy = CreateProxy("GTagCameraMod_LeftProxy", LeftHandTransform!);
                Plugin.Log.LogInfo("HandTracker: left proxy sphere spawned.");
            }
            if (RightProxy == null && IsAlive(RightHandTransform))
            {
                RightProxy = CreateProxy("GTagCameraMod_RightProxy", RightHandTransform!);
                Plugin.Log.LogInfo("HandTracker: right proxy sphere spawned.");
            }
        }

        private static GameObject CreateProxy(string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;

            // Invisible — we only want the collider, not the mesh
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) UnityEngine.Object.Destroy(mr);

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = ProxyLocalOffset;
            // ProxyRadius is the desired world radius; primitive Sphere has radius 0.5 in local mesh,
            // so localScale = ProxyRadius * 2 gives the right size.
            go.transform.localScale = Vector3.one * (ProxyRadius * 2f);

            return go;
        }
    }
}
