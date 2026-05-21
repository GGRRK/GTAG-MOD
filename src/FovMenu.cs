using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

namespace GTagCameraMod
{
    // Tablet-style menu built from primitives + TextMesh.
    //
    // Interaction is polling-based (NOT physics triggers):
    //   - HandTracker locates GT's left/right hand transforms via reflection.
    //   - Each frame we test whether either hand is inside each button's local AABB.
    //   - Edge-detection (was outside last frame, now inside) fires the button.
    //   - Handles use the same AABB test + a grip-button hold to drag.
    //
    // Why polling: GT puts player hands on a non-default physics setup and the
    // Unity OnTriggerEnter callback does not reliably fire against them. Distance
    // / bounds tests against the actual hand Transform are how mature GT mods do it.
    public class FovMenu : MonoBehaviour
    {
        // === FOV ===
        private const float FovStep = 5f;
        private const float FovMin = 30f;
        private const float FovMax = 170f;

        // === Panel dimensions (world meters; tablet aspect) ===
        private const float PanelWidth = 0.42f;
        private const float PanelHeight = 0.22f;
        private const float HandleSize = 0.05f;

        // === Interaction ===
        // How far perpendicular to a button (in meters) still counts as "touching."
        private const float TouchDepthM = 0.04f;
        // Cooldown per-button so a held hand doesn't spam presses (only one press per entry now,
        // but this also throttles in case of jitter).
        private const float TouchCooldown = 0.20f;

        // === First-time placement (offset from head when menu first becomes visible) ===
        private static readonly Vector3 InitialOffsetFromHead = new(0f, -0.15f, 0.55f);

        // === Palette ===
        private static readonly Color WoodFrame   = new(0.36f, 0.22f, 0.10f, 1f);
        private static readonly Color CreamCard   = new(0.94f, 0.90f, 0.78f, 1f);
        private static readonly Color DarkBrown   = new(0.18f, 0.10f, 0.04f, 1f);
        private static readonly Color RedAccent   = new(0.78f, 0.22f, 0.20f, 1f);
        private static readonly Color GreenAccent = new(0.20f, 0.58f, 0.25f, 1f);
        private static readonly Color OrangeCard  = new(0.88f, 0.55f, 0.10f, 1f);
        private static readonly Color HandlePink  = new(1.00f, 0.40f, 0.85f, 1f);

        // === Touch system ===
        private class TouchTarget
        {
            public Transform Transform = null!;
            public System.Action OnPressed = null!;
            // Local-space half-extents for the XY face of the button (in unscaled quad units)
            public Vector2 LocalHalfXY;
            public string Name = "";
            public bool LeftWasInside;
            public bool RightWasInside;
            public float LastFireTime;
        }

        private readonly List<TouchTarget> _buttons = new();
        private readonly List<Transform> _handles = new();

        // === State ===
        private Camera? _head;
        private TextMesh? _fovText;
        private GameObject? _visualsRoot;
        private bool _visible = true;
        private bool _hasBeenPositioned;
        private bool _aWasDown;

        private bool _isGrabbing;
        private XRNode _grabbingNode;
        private Vector3 _grabOffset;

        private static FovMenu? _instance;

        private void Awake() => _instance = this;
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Start() => BuildMenu();

        private void BuildMenu()
        {
            _visualsRoot = new GameObject("Visuals");
            _visualsRoot.transform.SetParent(transform, worldPositionStays: false);
            _visualsRoot.transform.localPosition = Vector3.zero;
            _visualsRoot.transform.localRotation = Quaternion.identity;

            // --- Wood frame ---
            MakeQuad("Frame",
                localPos: Vector3.zero,
                size: new Vector2(PanelWidth, PanelHeight),
                color: WoodFrame,
                parent: _visualsRoot.transform);

            // --- Title strip ---
            MakeText("Title", "GTAG CAMERA MOD",
                localPos: new Vector3(0f, PanelHeight * 0.42f, -0.002f),
                charSize: 0.0042f,
                color: CreamCard,
                parent: _visualsRoot.transform);

            // --- FOV card (left half) ---
            Vector3 fovCardPos = new(-PanelWidth * 0.24f, -0.015f, -0.002f);
            Vector2 fovCardSize = new(PanelWidth * 0.40f, PanelHeight * 0.62f);
            MakeQuad("FovCard", fovCardPos, fovCardSize, CreamCard, _visualsRoot.transform);

            MakeText("FovLabel", "FOV",
                localPos: fovCardPos + new Vector3(0f, fovCardSize.y * 0.32f, -0.003f),
                charSize: 0.0050f,
                color: DarkBrown,
                parent: _visualsRoot.transform);

            _fovText = MakeText("FovText", "90",
                localPos: fovCardPos + new Vector3(0f, fovCardSize.y * 0.04f, -0.003f),
                charSize: 0.0140f,
                color: DarkBrown,
                parent: _visualsRoot.transform);

            // Bigger FOV buttons (was 0.038 — too small for VR hand)
            float fovBtnSize = 0.055f;
            MakeButton("MinusButton", "-",
                localPos: fovCardPos + new Vector3(-fovCardSize.x * 0.34f, -fovCardSize.y * 0.30f, -0.004f),
                size: new Vector2(fovBtnSize, fovBtnSize),
                color: RedAccent,
                labelCharSize: 0.030f,
                labelColor: Color.white,
                onTouched: () => AdjustFov(-FovStep));

            MakeButton("PlusButton", "+",
                localPos: fovCardPos + new Vector3(+fovCardSize.x * 0.34f, -fovCardSize.y * 0.30f, -0.004f),
                size: new Vector2(fovBtnSize, fovBtnSize),
                color: GreenAccent,
                labelCharSize: 0.030f,
                labelColor: Color.white,
                onTouched: () => AdjustFov(+FovStep));

            // --- Disconnect card (right half) — whole card is the button ---
            Vector3 discCardPos = new(+PanelWidth * 0.24f, -0.015f, -0.002f);
            Vector2 discCardSize = new(PanelWidth * 0.40f, PanelHeight * 0.62f);

            MakeButton("DisconnectButton", "DISCONNECT\nLOBBY",
                localPos: discCardPos,
                size: discCardSize,
                color: OrangeCard,
                labelCharSize: 0.009f,
                labelColor: Color.white,
                onTouched: DisconnectLobby);

            // --- Side grab handles ---
            float handleX = PanelWidth / 2f + HandleSize / 2f + 0.008f;
            _handles.Add(MakeHandle("LeftHandle",  new Vector3(-handleX, 0f, 0f)).transform);
            _handles.Add(MakeHandle("RightHandle", new Vector3(+handleX, 0f, 0f)).transform);
        }

        // --- Construction helpers ---

        private GameObject MakeQuad(string name, Vector3 localPos, Vector2 size,
            Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var mc = go.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc); // no physics colliders needed; we poll positions
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.color = color;
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        private static TextMesh MakeText(string name, string text, Vector3 localPos,
            float charSize, Color color, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.color = color;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            return tm;
        }

        private void MakeButton(string name, string label, Vector3 localPos, Vector2 size,
            Color color, float labelCharSize, Color labelColor, System.Action onTouched)
        {
            var btn = MakeQuad(name, localPos, size, color, _visualsRoot!.transform);

            // Label coplanar with the button face (small offset to avoid z-fighting)
            var labelGo = new GameObject(name + "_Label");
            labelGo.transform.SetParent(btn.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.005f);
            labelGo.transform.localRotation = Quaternion.identity;
            labelGo.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.color = labelColor;
            tm.characterSize = labelCharSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;

            _buttons.Add(new TouchTarget
            {
                Transform = btn.transform,
                OnPressed = onTouched,
                LocalHalfXY = new Vector2(0.5f, 0.5f), // full quad bounds (-0.5 to +0.5 in local)
                Name = name,
            });
        }

        private GameObject MakeHandle(string name, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var auto = go.GetComponent<BoxCollider>();
            if (auto != null) Destroy(auto); // no physics; we poll
            go.transform.SetParent(_visualsRoot!.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * HandleSize;
            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.color = HandlePink;
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        // --- Main loop ---

        private void Update()
        {
            if (_head == null)
            {
                _head = Camera.main;
                if (_head == null) return;
                PlaceAtInitialPos();
                RefreshFovText();
            }

            HandleAButton();
            HandleKeyboardToggle();
            HandleDiagnosticDump();

            if (!_visible) return;

            HandTracker.TryGetLeftHandPos(out Vector3 leftPos);
            HandTracker.TryGetRightHandPos(out Vector3 rightPos);

            ProcessButtonTouches(leftPos, rightPos);
            ProcessHandleGrab(leftPos, rightPos);
        }

        private void ProcessButtonTouches(Vector3 leftPos, Vector3 rightPos)
        {
            foreach (var tt in _buttons)
            {
                bool leftInside  = IsInsideButton(tt, leftPos);
                bool rightInside = IsInsideButton(tt, rightPos);

                // Edge-detect: fire only on transition outside → inside, with cooldown
                if ((leftInside && !tt.LeftWasInside) || (rightInside && !tt.RightWasInside))
                {
                    if (Time.time - tt.LastFireTime >= TouchCooldown)
                    {
                        tt.LastFireTime = Time.time;
                        Plugin.Log.LogInfo($"Touch: {tt.Name} pressed (left={leftInside}, right={rightInside})");
                        tt.OnPressed();
                    }
                }
                tt.LeftWasInside = leftInside;
                tt.RightWasInside = rightInside;
            }
        }

        private static bool IsInsideButton(TouchTarget tt, Vector3 worldPos)
        {
            if (float.IsNaN(worldPos.x)) return false;
            var local = tt.Transform.InverseTransformPoint(worldPos);
            return Mathf.Abs(local.x) <= tt.LocalHalfXY.x
                && Mathf.Abs(local.y) <= tt.LocalHalfXY.y
                && Mathf.Abs(local.z) <= TouchDepthM;
        }

        private void ProcessHandleGrab(Vector3 leftPos, Vector3 rightPos)
        {
            if (_isGrabbing)
            {
                if (!HandTracker.TryGetGrip(_grabbingNode, out bool gripDown) || !gripDown)
                {
                    _isGrabbing = false;
                    Plugin.Log.LogInfo("Menu released.");
                    return;
                }
                Vector3 handPos = _grabbingNode == XRNode.LeftHand ? leftPos : rightPos;
                if (!float.IsNaN(handPos.x))
                {
                    transform.position = handPos + _grabOffset;
                }
                return;
            }

            // Not grabbing — look for a new grab. Need grip held + hand inside a handle.
            HandTracker.TryGetGrip(XRNode.LeftHand,  out bool leftGrip);
            HandTracker.TryGetGrip(XRNode.RightHand, out bool rightGrip);

            foreach (var h in _handles)
            {
                if (leftGrip && IsInsideHandle(h, leftPos))
                {
                    StartGrab(XRNode.LeftHand, leftPos);
                    return;
                }
                if (rightGrip && IsInsideHandle(h, rightPos))
                {
                    StartGrab(XRNode.RightHand, rightPos);
                    return;
                }
            }
        }

        private static bool IsInsideHandle(Transform handle, Vector3 worldPos)
        {
            if (float.IsNaN(worldPos.x)) return false;
            var local = handle.InverseTransformPoint(worldPos);
            // Use slightly bigger bounds (0.8 vs 0.5) for forgiving grab
            return Mathf.Abs(local.x) <= 0.8f && Mathf.Abs(local.y) <= 0.8f && Mathf.Abs(local.z) <= 0.8f;
        }

        private void StartGrab(XRNode node, Vector3 handPos)
        {
            _isGrabbing = true;
            _grabbingNode = node;
            _grabOffset = transform.position - handPos;
            Plugin.Log.LogInfo($"Menu grabbed by {node}.");
        }

        // --- A button / keyboard ---

        private void PlaceAtInitialPos()
        {
            if (_head == null || _hasBeenPositioned) return;
            transform.position = _head.transform.TransformPoint(InitialOffsetFromHead);
            var toHead = _head.transform.position - transform.position;
            if (toHead.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toHead, Vector3.up);
            _hasBeenPositioned = true;
        }

        private void HandleAButton()
        {
            if (!HandTracker.TryGetPrimary(XRNode.RightHand, out bool aDown))
            {
                _aWasDown = false;
                return;
            }
            if (aDown && !_aWasDown) ToggleVisibility();
            _aWasDown = aDown;
        }

        private void HandleKeyboardToggle()
        {
            if (Input.GetKeyDown(KeyCode.F10)) ToggleVisibility();
        }

        private void ToggleVisibility()
        {
            _visible = !_visible;
            _visualsRoot?.SetActive(_visible);
            if (!_visible) _isGrabbing = false;
            Plugin.Log.LogInfo($"Menu visible: {_visible}");
        }

        private void HandleDiagnosticDump()
        {
            if (!Input.GetKeyDown(KeyCode.F11)) return;
            Plugin.Log.LogInfo("=== GTagCameraMod diagnostic ===");
            Plugin.Log.LogInfo($"  HandTracker: {HandTracker.DescribeState()}");
            HandTracker.TryGetLeftHandPos(out Vector3 lp);
            HandTracker.TryGetRightHandPos(out Vector3 rp);
            Plugin.Log.LogInfo($"  Left hand pos:  {lp}");
            Plugin.Log.LogInfo($"  Right hand pos: {rp}");
            HandTracker.TryGetGrip(XRNode.LeftHand,  out bool lg);
            HandTracker.TryGetGrip(XRNode.RightHand, out bool rg);
            HandTracker.TryGetPrimary(XRNode.RightHand, out bool a);
            Plugin.Log.LogInfo($"  Grip L={lg}, R={rg}; A={a}");
            Plugin.Log.LogInfo($"  Menu pos: {transform.position}, visible={_visible}, grabbing={_isGrabbing}");
            Plugin.Log.LogInfo("================================");
        }

        // --- Actions ---

        private void AdjustFov(float delta)
        {
            var cam = _head ?? Camera.main;
            if (cam == null) return;
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + delta, FovMin, FovMax);
            RefreshFovText();
            Plugin.Log.LogInfo($"FOV: {cam.fieldOfView}");
        }

        private void RefreshFovText()
        {
            var cam = _head ?? Camera.main;
            if (cam == null || _fovText == null) return;
            _fovText.text = $"{Mathf.RoundToInt(cam.fieldOfView)}";
        }

        private void DisconnectLobby()
        {
            if (!PhotonHelper.InRoom)
            {
                Plugin.Log.LogInfo("Disconnect pressed but not currently in a lobby.");
                return;
            }
            PhotonHelper.LeaveRoom();
        }

        // --- Keyboard fallback for testing on PC where VR input may be flaky ---
        internal static void HandleKeyboardFallback()
        {
            if (_instance == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) _instance.AdjustFov(+FovStep);
            else if (Input.GetKeyDown(KeyCode.F7)) _instance.AdjustFov(-FovStep);
            else if (Input.GetKeyDown(KeyCode.F9)) _instance.DisconnectLobby();
        }
    }
}
