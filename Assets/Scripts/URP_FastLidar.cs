using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class URP_FastLidar : MonoBehaviour
{
    [Header("Lidar Specs")]
    public int horizontalResolutions = 1080; // High resolution: 3 rays per degree to shoot clearly through open doors
    public int verticalResolutions = 64;     // High resolution vertical density
    public float verticalFov = 30f;
    public float maxRange = 50f;
    public float updateHz = 15f;
    
    [Header("Rendering")]
    [Tooltip("Uses Custom/URPPointShader by default to fix URP scale/flickering issues!")]
    public Material pointCloudMaterial;

    [Header("Lidar Mode [Press L]")]
    [Tooltip("The layer for the Point Cloud. Default is Layer 1 (TransparentFX).")]
    public int lidarLayer = 1;
    
    private bool lidarModeActive = false;
    private Camera mainCamera;
    private int originalCullingMask;
    private CameraClearFlags originalClearFlags;
    private Color originalBackgroundColor;

    private Mesh pointMesh;
    private float nextUpdate = 0f;

    /// <summary>Time.time when the last full scan batch completed. Use this to avoid re-processing stale results.</summary>
    public float LastScanTime { get; private set; } = -1f;

    private NativeArray<RaycastCommand> commands;
    private NativeArray<RaycastHit> results;
    private Vector3[] vertices;
    private Color[] colors;
    private int[] indices;
    
    void Start()
    {
        int numPoints = horizontalResolutions * verticalResolutions;
        commands = new NativeArray<RaycastCommand>(numPoints, Allocator.Persistent);
        results = new NativeArray<RaycastHit>(numPoints, Allocator.Persistent);
        
        vertices = new Vector3[numPoints];
        colors = new Color[numPoints];
        indices = new int[numPoints];
        for(int i=0; i<numPoints; i++) indices[i] = i;

        pointMesh = new Mesh();
        pointMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        pointMesh.MarkDynamic();
        
        GetComponent<MeshFilter>().mesh = pointMesh;
        
        // Auto-assign the custom PSIZE shader to FIX THE FLICKERING
        if (pointCloudMaterial == null) 
        {
            Shader pointShader = Shader.Find("Custom/URPPointShader");
            if(pointShader != null) {
                pointCloudMaterial = new Material(pointShader);
            } else {
                Debug.LogError("Custom/URPPointShader missing! Please ensure the shader file exists.");
                pointCloudMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            }
            GetComponent<MeshRenderer>().material = pointCloudMaterial;
        } else {
            GetComponent<MeshRenderer>().material = pointCloudMaterial;
        }

        // Put the object on a specific layer so we can isolate it later!
        gameObject.layer = lidarLayer;
    }

    void Update()
    {
        HandleLidarModeToggle();

        if (Time.time < nextUpdate) return;
        nextUpdate = Time.time + 1f / updateHz;

        int numPoints = horizontalResolutions * verticalResolutions;
        float vFovStart = -verticalFov / 2f;
        float vFovStep = verticalFov / (verticalResolutions - 1);
        float hFovStep = 360f / horizontalResolutions;

        int index = 0;
        for (int v = 0; v < verticalResolutions; v++)
        {
            float vAngle = vFovStart + v * vFovStep;
            for (int h = 0; h < horizontalResolutions; h++)
            {
                float hAngle = h * hFovStep;
                Vector3 dir = transform.rotation * Quaternion.Euler(vAngle, hAngle, 0) * Vector3.forward;
                
                // Do not raycast against the lidar layer itself, or Unity's "Ignore Raycast" layout (Layer 2)
                int ignoreLayer = ~((1 << lidarLayer) | (1 << 2)); 
                commands[index] = new RaycastCommand(transform.position, dir, new QueryParameters(ignoreLayer, false, QueryTriggerInteraction.Ignore, false), maxRange);
                index++;
            }
        }

        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 64);
        handle.Complete();

        index = 0;
        for (int v = 0; v < verticalResolutions; v++)
        {
            for (int h = 0; h < horizontalResolutions; h++)
            {
                RaycastHit hit = results[index];
                if (hit.collider != null)
                {
                    vertices[index] = transform.InverseTransformPoint(hit.point);
                    float intensity = 1.0f - (hit.distance / maxRange);
                    // Blue far away, Red close up
                    colors[index] = Color.Lerp(Color.blue, Color.red, intensity);
                }
                else
                {
                    vertices[index] = Vector3.zero;
                    colors[index] = Color.clear;
                }
                index++;
            }
        }

        pointMesh.vertices = vertices;
        pointMesh.colors = colors;
        pointMesh.SetIndices(indices, MeshTopology.Points, 0);
        pointMesh.RecalculateBounds();
        LastScanTime = Time.time;   // signal that new data is ready
    }

    private void HandleLidarModeToggle()
    {
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            lidarModeActive = !lidarModeActive;

            if (lidarModeActive)
            {
                // Save original view configurations
                originalCullingMask = mainCamera.cullingMask;
                originalClearFlags = mainCamera.clearFlags;
                originalBackgroundColor = mainCamera.backgroundColor;

                // Engage Lidar Mode -> Only render the Lidar layer over absolute black!
                mainCamera.cullingMask = (1 << lidarLayer);
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = Color.black;
            }
            else
            {
                // Restore physical simulation view
                mainCamera.cullingMask = originalCullingMask;
                mainCamera.clearFlags = originalClearFlags;
                mainCamera.backgroundColor = originalBackgroundColor;
            }
        }
    }

    void OnDestroy()
    {
        // Safety feature so you don't stay stuck in the void if the script stops
        if (lidarModeActive && mainCamera != null) {
            mainCamera.cullingMask = originalCullingMask;
            mainCamera.clearFlags = originalClearFlags;
            mainCamera.backgroundColor = originalBackgroundColor;
        }

        if (commands.IsCreated) commands.Dispose();
        if (results.IsCreated) results.Dispose();
    }

    /// <summary>Read-only access to the latest batch of RaycastHit results for external mapping/pathfinding consumers.</summary>
    public NativeArray<RaycastHit> GetResults() => results;
}
