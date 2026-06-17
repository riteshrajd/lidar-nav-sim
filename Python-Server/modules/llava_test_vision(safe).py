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
image_path  = os.path.join(ROOT_DIR, "media", "image_input_3.png")
output_path = os.path.join(ROOT_DIR, "media", "output_image.png")

print("--- LLaVA Empty Table Finder ---")

if not os.path.exists(image_path):
    print(f"[ERROR] Could not find image at {image_path}")
    exit()

print(f"[1/4] Image found! Loading...")
img = Image.open(image_path).convert("RGB")
W, H = img.size
print(f"    Image size: {W}x{H}")

print(f"[2/4] Loading LLaVA from {model_path}...")
model, processor = mlx_vlm.load(model_path)
config = load_config(model_path)

processor.patch_size = 14
processor.vision_feature_select_strategy = "full"

print("[3/4] Model loaded! Asking for empty table coordinates...")

prompt_text = (
    "USER: <image>\n"
    "Find an empty table in this image. "
    "Reply with ONLY 4 normalized coordinates as: x1,y1,x2,y2\n"
    "Where x1,y1 is the top-left and x2,y2 is the bottom-right of the empty table, "
    "values between 0.0 and 1.0.\n"
    "ASSISTANT:"
)

print("[4/4] Running inference... (Thinking...)")
try:
    output = generate(
        model,
        processor,
        prompt=prompt_text,
        image=[img],
        verbose=False,
        max_tokens=60,
    )
    raw = output.strip()
    print(f"\n    Raw model output: {raw}")

    # Match any 4 floats/ints separated by commas or spaces, with optional brackets
    nums = re.findall(r'[\d.]+', raw)
    if len(nums) < 4:
        print("[ERROR] Could not find 4 coordinates in model output. Try running again.")
        exit()

    x1n, y1n, x2n, y2n = map(float, nums[:4])
    print(f"    Parsed coords (normalized): {x1n}, {y1n}, {x2n}, {y2n}")

    # Clamp to [0, 1]
    x1n, y1n, x2n, y2n = (
        max(0.0, min(1.0, x1n)),
        max(0.0, min(1.0, y1n)),
        max(0.0, min(1.0, x2n)),
        max(0.0, min(1.0, y2n)),
    )

    # Convert to pixels
    x1, y1, x2, y2 = int(x1n * W), int(y1n * H), int(x2n * W), int(y2n * H)
    print(f"    Bounding box (pixels): ({x1}, {y1}) -> ({x2}, {y2})")

    # =========================================================
    # MONOCULAR 3D GROUND-PLANE EXTRACTION MATH
    # =========================================================
    # Assuming Camera is mounted on chest (1.4 meters high) pointing straight ahead
    # Assuming iPhone/Standard Webcam FOV (Horizontal 90°, Vertical 60°)
    CAMERA_HEIGHT_M = 1.4
    FOV_V_DEG = 60.0
    FOV_H_DEG = 90.0
    
    # 1. DEPTH (Z): Calculate angle from the horizon down to the bottom of the object (y2)
    delta_y_pixels = y2 - (H / 2.0)
    if delta_y_pixels > 0:
        angle_down_rad = math.radians(delta_y_pixels / (H / FOV_V_DEG))
        distance_z_m = CAMERA_HEIGHT_M / math.tan(angle_down_rad)
    else:
        distance_z_m = 999.0 # Bottom of object is above horizon -> Infinitely far
        
    # 2. HORIZONTAL DRIFT (X): Calculate Azimuth offset
    x_center = (x1 + x2) / 2.0
    azimuth_deg = (x_center - (W / 2.0)) / (W / FOV_H_DEG)
    distance_x_m = distance_z_m * math.tan(math.radians(azimuth_deg))

    print(f"\n[GLOBAL TARGET PIN DEPLOYED]")
    if distance_z_m < 900:
        print(f"    Target is {distance_z_m:.1f} meters straight ahead (Z).")
        print(f"    Target is {distance_x_m:.1f} meters to the side (X).")
    else:
        print(f"    Target Distance: INFINITE (Object is above horizon)")
    # =========================================================

    draw = ImageDraw.Draw(img)
    draw.rectangle([x1, y1, x2, y2], outline="red", width=4)
    draw.rectangle([x1, max(0, y1 - 24), x1 + 130, y1], fill="red")
    draw.text((x1 + 4, max(0, y1 - 22)), "Empty Table", fill="white")
    
    # Draw Global Pin Data on the image
    if distance_z_m < 900:
        pin_text = f"Distance: {distance_z_m:.1f}m | Offset: {distance_x_m:.1f}m"
        tw = draw.textlength(pin_text)
        draw.rectangle([x1, y2, x1 + tw + 8, y2 + 24], fill="black")
        draw.text((x1 + 4, y2 + 4), pin_text, fill="lime")

    img.save(output_path)
    print(f"\n=========================================")
    print(f"Output saved to: {output_path}")
    print(f"=========================================\n")

except Exception as e:
    print(f"\n[CRITICAL ERROR] Inference crashed: {e}")
    import traceback
    traceback.print_exc()