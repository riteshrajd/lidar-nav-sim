using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Collections;
using System.Collections.Generic;

/// <summary>
/// Builds a 2-D occupancy grid from live URP_FastLidar hit data as the player
/// moves through the environment.
///
/// The LiDAR sensor is assumed to be a child of the Player and moves with it.
/// Hit points come back in world space via URP_FastLidar.GetResults(), so no
/// additional coordinate transform is needed.
///
/// CLASSIFICATION (flat floor assumption):
///   hit.point.y - player.y  <  obstacleMinHeight   →  Floor   (walkable)
///   obstacleMinHeight  ≤  offset  <  ceilHeight     →  Obstacle
///   offset  ≥  ceilHeight                           →  Ceiling return, ignored
///
/// Keys:
///   [M]  toggle minimap
/// </summary>
public class OccupancyGrid : MonoBehaviour
{
    // ── Cell state ────────────────────────────────────────────────────────────
    public enum CellState { Unknown = 0, Floor = 1, Obstacle = 2 }

    // ── References ────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("The LidarSensor child that has URP_FastLidar on it.")]
    public URP_FastLidar lidar;
    [Tooltip("The player transform (used as height reference).")]
    public Transform player;
    [Tooltip("PathFinder component — used to draw path and target on the minimap. Auto-found if blank.")]
    public PathFinder pathFinder;

    // ── Grid settings ─────────────────────────────────────────────────────────
    [Header("Grid")]
    [Tooltip("Size of each grid cell in metres.")]
    public float cellSize = 0.1f; // Halved from 0.2f to double precision and map narrow doorways more accurately.

    // ── Height classification ─────────────────────────────────────────────────
    [Header("LiDAR Classification (flat floor)")]
    [Tooltip("Hits with Y offset BELOW this are classified as floor.")]
    public float obstacleMinHeight  = 0.03f;
    [Tooltip("Hits with Y offset ABOVE this are ceiling returns and ignored.")]
    public float obstacleCeilHeight = 2.20f;
    [Tooltip("If an obstacle cell's LOWEST hit was ABOVE this height, treat it as a door frame/lintel (correctable). "
           + "Real walls have hits near floor level (~0.1m). Door lintels hit ~1.1m+ above player. "
           + "Lower = more aggressive door correction. Raise = more conservative.")]
    public float doorFrameMinHeight = 1.0f;  
    [Tooltip("Max distance (metres) at which a hit can flip an already-confirmed Floor cell to Obstacle. Beyond this, far hits can only add NEW cells — they cannot close an already-open path.")]
    public float obstacleCloseRadius = 2.5f; // Shortened to prevent far-away ray cluster edge-bleeding on doors
    [Tooltip("Max XZ distance (metres) from the player that gets written to the 2D grid. "
           + "Mimics a real LiDAR with limited sensing range. Set to 0 to disable (use full LiDAR range).")]
    public float mappingRadius = 8.0f;   // real indoor LiDAR typical range

    // ── 2-D Minimap ───────────────────────────────────────────────────────────
    [Header("Minimap")]
    public bool showMinimap = true;
    [Tooltip("Width and height of the minimap panel in screen pixels.")]
    public int  panelSize    = 300;
    [Tooltip("Screen pixels per grid cell.")]
    public int  pixelsPerCell = 3; // Reduced to maintain view area as cell size decreased
    [Tooltip("Margin from the screen corner.")]
    public int  margin       = 16;

    // ── Internal data ─────────────────────────────────────────────────────────
    private Dictionary<Vector2Int, CellState>   grid            = new();
    private Dictionary<Vector2Int, float>        floorElevation  = new();
    // Tracks the LOWEST Y offset that caused each cell to be marked Obstacle.
    // Door frames only get hit at high angles → high value. Real walls → low value.
    private Dictionary<Vector2Int, float>        lowestObstacleOffset = new();

    // Track last scan time so we only re-process when LiDAR has a fresh batch
    private float lastProcessedScanTime = -1f;

    /// <summary>Updated whenever any cell in the grid changes.
    /// PathFinder polls this to decide when to replan.</summary>
    public float LastUpdateTime { get; private set; } = -1f;

    // Minimap Textures (1×1 colour swatches, lazily created)
    private Texture2D texFloor, texObstacle, texUnknown, texPlayer, texBg, texBorder;
    private Texture2D texPath, texTarget; // path overlay colours
    private GUIStyle  labelStyle;
    private bool      guiReady = false;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        // Auto-locate LiDAR and Player if not set in Inspector
        if (lidar       == null) lidar       = GetComponentInChildren<URP_FastLidar>();
        if (lidar       == null) lidar       = FindAnyObjectByType<URP_FastLidar>();
        if (player      == null) player      = transform;
        if (pathFinder  == null) pathFinder  = GetComponent<PathFinder>() ??
                                               FindAnyObjectByType<PathFinder>();

        if (lidar == null)
            Debug.LogError("[OccupancyGrid] No URP_FastLidar found. Assign it in the Inspector.");
    }

    void Update()
    {
        // Only process when LiDAR has a genuinely new scan batch
        if (lidar == null || lidar.LastScanTime <= lastProcessedScanTime) return;
        lastProcessedScanTime = lidar.LastScanTime;

        ProcessLidarHits();

        // Walk-through correction: if the player is physically inside or touching
        // a cell marked Obstacle, they PROVE it is walkable — force it to Floor.
        // This catches narrow doors the door-frame heuristic misses.
        CorrectPlayerFootprint();

        // Key toggles
        if (Keyboard.current != null)
        {
            if (Keyboard.current.mKey.wasPressedThisFrame)
                showMinimap = !showMinimap;
        }
    }

    // ── Grid building ─────────────────────────────────────────────────────────

    void ProcessLidarHits()
    {
        NativeArray<RaycastHit> hits = lidar.GetResults();
        if (!hits.IsCreated) return;

        float playerY    = player.position.y;
        float playerX    = player.position.x;
        float playerZ    = player.position.z;
        float closeRadSq = obstacleCloseRadius * obstacleCloseRadius;
        bool  anyChanged = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null) continue;   // ray missed

            // XZ distance from player to hit (squared, avoids sqrt cost)
            float dx = hit.point.x - playerX;
            float dz = hit.point.z - playerZ;
            float distSq = dx * dx + dz * dz;

            // Mapping range limit — ignore hits beyond the configured radius
            if (mappingRadius > 0f && distSq > mappingRadius * mappingRadius) continue;

            float offsetY = hit.point.y - playerY;

            // Ceiling returns → ignore
            if (offsetY >= obstacleCeilHeight) continue;

            CellState newState = (offsetY < obstacleMinHeight)
                ? CellState.Floor
                : CellState.Obstacle;

            Vector2Int cell = WorldToCell(hit.point);

            // Store floor elevation for tile Y placement
            if (newState == CellState.Floor && !floorElevation.ContainsKey(cell))
                floorElevation[cell] = hit.point.y;

            // Track the lowest obstacle hit offset per cell (used for door frame detection)
            if (newState == CellState.Obstacle)
            {
                if (!lowestObstacleOffset.TryGetValue(cell, out float prev) || offsetY < prev)
                    lowestObstacleOffset[cell] = offsetY;
            }

            // Update grid — noise guard: avoid random floor rays overriding solid walls.
            // EXCEPTION: if the obstacle mark came from a high-angle hit only (door frame),
            // a confirmed floor hit IS allowed to correct it.
            if (grid.TryGetValue(cell, out CellState existing))
            {
                if (existing == CellState.Obstacle && newState == CellState.Floor)
                {
                    // Real wall: lowest obstacle hit was low → keep obstacle
                    if (!lowestObstacleOffset.TryGetValue(cell, out float lowestHit)
                        || lowestHit < doorFrameMinHeight)
                        continue; // real wall — do not downgrade

                    // Door frame: all obstacle hits were high-angle → allow floor correction
                    lowestObstacleOffset.Remove(cell); // reset for re-evaluation
                }
                else if (existing == CellState.Floor && newState == CellState.Obstacle)
                {
                    // ── Proximity guard ───────────────────────────────────────
                    // Far-away hits have poor angular resolution and can clip
                    // door edges, falsely closing an open path. Only NEARBY hits
                    // are trusted to upgrade Floor → Obstacle.
                    if (distSq > closeRadSq) continue; // too far — keep it open
                }
                else if (existing == newState) continue;
            }

            grid[cell] = newState;
            anyChanged  = true;

            // ── Ray Clearing (Bresenham) ────────────────────────────────────────────────
            // If the LiDAR ray hit this cell, then the airspace BETWEEN the player and this 
            // cell is PROVEN to be completely empty. We must paint it Walkable (Floor).
            // This immediately marks doorways and "hollow parts" as open paths on the map.
            // We subsample (i % 3) to keep performance buttery smooth.
            if (i % 3 == 0)
            {
                if (ClearPathBresenham(WorldToCell(player.position), cell))
                    anyChanged = true;
            }
        }

        if (anyChanged) LastUpdateTime = Time.time;
    }

    /// <summary>
    /// Traces a 2D line from the sensor to the hit point, painting all intermediate cells 
    /// as clear Floor. Since light traveled through them, they are open passageways!
    /// Returns true if it painted at least one new Floor cell.
    /// </summary>
    private bool ClearPathBresenham(Vector2Int start, Vector2Int end)
    {
        bool changed = false;
        int x0 = start.x, y0 = start.y;
        int x1 = end.x,   y1 = end.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        // Loop safety limit for massive rays
        int limit = 200; 

        while (limit-- > 0)
        {
            // Stop precisely *before* we overwrite the final hit point, 
            // which might be an actual Obstacle wall!
            if (x0 == x1 && y0 == y1) break;

            Vector2Int c = new Vector2Int(x0, y0);
            
            if (grid.TryGetValue(c, out CellState existing))
            {
                if (existing != CellState.Floor)
                {
                    // Erase ghost obstacles (like door frame bleeding) because a ray proved it's air!
                    grid[c] = CellState.Floor;
                    lowestObstacleOffset.Remove(c);
                    changed = true;
                }
            }
            else
            {
                grid[c] = CellState.Floor;
                changed = true;
            }

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }
        return changed;
    }

    /// <summary>
    /// Walk-through correction: the player's physical presence PROVES that
    /// their current cell and immediately adjacent cells are walkable.
    /// Forces any Obstacle cell in the player's 3×3 footprint to Floor and
    /// clears its lowestObstacleOffset so door-frame detection resets cleanly.
    ///
    /// This catches narrow doors that the height-threshold heuristic misses:
    /// once the player walks through once, those cells are permanently freed.
    /// </summary>
    void CorrectPlayerFootprint()
    {
        Vector2Int pc      = GetPlayerCell();
        bool       changed = false;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            var cell = new Vector2Int(pc.x + dx, pc.y + dy);
            if (grid.TryGetValue(cell, out CellState s) && s == CellState.Obstacle)
            {
                grid[cell] = CellState.Floor;
                lowestObstacleOffset.Remove(cell); // let door-frame tracking restart fresh
                changed = true;
            }
        }

        if (changed) LastUpdateTime = Time.time;
    }

    // ── 2-D Minimap ───────────────────────────────────────────────────────────

    void OnGUI()
    {
        // Blind-mode status line
        var bStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold };
        bStyle.normal.textColor = new Color(0.85f, 0.90f, 1f);

        int gridCount    = grid.Count;
        int floorCount   = 0;
        int obstacleCount = 0;
        foreach (var v in grid.Values)
        {
            if (v == CellState.Floor)    floorCount++;
            else if (v == CellState.Obstacle) obstacleCount++;
        }

        GUI.Label(new Rect(10, 40, 600, 24),
            $"Grid: {gridCount} cells  |  Floor: {floorCount}  Obstacles: {obstacleCount}  |  [M] map",
            bStyle);

        if (!showMinimap) return;

        EnsureGUI();

        // Panel anchored bottom-right
        int px = Screen.width  - panelSize - margin;
        int py = Screen.height - panelSize - margin;

        // Background
        DrawTex(new Rect(px - 2, py - 2, panelSize + 4, panelSize + 4), texBorder);
        DrawTex(new Rect(px,     py,     panelSize,      panelSize),     texBg);

        // Grid cells — centred on player
        Vector2Int playerCell = GetPlayerCell();
        int halfCells = panelSize / (2 * pixelsPerCell);

        // ── Path overlay ───────────────────────────────────────────────────────
        // Build path cell set — ExpandedPath already contains every cell along
        // each segment (Bresenham-interpolated), giving a solid green line.
        HashSet<Vector2Int> pathSet = null;
        if (pathFinder != null && pathFinder.ExpandedPath.Count > 0)
            pathSet = pathFinder.ExpandedPath; // already a HashSet, reuse directly

        // ── Grid cells ────────────────────────────────────────────────────────
        for (int gx = -halfCells; gx <= halfCells; gx++)
        for (int gy = -halfCells; gy <= halfCells; gy++)
        {
            Vector2Int cell = new(playerCell.x + gx, playerCell.y + gy);

            int sx = px + panelSize / 2 + gx * pixelsPerCell - pixelsPerCell / 2;
            int sy = py + panelSize / 2 - gy * pixelsPerCell - pixelsPerCell / 2;

            if (sx < px || sx + pixelsPerCell > px + panelSize) continue;
            if (sy < py || sy + pixelsPerCell > py + panelSize) continue;

            // Path cells drawn on top of the base grid colour
            if (pathSet != null && pathSet.Contains(cell))
            {
                DrawTex(new Rect(sx, sy, pixelsPerCell, pixelsPerCell), texPath);
                continue;
            }

            if (!grid.TryGetValue(cell, out CellState state)) continue;

            Texture2D tex = state switch
            {
                CellState.Floor    => texFloor,
                CellState.Obstacle => texObstacle,
                _                  => texUnknown
            };
            DrawTex(new Rect(sx, sy, pixelsPerCell, pixelsPerCell), tex);
        }

        // ── Target cell ───────────────────────────────────────────────────────
        if (pathFinder != null && pathFinder.HasTarget)
        {
            Vector2Int tc = pathFinder.TargetCell;
            int gx = tc.x - playerCell.x;
            int gy = tc.y - playerCell.y;
            int sx = px + panelSize / 2 + gx * pixelsPerCell - pixelsPerCell / 2;
            int sy = py + panelSize / 2 - gy * pixelsPerCell - pixelsPerCell / 2;
            if (sx >= px && sx + pixelsPerCell <= px + panelSize &&
                sy >= py && sy + pixelsPerCell <= py + panelSize)
                DrawTex(new Rect(sx - 1, sy - 1, pixelsPerCell + 2, pixelsPerCell + 2), texTarget);
        }

        // ── Player dot ────────────────────────────────────────────────────────
        DrawTex(new Rect(px + panelSize / 2 - 4, py + panelSize / 2 - 4, 8, 8), texPlayer);

        // Title
        GUI.Label(new Rect(px + 6, py + 4, panelSize - 12, 18), "OCCUPANCY MAP", labelStyle);

        // Legend
        int ly = py + panelSize - 20;
        DrawTex(new Rect(px + 6,   ly, 10, 10), texFloor);
        GUI.Label(new Rect(px + 19,  ly - 2, 50, 14), "Floor",    labelStyle);
        DrawTex(new Rect(px + 68,  ly, 10, 10), texObstacle);
        GUI.Label(new Rect(px + 81,  ly - 2, 70, 14), "Obstacle", labelStyle);
        DrawTex(new Rect(px + 152, ly, 10, 10), texPath);
        GUI.Label(new Rect(px + 165, ly - 2, 40, 14), "Path",     labelStyle);
        DrawTex(new Rect(px + 205, ly, 10, 10), texTarget);
        GUI.Label(new Rect(px + 218, ly - 2, 50, 14), "Target",   labelStyle);
        DrawTex(new Rect(px + 270, ly, 10, 10), texPlayer);
        GUI.Label(new Rect(px + 283, ly - 2, 30, 14), "You",      labelStyle);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Dictionary<Vector2Int, CellState> GetGrid()    => grid;
    public Vector2Int GetPlayerCell()                      => WorldToCell(player.position);
    public bool IsWalkable(Vector2Int cell)
        => !grid.TryGetValue(cell, out CellState s) || s == CellState.Floor;

    /// <summary>Wipe all explored data. Call via [C] key in PathFinder.</summary>
    public void ClearMap()
    {
        grid.Clear();
        floorElevation.Clear();
        lowestObstacleOffset.Clear();
        // Force the next LiDAR scan to be processed fresh
        lastProcessedScanTime = lidar != null ? lidar.LastScanTime : -1f;
        LastUpdateTime = Time.time;
        Debug.Log("[OccupancyGrid] Map cleared.");
    }

    // Convert world XZ to grid cell coordinates
    public Vector2Int WorldToCell(Vector3 world)
        => new(Mathf.RoundToInt(world.x / cellSize),
               Mathf.RoundToInt(world.z / cellSize));

    // Convert grid cell back to world XY (Y from stored elevation or player)
    public Vector3 CellToWorld(Vector2Int cell)
    {
        float y = floorElevation.TryGetValue(cell, out float fy) ? fy : player.position.y;
        return new Vector3(cell.x * cellSize, y, cell.y * cellSize);
    }

    // ── GUI helpers ───────────────────────────────────────────────────────────

    void EnsureGUI()
    {
        if (guiReady) return;
        texFloor    = MakeTex(new Color(0.68f, 0.72f, 0.74f, 1f));
        texObstacle = MakeTex(new Color(0.15f, 0.25f, 0.95f, 1f));
        texUnknown  = MakeTex(new Color(0.04f, 0.04f, 0.06f, 1f));
        texPlayer   = MakeTex(new Color(1.00f, 0.22f, 0.22f, 1f));
        texBg       = MakeTex(new Color(0.04f, 0.04f, 0.07f, 0.92f));
        texBorder   = MakeTex(new Color(0.30f, 0.55f, 1.00f, 0.70f));
        texPath     = MakeTex(new Color(0.10f, 0.90f, 0.35f, 1f));  // bright green
        texTarget   = MakeTex(new Color(1.00f, 0.55f, 0.05f, 1f));  // orange

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        labelStyle.normal.textColor = new Color(0.85f, 0.90f, 1f);
        guiReady = true;
    }

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    static void DrawTex(Rect r, Texture2D tex) => GUI.DrawTexture(r, tex);
}
