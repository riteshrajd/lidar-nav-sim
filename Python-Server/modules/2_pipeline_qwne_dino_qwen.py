import os
import time
import re
import transformers
from PIL import Image

# --- THE FIX: Force slow image processor (MLX-compatible) ---
_orig_from_pretrained = transformers.AutoImageProcessor.from_pretrained
def _patched_from_pretrained(pretrained_model_name_or_path, **kwargs):
    kwargs.setdefault("use_fast", False)
    return _orig_from_pretrained(pretrained_model_name_or_path, **kwargs)
transformers.AutoImageProcessor.from_pretrained = _patched_from_pretrained
# ------------------------------------------------------------

from mlx_vlm import load, generate
from mlx_vlm.prompt_utils import apply_chat_template
from mlx_vlm.utils import load_config

MODEL_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/models/Qwen2.5VL-3B-VLM-R1"
IMAGE_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/image_input_3.png"

def timer(func):
    """Decorator to print execution time of functions."""
    def wrapper(*args, **kwargs):
        start_time = time.time()
        result = func(*args, **kwargs)
        end_time = time.time()
        print(f"--> [{func.__name__}] took {end_time - start_time:.2f} seconds.")
        return result
    return wrapper

@timer
def load_vision_model(model_dir):
    print("\nLoading model into memory...")
    model, processor = load(model_dir)
    config = load_config(model_dir)
    return model, processor, config

@timer
def run_qwen_inference(model, processor, config, image_path, prompt, max_tokens=50):
    """Helper function to run inference modularly."""
    formatted_prompt = apply_chat_template(processor, config, prompt, num_images=1)
    output = generate(
        model,
        processor,
        image=[image_path],
        prompt=formatted_prompt,
        max_tokens=max_tokens,
        verbose=False,
        temp=0.1 # Keep temperature low for factual/navigational tasks
    )
    return output.strip()

@timer
def step1_check_availability(model, processor, config, image_path):
    print("\n[Step 1] Checking for unoccupied tables/chairs...")
    prompt = (
        "is there any unoccupied table with an unoccupied chair, if yes then make your first letter of result as Y and if no then N and if Y then give a space after y and then give an object detector the instruction to detect the exact object that i asked for." 
    )
    response = run_qwen_inference(model, processor, config, image_path, prompt)
    print(f"Step 1 Raw Output:\n{response}")
    
    if response.upper().startswith("Y"):
        # Extract the object description (everything after the 'Y ')
        target_object = response[1:].strip()
        return True, target_object
    return False, None

@timer
def step2_get_bounding_box(model, processor, config, image_path, target_object):
    print(f"\n[Step 2] Locating the specific object: '{target_object}'...")
    # Instruct Qwen to act as the object detector for the target it just identified
    prompt = (
        f"Find the {target_object}. It must be unoccupied. "
        "Output ONLY the bounding box in this exact format: <|box_start|>(x1,y1),(x2,y2)<|box_end|> "
        "using integers from 0-1000."
    )
    response = run_qwen_inference(model, processor, config, image_path, prompt, max_tokens=100)
    print(f"Step 2 Raw Output:\n{response}")
    return response

@timer
def main():
    print("=== STARTING HAPTIC NAV PIPELINE ===")
    overall_start = time.time()

    # 1. Setup
    model, processor, config = load_vision_model(MODEL_PATH)

    # 2. Reasoning: Check existence
    is_available, target_object = step1_check_availability(model, processor, config, IMAGE_PATH)

    # 3. Execution: Locate if exists
    if is_available and target_object:
        bbox_raw = step2_get_bounding_box(model, processor, config, IMAGE_PATH, target_object)
        print(f"\n[RESULT] Target '{target_object}' located at: {bbox_raw}")
        # (You can drop your original parsing & 3D math code here)
    else:
        print("\n[RESULT] No unoccupied table/chair found. Navigation halted.")

    print(f"\n=== PIPELINE FINISHED in {time.time() - overall_start:.2f} seconds ===")

if __name__ == "__main__":
    main()