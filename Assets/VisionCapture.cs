using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using System.Collections;
using System;

[System.Serializable]
public class VisionPayload {
    public string prompt;
    public string[] images_b64;
}

[System.Serializable]
public class VLMResponse {
    public string status;
    public int camera_index;
    public float target_x;
    public float target_y;
    public string message;
}

public class VisionCapture : MonoBehaviour {
    [Header("Network")]
    public string endpointUrl = "http://127.0.0.1:5050/vlm_target";
    public string hardcodedPrompt = "Find the nearest empty seat.";
    
    [Header("Resolution")]
    public int captureResolution = 512; // 512x512 keeps the AI fast.
    
    private Camera[] rigCameras = new Camera[4];
    private bool isProcessing = false;
    private GameObject redSpherePrefab;

    void Start() {
        // Step 1: Build the 4-camera Rig programmatically
        string[] names = {"FrontCam_0", "RightCam_1", "BackCam_2", "LeftCam_3"};
        float[] rotations = {0f, 90f, 180f, 270f};

        for(int i=0; i<4; i++) {
            GameObject camObj = new GameObject(names[i]);
            camObj.transform.SetParent(this.transform);
            camObj.transform.localPosition = Vector3.up * 0.5f; // Eye level
            camObj.transform.localRotation = Quaternion.Euler(0, rotations[i], 0);
            
            Camera cam = camObj.AddComponent<Camera>();
            cam.fieldOfView = 90f; // Perfect 90 corner-to-corner math
            
            // Disable so they heavily save FPS overhead until we press T!
            cam.enabled = false; 
            rigCameras[i] = cam;
        }

        // Generate the Target Lock visualizer primitive
        redSpherePrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        redSpherePrefab.name = "TargetLockVisual";
        redSpherePrefab.GetComponent<Renderer>().material.color = Color.red;
        redSpherePrefab.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
        redSpherePrefab.GetComponent<Renderer>().material.SetColor("_EmissionColor", Color.red * 2.0f);
        redSpherePrefab.transform.localScale = Vector3.one * 0.5f;
        Destroy(redSpherePrefab.GetComponent<Collider>()); // Don't block raycasts!
        redSpherePrefab.SetActive(false); 
    }

    void Update() {
        // [MOCK VLM] Let the user manually click to place the target in the 3D globe!
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) {
            if (Camera.main != null) {
                Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 200f)) {
                    redSpherePrefab.SetActive(true);
                    redSpherePrefab.transform.position = hit.point;
                    Debug.Log(">>> [MOCK VLM TARGET] Manually placed Red Sphere at XYZ: " + hit.point);
                }
            }
        }

        // Real Network Request
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame && !isProcessing) {
            StartCoroutine(CaptureTargetCommand());
        }
    }

    IEnumerator CaptureTargetCommand() {
        isProcessing = true;
        Debug.Log("[VISION] Capturing 360 surround view...");

        VisionPayload payload = new VisionPayload();
        payload.prompt = hardcodedPrompt;
        payload.images_b64 = new string[4];

        for (int i=0; i<4; i++) {
            RenderTexture rt = new RenderTexture(captureResolution, captureResolution, 24);
            rigCameras[i].targetTexture = rt;
            rigCameras[i].Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(captureResolution, captureResolution, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, captureResolution, captureResolution), 0, 0);
            tex.Apply();

            rigCameras[i].targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);

            byte[] bytes = tex.EncodeToJPG();
            payload.images_b64[i] = Convert.ToBase64String(bytes);
            Destroy(tex);
        }

        Debug.Log("[VISION] Sent to Python MLX VLM. Awaiting GPU inference...");

        string json = JsonUtility.ToJson(payload);
        using (UnityWebRequest request = new UnityWebRequest(endpointUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // Give the VLM 120 seconds maximum to think
            request.timeout = 120; 

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success) {
                Debug.LogError("VLM Network Error: " + request.error);
            } else {
                VLMResponse res = JsonUtility.FromJson<VLMResponse>(request.downloadHandler.text);
                
                if (res.status == "success") {
                    Debug.Log($"[VISION] AI Target Found! Cam Index: [{res.camera_index}] at Pixel: ({res.target_x}, {res.target_y})");
                    
                    // Core Math Context: Map 2D AI back to 3D Physical Space
                    Camera targetCam = rigCameras[res.camera_index];
                    
                    // VLM & Computer Vision systems return (0,0) at TOP-LEFT. 
                    // Unity ScreenPointToRay math expects (0,0) at BOTTOM-LEFT.
                    float unityInvertedY = captureResolution - res.target_y;
                    
                    Ray ray = targetCam.ScreenPointToRay(new Vector3(res.target_x, unityInvertedY, 0));
                    RaycastHit hit;
                    
                    if (Physics.Raycast(ray, out hit, 100f)) {
                        Debug.Log(">>> [TARGET LOCK] Spawning Red Sphere at XYZ: " + hit.point);
                        redSpherePrefab.SetActive(true);
                        redSpherePrefab.transform.position = hit.point;
                    } else {
                        Debug.LogWarning("VLM found an object, but the physical 3D Raycast missed/shot into infinite space!");
                    }
                } else {
                    Debug.LogWarning("[VISION] VLM Failed to find target: " + res.message);
                }
            }
        }
        isProcessing = false;
    }

    void OnGUI() {
        GUIStyle style = new GUIStyle();
        style.fontSize = 28;
        style.normal.textColor = Color.magenta;
        
        string status = isProcessing ? "AI SCANNING 360°... (Please wait ~15s)" : "[Click Mouse] to manually place target  |  [Press T] Real VLM AI";
        GUI.Label(new Rect(10, 100, 1000, 50), status, style);
    }

    public bool HasTarget() {
        return redSpherePrefab != null && redSpherePrefab.activeSelf;
    }

    public Vector3 GetTargetPosition() {
        return redSpherePrefab.transform.position;
    }
}
