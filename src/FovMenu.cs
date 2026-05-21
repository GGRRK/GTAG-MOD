using UnityEngine;

namespace GTagCameraMod
{
    // Floating world-space menu (no UnityEngine.UI — built from primitives + TextMesh).
    public class FovMenu : MonoBehaviour
    {
        private const float FovStep = 5f;
        private const float FovMin = 30f;
        private const float FovMax = 170f;

        // Physical sizes in world meters
        private const float PanelWidth = 0.30f;
        private const float PanelHeight = 0.20f;
        private const float FovButtonSize = 0.06f;
        private static readonly Vector2 DisconnectButtonSize = new(0.20f, 0.045f);

        // Local offset from the player's head: x=right, y=up, z=forward
        private static readonly Vector3 HeadOffset = new(0.30f, -0.15f, 0.45f);

        private const float FollowSmoothing = 6f;
        private const float TouchCooldown = 0.25f;

        private Camera? _head;
        private TextMesh? _fovText;
        private float _lastTouchTime;

        private static FovMenu? _instance;

        private void Awake() => _instance = this;
        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Start() => BuildMenu();

        private void BuildMenu()
        {
            // --- Background panel ---
            MakeQuad("Background",
                localPos: Vector3.zero,
                size: new Vector2(PanelWidth, PanelHeight),
                color: new Color(0.05f, 0.05f, 0.10f, 1f),
                parent: transform);

            // --- Title text (top) ---
            MakeText("Title", "GTAG CAMERA MOD",
                localPos: new Vector3(0f, +0.080f, -0.002f),
                charSize: 0.0035f,
                color: new Color(0.65f, 0.80f, 1f),
                parent: transform);

            // --- FOV value (high-middle) ---
            _fovText = MakeText("FovText", "FOV: 90",
                localPos: new Vector3(0f, +0.040f, -0.002f),
                charSize: 0.006f,
                color: Color.white,
                parent: transform);

            // --- Minus FOV button (mid, left) ---
            MakeButton("MinusButton", "-",
                localPos: new Vector3(-PanelWidth * 0.36f, -0.010f, -0.005f),
                size: new Vector2(FovButtonSize, FovButtonSize),
                color: new Color(0.70f, 0.18f, 0.18f),
                labelCharSize: 0.025f,
                onTouched: () => AdjustFov(-FovStep));

            // --- Plus FOV button (mid, right) ---
            MakeButton("PlusButton", "+",
                localPos: new Vector3(+PanelWidth * 0.36f, -0.010f, -0.005f),
                size: new Vector2(FovButtonSize, FovButtonSize),
                color: new Color(0.18f, 0.55f, 0.22f),
                labelCharSize: 0.025f,
                onTouched: () => AdjustFov(+FovStep));

            // --- Disconnect Lobby button (bottom, wide) ---
            MakeButton("DisconnectButton", "DISCONNECT LOBBY",
                localPos: new Vector3(0f, -0.075f, -0.005f),
                size: DisconnectButtonSize,
                color: new Color(0.85f, 0.55f, 0.10f),
                labelCharSize: 0.006f,
                onTouched: DisconnectLobby);
        }

        private GameObject MakeQuad(string name, Vector3 localPos, Vector2 size,
            Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;

            // Primitives auto-add a MeshCollider; remove it (buttons add their own BoxCollider)
            var mc = go.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc);

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            // Hidden/Internal-Colored ships with every Unity build, no pipeline-specific shader needed
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
            var btn = MakeQuad(name, localPos, size, color, transform);

            // Label sits in front of the button face. Local scale of label
            // un-does the parent's scale so the character size stays in meters.
            var labelGo = new GameObject(name + "_Label");
            labelGo.transform.SetParent(btn.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            labelGo.transform.localRotation = Quaternion.identity;
            // Counteract the parent quad's non-uniform scale so the label doesn't squish
            labelGo.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.color = Color.white;
            tm.characterSize = labelCharSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;

            // Hand-touch detection
            var bc = btn.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1f, 1f, 0.6f); // local; scales with parent transform

            var rb = btn.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var trigger = btn.AddComponent<HandTouchTrigger>();
            trigger.Menu = this;
            trigger.OnTouched = onTouched;
        }

        private void Update()
        {
            if (_head == null)
            {
                _head = Camera.main;
                if (_head == null) return;
                FollowHead(immediate: true);
                RefreshFovText();
                return;
            }
            FollowHead(immediate: false);
        }

        private void FollowHead(bool immediate)
        {
            if (_head == null) return;

            var targetPos = _head.transform.TransformPoint(HeadOffset);
            var toHead = _head.transform.position - targetPos;
            var targetRot = toHead.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(-toHead, Vector3.up)
                : Quaternion.identity;

            if (immediate)
            {
                transform.position = targetPos;
                transform.rotation = targetRot;
            }
            else
            {
                float a = Time.deltaTime * FollowSmoothing;
                transform.position = Vector3.Lerp(transform.position, targetPos, a);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, a);
            }
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

        // Keyboard fallback for testing/debugging
        internal static void HandleKeyboardFallback()
        {
            if (_instance == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) _instance.AdjustFov(+FovStep);
            else if (Input.GetKeyDown(KeyCode.F7)) _instance.AdjustFov(-FovStep);
            else if (Input.GetKeyDown(KeyCode.F9)) _instance.DisconnectLobby();
        }
    }

    // One per button: forwards OnTriggerEnter to the parent FovMenu
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
}
