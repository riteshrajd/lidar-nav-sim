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
            if (Keyboard.current.vKey.wasPressedThisFrame)
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

        string displayText = "[V] Capture Vision | [K] Toggle Camera";
        if (showSuccessMessage)
        {
            displayText += " (Image Saved!)";
        }

        // Positioned at X=10, Y=85 to sit precisely under the 3rd line of your existing text
        GUI.Label(new Rect(10, 85, 600, 40), displayText, textStyle);
    }

    IEnumerator CaptureAndSend()
    {
        yield return new WaitForEndOfFrame();

        int resWidth = Screen.width;
        int resHeight = Screen.height;

        RenderTexture rt = new RenderTexture(resWidth, resHeight, 24);
        
        // This is safe: If the chest camera is disabled visually, assigning a target texture 
        // and manually calling Render() snaps a photo in the background without switching your screen!
        chestCam.targetTexture = rt;
        
        Texture2D screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
        chestCam.Render();
        
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
        screenShot.Apply();
        
        chestCam.targetTexture = null;
        RenderTexture.active = null; 
        Destroy(rt);

        byte[] imageBytes = screenShot.EncodeToPNG();
        Destroy(screenShot);

        UnityWebRequest request = new UnityWebRequest(serverUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(imageBytes);
        request.uploadHandler.contentType = "image/png";
        request.downloadHandler = new DownloadHandlerBuffer();
        
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[VisionCapture] Server Error: {request.error}");
        }
        else
        {
            Debug.Log($"[VisionCapture] Success: {request.downloadHandler.text}");
            // Trigger the UI success message
            showSuccessMessage = true;
            successMessageTimer = messageDuration;
        }
    }
}
