# LiDAR Nav Sim

A Unity 6 simulation of autonomous navigation using a real-time LiDAR point cloud.  
The player physically moves through an indoor environment while the LiDAR sensor scans and builds a live 2D occupancy map. Designed as a research platform for blind-navigation and pathfinding systems.

---

## Features (current stage)

| Feature | Key | Description |
|---------|-----|-------------|
| LiDAR Point Cloud | — | 360° × 32-ring URP point cloud rendered in real time |
| Blind Mode | `B` | Hides all geometry; shows only LiDAR dots on black |
| 2D Occupancy Map | `M` | Live minimap built from LiDAR hits as you explore |
| Pathfinding | Left-click | Click in 3D to set target; Theta* path shown on minimap |
| Real-time Repath | `T` | Path updates automatically as new obstacles are discovered |
| Force Repath | `P` | Recalculate path immediately |
| Clear Map | `C` | Wipe explored data; target stays, path rebuilds as you re-explore |

**WASD / Arrow Keys** — move & rotate the player

---

## Prerequisites

| Requirement | Version |
|-------------|---------|
| Unity | **6.0.x (6000.4.0f1)** or later |
| Render Pipeline | **Universal Render Pipeline (URP)** |
| Input System | **New Input System** (`UnityEngine.InputSystem`) |
| Collections | `Unity.Collections` (included with Unity) |
| Jobs | `Unity.Jobs` (included with Unity) |

> ⚠️ This project will NOT work with the Built-in Render Pipeline. URP is required for the point cloud shader.

---

## Project Setup from Scratch

### Step 1 — Create the Unity Project

1. Open Unity Hub → **New Project**
2. Choose **3D (URP)** template
3. Name it `LiDAR-Nav-Sim` (or clone this repo directly)

If cloning:
```bash
git clone https://github.com/riteshrajd/lidar-nav-sim.git
```
Then open the folder in Unity Hub as an existing project.

---

### Step 2 — Enable the New Input System

1. **Edit → Project Settings → Player**
2. Under **Other Settings → Active Input Handling** → set to **"Both"** or **"New Input System Package"**
3. Unity will ask to restart — click **Yes**

---

### Step 3 — Scene Setup

The scene needs three things: a **Player**, a **LidarSensor** child, and an **OccupancyGrid** script.

#### 3a. Create the Player

1. **Hierarchy → right-click → 3D Object → Capsule** → rename to `Player`
2. Add Component → **Rigidbody**
   - Angular Drag: `10` (helps dampen any residual spin)
   - Interpolate: **Interpolate**
   - Collision Detection: **Discrete**
3. Add Component → **`PlayerMovement`** (WASD + Blind Mode)
4. Add Component → **`OccupancyGrid`** (LiDAR map + minimap)
5. Add Component → **`PathFinder`** (Theta* path + click-to-target)
6. Create a child **Camera** → position at `(0, 0.6, 0)` → **set Tag to `MainCamera`**
   *(Select Camera → Inspector → Tag dropdown → MainCamera)*

#### 3b. Create the LidarSensor

1. Inside `Player` → **right-click → Create Empty** → rename to `LidarSensor`

   > ⚠️ **If you already have a `LidarSensor` with old components** (e.g. `Generic Lidar Renderer`, `Mesh Filter`, `Mesh Renderer`), remove them first:
   > Right-click each component header in the Inspector → **Remove Component**

2. Add Component → **`URP_FastLidar`**
3. In the URP_FastLidar Inspector:
   - **Horizontal Resolutions**: `360`
   - **Vertical Resolutions**: `32`
   - **Vertical FOV**: `30`
   - **Max Range**: `50`
   - **Update Hz**: `15`
   - **Lidar Layer**: `1` (TransparentFX — Unity default, keeps the point cloud on its own render layer)
   - **Point Cloud Material**: leave blank (auto-assigns `Custom/URPPointShader`)

> The `PlayerMovement` script will **automatically mount** the LidarSensor as a child of the Player at runtime, even if it isn't already. No manual parenting needed beyond the initial setup.

#### 3c. Wire up the Inspector references

| GameObject | Component | Field | Assign |
|------------|-----------|-------|--------|
| Player | `PlayerMovement` | **Lidar** | drag `LidarSensor` |
| Player | `OccupancyGrid` | **Lidar** | drag `LidarSensor` |
| Player | `OccupancyGrid` | **Player** | drag `Player` |
| Player | `PathFinder` | **Grid** | drag `Player` (has OccupancyGrid on it) |
| Player | `PathFinder` | **View Camera** | drag the child `Camera` |

> All fields also **auto-find** at runtime via `GetComponentInChildren` / `FindAnyObjectByType`, so assigning in the Inspector is optional but recommended for clarity.

---

### Step 4 — The Point Cloud Shader

The LiDAR renders as a `MeshTopology.Points` mesh. Unity URP doesn't support `gl_PointSize` by default, so a custom shader is required.

**File:** `Assets/Shaders/URPPointShader.shader`

This is already included in the repo. Unity will find it automatically via:
```csharp
Shader.Find("Custom/URPPointShader")
```

If the shader is missing, `URP_FastLidar` will fall back to `Universal Render Pipeline/Particles/Unlit` and log an error.

---

### Step 5 — Verify the LiDAR Layer

The LidarSensor's GameObject must be on **Layer 1 (TransparentFX)** so:
- Blind Mode (`B`) can isolate it via camera culling mask
- LiDAR rays don't self-intersect with the point cloud mesh

The `URP_FastLidar` script sets this automatically in `Start()`:
```csharp
gameObject.layer = lidarLayer; // default = 1
```

---

### Step 6 — Add a Test Environment

Any closed indoor scene works. The repo includes `Assets/Scenes/SampleScene.unity` which has a multi-room interior.

If building your own:
- Floors, walls, and furniture should all have **Colliders** (so LiDAR rays can hit them)
- Make sure objects are **NOT on Layer 1** (TransparentFX) — that layer is reserved for the point cloud

---

### Step 7 — Vision Capture Integration

To enable capturing raw "vanilla" camera images without LiDAR dots for your Vision-Language Model (VLM) pipeline, follow these very specific steps:

1. **Create the Chest Camera:**
   - In your Hierarchy, right-click on your `Player` object → **Camera**.
   - Rename to `ChestCamera` and set its local position `Y = 1.2` (chest height).
   - In the Camera Inspector, **remove** the `Audio Listener` component so it doesn't conflict with the `MainCamera`.
   - Adjust the **Field of View** slider in the Camera component to change your vision coverage angle (e.g., zoom in or wide-angle).
2. **Hide the LiDAR Dots (CRITICAL STEP):**
   - Click on your `LidarSensor` object and look at the `URP_FastLidar` script settings in the Inspector. At the bottom right, note the **`Lidar Layer = 1`** setting. In Unity, Layer 1 corresponds to the built-in `TransparentFX` layer.
   - Now, click back on your `ChestCamera`.
   - In the Camera component, find the **Culling Mask** dropdown.
   - Open it and **uncheck `TransparentFX`** (which corresponds to Layer 1). This ensures your Chest Camera renders a clean, vanilla view without *any* LiDAR tracking points showing up in your captured frames.
3. **Attach the Script & UI Alignment:**
   - Add the custom `VisionCapture.cs` script to your new `ChestCamera`.
   - Ensure the `ChestCamera` GameObject remains active. `VisionCapture` handles itself elegantly: it executes `chestCam.enabled = false` automatically on `Start()` so it runs completely hidden in the background. It utilizes a custom native Unity rendering trick (`targetTexture`) to extract the frame data without flashing or changing your main game screen output.
   - **UI Integration**: The `VisionCapture.cs` renders HUD text securely at `X=10, Y=85` with a font size of 16. This aligns magically right beneath the `Target: none...` line of the original Occupancy Grid text, framing it as one uniform, centralized readout!
4. **Start the Python Server:**
   - Open a native terminal and navigate to the `Python-Scripts/` folder.
   - Run the provided networking server: `python3 vision_server.py`.
   - It runs natively on `.localhost:8000` via Python's `http.server` library (meaning: zero pip installation dependencies), saving valid hits directly to `media/visioncapture`.
5. **Operation in Play Mode:**
   - Press **[V]** to silently snap an image in the background. It POSTs over HTTP straight to your Python folder, confirming via an onscreen `(Image Saved!)` notification for 3 seconds.
   - Press **[K]** to actively hot-swap/toggle your local display between the `MainCamera` and `ChestCamera` so you can verify height alignments manually.

---

## How It Works

### LiDAR (URP_FastLidar)
- Fires `360 × 32 = 11,520` rays per scan using Unity's **Job System** (`RaycastCommand.ScheduleBatch`)
- Runs at 15 Hz by default
- Stores raw `RaycastHit[]` results accessible via `GetResults()`
- Signals a new scan is ready via the `LastScanTime` float property

### Occupancy Grid (OccupancyGrid)
- Reads `GetResults()` only when `LastScanTime` changes (no redundant processing)
- Classifies each hit by its Y-offset from the player:

```
0 ────── 0.25m ────── 2.20m ────► ∞
  Floor      Obstacle     Ceiling (ignored)
```

- Stores a `Dictionary<Vector2Int, CellState>` (world XZ → Floor / Obstacle / Unknown)
- **Door frame fix**: cells whose only obstacle hits are above `doorFrameMinHeight` (1.60m) can be corrected to Floor by close-range floor hits
- **Proximity guard**: a Floor cell can only be upgraded to Obstacle by hits within `obstacleCloseRadius` (4.0m). Distant low-resolution hits cannot close a confirmed open path.

### Blind Mode (PlayerMovement)
- Saves and restores the Main Camera's `cullingMask`, `clearFlags`, and `backgroundColor`
- In blind mode: culling mask = only Layer 1 (LiDAR layer) + UI layer
- Restores the full scene mask on toggle-off

---

## Inspector Tuning Reference

### OccupancyGrid
| Field | Default | Effect |
|-------|---------|--------|
| Cell Size | `0.4m` | Resolution of the grid. Smaller = more detail, more cells |
| Obstacle Min Height | `0.25m` | Y offset below which hits = floor |
| Obstacle Ceil Height | `2.20m` | Y offset above which hits = ceiling (ignored) |
| Door Frame Min Height | `1.60m` | Obstacle cells hit only above this height can self-correct to floor |
| Obstacle Close Radius | `4.0m` | Max range for a hit to flip Floor → Obstacle |

### URP_FastLidar
| Field | Default | Effect |
|-------|---------|--------|
| Horizontal Resolutions | `360` | Number of horizontal rays (angular resolution) |
| Vertical Resolutions | `32` | Number of vertical rings |
| Vertical FOV | `30°` | Vertical scan angle spread |
| Max Range | `50m` | How far rays travel |
| Update Hz | `15` | Scans per second |
| Lidar Layer | `1` | Unity render layer for the point cloud |

---

## File Structure

```
Assets/
├── Scripts/
│   ├── URP_FastLidar.cs        ← LiDAR sensor (job-based raycasts + point cloud mesh)
│   ├── VisionCapture.cs        ← Background camera capture over HTTP (V & K hotkeys)
│   └── GenericLidarSensor.cs   ← Legacy sensor (unused in current setup)
├── Shaders/
│   └── URPPointShader.shader   ← Custom PSIZE point cloud shader for URP
├── PlayerMovement.cs           ← WASD movement + Blind Mode toggle
├── OccupancyGrid.cs            ← Live 2D occupancy mapping + minimap HUD
├── GenericLidarRenderer.cs     ← Legacy renderer (unused in current setup)
Packages/
├── manifest.json               ← Package dependencies (restored by Unity automatically)
ProjectSettings/                ← URP config, input system, quality settings
Python-Scripts/
├── vision_server.py            ← Raw python HTTP server on 8000 to save vision capture
└── media/visioncapture/        ← Saved image target directory
```

---

## Roadmap

- [x] LiDAR point cloud (URP, job-based)
- [x] Blind Mode (LiDAR-only camera)
- [x] Real-time 2D occupancy grid from LiDAR hits
- [x] Door/opening detection (door frame correction + proximity guard)
- [x] Live minimap HUD
- [x] Click-to-place target in 3D world
- [x] Theta* pathfinding on the built grid (smooth any-angle paths)
- [x] Real-time path updates as map is explored
- [x] Green path + orange target overlay on minimap
- [ ] Autonomous player movement along path
- [ ] Uneven terrain support (surface normal classification)




took some code from here for lidar :-
https://github.com/aisimulationresearch/Sensor-Simulation-in-Unity/blob/main/bbx_camera.unitypackage