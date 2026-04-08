using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(Camera))]
public class VisionCapture : MonoBehaviour
{
    [Header("Camera Switching")]
    [Tooltip("Leave this blank and the script will find your Main Camera automatically")]
    public Camera mainCamera;
    
    [Header("Networking Settings")]
    public string serverUrl = "http://localhost:8000";
    
    private Camera chestCam;
    private bool isMainCameraActive = true;
    
    // UI state
    private bool isCapturing = false;
    private bool showSuccessMessage = false;
    private float successMessageTimer = 0f;
    private const float messageDuration = 3.0f; // 3 seconds
    private GUIStyle textStyle;

    void Start()
    {
        chestCam = GetComponent<Camera>();

        // Try to find the Main Camera automatically if not linked in the Inspector
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // Default state: Main Camera is ON, Chest Camera is OFF (running in background only)
        if (mainCamera != null) mainCamera.enabled = true;
        chestCam.enabled = false;
        isMainCameraActive = true;

        // Set up the simple white text style to match the other text
        textStyle = new GUIStyle();
        textStyle.fontSize = 16;
        textStyle.normal.textColor = Color.white;
        textStyle.fontStyle = FontStyle.Bold;
    }

    void Update()
    {
        // Handle UI timer
        if (showSuccessMessage)
        {
            successMessageTimer -= Time.deltaTime;
            if (successMessageTimer <= 0)
            {
                showSuccessMessage = false;
            }
        }

        if (Keyboard.current != null)
        {
            // Capture image manually in the background
            if (Keyboard.current.vKey.wasPressedThisFrame && !isCapturing)
            {
                StartCoroutine(CaptureAndSend());
            }

            // Toggle Camera View to see what the chest actually sees
            if (Keyboard.current.kKey.wasPressedThisFrame)
            {
                isMainCameraActive = !isMainCameraActive;
                
                if (mainCamera != null) 
                    mainCamera.enabled = isMainCameraActive;
                    
                chestCam.enabled = !isMainCameraActive; // Activates the chest camera to become the active display
            }
        }
    }

    void OnGUI()
    {
        if (textStyle == null) return;

        string displayText;

        if (isCapturing)
        {
            displayText = "[V] Capturing... | [K] Toggle Camera";
        }
        else
        {
            displayText = "[V] Capture Vision | [K] Toggle Camera";
            if (showSuccessMessage)
            {
                displayText += " (Image Saved!)";
            }
        }

        // Positioned at X=10, Y=85 to sit precisely under the 3rd line of your existing text
        GUI.Label(new Rect(10, 85, 600, 40), displayText, textStyle);
    }

    IEnumerator CaptureAndSend()
    {
        isCapturing = true;

        string[] labels = { "front", "right", "rear", "left" };
        float[] angles = { 0f, 90f, 180f, 270f };
        
        // Save base rotation to restore later
        Quaternion originalRotation = chestCam.transform.localRotation;
        
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        int successCount = 0;

        int resWidth = Screen.width;
        int resHeight = Screen.height;
        RenderTexture rt = new RenderTexture(resWidth, resHeight, 24);

        for (int i = 0; i < 4; i++)
        {
            // Rotate 90 degrees clockwise for each picture
            chestCam.transform.localRotation = originalRotation * Quaternion.Euler(0, angles[i], 0);
            
            // Wait for the renderer to process the new camera transform
            yield return new WaitForEndOfFrame();
            
            chestCam.targetTexture = rt;
            Texture2D screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
            chestCam.Render();
            
            RenderTexture.active = rt;
            screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
            screenShot.Apply();
            
            chestCam.targetTexture = null;
            RenderTexture.active = null; 

            byte[] imageBytes = screenShot.EncodeToPNG();
            Destroy(screenShot);

            UnityWebRequest request = new UnityWebRequest(serverUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(imageBytes);
            request.uploadHandler.contentType = "image/png";
            
            // Send Direction and Timestamp so Python can name the file properly
            request.SetRequestHeader("Direction", labels[i]);
            request.SetRequestHeader("Capture-Time", timestamp);
            
            request.downloadHandler = new DownloadHandlerBuffer();
            
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VisionCapture] Server Error ({labels[i]}): {request.error}");
            }
            else
            {
                successCount++;
            }
        }

        // Cleanup
        Destroy(rt);
        chestCam.transform.localRotation = originalRotation;

        if (successCount > 0)
        {
            Debug.Log($"[VisionCapture] Success: {successCount}/4 images sent to server.");
            showSuccessMessage = true;
            successMessageTimer = messageDuration;
        }

        isCapturing = false;
    }
}
