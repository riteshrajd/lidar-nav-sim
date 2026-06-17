# How It Works — Technical Deep-Dive

A detailed explanation of each script and the algorithms used.

---

## 1. LiDAR Sensor (`URP_FastLidar.cs`)

**Approach**: Uses Unity's **Job System** (`RaycastCommand.ScheduleBatch`) to fire all raycasts in parallel on background threads, bypassing the main thread bottleneck.

**Ray Configuration**:
- 360 horizontal rays × 32 vertical rings = **11,520 rays per scan**
- Vertical spread: 30° FOV (±15° from horizontal)
- Max range: 50m
- Fires at 15Hz

**Output**: A `RaycastHit[]` array accessible via `GetResults()`. Also maintains `LastScanTime` as a dirty-flag for downstream consumers.

**Point Cloud Rendering**: Hits are assembled into a `Mesh` with `MeshTopology.Points`. Each point is positioned at the hit location. The custom `URPPointShader.shader` uses `gl_PointSize` to render each vertex as a screen-space circle.

---

## 2. Occupancy Grid (`OccupancyGrid.cs`)

**Approach**: Reads LiDAR hits and classifies them into a 2D dictionary keyed by grid cell (`Vector2Int`).

**Height Classification**:
```
Player Y + 0.0m → +0.25m   = Floor hits (ignored as flat ground)
Player Y + 0.25m → +2.20m  = Obstacle (wall, furniture, person)
Player Y + 2.20m → ∞       = Ceiling (ignored)
```

**Door Frame Fix**: A cell classified as Obstacle purely from hits above 1.6m (e.g., a door lintel) can be downgraded back to Floor if a floor-level hit comes from within 4m. This prevents door frames from appearing as walls.

**Proximity Guard**: Floor cells can only be upgraded to Obstacle by hits within `obstacleCloseRadius` (4m). Distant, low-resolution hits cannot block a confirmed open corridor.

**Cell Size**: `0.1m` per cell (configurable). At this resolution, doorways remain passable.

**Minimap Rendering**: The grid is rasterized each frame onto a `Texture2D` using pixel colors:
- ⬛ Unknown → dark
- 🟩 Floor → green
- 🟥 Obstacle → red
- 🟧 Path → orange/yellow
- 🔵 Player → blue dot

---

## 3. Pathfinder — Theta* (`PathFinder.cs`)

**Algorithm**: Theta* (any-angle pathfinding) — a variant of A* that produces smooth, straight-line paths instead of staircase grid-hugging paths.

**Key difference from A***: When relaxing an edge from `current → neighbour`, Theta* first checks if there is direct line-of-sight from `parent(current) → neighbour`. If yes, it skips `current` entirely and connects directly — producing a smooth diagonal shortcut.

**Line-of-sight check**: Bresenham's line algorithm traverses all grid cells between two points and checks each for obstacles.

**Heuristic**: Octile distance (admissible for 8-connected grids).

**Expansion Limit**: 25,000 nodes hard cap to prevent Unity freezing.

**Minimap Overlay**: Theta* returns only a handful of waypoints. The full path line is reconstructed via Bresenham interpolation between each pair of waypoints, giving a solid green line on the minimap.

---

## 4. Vision Capture (`VisionCapture.cs`)

**Trigger**: User presses `V`.

**Capture Loop** (optimized order):
1. Rotate ChestCamera to 0° (front), 90° (right), 180° (rear), 270° (left).
2. At each angle: use `RenderTexture` → `ReadPixels` → `EncodeToPNG` → store bytes in memory.
3. Record **Snapshot Pose** at angle 0° (front): `snapshotPos`, `snapshotRot`.
4. Restore camera rotation immediately (user can now move freely).
5. Send all 4 images as HTTP POST requests to `pipeline_server.py`.
6. Parse JSON response from the `front` view → hand off to `VLMTargetManager`.

**Why capture all 4 first, then send?** Rotating the camera and capturing takes ~1-4 frames. Sending over HTTP takes ~50-200ms per image. Decoupling these two phases means the camera snaps all views quickly (minimizing the time the user must stand still) and then uploads in the background.

---

## 5. VLM Target Manager (`VLMTargetManager.cs`)

**Receives**: JSON string + Snapshot Position + Snapshot Rotation.

**Grid Coordinate → 3D World**:
1. Parse `grid_position` (e.g. `"6.2, 4.8"`) into floats `gx`, `gy`.
2. Convert to viewport UV: `u = gx / 10.0`, `v = 1.0 - (gy / 10.0)` (flip Y axis).
3. Temporarily move the ChestCamera to the Snapshot Pose.
4. Call `camera.ViewportPointToRay(new Vector3(u, v, 0))` to get a world-space ray.
5. Restore camera to its real pose.
6. Target world position: `snapshotPos + ray.direction * distance_meters`.

**Singleton Pattern**: `VLMTargetManager.Instance` is accessed by `PathFinder` for mutual exclusion (manual clicking is blocked while a VLM target is active).

**Self-Correction**: The VLM prompt tells Gemini about the orange sphere visible in the image (the previous estimate). Gemini is instructed to correct the coordinates if the sphere appears misaligned.

---

## 6. The VLM Prompt

The prompt in `main_pipeline.py` is the most important tunable parameter. It instructs Gemini to:
1. Identify the user's requested target on the overlaid 10×10 coordinate grid.
2. Return a strict JSON schema (no prose).
3. Apply self-correction if it sees the previous orange sphere estimate.
4. Use averaged grid coordinates around the target for precision.

The VLM (Gemini 2.5 Flash) is smart enough to:
- Disambiguate multiple similar targets ("the closer door", "the one on the left").
- Handle partially visible targets.
- Reason about perspective (closer objects appear larger, use that for distance estimation).
