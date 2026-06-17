import pygame
import heapq
import math
import sys

# --- Configuration ---
GRID_W = 60
GRID_H = 40
CELL_SIZE = 15
WIDTH = GRID_W * CELL_SIZE
HEIGHT = GRID_H * CELL_SIZE
FPS = 60
MOVE_DELAY_MS = 100
SENSOR_RADIUS = 5

# --- Cell States (Memory) ---
UNKNOWN = 0
EMPTY = 1
OBSTACLE = 2

# --- Colors ---
COLOR_UNKNOWN = (30, 30, 30)         # Blind mode unseen
COLOR_EMPTY = (200, 200, 200)        # Discovered safe space
COLOR_OBSTACLE = (50, 50, 200)       # Discovered wall (Blue)
COLOR_TRUE_EMPTY = (255, 255, 255)   # Omniscient safe space / Unseen
COLOR_TRUE_OBST = (200, 50, 50)      # Omniscient unseen wall (Red)
COLOR_AGENT = (50, 200, 50)          # Green
COLOR_TARGET = (255, 215, 0)         # Gold
COLOR_PATH = (0, 255, 255)           # Cyan line
COLOR_GRID = (60, 60, 60)

pygame.init()
screen = pygame.display.set_mode((WIDTH, HEIGHT))
pygame.display.set_caption("Topological Exploration Simulation")
clock = pygame.time.Clock()
font = pygame.font.SysFont(None, 24)

# --- State Variables ---
ground_truth = [[False for _ in range(GRID_H)] for _ in range(GRID_W)]
memory_grid = [[UNKNOWN for _ in range(GRID_H)] for _ in range(GRID_W)]

start_pos = (5, 5)
target_pos = (GRID_W - 5, GRID_H - 5)
agent_pos = start_pos
agent_path = []

blind_mode = False
auto_move = False
last_move_time = 0

def reset_agent():
    global agent_pos, agent_path, auto_move
    agent_pos = start_pos
    agent_path = []
    auto_move = False
    sense_surroundings()

def clear_all():
    global ground_truth, memory_grid, agent_pos, agent_path, auto_move
    ground_truth = [[False for _ in range(GRID_H)] for _ in range(GRID_W)]
    memory_grid = [[UNKNOWN for _ in range(GRID_H)] for _ in range(GRID_W)]
    agent_pos = start_pos
    agent_path = []
    auto_move = False
    sense_surroundings()

def heuristic(a, b):
    # Octile distance for 8-way movement
    dx = abs(a[0] - b[0])
    dy = abs(a[1] - b[1])
    return dx + dy + (1.414 - 2) * min(dx, dy)

def get_neighbors(node, grid):
    neighbors = []
    x, y = node
    for dx, dy in [(0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)]:
        nx, ny = x + dx, y + dy
        if 0 <= nx < GRID_W and 0 <= ny < GRID_H:
            if grid[nx][ny] != OBSTACLE:
                neighbors.append((nx, ny))
    return neighbors

def calculate_path(start, target, grid):
    open_set = []
    heapq.heappush(open_set, (0, start))
    came_from = {}
    g_score = {start: 0}
    
    while open_set:
        _, current = heapq.heappop(open_set)
        
        if current == target:
            path = []
            while current in came_from:
                path.append(current)
                current = came_from[current]
            path.reverse()
            return path
            
        for neighbor in get_neighbors(current, grid):
            cost = 1 if (neighbor[0] == current[0] or neighbor[1] == current[1]) else 1.414
            tentative_g_score = g_score[current] + cost
            
            if neighbor not in g_score or tentative_g_score < g_score[neighbor]:
                came_from[neighbor] = current
                g_score[neighbor] = tentative_g_score
                f_score = tentative_g_score + heuristic(neighbor, target)
                heapq.heappush(open_set, (f_score, neighbor))
                
    return []

def sense_surroundings():
    ax, ay = agent_pos
    changed = False
    for x in range(ax - SENSOR_RADIUS, ax + SENSOR_RADIUS + 1):
        for y in range(ay - SENSOR_RADIUS, ay + SENSOR_RADIUS + 1):
            if 0 <= x < GRID_W and 0 <= y < GRID_H:
                if math.hypot(x - ax, y - ay) <= SENSOR_RADIUS:
                    real_state = OBSTACLE if ground_truth[x][y] else EMPTY
                    if memory_grid[x][y] != real_state:
                        memory_grid[x][y] = real_state
                        changed = True
    return changed

def is_path_valid():
    if not agent_path:
        return False
    for node in agent_path:
        if memory_grid[node[0]][node[1]] == OBSTACLE:
            return False
    return True

def draw_text_bg(surface, text, font, pos, color=(255, 255, 255), bg_color=(0, 0, 0, 150)):
    text_surface = font.render(text, True, color)
    rect = text_surface.get_rect(topleft=pos)
    
    # Draw transparent background
    bg_surface = pygame.Surface((rect.width + 10, rect.height + 4), pygame.SRCALPHA)
    bg_surface.fill(bg_color)
    surface.blit(bg_surface, (rect.x - 5, rect.y - 2))
    surface.blit(text_surface, rect)

clear_all()
running = True
mouse_dragging = False
mouse_btn = None

while running:
    current_time = pygame.time.get_ticks()
    
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            running = False
            
        elif event.type == pygame.MOUSEBUTTONDOWN:
            mouse_dragging = True
            mouse_btn = event.button
            mx, my = pygame.mouse.get_pos()
            gx, gy = mx // CELL_SIZE, my // CELL_SIZE
            if 0 <= gx < GRID_W and 0 <= gy < GRID_H:
                # Add or erase obstacles
                if mouse_btn == 1:
                    ground_truth[gx][gy] = True
                    keys = pygame.key.get_pressed()
                    if keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT] or keys[pygame.K_v]:
                        memory_grid[gx][gy] = OBSTACLE
                        agent_path = calculate_path(agent_pos, target_pos, memory_grid)
                elif mouse_btn == 3:
                    ground_truth[gx][gy] = False
                    memory_grid[gx][gy] = UNKNOWN
                sense_surroundings()
                    
        elif event.type == pygame.MOUSEBUTTONUP:
            mouse_dragging = False
            
        elif event.type == pygame.MOUSEMOTION:
            if mouse_dragging:
                mx, my = pygame.mouse.get_pos()
                gx, gy = mx // CELL_SIZE, my // CELL_SIZE
                if 0 <= gx < GRID_W and 0 <= gy < GRID_H:
                    if mouse_btn == 1:
                        ground_truth[gx][gy] = True
                        keys = pygame.key.get_pressed()
                        if keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT] or keys[pygame.K_v]:
                            memory_grid[gx][gy] = OBSTACLE
                            agent_path = calculate_path(agent_pos, target_pos, memory_grid)
                    elif mouse_btn == 3:
                        ground_truth[gx][gy] = False
                        memory_grid[gx][gy] = UNKNOWN
                    sense_surroundings()
                    
        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_SPACE:
                auto_move = not auto_move
            elif event.key == pygame.K_b:
                blind_mode = not blind_mode
            elif event.key == pygame.K_r:
                reset_agent()
            elif event.key == pygame.K_c:
                clear_all()
            elif event.key == pygame.K_m:
                memory_grid = [[UNKNOWN for _ in range(GRID_H)] for _ in range(GRID_W)]
                agent_path = []
                sense_surroundings()
            elif event.key == pygame.K_g:
                # Single step
                auto_move = False
                if agent_pos != target_pos:
                    memory_changed = sense_surroundings()
                    if memory_changed or not is_path_valid() or not agent_path:
                        agent_path = calculate_path(agent_pos, target_pos, memory_grid)
                    if agent_path:
                        agent_pos = agent_path.pop(0)

    keys = pygame.key.get_pressed()
    
    # Manual movement
    if not auto_move and current_time - last_move_time > MOVE_DELAY_MS:
        dx, dy = 0, 0
        if keys[pygame.K_w]: dy -= 1
        if keys[pygame.K_s]: dy += 1
        if keys[pygame.K_a]: dx -= 1
        if keys[pygame.K_d]: dx += 1
        
        if dx != 0 or dy != 0:
            nx, ny = agent_pos[0] + dx, agent_pos[1] + dy
            if 0 <= nx < GRID_W and 0 <= ny < GRID_H:
                if not ground_truth[nx][ny]: 
                    agent_pos = (nx, ny)
                sense_surroundings()
                agent_path = calculate_path(agent_pos, target_pos, memory_grid)
            last_move_time = current_time

    # Agent auto-move
    if auto_move and current_time - last_move_time > MOVE_DELAY_MS:
        if agent_pos != target_pos:
            memory_changed = sense_surroundings()
            
            if memory_changed or not is_path_valid() or not agent_path:
                agent_path = calculate_path(agent_pos, target_pos, memory_grid)
                
            if agent_path:
                agent_pos = agent_path.pop(0)
                
        last_move_time = current_time

    # Draw Background
    screen.fill(COLOR_UNKNOWN)
    
    # Draw Grid Cells
    for x in range(GRID_W):
        for y in range(GRID_H):
            rect = pygame.Rect(x * CELL_SIZE, y * CELL_SIZE, CELL_SIZE, CELL_SIZE)
            
            mem_state = memory_grid[x][y]
            is_true_obst = ground_truth[x][y]
            
            if blind_mode:
                if mem_state == OBSTACLE:
                    color = COLOR_OBSTACLE
                elif mem_state == EMPTY:
                    color = COLOR_EMPTY
                else:
                    color = COLOR_UNKNOWN
            else:
                if mem_state == OBSTACLE:
                    color = COLOR_OBSTACLE
                elif is_true_obst:
                    color = COLOR_TRUE_OBST
                elif mem_state == EMPTY:
                    color = COLOR_EMPTY
                else:
                    color = COLOR_UNKNOWN # Draw as dark unknown even in omni if empty and unseen
                    
            pygame.draw.rect(screen, color, rect)
            pygame.draw.rect(screen, COLOR_GRID, rect, 1)

    # Draw Path
    if agent_path:
        points = [(node[0] * CELL_SIZE + CELL_SIZE // 2, node[1] * CELL_SIZE + CELL_SIZE // 2) for node in agent_path]
        points.insert(0, (agent_pos[0] * CELL_SIZE + CELL_SIZE // 2, agent_pos[1] * CELL_SIZE + CELL_SIZE // 2))
        if len(points) > 1:
            pygame.draw.lines(screen, COLOR_PATH, False, points, 2)

    # Draw Elements (Target, Agent, Bubble)
    tx, ty = target_pos[0] * CELL_SIZE + CELL_SIZE // 2, target_pos[1] * CELL_SIZE + CELL_SIZE // 2
    pygame.draw.circle(screen, COLOR_TARGET, (tx, ty), int(CELL_SIZE * 0.4))
    
    ax, ay = agent_pos[0] * CELL_SIZE + CELL_SIZE // 2, agent_pos[1] * CELL_SIZE + CELL_SIZE // 2
    pygame.draw.circle(screen, COLOR_AGENT, (ax, ay), int(CELL_SIZE * 0.4))
    
    # Bubble outline
    pygame.draw.circle(screen, (100, 255, 100), (ax, ay), int(SENSOR_RADIUS * CELL_SIZE), 1)

    # UI Panel
    mode_str = "BLIND (Only See Memory)" if blind_mode else "OMNISCIENT (See All Truth)"
    state_str = "PLAYING" if auto_move else "PAUSED"
    
    ui_lines = [
        f"Mode: {mode_str}   [B] Toggle",
        f"State: {state_str}   [Space] Auto-Play   [G] Step",
        f"Move: [WASD]   Wall: [L/R Click]   VLM Scout/Block: [Shift+Click]",
        f"[R] Reset Pos   [M] Forget Memory   [C] Clear All",
    ]
    
    for i, line in enumerate(ui_lines):
        draw_text_bg(screen, line, font, (10, 10 + i * 25))

    pygame.display.flip()
    clock.tick(FPS)

pygame.quit()
sys.exit()
