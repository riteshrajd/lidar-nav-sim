import os
import math
import torch
from PIL import Image, ImageDraw
from transformers import AutoProcessor, AutoModelForCausalLM

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR   = os.path.join(SCRIPT_DIR, "..")
model_path = os.path.join(ROOT_DIR, "models", "Florence-2-large-ft")

media_dir  = os.path.join(ROOT_DIR, "media")
output_dir = os.path.join(media_dir, "florence-output")
os.makedirs(output_dir, exist_ok=True)

MAX_IMAGES = 5

print("--- Florence-2 Exact Target Pinpoint ---")

print(f"[1/2] Loading Florence-2 from {model_path}...")
# FIX: Use CPU + float32 — MPS has an unfixable KV-cache bug with Florence-2's
# custom modeling_florence2.py that corrupts past_key_values regardless of settings.
# Florence-2 is fast enough on CPU for this use case.
device      = "cpu"
torch_dtype = torch.float32

model = AutoModelForCausalLM.from_pretrained(
    model_path,
    dtype=torch_dtype,
    trust_remote_code=True,
    attn_implementation="eager"
).to(device)
model.eval()

processor = AutoProcessor.from_pretrained(model_path, trust_remote_code=True)
print(f"[2/2] Model loaded on {device.upper()}! Starting batch process...\n")

task_prompt = "<OPEN_VOCABULARY_DETECTION>"
text_input  = "empty chair"
prompt      = task_prompt + text_input

for i in range(1, MAX_IMAGES + 1):
    input_filename  = f"image_input_{i}.png"
    output_filename = f"image_output_florence_{i}.png"
    image_path      = os.path.join(media_dir, input_filename)
    output_path     = os.path.join(output_dir, output_filename)

    if not os.path.exists(image_path):
        continue

    print(f"--- Processing: {input_filename} ---")

    try:
        img = Image.open(image_path).convert("RGB")
        W, H = img.size
        draw = ImageDraw.Draw(img)

        print(f"    Searching for '{text_input}'...")

        inputs       = processor(text=prompt, images=img, return_tensors="pt")
        input_ids    = inputs["input_ids"].to(device)
        pixel_values = inputs["pixel_values"].to(device, torch_dtype)

        with torch.no_grad():
            generated_ids = model.generate(
                input_ids=input_ids,
                pixel_values=pixel_values,
                max_new_tokens=1024,
                num_beams=3,
            )

        generated_text = processor.batch_decode(generated_ids, skip_special_tokens=False)[0]
        parsed_answer  = processor.post_process_generation(
            generated_text, task=task_prompt, image_size=(W, H)
        )

        detections = parsed_answer.get(task_prompt, {})
        bboxes     = detections.get("bboxes", [])

        if not bboxes:
            print(f"    [WARNING] Could not find any '{text_input}'.\n")
            img.save(output_path)
            continue

        def box_area(b):
            return (b[2] - b[0]) * (b[3] - b[1])
        target_box      = max(bboxes, key=box_area)
        x1, y1, x2, y2 = target_box
        print(f"    Target locked at pixels: [{x1:.1f}, {y1:.1f}, {x2:.1f}, {y2:.1f}]")

        draw.rectangle([x1, y1, x2, y2], outline="lime", width=6)

        # =========================================================
        # 3D GROUND-PLANE EXTRACTION MATH
        # =========================================================
        target_cx = x1 + ((x2 - x1) / 2.0)
        target_cy = y1 + ((y2 - y1) / 2.0)

        CAMERA_HEIGHT_M = 1.4
        FOV_V_DEG       = 60.0
        FOV_H_DEG       = 90.0

        delta_y_pixels = target_cy - (H / 2.0)
        if delta_y_pixels > 0:
            angle_down_rad = math.radians(delta_y_pixels / (H / FOV_V_DEG))
            distance_z_m   = CAMERA_HEIGHT_M / math.tan(angle_down_rad)
        else:
            distance_z_m = 999.0

        azimuth_deg  = (target_cx - (W / 2.0)) / (W / FOV_H_DEG)
        distance_x_m = distance_z_m * math.tan(math.radians(azimuth_deg))
        # =========================================================

        r = 15
        draw.ellipse([target_cx-r, target_cy-r, target_cx+r, target_cy+r], outline="lime", width=3)
        draw.line([target_cx, target_cy-r-10, target_cx, target_cy+r+10], fill="lime", width=2)
        draw.line([target_cx-r-10, target_cy, target_cx+r+10, target_cy],  fill="lime", width=2)

        if distance_z_m < 900:
            pin_text  = f"Z: {distance_z_m:.1f}m | X: {distance_x_m:.1f}m"
            text_bbox = draw.textbbox((x1+14, y1-26), pin_text)
            draw.rectangle([x1+10, y1-30, text_bbox[2]+10, text_bbox[3]+4], fill="black")
            draw.text((x1+14, y1-26), pin_text, fill="lime")

        img.save(output_path)
        print(f"    [SUCCESS] Saved to: {output_path}\n")

    except Exception as e:
        print(f"    [CRITICAL ERROR] on {input_filename}: {e}\n")
        import traceback
        traceback.print_exc()

print("--- Precision Batch Complete ---")