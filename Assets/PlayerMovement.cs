using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed     = 3.0f;
    public float rotationSpeed = 100.0f;

    [Header("Simulation State")]
    public int   obstacleHits = 0;
    public bool  blindMode    = false;
    public AStarManager aStarManager;

    [Header("Blind Mode – LiDAR Layer Isolation")]
    // The LidarSensor child GameObject (has URP_FastLidar on it)
    public URP_FastLidar lidar;

    private Rigidbody    rb;
    private LineRenderer directionArrow;

    // Saved camera state so we can restore it
    private int              savedCullingMask;
    private CameraClearFlags savedClearFlags;
    private Color            savedBgColor;
    private bool             savedStateValid = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (aStarManager == null) aStarManager = GetComponent<AStarManager>();
        if (rb == null) Debug.LogWarning("[Player] Rigidbody not found.");

        // Auto-find lidar if not assigned
        if (lidar == null) lidar = GetComponentInChildren<URP_FastLidar>();
        if (lidar == null) lidar = FindAnyObjectByType<URP_FastLidar>();

        // Direction arrow
        GameObject arrowObj = new("DirectionArrow");
        arrowObj.transform.SetParent(this.transform);
        directionArrow = arrowObj.AddComponent<LineRenderer>();
        directionArrow.startWidth    = 0.15f;
        directionArrow.endWidth      = 0.0f;
        directionArrow.positionCount = 2;
        directionArrow.useWorldSpace = true;
        directionArrow.material      = new Material(Shader.Find("Hidden/Internal-Colored"));
        directionArrow.startColor    = Color.cyan;
        directionArrow.endColor      = Color.blue;
    }

    void Update()
    {
        float translationDir = 0f;
        float rotationDir    = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)    translationDir += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)  translationDir -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) rotationDir    += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)  rotationDir    -= 1f;

            // B – Blind Mode Toggle (only LiDAR dots visible)
            if (Keyboard.current.bKey.wasPressedThisFrame)
                ToggleBlindMode();

            // P – Force A* repath
            if (Keyboard.current.pKey.wasPressedThisFrame && aStarManager != null)
                aStarManager.CalculatePath();

            // C – Clear all A* data
            if (Keyboard.current.cKey.wasPressedThisFrame && aStarManager != null)
                aStarManager.ClearAll();
        }

        // Movement
        transform.Translate(0, 0, translationDir * moveSpeed * Time.deltaTime);
        transform.Rotate(0, rotationDir * rotationSpeed * Time.deltaTime, 0);

        // Update direction arrow
        if (directionArrow != null)
        {
            Vector3 start = transform.position + Vector3.up * 0.5f;
            directionArrow.SetPosition(0, start);
            directionArrow.SetPosition(1, start + transform.forward * 1.5f);
        }
    }

    // ── Blind Mode ────────────────────────────────────────────────────────────

    void ToggleBlindMode()
    {
        blindMode = !blindMode;
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        if (blindMode)
        {
            // Save current camera state
            savedCullingMask  = mainCam.cullingMask;
            savedClearFlags   = mainCam.clearFlags;
            savedBgColor      = mainCam.backgroundColor;
            savedStateValid   = true;

            // Determine which layer the LiDAR point cloud lives on
            int lidarLayerMask = (lidar != null)
                ? (1 << lidar.lidarLayer)
                : (1 << 1); // default TransparentFX (layer 1)

            // Also keep the UI layer (layer 5) so OnGUI minimap stays visible
            int uiLayerMask = (1 << 5);

            // Show ONLY LiDAR point cloud + UI — black everything else out
            mainCam.cullingMask      = lidarLayerMask | uiLayerMask;
            mainCam.clearFlags       = CameraClearFlags.SolidColor;
            mainCam.backgroundColor  = Color.black;

            Debug.Log("[BlindMode] ON — LiDAR dots only.");
        }
        else
        {
            if (savedStateValid)
            {
                mainCam.cullingMask     = savedCullingMask;
                mainCam.clearFlags      = savedClearFlags;
                mainCam.backgroundColor = savedBgColor;
                savedStateValid         = false;
            }
            Debug.Log("[BlindMode] OFF — scene restored.");
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Wall") || collision.gameObject.tag == "Obstacle")
            obstacleHits++;
    }
}
