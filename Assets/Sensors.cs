using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;

[System.Serializable]
public struct PositionData {
    public float x;
    public float y;
    public float z;
}

[System.Serializable]
public class SensorPayload {
    public float[] distances;
    public string[] tags;
    public PositionData position;
    public float rotation_y;
}

[System.Serializable]
public class SteeringPush {
    public string action;
    public float turn_value; 
}

[System.Serializable]
public class HapticCommands {
    public float[] ambient_belt_intensities;
    public SteeringPush steering_push;
}

[System.Serializable]
public class BrainResponse {
    public string status;
    public HapticCommands haptic_commands;
}

public class Sensors : MonoBehaviour
{
    [Header("Sensor Configuration")]
    public int numZones = 16;
    public float maxDistance = 5.0f; // Flask backend processes up to 5m
    public float displayRadius = 2.0f; // Visual lines only draw out to 2m
    public float sendInterval = 0.1f;
    public string endpointUrl = "http://127.0.0.1:5050/sensordata";
    
    [Header("Links")]
    public PlayerMovement playerMovement;

    private float timer = 0f;
    public string currentSteeringAdvice = "Disabled (Starting up...)";

    // Visual glowing laser lines for the game view
    private LineRenderer[] rayLines;

    void Start() {
        if(playerMovement == null) {
            playerMovement = GetComponent<PlayerMovement>();
        }

        // Instantiate 16 physical LineRenderers to act as the visual "Aura"
        rayLines = new LineRenderer[numZones];
        for (int i = 0; i < numZones; i++) {
            GameObject lineObj = new GameObject($"RayLine_{i}");
            lineObj.transform.SetParent(this.transform);
            
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.startWidth = 0.05f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            
            // Robust primitive shader so it renders universally
            lr.material = new Material(Shader.Find("Hidden/Internal-Colored"));
            rayLines[i] = lr;
        }
    }

    void Update()
    {
        // 1. Send data to Flask Brain continuously
        timer += Time.deltaTime;
        if (timer >= sendInterval)
        {
            timer = 0f;
            StartCoroutine(ScanAndSend());
        }

        // 2. Smoothly Update the 16 Aura Lines every frame
        float angleStep = 360f / numZones;
        for (int i = 0; i < numZones; i++)
        {
            float currentAngle = i * angleStep;
            Vector3 direction = Quaternion.Euler(0, currentAngle, 0) * transform.forward;

            float currentDist = maxDistance;
            RaycastHit hit;
            if (Physics.Raycast(transform.position, direction, out hit, maxDistance)) {
                currentDist = hit.distance;
            }

            // Bind the visual laser length to max 2.0 meters, shrinking if object is close!
            float renderDist = Mathf.Min(currentDist, displayRadius);
            
            Color lineColor = Color.green;
            if (currentDist < displayRadius) {
                // If it hits inside 2.0m, it lerps from Yellow to Red as it gets dangerously close
                lineColor = Color.Lerp(Color.red, Color.yellow, (currentDist - 0.5f) / 1.5f);
            }

            LineRenderer lr = rayLines[i];
            lr.SetPosition(0, transform.position); // Origin is the capsule
            lr.SetPosition(1, transform.position + direction * renderDist); // End tip is the collision
            
            lr.startColor = lineColor;
            lr.endColor = lineColor;
        }
    }

    IEnumerator ScanAndSend()
    {
        SensorPayload payload = new SensorPayload();
        payload.distances = new float[numZones];
        payload.tags = new string[numZones];
        
        Vector3 pos = transform.position;
        payload.position = new PositionData { x = pos.x, y = pos.y, z = pos.z };
        payload.rotation_y = transform.eulerAngles.y;

        float angleStep = 360f / numZones;

        for (int i = 0; i < numZones; i++)
        {
            float currentAngle = i * angleStep;
            Vector3 direction = Quaternion.Euler(0, currentAngle, 0) * transform.forward;

            RaycastHit hit;
            if (Physics.Raycast(transform.position, direction, out hit, maxDistance)) {
                payload.distances[i] = hit.distance;
                payload.tags[i] = string.IsNullOrEmpty(hit.collider.tag) ? "Untagged" : hit.collider.tag;
            } else {
                payload.distances[i] = maxDistance;
                payload.tags[i] = "None";
            }
        }

        string json = JsonUtility.ToJson(payload);
        
        using (UnityWebRequest request = new UnityWebRequest(endpointUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                BrainResponse response = JsonUtility.FromJson<BrainResponse>(request.downloadHandler.text);
                if (response != null && response.haptic_commands != null && response.haptic_commands.steering_push != null) {
                    currentSteeringAdvice = response.haptic_commands.steering_push.action;
                }
            }
        }
    }
    
    void OnGUI()
    {
        GUIStyle hdr = new GUIStyle();
        hdr.fontSize = 24;
        hdr.normal.textColor = Color.white;
        
        if (playerMovement != null) {
            GUI.Label(new Rect(10, 10, 600, 30), $"Hits: {playerMovement.obstacleHits} | Blind Mode [B]: {(playerMovement.blindMode ? "ON" : "OFF")}", hdr);
        }

        GUIStyle pilotStyle = new GUIStyle();
        pilotStyle.fontSize = 24;
        pilotStyle.normal.textColor = Color.yellow;
        
        GUI.Label(new Rect(10, 50, 800, 30), $"[PILOT ADVICE]: {currentSteeringAdvice}", pilotStyle);
    }
}
