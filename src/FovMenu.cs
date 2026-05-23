using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

namespace GTagCameraMod
{
    // Tablet-style menu using Cube primitives + default materials + TextMesh.
    //
    // Interaction model (copied from the canonical GT menu library pattern):
    //   - HandTracker spawns two invisible proxy Sphere colliders parented to
    //     GorillaLocomotion.Player.Instance.leftHandTransform / rightHandTransform.
    //   - Each button is a Cube with its auto BoxCollider set to isTrigger=true.
    //   - The proxy spheres inherit a Rigidbody from the GT hand hierarchy, so
    //     OnTriggerEnter on the button fires reliably when the proxy enters.
    //   - Handles use OnTriggerStay + grip-button hold for "grab to drag".
    //   - No custom shader: default primitive materials respect depth correctly,
    //     so the menu is occluded by walls.
    public class FovMenu : MonoBehaviour
    {
        // === FOV ===
        private const float FovStep = 5f;
        private const float FovMin = 30f;
        private const float FovMax = 170f;

        // === Panel dimensions (world meters) ===
        private const float PanelWidth = 0.44f;
        private const float PanelHeight = 0.24f;
        private const float PanelDepth = 0.012f;

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

        // === State ===
        private Camera? _head;
        private TextMesh? _fovText;
        private GameObject? _visualsRoot;
        private bool _visible = true;
        private bool _hasBeenPositioned;
        private bool _aWasDown;

        private readonly List<Transform> _handles = new();
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

            // --- Wood frame (the panel body itself) ---
            MakeCube("Frame",
                localPos: Vector3.zero,
                size: new Vector3(PanelWidth, PanelHeight, PanelDepth),
                color: WoodFrame,
                parent: _visualsRoot.transform);

            // --- Title text on top of the frame ---
            MakeText("Title", "GTAG CAMERA MOD",
                localPos: new Vector3(0f, PanelHeight * 0.40f, -PanelDepth * 0.55f),
                charSize: 0.0045f,
                color: CreamCard,
                parent: _visualsRoot.transform);

            // --- FOV card (left half) ---
            Vector3 fovCardPos = new(-PanelWidth * 0.23f, -0.015f, -PanelDepth * 0.55f);
            Vector3 fovCardSize = new(PanelWidth * 0.42f, PanelHeight * 0.62f, 0.004f);
            MakeCube("FovCard", fovCardPos, fovCardSize, CreamCard, _visualsRoot.transform);

            MakeText("FovLabel", "FOV",
                localPos: fovCardPos + new Vector3(0f, fovCardSize.y * 0.34f, -fovCardSize.z * 0.55f),
                charSize: 0.0055f,
                color: DarkBrown,
                parent: _visualsRoot.transform);

            _fovText = MakeText("FovText", "90",
                localPos: fovCardPos + new Vector3(0f, fovCardSize.y * 0.05f, -fovCardSize.z * 0.55f),
                charSize: 0.0150f,
                color: DarkBrown,
                parent: _visualsRoot.transform);

            // FOV +/- buttons sized to be comfortably inside the card
            Vector3 fovBtnSize = new(0.060f, 0.060f, 0.012f);
            MakeButton("MinusButton", "-",
                localPos: fovCardPos + new Vector3(-fovCardSize.x * 0.32f, -fovCardSize.y * 0.30f, -fovCardSize.z * 0.55f - fovBtnSize.z * 0.55f),
                size: fovBtnSize,
                color: RedAccent,
                labelCharSize: 0.030f,
                labelColor: Color.white,
                onPressed: () => AdjustFov(-FovStep));

            MakeButton("PlusButton", "+",
                localPos: fovCardPos + new Vector3(+fovCardSize.x * 0.32f, -fovCardSize.y * 0.30f, -fovCardSize.z * 0.55f - fovBtnSize.z * 0.55f),
                size: fovBtnSize,
                color: GreenAccent,
                labelCharSize: 0.030f,
                labelColor: Color.white,
                onPressed: () => AdjustFov(+FovStep));

            // --- Disconnect card (right half, whole-card button) ---
            Vector3 discCardPos = new(+PanelWidth * 0.23f, -0.015f, -PanelDepth * 0.55f - 0.006f);
            Vector3 discCardSize = new(PanelWidth * 0.42f, PanelHeight * 0.62f, 0.012f);

            MakeButton("DisconnectButton", "DISCONNECT\nLOBBY",
                localPos: discCardPos,
                size: discCardSize,
                color: OrangeCard,
                labelCharSize: 0.010f,
                labelColor: Color.white,
                onPressed: DisconnectLobby);

            // --- Pink grab handles on outer left/right (bigger = easier to grab) ---
            float handleSizeM = 0.07f;
            float handleX = PanelWidth / 2f + handleSizeM * 0.6f;
            Vector3 handleSize = new(handleSizeM, handleSizeM, handleSizeM);
            _handles.Add(MakeHandle("LeftHandle",  new Vector3(-handleX, 0f, 0f), handleSize).transform);
            _handles.Add(MakeHandle("RightHandle", new Vector3(+handleX, 0f, 0f), handleSize).transform);
        }

        // --- Construction helpers ---

        // Visual-only cube: keep the auto BoxCollider OFF since this isn't interactive.
        // Default material respects depth (closes over walls correctly).
        private GameObject MakeCube(string name, Vector3 localPos, Vector3 size, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            // Visual only — drop the auto collider
            var bc = go.GetComponent<BoxCollider>();
            if (bc != null) Destroy(bc);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            // Just change color on the default material; don't replace the shader
            go.GetComponent<Renderer>().material.color = color;
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
            // The default font material on some Unity builds binds to GUI/Text Shader (ZTest Always),
            // which makes text render through walls. Force GUI/3D Text Shader (ZTest LEqual) while
            // keeping the font's glyph atlas as the texture.
            ApplyDepthTestedTextShader(tm, color);
            return tm;
        }

        // Replaces a TextMesh's material with an explicit GUI/3D Text Shader build so the text
        // gets occluded by closer geometry instead of bleeding through walls.
        private static void ApplyDepthTestedTextShader(TextMesh tm, Color color)
        {
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var shader = Shader.Find("GUI/3D Text Shader");
            if (shader == null) return; // Unity build doesn't ship it — fall back to default
            var srcMat = tm.font != null ? tm.font.material : mr.sharedMaterial;
            var mat = new Material(shader);
            if (srcMat != null) mat.mainTexture = srcMat.mainTexture;
            mat.color = color;
            mr.material = mat;
        }

        // Interactive button: keep auto BoxCollider, set isTrigger=true. The proxy
        // sphere on the user's hand (managed by HandTracker) fires OnTriggerEnter
        // here. No Rigidbody on the button — the proxy has one via its GT hand parent.
        private GameObject MakeButton(string name, string label, Vector3 localPos, Vector3 size,
            Color color, float labelCharSize, Color labelColor, System.Action onPressed)
        {
            var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btn.name = name;
            btn.GetComponent<BoxCollider>().isTrigger = true;
            btn.transform.SetParent(_visualsRoot!.transform, worldPositionStays: false);
            btn.transform.localPosition = localPos;
            btn.transform.localScale = size;
            var renderer = btn.GetComponent<Renderer>();
            renderer.material.color = color;

            var trigger = btn.AddComponent<ButtonTrigger>();
            trigger.Name = name;
            trigger.OnPressed = onPressed;
            trigger.Renderer = renderer;
            trigger.BaseColor = color;
            // Hover tint = midway toward white. Strong enough to be obvious in VR, subtle enough
            // not to look broken when hovered for a long time before pressing.
            trigger.HoverColor = Color.Lerp(color, Color.white, 0.35f);

            // Label is a TextMesh placed just in front of the button face
            var labelGo = new GameObject(name + "_Label");
            labelGo.transform.SetParent(btn.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.55f); // -0.55 of cube depth = just outside front face
            labelGo.transform.localRotation = Quaternion.identity;
            labelGo.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f / size.z);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.color = labelColor;
            tm.characterSize = labelCharSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            ApplyDepthTestedTextShader(tm, labelColor);

            return btn;
        }

        // Grab handle: same trigger pattern. Uses HandleTrigger which subscribes to
        // OnTriggerStay so it can poll the grip button while a proxy is inside.
        private GameObject MakeHandle(string name, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.GetComponent<BoxCollider>().isTrigger = true;
            go.transform.SetParent(_visualsRoot!.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().material.color = HandlePink;

            var trigger = go.AddComponent<HandleTrigger>();
            trigger.Menu = this;
            return go;
        }

        // --- Main loop ---

        private void Update()
        {
            HandTracker.Tick(); // find GT hands + spawn proxies if not done yet

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

            if (_isGrabbing) UpdateGrab();
        }

        private void PlaceAtInitialPos()
        {
            if (_head == null || _hasBeenPositioned) return;
            transform.position = _head.transform.TransformPoint(InitialOffsetFromHead);
            var toHead = _head.transform.position - transform.position;
            if (toHead.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toHead, Vector3.up);
            _hasBeenPositioned = true;
        }

        // --- Buttons / grab callbacks ---

        internal void TryStartGrab(XRNode node)
        {
            if (_isGrabbing) return;
            var handTransform = node == XRNode.LeftHand
                ? HandTracker.LeftHandTransform
                : HandTracker.RightHandTransform;
            if (handTransform == null) return;
            _isGrabbing = true;
            _grabbingNode = node;
            _grabOffset = transform.position - handTransform.position;
            Plugin.Log.LogInfo($"Menu grabbed by {node}.");
        }

        private void UpdateGrab()
        {
            if (!HandTracker.TryGetGrip(_grabbingNode, out bool gripDown) || !gripDown)
            {
                _isGrabbing = false;
                Plugin.Log.LogInfo("Menu released.");
                return;
            }
            var handTransform = _grabbingNode == XRNode.LeftHand
                ? HandTracker.LeftHandTransform
                : HandTracker.RightHandTransform;
            if (handTransform == null) return;
            transform.position = handTransform.position + _grabOffset;
        }

        // --- Toggle / keyboard / diagnostics ---

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
            HandTracker.TryGetGrip(XRNode.LeftHand,  out bool lg);
            HandTracker.TryGetGrip(XRNode.RightHand, out bool rg);
            HandTracker.TryGetPrimary(XRNode.RightHand, out bool a);
            Plugin.Log.LogInfo($"  Grip L={lg}, R={rg}; A={a}");
            Plugin.Log.LogInfo($"  Menu pos: {transform.position}, visible={_visible}, grabbing={_isGrabbing}");
            if (HandTracker.LeftProxy != null)
                Plugin.Log.LogInfo($"  LeftProxy world pos: {HandTracker.LeftProxy.transform.position}");
            if (HandTracker.RightProxy != null)
                Plugin.Log.LogInfo($"  RightProxy world pos: {HandTracker.RightProxy.transform.position}");
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

        internal static void HandleKeyboardFallback()
        {
            if (_instance == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) _instance.AdjustFov(+FovStep);
            else if (Input.GetKeyDown(KeyCode.F7)) _instance.AdjustFov(-FovStep);
            else if (Input.GetKeyDown(KeyCode.F9)) _instance.DisconnectLobby();
        }
    }

    // Filters trigger events to only fire when one of our proxy spheres enters.
    // Without this filter we might trigger on GT's own hand colliders or other
    // physics objects in the scene and behave erratically.
    //
    // Hover feedback: while a proxy is inside the button volume, the button tints
    // brighter. Restores the base color when the last proxy leaves. This is the
    // fastest "is the mod tracking my hand?" signal — if the button tints, the
    // chain is working; if it doesn't, HandTracker is the culprit.
    internal class ButtonTrigger : MonoBehaviour
    {
        internal string Name = "";
        internal System.Action OnPressed = null!;
        internal Renderer? Renderer;
        internal Color BaseColor;
        internal Color HoverColor;

        private const float Cooldown = 0.25f;
        private float _lastFire;
        private int _proxiesInside;

        private void OnTriggerEnter(Collider other)
        {
            var node = HandTracker.IdentifyProxy(other.gameObject);
            if (node == null) return; // not one of our proxies

            _proxiesInside++;
            if (_proxiesInside == 1 && Renderer != null) Renderer.material.color = HoverColor;

            if (Time.time - _lastFire < Cooldown) return;
            _lastFire = Time.time;
            Plugin.Log.LogInfo($"Button pressed: {Name} (by {node})");
            OnPressed?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            var node = HandTracker.IdentifyProxy(other.gameObject);
            if (node == null) return;

            _proxiesInside = Mathf.Max(0, _proxiesInside - 1);
            if (_proxiesInside == 0 && Renderer != null) Renderer.material.color = BaseColor;
        }
    }

    internal class HandleTrigger : MonoBehaviour
    {
        internal FovMenu Menu = null!;

        private void OnTriggerStay(Collider other)
        {
            var node = HandTracker.IdentifyProxy(other.gameObject);
            if (node == null) return;
            if (!HandTracker.TryGetGrip(node.Value, out bool gripDown) || !gripDown) return;
            Menu.TryStartGrab(node.Value);
        }
    }
}
