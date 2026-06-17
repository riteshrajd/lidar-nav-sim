"""
Target Detection + 3D Localization Pipeline
============================================
Just run: python detect_target.py
"""

import os
import math
import torch
import numpy as np
from PIL import Image, ImageDraw

# ─────────────────────────────────────────────────────────────────────────────
# CONFIGURATION – Hardcoded Inputs
# ─────────────────────────────────────────────────────────────────────────────
IMAGE_PATH  = "../media/image_input_1.png"
TARGET_TEXT = "empty chair"

BOX_THRESH  = 0.30  # Minimum confidence for object detection
# ─────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR   = os.path.join(SCRIPT_DIR, "..")

DINO_PATH  = os.path.join(ROOT_DIR, "models", "grounding-dino-base")
DEPTH_PATH = os.path.join(ROOT_DIR, "models", "Depth-Anything-V2-Small-hf")
OUTPUT_DIR = os.path.join(ROOT_DIR, "media", "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)

# ── CAMERA INTRINSICS ──
H_FOV_DEG = 70.0   # Horizontal field of view (degrees)
V_FOV_DEG = 50.0   # Vertical field of view (degrees)

def get_device():
    if torch.backends.mps.is_available(): return torch.device("mps")
    if torch.cuda.is_available(): return torch.device("cuda")
    return torch.device("cpu")

def load_dino(device):
    from transformers import AutoProcessor, AutoModelForZeroShotObjectDetection
    print(f"[DINO] Loading from {DINO_PATH} …")
    processor = AutoProcessor.from_pretrained(DINO_PATH)
    model     = AutoModelForZeroShotObjectDetection.from_pretrained(DINO_PATH).to(device)
    model.eval()
    return processor, model

def load_depth(device):
    from transformers import AutoImageProcessor, AutoModelForDepthEstimation
    print(f"[Depth] Loading from {DEPTH_PATH} …")
    processor = AutoImageProcessor.from_pretrained(DEPTH_PATH)
    model     = AutoModelForDepthEstimation.from_pretrained(DEPTH_PATH).to(device)
    model.eval()
    return processor, model

def detect_target(image: Image.Image, text: str, processor, model, device, box_thresh=0.30):
    """
    Manually parses DINO logits to avoid transformers versioning issues.
    """
    query = text.strip()
    if not query.endswith("."): query += "."

    inputs = processor(images=image, text=query, return_tensors="pt")
    inputs = {k: v.to(device) for k, v in inputs.items()}

    with torch.no_grad():
        outputs = model(**inputs)

    # outputs.logits shape: (batch, num_queries, seq_len)
    # Apply sigmoid to get probabilities, take the max over the text tokens
    probs = outputs.logits.sigmoid()[0] 
    scores = probs.max(dim=-1).values.cpu().numpy()

    best_idx = int(np.argmax(scores))
    best_score = float(scores[best_idx])

    if best_score < box_thresh:
        return None, None

    # Get the best box. Format is normalized [cx, cy, w, h]
    cx_n, cy_n, w_n, h_n = outputs.pred_boxes[0, best_idx].cpu().numpy()
    
    # Convert to absolute pixel coordinates [x1, y1, x2, y2]
    W, H = image.size
    cx, cy = cx_n * W, cy_n * H
    w, h   = w_n * W, h_n * H

    box = np.array([cx - w/2, cy - h/2, cx + w/2, cy + h/2])
    return box, best_score

def estimate_depth(image: Image.Image, processor, model, device):
    inputs = processor(images=image, return_tensors="pt")
    inputs = {k: v.to(device) for k, v in inputs.items()}

    with torch.no_grad():
        depth_output = model(**inputs)

    predicted = torch.nn.functional.interpolate(
        depth_output.predicted_depth.unsqueeze(1),
        size=image.size[::-1], 
        mode="bicubic",
        align_corners=False,
    ).squeeze().cpu().numpy()

    return predicted.astype(np.float32)

def localize(box, depth_map, img_w, img_h):
    x1, y1, x2, y2 = box
    cx, cy = (x1 + x2) / 2.0, (y1 + y2) / 2.0

    # Normalise to [-0.5, +0.5] range
    nx = (cx - img_w / 2.0) / img_w 
    ny = (img_h / 2.0 - cy) / img_h 

    azimuth_deg   = nx * H_FOV_DEG
    elevation_deg = ny * V_FOV_DEG

    # Sample depth patch
    px, py = int(np.clip(cx, 0, img_w - 1)), int(np.clip(cy, 0, img_h - 1))
    patch_r = max(2, int(min(x2-x1, y2-y1) * 0.1))
    
    py0, py1 = max(0, py - patch_r), min(img_h, py + patch_r)
    px0, px1 = max(0, px - patch_r), min(img_w, px + patch_r)
    
    rel_depth = float(np.median(depth_map[py0:py1, px0:px1]))

    return azimuth_deg, elevation_deg, rel_depth, (cx, cy)

def annotate(image: Image.Image, box, cx, cy, score, azimuth, elevation, rel_depth, target_text) -> Image.Image:
    img  = image.copy()
    draw = ImageDraw.Draw(img)
    W, H = img.size
    x1, y1, x2, y2 = box

    draw.rectangle([x1, y1, x2, y2], outline="lime", width=4)

    r = 14
    draw.ellipse([cx-r, cy-r, cx+r, cy+r], outline="lime", width=3)
    draw.line([cx, cy-r-8, cx, cy+r+8],  fill="lime", width=2)
    draw.line([cx-r-8, cy, cx+r+8, cy],  fill="lime", width=2)

    bar_h = int(np.clip(rel_depth / 200.0 * (y2 - y1), 4, y2 - y1))
    draw.rectangle([x1-10, y2 - bar_h, x1-4, y2], fill="cyan")

    lines = [
        f"Target : {target_text}",
        f"Conf   : {score:.2f}",
        f"Azimuth: {azimuth:+.1f}°  ({'RIGHT' if azimuth>0 else 'LEFT'})",
        f"Elev   : {elevation:+.1f}°  ({'UP' if elevation>0 else 'DOWN'})",
        f"Depth  : {rel_depth:.1f}",
    ]
    
    line_h, pad = 18, 8
    box_h = len(lines) * line_h + pad * 2
    box_w = 300
    label_x = max(0, min(int(x1), W - box_w - 4))
    label_y = max(0, int(y1) - box_h - 4)

    draw.rectangle([label_x, label_y, label_x + box_w, label_y + box_h], fill=(0, 0, 0, 200))
    for i, line in enumerate(lines):
        draw.text((label_x + pad, label_y + pad + i * line_h), line, fill="lime")

    return img

def main():
    if not os.path.exists(IMAGE_PATH):
        raise FileNotFoundError(f"Image not found: {IMAGE_PATH}")

    device = get_device()
    print(f"\n[System] Running on: {device}")

    dino_proc, dino_model = load_dino(device)
    dep_proc, dep_model   = load_depth(device)

    image = Image.open(IMAGE_PATH).convert("RGB")
    W, H  = image.size
    print(f"\n[Image] {IMAGE_PATH} ({W}×{H})")
    print(f"[Query] '{TARGET_TEXT}'\n")

    print("[1/3] Running Grounding DINO …")
    box, score = detect_target(image, TARGET_TEXT, dino_proc, dino_model, device, BOX_THRESH)

    if box is None:
        print(f"\n[RESULT] Target '{TARGET_TEXT}' NOT FOUND. Try lowering BOX_THRESH or rephrasing.\n")
        return

    print(f"       Found! box={box.astype(int).tolist()}  score={score:.3f}")

    print("[2/3] Running Depth Anything V2 …")
    depth_map = estimate_depth(image, dep_proc, dep_model, device)
    
    print("[3/3] Computing 3D direction …")
    azimuth, elevation, rel_depth, (cx, cy) = localize(box, depth_map, W, H)

    print("\n" + "="*50)
    print(f"  TARGET      : {TARGET_TEXT}")
    print(f"  CONFIDENCE  : {score:.2f}")
    print(f"  AZIMUTH     : {azimuth:+.1f}°")
    print(f"  ELEVATION   : {elevation:+.1f}°")
    print(f"  REL. DEPTH  : {rel_depth:.1f}")
    print("="*50 + "\n")

    result_img = annotate(image, box, cx, cy, score, azimuth, elevation, rel_depth, TARGET_TEXT)
    out_name   = os.path.splitext(os.path.basename(IMAGE_PATH))[0] + "_detected.png"
    out_path   = os.path.join(OUTPUT_DIR, out_name)
    result_img.save(out_path)
    print(f"[Saved] {out_path}\n")

if __name__ == "__main__":
    main()