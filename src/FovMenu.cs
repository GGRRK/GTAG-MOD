using UnityEngine;
using UnityEngine.UI;

namespace GTagCameraMod
{
    // A floating world-space menu that follows the player's head and lets them
    // adjust the FOV by physically touching "-" / "+" buttons with their gorilla hand.
    public class FovMenu : MonoBehaviour
    {
        private const float FovStep = 5f;
        private const float FovMin = 30f;
        private const float FovMax = 170f;

        // Physical size of the panel in world meters
        private const float MenuWidth = 0.30f;
        private const float MenuHeight = 0.15f;

        // Offset from the player's head (x=right, y=up, z=forward, in head's local frame)
        private static readonly Vector3 HeadOffset = new(0.30f, -0.15f, 0.45f);

        // How fast the menu eases into the target position (higher = snappier)
        private const float FollowSmoothing = 6f;

        // Anti-spam: ignore repeated touches within this many seconds
        private const float TouchCooldown = 0.25f;

        private Camera? _head;
        private Text? _fovText;
        private float _lastTouchTime;

        // Singleton-ish so the static keyboard fallback can find the active menu
        private static FovMenu? _instance;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Start()
        {
            BuildPanel();
        }

        private void BuildPanel()
        {
            // --- Root canvas (world-space) ---
            var canvasGo = new GameObject("FovMenuCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(300f, 150f);
            // Scale so 300 canvas units == MenuWidth meters in world space
            canvasRect.localScale = Vector3.one * (MenuWidth / 300f);

            // --- Background ---
            MakePanel(canvasRect, "BG", new Color(0.05f, 0.05f, 0.10f, 0.92f),
                Vector2.zero, Vector2.one);

            // --- Title strip ---
            MakeText(canvasRect, "Title", "GTAG CAMERA MOD",
                new Vector2(0f, 0.78f), new Vector2(1f, 1f),
                fontSize: 16, anchor: TextAnchor.MiddleCenter,
                color: new Color(0.65f, 0.80f, 1f));

            // --- FOV value (live updates) ---
            var fovGo = MakeText(canvasRect, "FovText", "FOV: 90",
                new Vector2(0.27f, 0.16f), new Vector2(0.73f, 0.78f),
                fontSize: 30, anchor: TextAnchor.MiddleCenter, color: Color.white);
            _fovText = fovGo.GetComponent<Text>();

            // --- Minus button (left) ---
            MakeButton(canvasRect, "MinusButton", "-",
                new Vector2(0.02f, 0.05f), new Vector2(0.25f, 0.95f),
                new Color(0.70f, 0.18f, 0.18f),
                onTouched: () => AdjustFov(-FovStep));

            // --- Plus button (right) ---
            MakeButton(canvasRect, "PlusButton", "+",
                new Vector2(0.75f, 0.05f), new Vector2(0.98f, 0.95f),
                new Color(0.18f, 0.55f, 0.22f),
                onTouched: () => AdjustFov(+FovStep));
        }

        private static GameObject MakePanel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static GameObject MakeText(Transform parent, string name, string text,
            Vector2 anchorMin, Vector2 anchorMax,
            int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var t = go.AddComponent<Text>();
            t.text = text;
            t.alignment = anchor;
            t.color = color;
            t.fontSize = fontSize;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return go;
        }

        private void MakeButton(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color color, System.Action onTouched)
        {
            var go = MakePanel(parent, name, color, anchorMin, anchorMax);

            // Label sits over the colored panel
            MakeText(go.transform, "Label", label,
                Vector2.zero, Vector2.one,
                fontSize: 64, anchor: TextAnchor.MiddleCenter, color: Color.white);

            // Hand-touch detection: any collider entering this button's trigger
            // calls onTouched(). Gorilla Tag hand colliders are physics colliders,
            // so they should activate this trigger when the hand passes through.
            var rt = (RectTransform)go.transform;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(rt.rect.width, rt.rect.height, 50f);
            bc.center = Vector3.zero;

            // OnTriggerEnter only fires if at least one of the two colliders has
            // a Rigidbody. Add a kinematic one so this button is the receiver.
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var trigger = go.AddComponent<HandTouchTrigger>();
            trigger.Menu = this;
            trigger.OnTouched = onTouched;
        }

        private void Update()
        {
            // Lazy-discover the player's head camera (GT's main camera).
            if (_head == null)
            {
                _head = Camera.main;
                if (_head == null) return;

                // Snap to position on first frame so it doesn't fly in from the origin
                SnapToHead(immediate: true);
                RefreshFovText();
                return;
            }

            SnapToHead(immediate: false);
        }

        private void SnapToHead(bool immediate)
        {
            if (_head == null) return;

            var targetPos = _head.transform.TransformPoint(HeadOffset);

            // Face the player so the menu is readable from the head's position
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
                float t = Time.deltaTime * FollowSmoothing;
                transform.position = Vector3.Lerp(transform.position, targetPos, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
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

        // Keyboard fallback so the user can verify FOV changes even if the
        // hand-touch triggers don't fire in-game on the first try.
        internal static void HandleKeyboardFallback()
        {
            if (_instance == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) _instance.AdjustFov(+FovStep);
            else if (Input.GetKeyDown(KeyCode.F7)) _instance.AdjustFov(-FovStep);
        }
    }

    // Attached to each button. Forwards OnTriggerEnter to the parent FovMenu.
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
