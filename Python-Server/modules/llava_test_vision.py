import os
import re
import math
from PIL import Image, ImageDraw
import mlx_vlm
from mlx_vlm import generate
from mlx_vlm.utils import load_config

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR   = os.path.join(SCRIPT_DIR, "..")
model_path  = os.path.join(ROOT_DIR, "models", "llava-1.5-7b-4bit")

MAX_IMAGES = 5

print("--- LLaVA Grid-Based Target Finder ---")

print(f"[1/2] Loading LLaVA from {model_path}...")
model, processor = mlx_vlm.load(model_path)
config = load_config(model_path)
processor.patch_size = 14
processor.vision_feature_select_strategy = "full"
print("[2/2] Model loaded! Starting batch process...\n")

for i in range(1, MAX_IMAGES + 1):
    input_filename = f"image_input_{i}.png"
    output_filename = f"image_output_{i}_llava.png"
    
    image_path  = os.path.join(ROOT_DIR, "media", input_filename)
    output_path = os.path.join(ROOT_DIR, "media", output_filename)

    if not os.path.exists(image_path):
        continue

    print(f"--- Processing: {input_filename} ---")
    
    try:
        # Load the original image
        img = Image.open(image_path).convert("RGB")
        W, H = img.size
        
        # Create a copy to draw the grid onto
        grid_img = img.copy()
        draw = ImageDraw.Draw(grid_img)
        
        dx = W / 3.0
        dy = H / 3.0
        
        # 1. DRAW THE GRID LINES
        for j in range(1, 3):
            draw.line([(j*dx, 0), (j*dx, H)], fill="yellow", width=4)
            draw.line([(0, j*dy), (W, j*dy)], fill="yellow", width=4)
            
        # 2. DRAW THE TILE NUMBERS
        for n in range(1, 10):
            row = (n - 1) // 3
            col = (n - 1) % 3
            cx = col * dx + (dx / 2)
            cy = row * dy + (dy / 2)
            
            # Draw a black box so the text is visible against any background
            draw.rectangle([cx-15, cy-15, cx+15, cy+15], fill="black")
            # Draw the number
            draw.text((cx-4, cy-6), str(n), fill="yellow")

        # 3. ASK THE VLM
        prompt_text = (
            "USER: <image>\n"
            "This image is divided into a 3x3 grid with tiles numbered 1 through 9. "
            "Look closely at all the tiles. Which single tile number contains an EMPTY table or EMPTY chair (no humans sitting there)? "
            "Reply with ONLY the single digit of the best tile (1-9). Do not explain.\n"
            "ASSISTANT:"
        )

        print("    Running inference on grid image...")
        output = generate(
            model,
            processor,
            prompt=prompt_text,
            image=[grid_img], # We feed it the image WITH the drawn grid
            verbose=False,
            max_tokens=10,
        )
        
        raw = output.strip()
        print(f"    Raw model output: {raw}")

        # Extract the first digit it mentions
        match = re.search(r'[1-9]', raw)
        if not match:
            print("    [WARNING] Model did not return a valid tile number (1-9).\n")
            grid_img.save(output_path)
            continue
            
        target_tile = int(match.group())
        print(f"    Target acquired: Tile {target_tile}")

        # 4. HIGHLIGHT THE CHOSEN TILE
        row = (target_tile - 1) // 3
        col = (target_tile - 1) % 3
        x1, y1 = col * dx, row * dy
        x2, y2 = (col + 1) * dx, (row + 1) * dy
        
        # Draw a thick lime green border around the chosen tile
        draw.rectangle([x1, y1, x2, y2], outline="lime", width=8)
        
        # =========================================================
        # 3D GROUND-PLANE EXTRACTION MATH (Using Tile Center)
        # =========================================================
        # We use the absolute center of the selected tile as our waypoint
        target_cx = x1 + (dx / 2.0)
        target_cy = y1 + (dy / 2.0)
        
        CAMERA_HEIGHT_M = 1.4
        FOV_V_DEG = 60.0
        FOV_H_DEG = 90.0
        
        delta_y_pixels = target_cy - (H / 2.0)
        if delta_y_pixels > 0:
            angle_down_rad = math.radians(delta_y_pixels / (H / FOV_V_DEG))
            distance_z_m = CAMERA_HEIGHT_M / math.tan(angle_down_rad)
        else:
            distance_z_m = 999.0 
            
        azimuth_deg = (target_cx - (W / 2.0)) / (W / FOV_H_DEG)
        distance_x_m = distance_z_m * math.tan(math.radians(azimuth_deg))

        # Draw a crosshair in the center of the chosen tile
        r = 15
        draw.ellipse([target_cx - r, target_cy - r, target_cx + r, target_cy + r], outline="lime", width=3)
        draw.line([target_cx, target_cy - r - 10, target_cx, target_cy + r + 10], fill="lime", width=2)
        draw.line([target_cx - r - 10, target_cy, target_cx + r + 10, target_cy], fill="lime", width=2)

        # Draw Label
        if distance_z_m < 900:
            pin_text = f"Tile {target_tile} -> Z:{distance_z_m:.1f}m X:{distance_x_m:.1f}m"
            draw.rectangle([x1+10, y2-30, x1+220, y2-10], fill="black")
            draw.text((x1+14, y2-26), pin_text, fill="lime")

        grid_img.save(output_path)
        print(f"    [SUCCESS] Saved grid visualization to: {output_path}\n")

    except Exception as e:
        print(f"    [CRITICAL ERROR] Inference crashed on {input_filename}: {e}\n")

print("--- Grid Batch Complete ---")