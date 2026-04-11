using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Theta* pathfinding on the live OccupancyGrid.
///
/// Theta* is an any-angle path planner. Unlike A* it is not constrained to grid
/// edges: it checks line-of-sight between nodes and cuts directly across open
/// space, producing smooth, natural paths with no staircase artifacts.
///
/// Keys / Input
///   Left-Click  → cast ray into 3-D world, place target at hit point
///   [T]         → toggle real-time path (recalculate whenever map changes)
///   [P]         → force repath now
///   [C]         → clear map + path + target
/// </summary>
public class PathFinder : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("The OccupancyGrid on the Player (auto-found if blank).")]
    public OccupancyGrid grid;
    [Tooltip("Player transform used as path start (auto-found if blank).")]
    public Transform     player;
    [Tooltip("Camera for mouse raycasts. Defaults to Camera.main.")]
    public Camera        viewCamera;

    // ── Target Marker ─────────────────────────────────────────────────────────
    [Header("Target Marker")]
    [Tooltip("Radius of the glowing sphere placed at the clicked target.")]
    public float markerRadius = 0.25f;

    // ── Settings ──────────────────────────────────────────────────────────────
    [Header("Path Settings")]
    [Tooltip("When ON, path recalculates automatically whenever new obstacles appear.")]
    public bool realtimePath = false;
    [Tooltip("Max search radius in grid cells from the player. Prevents flood-fill through infinite unknown space. (cells × cellSize = metres)")]
    public int maxSearchRadius = 600; // 600 cells × 0.1m = 60m
    [Tooltip("Max nodes expanded before giving up. Hard crash guard.")]
    public int maxExpansions   = 25000;

    // ── Public state (read by OccupancyGrid for minimap overlay) ─────────────
    public List<Vector2Int> CurrentPath    { get; private set; } = new();
    /// <summary>
    /// Every grid cell that lies ON the path line, not just waypoints.
    /// Theta* returns only a handful of straight-line waypoints; this expands
    /// them with Bresenham interpolation so the minimap draws a solid green line.
    /// </summary>
    public HashSet<Vector2Int> ExpandedPath   { get; private set; } = new();
    public bool                HasTarget      { get; private set; } = false;
    public Vector2Int          TargetCell     { get; private set; }
    public Vector3             TargetWorldPos { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────
    private GameObject targetMarker;
    private float      lastKnownGridUpdate = -1f;
    private Vector2Int lastPlayerCell;            // tracks when start cell changes

    // Exclude layer 1 (TransparentFX = LiDAR point cloud) from click raycasts
    private static readonly int ClickMask = ~(1 << 1);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        if (grid       == null) grid      = GetComponent<OccupancyGrid>() ?? FindAnyObjectByType<OccupancyGrid>();
        if (player     == null) player    = transform;
        if (viewCamera == null) viewCamera = Camera.main;

        // Build the target marker (hidden until a target is set)
        targetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetMarker.name = "TargetMarker";
        targetMarker.transform.localScale = Vector3.one * markerRadius * 2f;
        Destroy(targetMarker.GetComponent<Collider>());

        // Bright orange material — visible in both normal and blind mode
        var mat = new Material(Shader.Find("Sprites/Default"))
                  { color = new Color(1f, 0.55f, 0.05f) };
        targetMarker.GetComponent<Renderer>().material = mat;
        targetMarker.SetActive(false);
    }

    void Update()
    {
        HandleKeys();
        HandleMouseClick();

        // Real-time replanning — two independent triggers:
        //   1. Player moved to a different grid cell → start of path changed → repath
        //   2. Grid changed (new obstacle found) AND that obstacle is on the drawn path → repath
        if (realtimePath && HasTarget && grid != null)
        {
            Vector2Int currentCell = grid.GetPlayerCell();
            float      gt          = grid.LastUpdateTime;

            bool playerMoved  = currentCell != lastPlayerCell;
            bool gridChanged  = gt > lastKnownGridUpdate;

            lastPlayerCell        = currentCell;
            if (gridChanged) lastKnownGridUpdate = gt;

            if (playerMoved || (gridChanged && !IsCurrentPathValid()))
                RecalculatePath();
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    void HandleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // [T] toggle real-time pathfinding
        if (kb.tKey.wasPressedThisFrame)
        {
            realtimePath = !realtimePath;
            if (realtimePath && HasTarget) RecalculatePath();
        }

        // [P] force repath immediately
        if (kb.pKey.wasPressedThisFrame && HasTarget)
            RecalculatePath();

        // [C] clear EVERYTHING: map + path + target
        if (kb.cKey.wasPressedThisFrame)
            ClearAll();

        // [Q] clear only Target and Path (keep the map)
        if (kb.qKey.wasPressedThisFrame)
            ClearTarget();
    }

    void HandleMouseClick()
    {
        // ── Mutual Exclusion ──
        // If a VLM target is active, we prevent manual target placement
        // to avoid conflicting markers and paths.
        if (VLMTargetManager.Instance != null && VLMTargetManager.Instance.HasTarget)
        {
            // Optional: You could play a "denied" sound or log here
            return;
        }

        if (Mouse.current == null)                            return;
        if (!Mouse.current.leftButton.wasPressedThisFrame)   return;
        if (viewCamera == null)                              return;

        Vector2 mp  = Mouse.current.position.ReadValue();
        Ray     ray = viewCamera.ScreenPointToRay(new Vector3(mp.x, mp.y, 0));

        if (Physics.Raycast(ray, out RaycastHit hit, 300f, ClickMask))
            SetTarget(hit.point);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Place a navigation target at a 3-D world position.</summary>
    public void SetTarget(Vector3 worldPos)
    {
        TargetWorldPos = worldPos;
        TargetCell     = grid.WorldToCell(worldPos);
        HasTarget      = true;

        targetMarker.transform.position = worldPos + Vector3.up * markerRadius;
        targetMarker.SetActive(true);

        RecalculatePath();
        Debug.Log($"[PathFinder] Target set → cell {TargetCell}  world {worldPos:F1}");
    }

    /// <summary>Run Theta* from the player's current cell to the target cell.</summary>
    public void RecalculatePath()
    {
        if (!HasTarget || grid == null) return;
        Vector2Int start = grid.GetPlayerCell();
        CurrentPath  = (start == TargetCell) ? new() : ThetaStar(start, TargetCell);
        ExpandedPath = BresenhamExpand(start, CurrentPath);
    }

    /// <summary>
    /// [C] — Clear the map and the current path.
    /// The target is intentionally KEPT so a new path builds naturally as you
    /// re-explore the area after clearing.
    /// </summary>
    public void ClearAll()
    {
        CurrentPath  = new();
        ExpandedPath = new();
        // Target + marker stay visible
        grid?.ClearMap();
        // Immediately replan through now-empty (all walkable) space
        if (HasTarget) RecalculatePath();
    }

    /// <summary>
    /// [Q] — Clear the current navigation target and path.
    /// This also resets the VLM state.
    /// </summary>
    public void ClearTarget()
    {
        HasTarget = false;
        CurrentPath.Clear();
        ExpandedPath.Clear();
        
        if (targetMarker != null) targetMarker.SetActive(false);
        if (VLMTargetManager.Instance != null) VLMTargetManager.Instance.ClearVLMTarget();

        Debug.Log("[PathFinder] Target and Path cleared.");
    }

    // ── Theta* ────────────────────────────────────────────────────────────────
    //
    // Algorithm overview:
    //   Like A*, but when relaxing an edge (current → neighbour) we first try
    //   to "skip" current and connect grandparent(current) directly to neighbour
    //   if there is unobstructed line-of-sight between them. This produces smooth
    //   any-angle paths that cut across open space instead of hugging grid edges.
    //
    // Open-list implementation: List with lazy deletion.
    //   Stale entries (g-score mismatch) are silently skipped when popped.
    //   Simple and allocation-friendly for the typical grid sizes we deal with.

    List<Vector2Int> ThetaStar(Vector2Int start, Vector2Int goal)
    {
        var parent = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
        var g      = new Dictionary<Vector2Int, float>      { [start] = 0f };
        var closed = new HashSet<Vector2Int>();
        var open   = new List<(float f, Vector2Int cell)>
                     { (Octile(start, goal), start) };

        int expansions = 0;
        while (open.Count > 0 && expansions < maxExpansions)
        {
            expansions++;

            // Pop node with lowest f  (O(n) scan — fine for typical map sizes)
            int bi = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bi].f) bi = i;

            Vector2Int cur = open[bi].cell;
            open.RemoveAt(bi);

            if (closed.Contains(cur)) continue;   // stale entry — skip
            if (cur == goal)          return Retrace(parent, start, goal);
            closed.Add(cur);

            foreach (var nb in Neighbors(cur))
            {
                if (closed.Contains(nb)) continue;
                if (!IsWalkable(nb))     continue;

                // ── Search radius guard ───────────────────────────────────
                // Unknown cells are walkable, so without this the search
                // floods infinitely through unexplored space and crashes Unity.
                if (Mathf.Abs(nb.x - start.x) > maxSearchRadius ||
                    Mathf.Abs(nb.y - start.y) > maxSearchRadius) continue;

                // Theta*: try grandparent → neighbour line-of-sight shortcut
                Vector2Int p = parent[cur];
                float gNew;
                Vector2Int pNb;

                if (p != cur && LineOfSight(p, nb))
                {
                    // Any-angle shortcut: connect grandparent directly to nb
                    gNew = g[p] + Dist(p, nb);
                    pNb  = p;
                }
                else
                {
                    // Standard A* edge relaxation
                    gNew = g[cur] + Dist(cur, nb);
                    pNb  = cur;
                }

                if (!g.TryGetValue(nb, out float gOld) || gNew < gOld - 1e-5f)
                {
                    g[nb]      = gNew;
                    parent[nb] = pNb;
                    open.Add((gNew + Octile(nb, goal), nb));
                }
            }
        }

        if (expansions >= maxExpansions)
            Debug.LogWarning($"[PathFinder] Expansion limit ({maxExpansions}) reached. No path or target too far.");

        return new List<Vector2Int>(); // no path found
    }

    // ── Theta* helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Expands a list of Theta* waypoints into EVERY cell that lies on the
    /// straight-line segments between them using Bresenham's algorithm.
    /// This gives a thick, continuous path line on the minimap instead of
    /// just the sparse waypoint dots that Theta* returns.
    /// </summary>
    HashSet<Vector2Int> BresenhamExpand(Vector2Int start, List<Vector2Int> waypoints)
    {
        var cells = new HashSet<Vector2Int>();
        if (waypoints.Count == 0) return cells;

        Vector2Int prev = start;
        foreach (var wp in waypoints)
        {
            // Walk every cell on the Bresenham line from prev → wp
            int x = prev.x, y = prev.y;
            int x1 = wp.x, y1 = wp.y;
            int dx = Mathf.Abs(x1 - x), dy = Mathf.Abs(y1 - y);
            int sx = x < x1 ? 1 : -1, sy = y < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                cells.Add(new Vector2Int(x, y));
                if (x == x1 && y == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 <  dx) { err += dx; y += sy; }
            }
            prev = wp;
        }
        return cells;
    }

    /// <summary>Bresenham line-of-sight: returns true if every cell on the line is walkable.</summary>
    bool LineOfSight(Vector2Int a, Vector2Int b)
    {
        int x = a.x, y = a.y, x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x), dy = Mathf.Abs(y1 - y);
        int sx = x < x1 ? 1 : -1, sy = y < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (!IsWalkable(new Vector2Int(x, y))) return false;
            if (x == x1 && y == y1) return true;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 <  dx) { err += dx; y += sy; }
        }
    }

    bool IsCurrentPathValid()
    {
        // Check ExpandedPath — the FULL drawn line between waypoints (Bresenham).
        // CurrentPath only has the sparse Theta* waypoints, not the cells in
        // between them. A wall could appear mid-segment and CurrentPath would
        // miss it entirely, leaving the green line visually passing through obstacles.
        var data = grid.GetGrid();
        foreach (var cell in ExpandedPath)
            if (data.TryGetValue(cell, out var s) && s == OccupancyGrid.CellState.Obstacle)
                return false;
        return true;
    }

    IEnumerable<Vector2Int> Neighbors(Vector2Int n)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            yield return new Vector2Int(n.x + dx, n.y + dy);
        }
    }

    bool IsWalkable(Vector2Int cell)
    {
        // Unknown cells are optimistically walkable — path will reroute when
        // the LiDAR discovers an obstacle in a previously-unseen cell.
        if (grid.GetGrid().TryGetValue(cell, out var s))
            return s != OccupancyGrid.CellState.Obstacle;
        return true;
    }

    /// <summary>Octile distance heuristic — admissible for 8-connected grids.</summary>
    float Octile(Vector2Int a, Vector2Int b)
    {
        float dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
        return Mathf.Max(dx, dy) + (1.414f - 1f) * Mathf.Min(dx, dy);
    }

    float Dist(Vector2Int a, Vector2Int b)
        => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    List<Vector2Int> Retrace(
        Dictionary<Vector2Int, Vector2Int> parent,
        Vector2Int start, Vector2Int goal)
    {
        var path   = new List<Vector2Int>();
        var cur    = goal;
        int safety = 50000; // prevent infinite loops from corrupted parent chain

        while (cur != start && safety-- > 0)
        {
            path.Add(cur);
            if (!parent.TryGetValue(cur, out var next) || next == cur) break;
            cur = next;
        }
        path.Reverse();
        return path;
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        var style = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold };
        style.normal.textColor = new Color(0.85f, 0.9f, 1f);

        string tgt  = HasTarget          ? $"cell {TargetCell}" : "none — left-click in 3D to set";
        string path = CurrentPath.Count > 0 ? $"{CurrentPath.Count} steps" : "—";
        string rt   = realtimePath       ? "ON ✓" : "OFF";

        GUI.Label(new Rect(10, 66, 900, 22),
            $"Target: {tgt}   |   Path: {path}   |   [T] Realtime: {rt}   [P] repath   [C] clear all",
            style);
    }
}
