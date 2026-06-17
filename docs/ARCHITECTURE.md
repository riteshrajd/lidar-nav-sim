# Haptic-Nav Sim — Full Architecture

This document describes the complete project structure and architecture for anyone who wants to understand, fork, or reuse specific parts of this project.

---

## Project Structure

```
Haptic-Nav-Sim/                     ← Unity project root
│
├── Assets/                         ← Unity assets
│   ├── OccupancyGrid.cs            ← Core: Live 2D LiDAR mapping
│   ├── PathFinder.cs               ← Core: Theta* pathfinding
│   ├── PlayerMovement.cs           ← Core: WASD + Blind Mode
│   ├── GenericLidarRenderer.cs     ← Legacy (unused)
│   ├── Scripts/
│   │   ├── URP_FastLidar.cs        ← LiDAR sensor (Job System)
│   │   ├── VisionCapture.cs        ← 4-directional image capture
│   │   ├── VLMTargetManager.cs     ← VLM target 2D→3D projection
│   │   └── Utils.cs                ← Shared helpers
│   ├── Shaders/
│   │   └── URPPointShader.shader   ← Custom URP point cloud shader
│   ├── Scenes/                     ← Unity scene files
│   ├── Materials/                  ← Materials
│   └── Settings/                   ← URP render pipeline settings
│
├── Python-Server/                  ← Python AI backend
│   ├── requirements.txt            ← pip dependencies
│   ├── readme.md                   ← Python-side readme
│   ├── src/
│   │   ├── pipeline/
│   │   │   ├── pipeline_server.py  ← HTTP server (port 8000)
│   │   │   ├── main_pipeline.py    ← Orchestrates VLM + gridding
│   │   │   ├── step1_grid.py       ← Overlays coordinate grid on image
│   │   │   ├── step2_vision.py     ← Gemini VLM inference
│   │   │   ├── step3_parser.py     ← Parses + validates JSON response
│   │   │   └── media/              ← Image I/O (gitignored)
│   │   ├── add_grid.py             ← Standalone grid overlay script
│   │   └── manual_vision_sim.py    ← Standalone VLM test script
│   └── modules/                    ← Extra/experimental modules
│
├── docs/                           ← Project documentation
│   ├── ARCHITECTURE.md             ← This file
│   ├── UNITY_SETUP.md              ← Unity setup guide
│   ├── PYTHON_SETUP.md             ← Python backend setup guide
│   ├── HOW_IT_WORKS.md             ← Technical deep-dive
│   └── vlm_navigation_plan.md      ← Research roadmap
│
├── ProjectSettings/                ← Unity project settings (committed)
├── Packages/manifest.json          ← Unity package dependencies
├── README.md                       ← Project overview
├── LICENSE                         ← MIT License
└── .gitignore                      ← Ignore rules
```

---

## System Overview

### Two Completely Independent Systems

The project has two independent navigation systems that work in complementary ways:

#### 1. LiDAR Safety Bubble (Local, Real-time)
- **Script**: `URP_FastLidar.cs` + `OccupancyGrid.cs` + `PathFinder.cs`
- **What it does**: Fires ~11,500 raycasts per frame using Unity's Job System to build a live 2D map of the immediate surroundings (floor, walls, obstacles).
- **Output**: A minimap overlay showing explored area and a Theta* path to a manual or VLM-set target.
- **Range**: ~50m radius, updates at 15Hz.
- **Use case**: Prevent walking into walls, find path around furniture.

#### 2. VLM Semantic Compass (Global, On-Demand)
- **Scripts**: `VisionCapture.cs` + `VLMTargetManager.cs` + Python server
- **What it does**: On button press (`V`), snaps 4 camera views, sends the front view to a Google Gemini VLM, which identifies the user's requested target on a coordinate grid and returns JSON.
- **Output**: A 3D orange sphere marker in the Unity world + Theta* path to it.
- **Latency**: 2-5 seconds for VLM inference.
- **Use case**: "Take me to the washroom", "Find the exit", "Where is the elevator?"

---

## Data Flow

```
User presses [V]
      │
      ▼
VisionCapture.cs
  ├── Records "Snapshot Pose" (position + rotation at this exact moment)
  ├── Rotates ChestCamera 0°, 90°, 180°, 270° — captures 4 PNG frames into memory
  └── Sends all 4 to pipeline_server.py (HTTP POST, port 8000)
      │
      ▼
pipeline_server.py (Python)
  └── Triggers main_pipeline.py for the "front" view only
        │
        ▼
  step1_grid.py
    └── Draws 10×10 coordinate grid overlay on the image
        │
        ▼
  step2_vision.py
    └── Sends gridded image + prompt to Gemini 2.5 Flash API
        └── Returns structured JSON:
            { "target_spotted": true,
              "target_name": "washroom door",
              "grid_position": "6.2, 4.8",
              "distance_meters": 7.5,
              "next_action": "Walk forward" }
        │
        ▼
  step3_parser.py
    └── Validates and cleans the JSON response
        │
        ▼
pipeline_server.py → HTTP 200 response with JSON body
      │
      ▼
VisionCapture.cs (Unity)
  └── Passes JSON + Snapshot Pose to VLMTargetManager.cs
        │
        ▼
VLMTargetManager.cs
  ├── Parses grid_position (e.g. "6.2, 4.8") → Viewport UV (u=0.62, v=0.52)
  ├── Uses Snapshot camera pose to cast ViewportPointToRay
  ├── Places orange marker at: SnapshotPos + RayDir * distance_meters
  └── Calls PathFinder.SetTarget() → Theta* path drawn on minimap
```

---

## Key Design Decisions

### Why "Snapshot Pose"?
VLM inference takes 2-5 seconds. The user will physically move during this time. By recording the exact camera position and rotation *at the moment of capture*, the target is projected correctly relative to where the photo was taken, not where the user is standing when the result arrives.

### Why LiDAR + VLM (not just one)?
- **VLM alone**: Cannot detect obstacles in real-time. Would walk you into a wall.
- **LiDAR alone**: Has limited range, cannot understand semantics ("which door is the exit?").
- **Together**: LiDAR handles the 0-50m safety bubble. VLM handles the big picture ("I am in a mall, the exit is past the food court").

### Why Cloud VLM (Gemini)?
For a proof-of-concept with limited hardware, cloud inference is the only viable option. The architecture is intentionally designed to swap out `step2_vision.py` for a local model (LLaVA, MLX Gemma, etc.) when running on an edge device.

---

## License
MIT — See [LICENSE](../LICENSE).
