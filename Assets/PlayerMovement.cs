using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal player controller.
/// WASD / Arrow keys  →  move & rotate
/// B                  →  toggle Blind Mode (LiDAR-only view)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed     = 3.0f;
    public float rotationSpeed = 100.0f;

    [Header("Blind Mode")]
    /// <summary>Drag the LidarSensor child here (has URP_FastLidar on it).</summary>
    public URP_FastLidar lidar;

    public bool BlindMode { get; private set; } = false;

    // ── Saved camera state ────────────────────────────────────────────────────
    private int              savedCullingMask;
    private CameraClearFlags savedClearFlags;
    private Color            savedBgColor;
    private bool             savedStateValid = false;

    void Start()
    {
        // Freeze Y rotation driven by physics so we control it manually
        GetComponent<Rigidbody>().constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        // Auto-locate the LiDAR if not assigned in Inspector
        if (lidar == null) lidar = GetComponentInChildren<URP_FastLidar>();
        if (lidar == null) lidar = FindAnyObjectByType<URP_FastLidar>();
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        // ── Movement ──────────────────────────────────────────────────────────
        float fwd = 0f, rot = 0f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)    fwd += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)  fwd -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) rot += 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)  rot -= 1f;

        transform.Translate(0, 0, fwd * moveSpeed * Time.deltaTime);
        transform.Rotate(0, rot * rotationSpeed * Time.deltaTime, 0);

        // ── Blind Mode Toggle  [B] ────────────────────────────────────────────
        if (Keyboard.current.bKey.wasPressedThisFrame)
            ToggleBlindMode();
    }

    // ── Blind Mode ────────────────────────────────────────────────────────────

    void ToggleBlindMode()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        BlindMode = !BlindMode;

        if (BlindMode)
        {
            // Save
            savedCullingMask = cam.cullingMask;
            savedClearFlags  = cam.clearFlags;
            savedBgColor     = cam.backgroundColor;
            savedStateValid  = true;

            // Show ONLY the LiDAR layer (layer set on the LidarSensor GO)
            // Default is TransparentFX = layer 1
            int lidarLayer = (lidar != null) ? lidar.lidarLayer : 1;
            cam.cullingMask     = (1 << lidarLayer);
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
        }
        else
        {
            if (savedStateValid)
            {
                cam.cullingMask     = savedCullingMask;
                cam.clearFlags      = savedClearFlags;
                cam.backgroundColor = savedBgColor;
                savedStateValid     = false;
            }
        }
    }

    // ── Minimal HUD ───────────────────────────────────────────────────────────

    void OnGUI()
    {
        var style = new GUIStyle
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = BlindMode
            ? new Color(0.4f, 1f, 0.4f)   // green when dots-only
            : new Color(0.9f, 0.9f, 0.9f); // white normally

        string label = BlindMode
            ? "[B] Blind Mode: ON  — LiDAR dots only"
            : "[B] Blind Mode: OFF";

        GUI.Label(new Rect(10, 10, 500, 30), label, style);
    }
}
