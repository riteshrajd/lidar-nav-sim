import os
import torch
import numpy as np
from PIL import Image, ImageDraw

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR   = os.path.join(SCRIPT_DIR, "..")
DINO_PATH  = os.path.join(ROOT_DIR, "models", "grounding-dino-base")
OUTPUT_DIR = os.path.join(ROOT_DIR, "media", "output", "dino")

def get_device():
    if torch.backends.mps.is_available(): return torch.device("mps")
    if torch.cuda.is_available(): return torch.device("cuda")
    return torch.device("cpu")

def load_dino(device):
    from transformers import AutoProcessor, AutoModelForZeroShotObjectDetection
    print(f"[DINO] Loading model...")
    processor = AutoProcessor.from_pretrained(DINO_PATH)
    model     = AutoModelForZeroShotObjectDetection.from_pretrained(DINO_PATH).to(device)
    model.eval()
    return processor, model

def detect_all_targets(image: Image.Image, text: str, processor, model, device, box_thresh=0.30):
    """Finds ALL instances of the target text above the threshold."""
    query = text.strip()
    if not query.endswith("."): query += "."

    inputs = processor(images=image, text=query, return_tensors="pt")
    inputs = {k: v.to(device) for k, v in inputs.items()}

    with torch.no_grad():
        outputs = model(**inputs)

    probs = outputs.logits.sigmoid()[0] 
    scores = probs.max(dim=-1).values.cpu().numpy()

    # Find ALL boxes that meet the threshold
    valid_indices = np.where(scores >= box_thresh)[0]
    
    detections = []
    W, H = image.size

    for idx in valid_indices:
        score = float(scores[idx])
        cx_n, cy_n, w_n, h_n = outputs.pred_boxes[0, idx].cpu().numpy()
        
        cx, cy = cx_n * W, cy_n * H
        w, h   = w_n * W, h_n * H
        box = [cx - w/2, cy - h/2, cx + w/2, cy + h/2]
        
        detections.append((box, score))

    return detections

def save_annotated_image(image: Image.Image, detections, prompt: str, original_filename: str):
    """Draws boxes and saves the file to the output directory."""
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    img = image.copy()
    draw = ImageDraw.Draw(img)

    for box, score in detections:
        x1, y1, x2, y2 = box
        draw.rectangle([x1, y1, x2, y2], outline="red", width=4)
        
        label = f"{prompt} ({score:.2f})"
        draw.rectangle([x1, max(0, y1 - 20), x1 + len(label)*6, y1], fill="red")
        draw.text((x1 + 2, max(0, y1 - 18)), label, fill="white")

    out_name = os.path.splitext(original_filename)[0] + "_dino.png"
    out_path = os.path.join(OUTPUT_DIR, out_name)
    img.save(out_path)
    print(f"[Saved] Annotated image saved to: {out_path}")
    return out_path

def run_dino_detector(image_path: str, prompt: str, box_thresh: float = 0.30):
    """Main callable function for the pipeline."""
    if not os.path.exists(image_path):
        print(f"[ERROR] Image not found: {image_path}")
        return None

    device = get_device()
    processor, model = load_dino(device)
    image = Image.open(image_path).convert("RGB")

    print(f"[DINO] Searching for '{prompt}'...")
    detections = detect_all_targets(image, prompt, processor, model, device, box_thresh)

    if not detections:
        print(f"[RESULT] No objects found matching '{prompt}' above threshold {box_thresh}.")
        return None

    print(f"[RESULT] Found {len(detections)} matching object(s).")
    filename = os.path.basename(image_path)
    saved_path = save_annotated_image(image, detections, prompt, filename)
    
    return saved_path

# ==========================================
# USAGE TEST
# ==========================================
if __name__ == "__main__":
    TEST_IMAGE = os.path.join(ROOT_DIR, "media", "image_input_3.png")
    TEST_PROMPT = "green chair"
    
    run_dino_detector(TEST_IMAGE, TEST_PROMPT, box_thresh=0.25)