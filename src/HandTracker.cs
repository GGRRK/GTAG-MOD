using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GTagCameraMod
{
    // Locates Gorilla Tag's hand transforms via reflection (tries GorillaTagger.Instance.offlineVRRig
    // first — the path most current GT mods use — then falls back to GorillaLocomotion.Player.Instance).
    // Also manages two invisible "proxy" sphere colliders parented to the hands; those proxies are
    // what actually fire OnTriggerEnter on our menu buttons (the canonical pattern from the
    // 0xVidde/Gorilla-Tag-Menu-Library library).
    internal static class HandTracker
    {
        public enum BindingSource { None, GorillaTagger, GorillaLocomotion }

        public static Transform? LeftHandTransform  { get; private set; }
        public static Transform? RightHandTransform { get; private set; }
        public static BindingSource Source { get; private set; } = BindingSource.None;

        public static GameObject? LeftProxy  { get; private set; }
        public static GameObject? RightProxy { get; private set; }

        // 0,-0.05,0 sits the proxy ~5cm "down" in the hand's local frame — roughly the palm center.
        private static readonly Vector3 ProxyLocalOffset = new(0f, -0.05f, 0f);
        private const float ProxyRadius = 0.025f; // 2.5 cm — generous touch

        private static int _lastSearchFrame = -1;
        private const int SearchEveryFrames = 30; // ~0.5 s at 60 fps until bound

        // Reads BOTH the boolean and float grip features — some OpenXR runtimes only populate one.
        public static bool TryGetGrip(XRNode node, out bool gripDown)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);

            if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool b) && b)
            {
                gripDown = true;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.grip, out float v))
            {
                gripDown = v >= 0.5f;
                return true;
            }

            gripDown = false;
            return false;
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
            return $"src={Source}, " +
                   $"L={(IsAlive(LeftHandTransform) ? LeftHandTransform!.position.ToString("F2") : "null")}, " +
                   $"R={(IsAlive(RightHandTransform) ? RightHandTransform!.position.ToString("F2") : "null")}, " +
                   $"LProxy={(LeftProxy != null ? "ok" : "null")}, " +
                   $"RProxy={(RightProxy != null ? "ok" : "null")}";
        }

        // Called every frame from FovMenu. Finds GT hands lazily; once they exist, ensures the
        // proxy spheres are alive and parented correctly. Re-tries periodically until bound.
        public static void Tick()
        {
            EnsureHandsFound();
            EnsureProxies();
        }

        private static bool IsAlive(Transform? t) => t != null && t.gameObject != null;

        // ---- Hand lookup ----

        private static void EnsureHandsFound()
        {
            if (IsAlive(LeftHandTransform) && IsAlive(RightHandTransform)) return;
            if (_lastSearchFrame != -1 && Time.frameCount - _lastSearchFrame < SearchEveryFrames) return;
            _lastSearchFrame = Time.frameCount;

            // 1) Preferred path: GorillaTagger.Instance.offlineVRRig.{left,right}HandTransform
            if (TryBindViaGorillaTagger()) return;

            // 2) Fallback: GorillaLocomotion.Player.Instance.{left,right}HandTransform
            //    Prefer the *HandFollower variants (the visible follower) over the raw transforms.
            TryBindViaGorillaLocomotion();
        }

        private static bool TryBindViaGorillaTagger()
        {
            Type? taggerType = FindType("GorillaTagger");
            if (taggerType == null) return false;

            object? tagger = GetSingleton(taggerType);
            if (tagger == null) return false;

            // offlineVRRig is a MonoBehaviour holding the local player's hand transforms.
            object? rig = GetMemberValue(taggerType, tagger, new[] { "offlineVRRig", "OfflineVRRig" });
            if (rig == null) return false;

            var rigType = rig.GetType();
            var left  = GetMemberValue(rigType, rig, new[] { "leftHandTransform",  "leftHand",  "leftHandFollower"  }) as Transform;
            var right = GetMemberValue(rigType, rig, new[] { "rightHandTransform", "rightHand", "rightHandFollower" }) as Transform;

            if (left == null || right == null) return false;

            LeftHandTransform = left;
            RightHandTransform = right;
            Source = BindingSource.GorillaTagger;
            Plugin.Log.LogInfo($"HandTracker: bound via GorillaTagger.offlineVRRig ({rigType.FullName})");
            return true;
        }

        private static bool TryBindViaGorillaLocomotion()
        {
            Type? playerType = FindType("GorillaLocomotion.Player") ?? FindType("GorillaLocomotion.GTPlayer");
            if (playerType == null)
            {
                Plugin.Log.LogWarning("HandTracker: no GT player class found yet (game still loading?).");
                return false;
            }

            object? player = GetSingleton(playerType);
            if (player == null)
            {
                Plugin.Log.LogWarning($"HandTracker: {playerType.FullName}.Instance is null.");
                return false;
            }

            // Prefer the visible follower over the raw physics target
            var left = GetMemberValue(playerType, player, new[] { "leftHandFollower",  "leftHandTransform",  "leftControllerTransform"  }) as Transform;
            var right = GetMemberValue(playerType, player, new[] { "rightHandFollower", "rightHandTransform", "rightControllerTransform" }) as Transform;

            if (left == null || right == null)
            {
                Plugin.Log.LogWarning($"HandTracker: GorillaLocomotion path found type but hand fields missing (L={left != null}, R={right != null}).");
                return false;
            }

            LeftHandTransform = left;
            RightHandTransform = right;
            Source = BindingSource.GorillaLocomotion;
            Plugin.Log.LogInfo($"HandTracker: bound via {playerType.FullName}");
            return true;
        }

        // ---- Reflection helpers ----

        private static Type? FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* dynamic asm; ignore */ }
            }
            return null;
        }

        private static object? GetSingleton(Type t)
        {
            const BindingFlags f = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = t.GetProperty("Instance", f);
            if (prop != null) { var v = prop.GetValue(null); if (v != null) return v; }
            var field = t.GetField("Instance", f);
            if (field != null) { var v = field.GetValue(null); if (v != null) return v; }
            return null;
        }

        private static object? GetMemberValue(Type t, object instance, string[] names)
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var n in names)
            {
                var field = t.GetField(n, f);
                if (field != null) { var v = field.GetValue(instance); if (v != null) return v; }
                var prop = t.GetProperty(n, f);
                if (prop != null && prop.CanRead) { var v = prop.GetValue(instance); if (v != null) return v; }
            }
            return null;
        }

        // ---- Proxy management ----

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

            // Invisible — only the SphereCollider matters
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) UnityEngine.Object.Destroy(mr);

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = ProxyLocalOffset;
            // Primitive Sphere mesh has radius 0.5 in local; localScale = ProxyRadius * 2 gives that radius in world.
            go.transform.localScale = Vector3.one * (ProxyRadius * 2f);

            return go;
        }
    }
}
