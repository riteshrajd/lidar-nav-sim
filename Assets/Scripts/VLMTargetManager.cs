using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages targets identified by the VLM.
/// Projects 2D grid coordinates from a past snapshot into 3D world space.
/// </summary>
public class VLMTargetManager : MonoBehaviour
{
    public static VLMTargetManager Instance { get; private set; }

    [Header("Settings")]
    public float gridSize = 10.0f;

    [Header("References")]
    public Camera chestCamera;

    private Vector3 currentTargetWorldPos;
    private bool hasActiveTarget = false;

    [Serializable]
    public class VLMResponse
    {
        public bool target_spotted;
        public string target_name;
        public string grid_position;
        public string direction;
        public float distance_meters;
        public string next_action;
        public string what_i_see;
    }

    void Awake()
    {
        Instance = this;
        if (chestCamera == null) chestCamera = GetComponent<Camera>();
        if (chestCamera == null) chestCamera = GetComponentInChildren<Camera>();
    }

    public void ProcessVLMResponse(string json, Vector3 snapshotPos, Quaternion snapshotRot)
    {
        try
        {
            VLMResponse response = JsonUtility.FromJson<VLMResponse>(json);

            if (response != null && response.target_spotted)
            {
                PlaceTarget(response, snapshotPos, snapshotRot);
            }
            else
            {
                Debug.Log("[VLMTargetManager] No target spotted in VLM response.");
                ClearVLMTarget();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[VLMTargetManager] Error parsing JSON: {e.Message}\nJSON: {json}");
        }
    }

    private void PlaceTarget(VLMResponse response, Vector3 snapshotPos, Quaternion snapshotRot)
    {
        // 1. Parse Grid Coordinates (e.g. "3.5, 4.2")
        string[] parts = response.grid_position.Split(',');
        if (parts.Length != 2) return;

        float gx = float.Parse(parts[0].Trim());
        float gy = float.Parse(parts[1].Trim());

        // 2. Convert to Viewport Coordinates (Normalized 0-1)
        float u = gx / gridSize;
        float v = 1.0f - (gy / gridSize);

        // 3. Create projection ray relative to the SNAPSHOT pose
        Vector3    originalPos = chestCamera.transform.position;
        Quaternion originalRot = chestCamera.transform.rotation;

        chestCamera.transform.position = snapshotPos;
        chestCamera.transform.rotation = snapshotRot;

        Ray wallRay = chestCamera.ViewportPointToRay(new Vector3(u, v, 0));

        // Restore camera pose
        chestCamera.transform.position = originalPos;
        chestCamera.transform.rotation = originalRot;

        // 4. Place marker at the estimated distance
        float distance = response.distance_meters;
        if (distance <= 0) distance = 5f;

        currentTargetWorldPos = snapshotPos + wallRay.direction * distance;
        hasActiveTarget = true;

        // 5. TRIGGER PATHFINDING (NOW HANDLING MARKER VISUAL)
        PathFinder pathFinder = FindAnyObjectByType<PathFinder>();
        if (pathFinder != null)
        {
            pathFinder.realtimePath = true; // Force realtime mode
            pathFinder.SetTarget(currentTargetWorldPos);
            Debug.Log("[VLMTargetManager] PathFinder target set and realtime mode enabled.");
        }

        Debug.Log($"[VLMTargetManager] Target '{response.target_name}' projected to {currentTargetWorldPos}. Est Distance: {distance}m");
        Debug.Log($"[VLMTargetManager] Assistant says: {response.next_action}");
    }

    public void ClearVLMTarget()
    {
        hasActiveTarget = false;
        Debug.Log("[VLMTargetManager] Target state cleared.");
    }

    public bool HasTarget => hasActiveTarget;
    public Vector3 TargetPosition => currentTargetWorldPos;

    void OnGUI()
    {
        if (!hasActiveTarget) return;

        // Simple HUD indicator
        var style = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold };
        style.normal.textColor = new Color(1f, 0.55f, 0.05f);

        Vector3 screenPos = Camera.main.WorldToScreenPoint(currentTargetWorldPos);
        if (screenPos.z > 0)
        {
            GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y - 40, 200, 30), "VLM TARGET", style);
        }
    }
}
