using UnityEngine;
using UnityEngine.XR;

namespace GTagCameraMod
{
    // Floating world-space menu built from primitives + TextMesh. No UnityEngine.UI.
    //
    // Interactions:
    //   - A button on right controller toggles visibility (F10 keyboard fallback)
    //   - Touch a side handle + hold grip on the same hand → menu follows that hand
    //   - Release grip → menu stays at new position
    //   - Touch the FOV +/- buttons or the DISCONNECT card to activate them
    public class FovMenu : MonoBehaviour
    {
        // === FOV ===
        private const float FovStep = 5f;
        private const float FovMin = 30f;
        private const float FovMax = 170f;

        // === Sizes in world meters — tablet aspect ratio ===
        private const float PanelWidth = 0.42f;
        private const float PanelHeight = 0.22f;
        private const float HandleSize = 0.045f;

        // === First-time placement (offset from head when menu first becomes visible) ===
        private static readonly Vector3 InitialOffsetFromHead = new(0f, -0.15f, 0.55f);

        // === Palette (wood + cream aesthetic) ===
        private static readonly Color WoodFrame   = new(0.36f, 0.22f, 0.10f, 1f);
        private static readonly Color CreamCard   = new(0.94f, 0.90f, 0.78f, 1f);
        private static readonly Color DarkBrown   = new(0.18f, 0.10f, 0.04f, 1f);
        private static readonly Color RedAccent   = new(0.78f, 0.22f, 0.20f, 1f);
        private static readonly Color GreenAccent = new(0.20f, 0.58f, 0.25f, 1f);
        private static readonly Color OrangeCard  = new(0.88f, 0.55f, 0.10f, 1f);
        private static readonly Color HandlePink  = new(1.00f, 0.40f, 0.85f, 1f);

        // === Interaction ===
        private const float TouchCooldown = 0.25f;

        private Camera? _head;
        private TextMesh? _fovText;
        private float _lastTouchTime;

        private GameObject? _visualsRoot;
        private bool _visible = true;
        private bool _hasBeenPositioned;

        private bool _aWasDown;

        private Transform? _grabbingHand;
        private XRNode _grabbingNode = XRNode.RightHand;
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

            // --- Wood frame (full panel) ---
            MakeQuad("Frame",
                localPos: Vector3.zero,
                size: new Vector2(PanelWidth, PanelHeight),
                color: WoodFrame,
                parent: _visualsRoot.transform);

            // --- Title strip text at the top of the frame ---
            MakeText("Title", "GTAG CAMERA MOD",
                localPos: new Vector3(0f, PanelHeight * 0.42f, -0.002f),
                charSize: 0.0042f,
                color: CreamCard,
                parent: _visualsRoot.transform);

            // --- FOV card (left half) ---
            Vector3 fovCardPos = new(-PanelWidth * 0.24f, -0.01f, -0.002f);
            Vector2 fovCardSize = new(PanelWidth * 0.40f, PanelHeight * 0.62f);

            MakeQuad("FovCard", fovCardPos, fovCardSize, CreamCard, _visualsRoot.transform);

            MakeText("FovLabel", "FOV",
                localPos: fovCardPos + new Vector3(0f, fovCardSize.y * 0.32f, -0.001f),
                charSize: 0.0050f,
                color: DarkBrown,
                parent: _visualsRoot.transform);

            _fovText = MakeText("FovText", "90",
                localPos: fovCardPos + new Vector3(0f, fovCardSize.y * 0.02f, -0.001f),
                charSize: 0.0140f,
                color: DarkBrown,
                parent: _visualsRoot.transform);

            // Small +/- buttons positioned at the lower-left and lower-right of the FOV card
            float fovBtnSize = 0.038f;
            MakeButton("MinusButton", "-",
                localPos: fovCardPos + new Vector3(-fovCardSize.x * 0.35f, -fovCardSize.y * 0.30f, -0.003f),
                size: new Vector2(fovBtnSize, fovBtnSize),
                color: RedAccent,
                labelCharSize: 0.022f,
                labelColor: Color.white,
                onTouched: () => AdjustFov(-FovStep));

            MakeButton("PlusButton", "+",
                localPos: fovCardPos + new Vector3(+fovCardSize.x * 0.35f, -fovCardSize.y * 0.30f, -0.003f),
                size: new Vector2(fovBtnSize, fovBtnSize),
                color: GreenAccent,
                labelCharSize: 0.022f,
                labelColor: Color.white,
                onTouched: () => AdjustFov(+FovStep));

            // --- Disconnect card (right half) — the entire card is the button ---
            Vector3 discCardPos = new(+PanelWidth * 0.24f, -0.01f, -0.002f);
            Vector2 discCardSize = new(PanelWidth * 0.40f, PanelHeight * 0.62f);

            MakeButton("DisconnectButton", "DISCONNECT\nLOBBY",
                localPos: discCardPos,
                size: discCardSize,
                color: OrangeCard,
                labelCharSize: 0.008f,
                labelColor: Color.white,
                onTouched: DisconnectLobby);

            // --- Side grab handles (pink for visibility) ---
            float handleX = PanelWidth / 2f + HandleSize / 2f + 0.005f;
            MakeHandle("LeftHandle", new Vector3(-handleX, 0f, 0f));
            MakeHandle("RightHandle", new Vector3(+handleX, 0f, 0f));
        }

        private GameObject MakeQuad(string name, Vector3 localPos, Vector2 size,
            Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;

            var mc = go.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc);

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

            var labelGo = new GameObject(name + "_Label");
            labelGo.transform.SetParent(btn.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            labelGo.transform.localRotation = Quaternion.identity;
            // Counteract the parent quad's non-uniform scale so text doesn't squish
            labelGo.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.color = labelColor;
            tm.characterSize = labelCharSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;

            var bc = btn.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1f, 1f, 0.6f);

            var rb = btn.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var trigger = btn.AddComponent<HandTouchTrigger>();
            trigger.Menu = this;
            trigger.OnTouched = onTouched;
        }

        private void MakeHandle(string name, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            var auto = go.GetComponent<BoxCollider>();
            if (auto != null) Destroy(auto);

            go.transform.SetParent(_visualsRoot!.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * HandleSize;

            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.color = HandlePink;
            go.GetComponent<Renderer>().material = mat;

            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = Vector3.one * 1.6f;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var ht = go.AddComponent<HandleTrigger>();
            ht.Menu = this;
        }

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
            HandleGrab();
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

        private void HandleAButton()
        {
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!right.TryGetFeatureValue(CommonUsages.primaryButton, out bool aDown))
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
            if (!_visible) _grabbingHand = null;
            Plugin.Log.LogInfo($"Menu visible: {_visible}");
        }

        private void HandleGrab()
        {
            if (_grabbingHand == null) return;

            var device = InputDevices.GetDeviceAtXRNode(_grabbingNode);
            if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripDown) || !gripDown)
            {
                _grabbingHand = null;
                Plugin.Log.LogInfo("Menu released.");
                return;
            }

            transform.position = _grabbingHand.position + _grabOffset;
        }

        internal void TryStartGrab(Collider other)
        {
            if (_grabbingHand != null || _head == null) return;

            var relative = other.transform.position - _head.transform.position;
            var isRight = Vector3.Dot(relative, _head.transform.right) > 0f;
            var node = isRight ? XRNode.RightHand : XRNode.LeftHand;

            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripDown) || !gripDown)
                return;

            _grabbingHand = other.transform;
            _grabbingNode = node;
            _grabOffset = transform.position - other.transform.position;
            Plugin.Log.LogInfo($"Menu grabbed ({node}).");
        }

        internal void TryTouch(System.Action action)
        {
            if (Time.time - _lastTouchTime < TouchCooldown) return;
            _lastTouchTime = Time.time;
            action();
        }

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

    internal class HandTouchTrigger : MonoBehaviour
    {
        internal FovMenu? Menu;
        internal System.Action? OnTouched;

        private void OnTriggerEnter(Collider _)
        {
            if (Menu == null || OnTouched == null) return;
            Menu.TryTouch(OnTouched);
        }
    }

    internal class HandleTrigger : MonoBehaviour
    {
        internal FovMenu? Menu;

        private void OnTriggerStay(Collider other) => Menu?.TryStartGrab(other);
    }
}
