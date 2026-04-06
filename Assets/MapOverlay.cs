using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Renders a real-time 2-D occupancy-grid minimap in screen space using
/// Unity's immediate-mode GUI (OnGUI).  No Canvas or UGUI required.
///
/// Attach this script to any active GameObject (e.g. the Player or a 
/// dedicated "HUD" empty object).  Assign the AStarManager reference in
/// the Inspector.
///
/// CONTROLS  – all in the Inspector or via the keyboard shortcuts shown below:
///   [M]  Toggle the minimap on / off
/// </summary>
public class MapOverlay : MonoBehaviour
{
    [Header("References")]
    public AStarManager aStarManager;

    [Header("Minimap Layout")]
    [Tooltip("Width & height of the minimap panel in screen pixels.")]
    public int    panelSize    = 320;
    [Tooltip("Pixels rendered per grid cell.")]
    public int    pixelsPerCell = 5;
    [Tooltip("Margin from the bottom-right corner in pixels.")]
    public int    margin       = 16;

    [Header("Display")]
    public bool showMinimap = true;
    [Tooltip("0 = fully transparent  1 = fully opaque")]
    [Range(0f, 1f)]
    public float panelAlpha = 0.88f;

    // ── GUI textures (1×1 pixel colour swatches) ────────────────────────────
    private Texture2D texUnknown;
    private Texture2D texEmpty;
    private Texture2D texObstacle;
    private Texture2D texPath;
    private Texture2D texPlayer;
    private Texture2D texTarget;
    private Texture2D texBackground;
    private Texture2D texBorder;

    private GUIStyle  labelStyle;
    private bool      stylesReady = false;

    void Start()
    {
        if (aStarManager == null)
            aStarManager = FindAnyObjectByType<AStarManager>();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            showMinimap = !showMinimap;
    }

    // ── OnGUI ────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!showMinimap || aStarManager == null) return;

        EnsureStyles();

        // Panel position: bottom-right
        int px = Screen.width  - panelSize - margin;
        int py = Screen.height - panelSize - margin;

        // ── Background ────────────────────────────────────────────────────────
        var darkBg = new Color(0.05f, 0.05f, 0.08f, panelAlpha);
        DrawRect(new Rect(px - 2, py - 2, panelSize + 4, panelSize + 4), texBorder);
        DrawRect(new Rect(px,     py,     panelSize,      panelSize),      texBackground, darkBg);

        // ── Grid cells ────────────────────────────────────────────────────────
        var memory   = aStarManager.GetMemory();
        var path     = aStarManager.GetCurrentPath();
        var pathSet  = new HashSet<Vector2Int>(path);

        Vector2Int playerCell = aStarManager.GetPlayerCell();
        Vector2Int targetCell = aStarManager.HasTarget() ? aStarManager.GetTargetCell() : playerCell;

        // Cells visible from panel centre
        int halfCells = panelSize / (2 * pixelsPerCell);

        for (int gx = -halfCells; gx <= halfCells; gx++)
        for (int gy = -halfCells; gy <= halfCells; gy++)
        {
            Vector2Int cell = new(playerCell.x + gx, playerCell.y + gy);

            // Screen position for this cell
            // Note: grid Y → screen: invert so "forward" (positive Z) goes up
            int sx = px + panelSize / 2 + gx * pixelsPerCell - pixelsPerCell / 2;
            int sy = py + panelSize / 2 - gy * pixelsPerCell - pixelsPerCell / 2;   // Y inverted

            if (sx < px || sx + pixelsPerCell > px + panelSize) continue;
            if (sy < py || sy + pixelsPerCell > py + panelSize) continue;

            Rect cellRect = new(sx, sy, pixelsPerCell, pixelsPerCell);

            // Choose colour
            if (pathSet.Contains(cell))
            {
                DrawRect(cellRect, texPath);
            }
            else if (memory.TryGetValue(cell, out CellState state))
            {
                switch (state)
                {
                    case CellState.Empty:    DrawRect(cellRect, texEmpty);    break;
                    case CellState.Obstacle: DrawRect(cellRect, texObstacle); break;
                    default:                 DrawRect(cellRect, texUnknown);  break;
                }
            }
            // else: unknown — draw nothing (leave background = black)
        }

        // ── Target dot ───────────────────────────────────────────────────────
        if (aStarManager.HasTarget())
        {
            int tgx = targetCell.x - playerCell.x;
            int tgy = targetCell.y - playerCell.y;

            int tsx = px + panelSize / 2 + tgx * pixelsPerCell - 4;
            int tsy = py + panelSize / 2 - tgy * pixelsPerCell - 4;

            if (tsx >= px && tsx < px + panelSize - 8 && tsy >= py && tsy < py + panelSize - 8)
                DrawRect(new Rect(tsx, tsy, 8, 8), texTarget);
        }

        // ── Player dot (always centre) ─────────────────────────────────────
        DrawRect(new Rect(px + panelSize / 2 - 5, py + panelSize / 2 - 5, 10, 10), texPlayer);

        // ── Labels ────────────────────────────────────────────────────────────
        GUI.Label(new Rect(px + 6, py + 4, panelSize - 12, 20),
                  "2D MAP  [M] toggle", labelStyle);

        // Legend row at bottom
        int ly = py + panelSize - 20;
        DrawRect(new Rect(px + 6,  ly, 10, 10), texEmpty);
        GUI.Label(new Rect(px + 18, ly - 2, 50,  14), "Floor",    labelStyle);
        DrawRect(new Rect(px + 68,  ly, 10, 10), texObstacle);
        GUI.Label(new Rect(px + 80, ly - 2, 64,  14), "Obstacle", labelStyle);
        DrawRect(new Rect(px + 144, ly, 10, 10), texPath);
        GUI.Label(new Rect(px + 156, ly - 2, 40, 14), "Path",     labelStyle);
        DrawRect(new Rect(px + 196, ly, 10, 10), texPlayer);
        GUI.Label(new Rect(px + 208, ly - 2, 50, 14), "You",      labelStyle);
        DrawRect(new Rect(px + 244, ly, 10, 10), texTarget);
        GUI.Label(new Rect(px + 256, ly - 2, 50, 14), "Target",   labelStyle);

        // Path length / status
        string status = aStarManager.HasTarget()
            ? (path.Count > 0 ? $"Path: {path.Count} steps" : "No path found")
            : "No target set  [Click] to place";
        GUI.Label(new Rect(px + 6, py + panelSize - 36, panelSize - 12, 16), status, labelStyle);
    }

    // ── Style & texture helpers ───────────────────────────────────────────────

    private void EnsureStyles()
    {
        if (stylesReady) return;

        texUnknown   = MakeTex(new Color(0.06f, 0.06f, 0.08f, 1f));
        texEmpty     = MakeTex(new Color(0.70f, 0.72f, 0.74f, 1f));
        texObstacle  = MakeTex(new Color(0.20f, 0.35f, 0.90f, 1f));
        texPath      = MakeTex(new Color(0.10f, 0.90f, 0.50f, 1f));
        texPlayer    = MakeTex(new Color(1.00f, 0.22f, 0.22f, 1f));
        texTarget    = MakeTex(new Color(1.00f, 0.90f, 0.10f, 1f));
        texBackground = MakeTex(new Color(0.05f, 0.05f, 0.08f, panelAlpha));
        texBorder    = MakeTex(new Color(0.35f, 0.60f, 1.00f, 0.75f));

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold
        };
        labelStyle.normal.textColor = new Color(0.85f, 0.90f, 1.00f);

        stylesReady = true;
    }

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private static void DrawRect(Rect r, Texture2D tex, Color? tint = null)
    {
        Color prev = GUI.color;
        if (tint.HasValue) GUI.color = tint.Value;
        GUI.DrawTexture(r, tex);
        GUI.color = prev;
    }
}
