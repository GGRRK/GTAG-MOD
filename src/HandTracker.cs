using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GTagCameraMod
{
    // Locates the player's hand transforms via reflection into Gorilla Tag's
    // GorillaLocomotion.Player singleton (no compile-time game DLL reference).
    //
    // Confirmed via the canonical Another-Axiom/GorillaLocomotion repo:
    //   namespace GorillaLocomotion
    //   class Player { public static Player Instance { get; } ; public Transform leftHandTransform; public Transform rightHandTransform; ... }
    //
    // Falls back to Unity XR InputDevices.devicePosition if the GT player class
    // can't be found (likely if the mod loads before GT initializes).
    internal static class HandTracker
    {
        private static Transform? _leftHand;
        private static Transform? _rightHand;
        private static Transform? _xrOrigin;
        private static int _lastSearchFrame = -1;
        private const int SearchEveryFrames = 60; // re-scan once a second if not found

        public static bool TryGetLeftHandPos(out Vector3 pos)  => TryGetHandPos(XRNode.LeftHand,  ref _leftHand,  out pos);
        public static bool TryGetRightHandPos(out Vector3 pos) => TryGetHandPos(XRNode.RightHand, ref _rightHand, out pos);

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

        public static string DescribeState()
        {
            return $"left={(IsAlive(_leftHand) ? _leftHand!.position.ToString("F2") : "null")}, " +
                   $"right={(IsAlive(_rightHand) ? _rightHand!.position.ToString("F2") : "null")}, " +
                   $"xrOrigin={(IsAlive(_xrOrigin) ? "set" : "null")}";
        }

        private static bool IsAlive(Transform? t) => t != null && t.gameObject != null;

        private static bool TryGetHandPos(XRNode node, ref Transform? cache, out Vector3 pos)
        {
            EnsureSearched();

            if (IsAlive(cache))
            {
                pos = cache!.position;
                return true;
            }

            // Fall back to Unity XR raw position. Only useful if we have an XR origin to convert.
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPos))
            {
                if (IsAlive(_xrOrigin))
                {
                    pos = _xrOrigin!.TransformPoint(localPos);
                    return true;
                }
                // No origin transform — return raw (likely wrong in world space but better than nothing)
                pos = localPos;
                return false;
            }

            pos = default;
            return false;
        }

        private static void EnsureSearched()
        {
            // Re-search periodically until we have both hands. Once we have them, cache permanently.
            if (IsAlive(_leftHand) && IsAlive(_rightHand)) return;
            if (_lastSearchFrame != -1 && Time.frameCount - _lastSearchFrame < SearchEveryFrames) return;
            _lastSearchFrame = Time.frameCount;

            FindGTHands();
            FindXROrigin();
        }

        private static void FindGTHands()
        {
            // Resolve GorillaLocomotion.Player without a compile-time reference.
            // Search loaded assemblies because GT's Assembly-CSharp may not be on the
            // standard CLR Type.GetType path until after the game initializes.
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
                catch { /* asm may be dynamic, ignore */ }
            }

            if (playerType == null)
            {
                Plugin.Log.LogWarning("HandTracker: GorillaLocomotion.Player not found yet (game still loading?)");
                return;
            }

            object? instance = null;
            var instanceProp = playerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (instanceProp != null) instance = instanceProp.GetValue(null);
            if (instance == null)
            {
                var instanceField = playerType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (instanceField != null) instance = instanceField.GetValue(null);
            }
            if (instance == null)
            {
                Plugin.Log.LogWarning("HandTracker: GorillaLocomotion.Player.Instance is null (game still loading?)");
                return;
            }

            _leftHand  = FindTransformMember(playerType, instance, new[] { "leftHandTransform",  "leftHandFollower",  "leftControllerTransform"  });
            _rightHand = FindTransformMember(playerType, instance, new[] { "rightHandTransform", "rightHandFollower", "rightControllerTransform" });

            Plugin.Log.LogInfo($"HandTracker: GT player bound. left={(IsAlive(_leftHand) ? "ok" : "missing")}, right={(IsAlive(_rightHand) ? "ok" : "missing")}");
        }

        private static Transform? FindTransformMember(Type t, object instance, string[] names)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var n in names)
            {
                var f = t.GetField(n, flags);
                if (f != null && f.GetValue(instance) is Transform tf) return tf;
                var p = t.GetProperty(n, flags);
                if (p != null && p.CanRead && p.GetValue(instance) is Transform tp) return tp;
            }
            return null;
        }

        private static void FindXROrigin()
        {
            if (IsAlive(_xrOrigin)) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Best guess: rig is the parent of the head camera
            if (cam.transform.parent != null) _xrOrigin = cam.transform.parent;
        }
    }
}
