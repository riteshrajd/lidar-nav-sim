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
///   [G]  toggle 3-D floor quads
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

    // ── Grid settings ─────────────────────────────────────────────────────────
    [Header("Grid")]
    [Tooltip("Size of each grid cell in metres.")]
    public float cellSize = 0.4f;

    // ── Height classification ─────────────────────────────────────────────────
    [Header("LiDAR Classification (flat floor)")]
    [Tooltip("Hits with Y offset BELOW this are classified as floor.")]
    public float obstacleMinHeight  = 0.25f;
    [Tooltip("Hits with Y offset ABOVE this are ceiling returns and ignored.")]
    public float obstacleCeilHeight = 2.20f;

    // ── 3-D tile visualisation ────────────────────────────────────────────────
    [Header("3-D Tiles")]
    public bool show3DTiles = true;

    // ── 2-D Minimap ───────────────────────────────────────────────────────────
    [Header("Minimap")]
    public bool showMinimap = true;
    [Tooltip("Width and height of the minimap panel in screen pixels.")]
    public int  panelSize    = 300;
    [Tooltip("Screen pixels per grid cell.")]
    public int  pixelsPerCell = 5;
    [Tooltip("Margin from the screen corner.")]
    public int  margin       = 16;

    // ── Internal data ─────────────────────────────────────────────────────────
    private Dictionary<Vector2Int, CellState> grid     = new();
    private Dictionary<Vector2Int, float>     floorElevation = new(); // world Y of floor per cell
    private Dictionary<Vector2Int, GameObject> tiles   = new();

    // Track last scan time so we only re-process when LiDAR has a fresh batch
    private float lastProcessedScanTime = -1f;

    // 3-D tile colours
    private static readonly Color COL_FLOOR    = new(0.68f, 0.72f, 0.75f, 0.40f);
    private static readonly Color COL_OBSTACLE = new(0.15f, 0.25f, 0.95f, 0.80f);

    // Minimap Textures (1×1 colour swatches, lazily created)
    private Texture2D texFloor, texObstacle, texUnknown, texPlayer, texBg, texBorder;
    private GUIStyle  labelStyle;
    private bool      guiReady = false;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        // Auto-locate LiDAR and Player if not set in Inspector
        if (lidar  == null) lidar  = GetComponentInChildren<URP_FastLidar>();
        if (lidar  == null) lidar  = FindAnyObjectByType<URP_FastLidar>();
        if (player == null) player = transform;

        if (lidar == null)
            Debug.LogError("[OccupancyGrid] No URP_FastLidar found. Assign it in the Inspector.");
    }

    void Update()
    {
        // Only process when LiDAR has a genuinely new scan batch
        if (lidar == null || lidar.LastScanTime <= lastProcessedScanTime) return;
        lastProcessedScanTime = lidar.LastScanTime;

        ProcessLidarHits();

        // Key toggles
        if (Keyboard.current != null)
        {
            if (Keyboard.current.mKey.wasPressedThisFrame)
                showMinimap = !showMinimap;
            if (Keyboard.current.gKey.wasPressedThisFrame)
                Toggle3DTiles();
        }
    }

    // ── Grid building ─────────────────────────────────────────────────────────

    void ProcessLidarHits()
    {
        NativeArray<RaycastHit> hits = lidar.GetResults();
        if (!hits.IsCreated) return;

        float playerY    = player.position.y;
        bool  anyChanged = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null) continue;   // ray missed

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

            // Update grid — obstacles are permanent (noise guard: never downgrade)
            if (grid.TryGetValue(cell, out CellState existing))
            {
                if (existing == CellState.Obstacle && newState == CellState.Floor) continue;
                if (existing == newState) continue;
            }

            grid[cell] = newState;
            if (show3DTiles) UpdateTile(cell, newState);
            anyChanged = true;
        }

        // If tiles toggled off mid-run, changes still tracked; they'll render on next G press
        _ = anyChanged;
    }

    // ── 3-D Tile visuals ──────────────────────────────────────────────────────

    void UpdateTile(Vector2Int cell, CellState state)
    {
        if (!tiles.TryGetValue(cell, out GameObject tile))
        {
            tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.name = $"Cell_{cell.x}_{cell.y}";
            tile.transform.SetParent(transform, worldPositionStays: true);
            tile.transform.rotation   = Quaternion.Euler(90, 0, 0);
            tile.transform.localScale = Vector3.one * cellSize * 0.9f;
            Destroy(tile.GetComponent<Collider>());
            tile.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
            tiles[cell] = tile;
        }

        // Position at recorded floor elevation (or player Y if not known)
        float tileY = floorElevation.TryGetValue(cell, out float fy) ? fy : player.position.y;
        tile.transform.position = new Vector3(cell.x * cellSize, tileY + 0.04f, cell.y * cellSize);

        tile.GetComponent<Renderer>().material.color = state switch
        {
            CellState.Floor    => COL_FLOOR,
            CellState.Obstacle => COL_OBSTACLE,
            _                  => Color.clear
        };
        tile.SetActive(show3DTiles);
    }

    void Toggle3DTiles()
    {
        show3DTiles = !show3DTiles;
        foreach (var kv in tiles)
            kv.Value.SetActive(show3DTiles);

        // If turning back on, repaint all tiles
        if (show3DTiles)
            foreach (var kv in grid) UpdateTile(kv.Key, kv.Value);
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
            $"Grid: {gridCount} cells  |  Floor: {floorCount}  Obstacles: {obstacleCount}  |  [G] tiles  [M] map",
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

        for (int gx = -halfCells; gx <= halfCells; gx++)
        for (int gy = -halfCells; gy <= halfCells; gy++)
        {
            Vector2Int cell = new(playerCell.x + gx, playerCell.y + gy);

            int sx = px + panelSize / 2 + gx * pixelsPerCell - pixelsPerCell / 2;
            int sy = py + panelSize / 2 - gy * pixelsPerCell - pixelsPerCell / 2; // Y flipped

            if (sx < px || sx + pixelsPerCell > px + panelSize) continue;
            if (sy < py || sy + pixelsPerCell > py + panelSize) continue;

            if (!grid.TryGetValue(cell, out CellState state)) continue;

            Texture2D tex = state switch
            {
                CellState.Floor    => texFloor,
                CellState.Obstacle => texObstacle,
                _                  => texUnknown
            };
            DrawTex(new Rect(sx, sy, pixelsPerCell, pixelsPerCell), tex);
        }

        // Player dot – always dead centre
        DrawTex(new Rect(px + panelSize / 2 - 4, py + panelSize / 2 - 4, 8, 8), texPlayer);

        // Title
        GUI.Label(new Rect(px + 6, py + 4, panelSize - 12, 18), "OCCUPANCY MAP", labelStyle);

        // Legend
        int ly = py + panelSize - 20;
        DrawTex(new Rect(px + 6, ly, 10, 10), texFloor);
        GUI.Label(new Rect(px + 19, ly - 2, 50, 14), "Floor",    labelStyle);
        DrawTex(new Rect(px + 68, ly, 10, 10), texObstacle);
        GUI.Label(new Rect(px + 81, ly - 2, 70, 14), "Obstacle", labelStyle);
        DrawTex(new Rect(px + 152, ly, 10, 10), texPlayer);
        GUI.Label(new Rect(px + 164, ly - 2, 40, 14), "You",     labelStyle);
    }

    // ── Public API (for future A* / target placement) ─────────────────────────

    public Dictionary<Vector2Int, CellState> GetGrid()    => grid;
    public Vector2Int GetPlayerCell()                      => WorldToCell(player.position);
    public bool IsWalkable(Vector2Int cell)
        => !grid.TryGetValue(cell, out CellState s) || s == CellState.Floor;

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
