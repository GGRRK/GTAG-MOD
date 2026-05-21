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
        private const float PanelHeight = 0.15f;
        private const float ButtonSize = 0.07f;

        // Local offset from the player's head: x=right, y=up, z=forward
        private static readonly Vector3 HeadOffset = new(0.30f, -0.15f, 0.45f);

        // Higher = the menu snaps to your head more aggressively
        private const float FollowSmoothing = 6f;

        // Anti-spam: ignore repeated touches within this many seconds
        private const float TouchCooldown = 0.25f;

        private Camera? _head;
        private TextMesh? _fovText;
        private float _lastTouchTime;

        // Active instance so the static keyboard fallback can reach it
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

            // --- Title text (top strip) ---
            MakeText("Title", "GTAG CAMERA MOD",
                localPos: new Vector3(0f, PanelHeight * 0.35f, -0.002f),
                charSize: 0.0035f,
                color: new Color(0.65f, 0.80f, 1f),
                parent: transform);

            // --- FOV value (live, middle of panel) ---
            _fovText = MakeText("FovText", "FOV: 90",
                localPos: new Vector3(0f, 0f, -0.002f),
                charSize: 0.006f,
                color: Color.white,
                parent: transform);

            // --- Minus button (left) ---
            MakeButton("MinusButton", "-",
                localPos: new Vector3(-PanelWidth * 0.36f, -PanelHeight * 0.15f, -0.005f),
                color: new Color(0.70f, 0.18f, 0.18f),
                onTouched: () => AdjustFov(-FovStep));

            // --- Plus button (right) ---
            MakeButton("PlusButton", "+",
                localPos: new Vector3(+PanelWidth * 0.36f, -PanelHeight * 0.15f, -0.005f),
                color: new Color(0.18f, 0.55f, 0.22f),
                onTouched: () => AdjustFov(+FovStep));
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

        private void MakeButton(string name, string label, Vector3 localPos, Color color,
            System.Action onTouched)
        {
            var btn = MakeQuad(name, localPos, new Vector2(ButtonSize, ButtonSize), color, transform);

            // Label sits in front of the button face
            MakeText(name + "_Label", label,
                localPos: new Vector3(0f, 0.005f, -0.001f),
                charSize: 0.025f,
                color: Color.white,
                parent: btn.transform);

            // BoxCollider trigger detects when a hand collider enters the button volume
            var bc = btn.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1f, 1f, 0.6f); // local; scales with parent transform

            // OnTriggerEnter only fires if one of the colliders has a Rigidbody; make this one
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

        // Keyboard fallback for testing/debugging when hand-touch can't be verified
        internal static void HandleKeyboardFallback()
        {
            if (_instance == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) _instance.AdjustFov(+FovStep);
            else if (Input.GetKeyDown(KeyCode.F7)) _instance.AdjustFov(-FovStep);
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
