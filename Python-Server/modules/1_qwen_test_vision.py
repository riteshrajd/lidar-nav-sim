import os
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

def run_test():

    print(f"[2/4] Loading model from {MODEL_PATH}...")
    model, processor = load(MODEL_PATH)
    config = load_config(MODEL_PATH)

    print("[3/4] Formatting prompt...")
    prompt = "is there any unoccupied table with an unoccupied chair, if yes then make your first letter of result as Y and if no then N and if Y then give a space after y and then give an object detector the instruction to detect the exact object that i asked for."
    formatted_prompt = apply_chat_template(
        processor,
        config,
        prompt,
        num_images=1
    )

    print("[4/4] Running inference...")
    output = generate(
        model,
        processor,
        image=[IMAGE_PATH],
        prompt=formatted_prompt,
        max_tokens=20,
        verbose=False
    )
    
    print("\n=== MODEL OUTPUT ===")
    print(output.strip())
    print("====================\n")

if __name__ == "__main__":
    run_test()