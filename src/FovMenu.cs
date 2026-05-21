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
    //   - Touch the FOV +/- buttons or the DISCONNECT button to activate them
    public class FovMenu : MonoBehaviour
    {
        // === FOV ===
        private const float FovStep = 5f;
        private const float FovMin = 30f;
        private const float FovMax = 170f;

        // === Sizes in world meters ===
        private const float PanelWidth = 0.30f;
        private const float PanelHeight = 0.20f;
        private const float FovButtonSize = 0.06f;
        private static readonly Vector2 DisconnectButtonSize = new(0.20f, 0.045f);
        private const float HandleSize = 0.045f;

        // === First-time placement (offset from head when menu first becomes visible) ===
        private static readonly Vector3 InitialOffsetFromHead = new(0f, -0.15f, 0.55f);

        // === Interaction ===
        private const float TouchCooldown = 0.25f;

        private Camera? _head;
        private TextMesh? _fovText;
        private float _lastTouchTime;

        // The wrapping GameObject holding all visuals. Toggle SetActive to show/hide.
        private GameObject? _visualsRoot;
        private bool _visible = true;
        private bool _hasBeenPositioned;

        // Edge detection for the A button so one press = one toggle
        private bool _aWasDown;

        // Grab state
        private Transform? _grabbingHand;
        private XRNode _grabbingNode = XRNode.RightHand;
        private Vector3 _grabOffset;

        private static FovMenu? _instance;

        private void Awake() => _instance = this;
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Start() => BuildMenu();

        private void BuildMenu()
        {
            // Wrap visuals in a child object so SetActive toggles everything at once.
            _visualsRoot = new GameObject("Visuals");
            _visualsRoot.transform.SetParent(transform, worldPositionStays: false);
            _visualsRoot.transform.localPosition = Vector3.zero;
            _visualsRoot.transform.localRotation = Quaternion.identity;

            // --- Background panel ---
            MakeQuad("Background",
                localPos: Vector3.zero,
                size: new Vector2(PanelWidth, PanelHeight),
                color: new Color(0.05f, 0.05f, 0.10f, 1f),
                parent: _visualsRoot.transform);

            // --- Title ---
            MakeText("Title", "GTAG CAMERA MOD",
                localPos: new Vector3(0f, +0.080f, -0.002f),
                charSize: 0.0035f,
                color: new Color(0.65f, 0.80f, 1f),
                parent: _visualsRoot.transform);

            // --- FOV value (live) ---
            _fovText = MakeText("FovText", "FOV: 90",
                localPos: new Vector3(0f, +0.040f, -0.002f),
                charSize: 0.006f,
                color: Color.white,
                parent: _visualsRoot.transform);

            // --- FOV buttons ---
            MakeButton("MinusButton", "-",
                localPos: new Vector3(-PanelWidth * 0.36f, -0.010f, -0.005f),
                size: new Vector2(FovButtonSize, FovButtonSize),
                color: new Color(0.70f, 0.18f, 0.18f),
                labelCharSize: 0.025f,
                onTouched: () => AdjustFov(-FovStep));

            MakeButton("PlusButton", "+",
                localPos: new Vector3(+PanelWidth * 0.36f, -0.010f, -0.005f),
                size: new Vector2(FovButtonSize, FovButtonSize),
                color: new Color(0.18f, 0.55f, 0.22f),
                labelCharSize: 0.025f,
                onTouched: () => AdjustFov(+FovStep));

            // --- Disconnect Lobby (wide, bottom) ---
            MakeButton("DisconnectButton", "DISCONNECT LOBBY",
                localPos: new Vector3(0f, -0.075f, -0.005f),
                size: DisconnectButtonSize,
                color: new Color(0.85f, 0.55f, 0.10f),
                labelCharSize: 0.006f,
                onTouched: DisconnectLobby);

            // --- Side handles for dragging ---
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
            Color color, float labelCharSize, System.Action onTouched)
        {
            var btn = MakeQuad(name, localPos, size, color, _visualsRoot!.transform);

            var labelGo = new GameObject(name + "_Label");
            labelGo.transform.SetParent(btn.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            labelGo.transform.localRotation = Quaternion.identity;
            labelGo.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.color = Color.white;
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

            // Primitives auto-add a BoxCollider; remove it and add our own configured.
            var auto = go.GetComponent<BoxCollider>();
            if (auto != null) Destroy(auto);

            go.transform.SetParent(_visualsRoot!.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * HandleSize;

            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.color = new Color(1f, 0.40f, 0.85f); // bright pink for visibility
            go.GetComponent<Renderer>().material = mat;

            // Larger trigger volume than the visual cube — easier to grab
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
            // Lazy: spawn position once we have a head camera reference
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
            {
                transform.rotation = Quaternion.LookRotation(-toHead, Vector3.up);
            }
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

            // Release any in-progress grab on hide
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

            // Menu follows the grabbing hand at the offset captured when grab started
            transform.position = _grabbingHand.position + _grabOffset;
        }

        // Called by HandleTrigger when a collider enters or stays in a side handle
        internal void TryStartGrab(Collider other)
        {
            if (_grabbingHand != null || _head == null) return;

            // Determine which hand entered by spatial side relative to the head
            var relative = other.transform.position - _head.transform.position;
            var isRight = Vector3.Dot(relative, _head.transform.right) > 0f;
            var node = isRight ? XRNode.RightHand : XRNode.LeftHand;

            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripDown) || !gripDown)
            {
                // Hand in handle but no grip → not grabbing
                return;
            }

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
            _fovText.text = $"FOV: {Mathf.RoundToInt(cam.fieldOfView)}";
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

        // Keyboard fallbacks for testing on PC where VR input may be flaky
        internal static void HandleKeyboardFallback()
        {
            if (_instance == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) _instance.AdjustFov(+FovStep);
            else if (Input.GetKeyDown(KeyCode.F7)) _instance.AdjustFov(-FovStep);
            else if (Input.GetKeyDown(KeyCode.F9)) _instance.DisconnectLobby();
        }
    }

    // Attached to each action button (FOV +/-, Disconnect). Fires on hand entering trigger.
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

    // Attached to each side handle. Forwards to FovMenu.TryStartGrab, which checks grip state.
    internal class HandleTrigger : MonoBehaviour
    {
        internal FovMenu? Menu;

        // Fires every fixed-update tick while a collider with a Rigidbody overlaps —
        // lets the menu grab the moment the user presses grip with their hand inside.
        private void OnTriggerStay(Collider other)
        {
            Menu?.TryStartGrab(other);
        }
    }
}
