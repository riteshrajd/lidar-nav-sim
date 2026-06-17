# Unity Setup Guide

Complete step-by-step guide to set up the Unity simulation from scratch.

---

## Requirements

| Requirement | Version |
|-------------|---------|
| Unity | **6.0.x (6000.4.0f1)** or later |
| Render Pipeline | **Universal Render Pipeline (URP)** |
| Input System | **New Input System** |

> ⚠️ **URP is mandatory**. The LiDAR point cloud uses a custom shader (`Custom/URPPointShader`) that only works with URP.

---

## Step 1 — Open the Project

Clone and open in Unity Hub:
```bash
git clone https://github.com/riteshrajd/lidar-nav-sim.git
```
Then **Add project from disk** in Unity Hub and open the cloned folder.

---

## Step 2 — Enable the New Input System

1. **Edit → Project Settings → Player → Other Settings**
2. **Active Input Handling** → set to **"Both"**
3. Click **Yes** when Unity asks to restart.

---

## Step 3 — The Player Hierarchy

Your `Player` GameObject needs this exact hierarchy:

```
Player (Capsule)
  ├── Camera             ← MainCamera, position Y=0.6
  ├── ChestCamera        ← VLM capture camera, position Y=1.2
  └── LidarSensor        ← Empty GameObject, Layer 1 (TransparentFX)
```

### Components on Player:
- **Rigidbody**: Interpolate=Interpolate, Angular Drag=10
- **`PlayerMovement`**: WASD movement + Blind Mode
- **`OccupancyGrid`**: LiDAR 2D mapping + minimap
- **`PathFinder`**: Theta* navigation
- **`VisionCapture`**: Image capture (attach to ChestCamera, not Player)
- **`VLMTargetManager`**: VLM result handler

### Components on LidarSensor:
- **`URP_FastLidar`**: The LiDAR sensor
  - Horizontal Resolutions: `360`
  - Vertical Resolutions: `32`
  - Vertical FOV: `30`
  - Max Range: `50`
  - Update Hz: `15`
  - Lidar Layer: `1`

---

## Step 4 — Inspector Wiring

| Component | Field | Drag This |
|-----------|-------|-----------|
| `PlayerMovement` | Lidar | `LidarSensor` |
| `OccupancyGrid` | Lidar | `LidarSensor` |
| `OccupancyGrid` | Player | `Player` |
| `PathFinder` | Grid | `Player` |
| `PathFinder` | View Camera | `Camera` (child) |
| `VLMTargetManager` | Chest Camera | `ChestCamera` |

> All fields also auto-resolve at runtime via `FindAnyObjectByType`, so wiring is optional but recommended.

---

## Step 5 — ChestCamera Culling Mask (Critical!)

The ChestCamera must NOT render the LiDAR point cloud (it would confuse the VLM).

1. Click `ChestCamera` in the Hierarchy.
2. In the Camera component, find **Culling Mask**.
3. **Uncheck `TransparentFX`** (Layer 1 = where LiDAR renders).

Now the ChestCamera sees a clean scene without LiDAR dots.

---

## Step 6 — VisionCapture Script Location

Add `VisionCapture.cs` to the **ChestCamera GameObject** (not the Player). It disables the camera's live rendering automatically via `chestCam.enabled = false` on Start, so it only activates when capturing.

---

## Step 7 — The Point Cloud Shader

`Assets/Shaders/URPPointShader.shader` must be present. It's committed to the repo. If Unity complains the shader is missing, re-import the Assets folder.

---

## Controls Reference

| Key | Action |
|-----|--------|
| `WASD` / Arrows | Move + Rotate player |
| `V` | Trigger VLM vision capture + target search |
| `Q` | Clear VLM target, path, and reset nav state |
| `Left Click` | Place manual target (disabled when VLM target is active) |
| `T` | Toggle real-time path recalculation |
| `P` | Force path recalculation |
| `C` | Clear LiDAR map (keeps target, rebuilds path) |
| `B` | Toggle Blind Mode (LiDAR-only view) |
| `M` | Toggle minimap overlay |
| `K` | Switch between MainCamera and ChestCamera views |

---

## Troubleshooting

**LiDAR dots appear in captured image**
→ ChestCamera Culling Mask is including Layer 1 (TransparentFX). Uncheck it.

**"Target" marker doesn't appear after pressing V**
→ Check the Python server is running. Check Unity Console for `[VisionCapture]` error logs.

**Two orange spheres appear**
→ An older version of `VLMTargetManager` created its own sphere. The current version delegates to `PathFinder`. Pull the latest code.

**Path doesn't draw after VLM target is set**
→ Ensure `PathFinder` has a reference to the `Grid` (OccupancyGrid). The VLM result triggers `PathFinder.realtimePath = true` automatically.
