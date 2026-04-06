using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;

public enum CellState { Unknown = 0, Empty = 1, Obstacle = 2 }

/// <summary>
/// Reads hit data directly from URP_FastLidar (the actual LiDAR on the LidarSensor child)
/// to build a 2-D occupancy grid, then runs A* over it.
/// Unexplored cells = walkable; obstacles are discovered as the player moves.
/// </summary>
public class AStarManager : MonoBehaviour
{
    [Header("References")]
    public Transform   player;
    public VisionCapture visionCapture;
    /// <summary>Drag the LidarSensor GameObject here (the one with URP_FastLidar on it).</summary>
    public URP_FastLidar lidar;

    [Header("Grid Settings")]
    public float gridSize     = 0.5f;   // metres per cell
    public float sensorRadius = 8.0f;   // ignore hits beyond this distance from player

    [Header("LiDAR Hit Classification")]
    [Tooltip("Hits with world-Y this far above the player's feet are obstacles.")]
    public float obstacleMinHeight  = 0.30f;
    [Tooltip("Hits above this height (ceiling returns) are ignored.")]
    public float obstacleCeilHeight = 2.20f;

    [Header("Pathfinding")]
    public float repathInterval = 0.15f;

    // ── Internal state ────────────────────────────────────────────────────────
    private Dictionary<Vector2Int, CellState> memory = new();
    private Dictionary<Vector2Int, Vector3>   floorY = new();
    private Dictionary<Vector2Int, GameObject> tiles  = new();

    private List<Vector2Int> currentPath = new();
    private Vector2Int  targetCell;
    private bool        hasTarget    = false;
    private bool        memoryDirty  = false;
    private float       repathTimer  = 0f;
    private LineRenderer pathLine;

    // ── Colors ────────────────────────────────────────────────────────────────
    public static readonly Color COL_UNKNOWN  = new(0.10f, 0.10f, 0.12f, 0.35f);
    public static readonly Color COL_EMPTY    = new(0.75f, 0.75f, 0.78f, 0.45f);
    public static readonly Color COL_OBSTACLE = new(0.20f, 0.20f, 0.85f, 0.80f);
    public static readonly Color COL_PATH     = new(0.10f, 0.90f, 0.55f, 0.85f);

    void Start()
    {
        if (player        == null) player        = transform;
        if (visionCapture == null) visionCapture = GetComponent<VisionCapture>();

        // Auto-find URP_FastLidar if not assigned (looks in children first)
        if (lidar == null) lidar = GetComponentInChildren<URP_FastLidar>();
        if (lidar == null) lidar = FindAnyObjectByType<URP_FastLidar>();

        if (lidar == null)
            Debug.LogWarning("[A*] No URP_FastLidar found! Assign it in the Inspector.");

        // Path line renderer
        GameObject go = new("PathLine");
        go.transform.SetParent(transform);
        pathLine = go.AddComponent<LineRenderer>();
        pathLine.startWidth    = pathLine.endWidth = 0.09f;
        pathLine.material      = new Material(Shader.Find("Sprites/Default"));
        pathLine.startColor    = pathLine.endColor = COL_PATH;
        pathLine.useWorldSpace = true;
        pathLine.positionCount = 0;
    }

    void Update()
    {
        bool changed = ScanFromLidar();
        if (changed) memoryDirty = true;

        repathTimer += Time.deltaTime;
        if (hasTarget && (memoryDirty || currentPath.Count == 0) && repathTimer >= repathInterval)
        {
            if (memoryDirty && hasTarget && IsPathStillValid())
            {
                memoryDirty = false;
                return;
            }
            repathTimer = 0f;
            memoryDirty = false;
            currentPath = CalculatePath(WorldToGrid(player.position), targetCell);
            DrawPath();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetTarget(Vector3 worldPos)
    {
        targetCell  = WorldToGrid(worldPos);
        hasTarget   = true;
        memoryDirty = true;
        Debug.Log($"[A*] Target set → cell {targetCell}  world {worldPos}");
    }

    public bool HasTarget()                  => hasTarget;
    public List<Vector2Int> GetCurrentPath() => currentPath;
    public Dictionary<Vector2Int, CellState> GetMemory() => memory;
    public Vector2Int GetTargetCell()        => targetCell;
    public Vector2Int GetPlayerCell()        => WorldToGrid(player.position);

    public Vector3 GetTargetPosition()
        => new(targetCell.x * gridSize, player.position.y, targetCell.y * gridSize);

    public void ClearAll()
    {
        memory.Clear();
        floorY.Clear();
        foreach (var kv in tiles) Destroy(kv.Value);
        tiles.Clear();
        currentPath.Clear();
        pathLine.positionCount = 0;
        hasTarget   = false;
        memoryDirty = false;
    }

    public void ClearData() => ClearAll(); // backwards compat

    public void CalculatePath()
    {
        if (!hasTarget) { Debug.LogWarning("[A*] No target set."); return; }
        repathTimer = 0f;
        memoryDirty = false;
        currentPath = CalculatePath(WorldToGrid(player.position), targetCell);
        DrawPath();
    }

    // ── LiDAR Scan (reads URP_FastLidar RaycastHit results) ──────────────────

    private bool ScanFromLidar()
    {
        if (lidar == null) return false;

        // Access the raw RaycastHit results from URP_FastLidar via public accessor
        NativeArray<RaycastHit> hits = lidar.GetResults();
        if (!hits.IsCreated || hits.Length == 0) return false;

        bool changed    = false;
        Vector3 pPos    = player.position;
        float radiusSq  = sensorRadius * sensorRadius;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null) continue;          // no hit at this ray

            Vector3 worldPt = hit.point;

            float dx = worldPt.x - pPos.x;
            float dz = worldPt.z - pPos.z;
            if (dx * dx + dz * dz > radiusSq) continue; // outside sensor radius

            Vector2Int cell    = WorldToGrid(worldPt);
            float      offsetY = worldPt.y - pPos.y;

            // Record floor height for 3D path line
            if (!floorY.ContainsKey(cell))
                floorY[cell] = new Vector3(worldPt.x, pPos.y, worldPt.z);

            // Classify by height
            CellState newState;
            if (offsetY < obstacleMinHeight)
                newState = CellState.Empty;
            else if (offsetY < obstacleCeilHeight)
                newState = CellState.Obstacle;
            else
                continue; // ceiling — ignore

            if (!memory.TryGetValue(cell, out CellState old) || old != newState)
            {
                // Noise guard: don't downgrade confirmed obstacle → empty
                if (old == CellState.Obstacle && newState == CellState.Empty) continue;

                memory[cell] = newState;
                UpdateTile(cell, newState);
                changed = true;
            }
        }
        return changed;
    }

    // ── A* ────────────────────────────────────────────────────────────────────

    public List<Vector2Int> CalculatePath(Vector2Int start, Vector2Int goal)
    {
        var openSet  = new List<(float f, Vector2Int cell)>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore   = new Dictionary<Vector2Int, float>();

        gScore[start] = 0f;
        openSet.Add((Heuristic(start, goal), start));

        while (openSet.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < openSet.Count; i++)
                if (openSet[i].f < openSet[best].f) best = i;

            Vector2Int current = openSet[best].cell;
            openSet.RemoveAt(best);

            if (current == goal) return Retrace(cameFrom, start, goal);

            foreach (var nb in GetNeighbors(current))
            {
                // Unknown = walkable (optimistic exploration assumption)
                if (memory.TryGetValue(nb, out CellState st) && st == CellState.Obstacle)
                    continue;

                bool  diag = nb.x != current.x && nb.y != current.y;
                float cost = gScore[current] + (diag ? 1.414f : 1f);

                if (!gScore.TryGetValue(nb, out float oldG) || cost < oldG)
                {
                    cameFrom[nb] = current;
                    gScore[nb]   = cost;
                    openSet.Add((cost + Heuristic(nb, goal), nb));
                }
            }
        }

        Debug.LogWarning("[A*] No path found.");
        return new List<Vector2Int>();
    }

    // ── Path Drawing ──────────────────────────────────────────────────────────

    private void DrawPath()
    {
        foreach (var kv in memory) UpdateTile(kv.Key, kv.Value);

        if (currentPath.Count == 0)
        {
            pathLine.positionCount = 0;
            return;
        }

        var points = new List<Vector3>();
        points.Add(GetFloorPos(WorldToGrid(player.position)));

        foreach (var cell in currentPath)
        {
            points.Add(GetFloorPos(cell));
            if (tiles.TryGetValue(cell, out var tile))
                tile.GetComponent<Renderer>().material.color = COL_PATH;
        }

        pathLine.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            pathLine.SetPosition(i, points[i] + Vector3.up * 0.06f);

        Debug.Log($"[A*] Path recalculated — {currentPath.Count} steps.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsPathStillValid()
    {
        foreach (var cell in currentPath)
            if (memory.TryGetValue(cell, out var st) && st == CellState.Obstacle)
                return false;
        return true;
    }

    private List<Vector2Int> GetNeighbors(Vector2Int n)
    {
        var list = new List<Vector2Int>(8);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            list.Add(new Vector2Int(n.x + dx, n.y + dy));
        }
        return list;
    }

    private float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        return dx + dy + (1.414f - 2f) * Mathf.Min(dx, dy); // octile
    }

    public Vector2Int WorldToGrid(Vector3 world)
        => new(Mathf.RoundToInt(world.x / gridSize),
               Mathf.RoundToInt(world.z / gridSize));

    private Vector3 GetFloorPos(Vector2Int cell)
    {
        if (floorY.TryGetValue(cell, out var p)) return p;
        return new Vector3(cell.x * gridSize, player.position.y, cell.y * gridSize);
    }

    private List<Vector2Int> Retrace(Dictionary<Vector2Int, Vector2Int> came,
                                     Vector2Int start, Vector2Int end)
    {
        var path = new List<Vector2Int>();
        var cur  = end;
        while (cur != start) { path.Add(cur); cur = came[cur]; }
        path.Reverse();
        return path;
    }

    // ── Tile Visuals ──────────────────────────────────────────────────────────

    private void UpdateTile(Vector2Int cell, CellState state)
    {
        if (!tiles.TryGetValue(cell, out var tile))
        {
            tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.name = $"Tile_{cell.x}_{cell.y}";
            tile.transform.SetParent(transform);
            tile.transform.rotation   = Quaternion.Euler(90, 0, 0);
            tile.transform.localScale = Vector3.one * gridSize * 0.92f;
            Destroy(tile.GetComponent<Collider>());
            tile.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
            tiles[cell] = tile;
        }

        tile.transform.position = GetFloorPos(cell) + Vector3.up * 0.03f;

        Color c = state switch
        {
            CellState.Empty    => COL_EMPTY,
            CellState.Obstacle => COL_OBSTACLE,
            _                  => COL_UNKNOWN
        };
        tile.GetComponent<Renderer>().material.color = c;
    }
}