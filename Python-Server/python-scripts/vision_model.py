import os
import sys
import time
from PIL import Image

import mlx_vlm
from mlx_vlm import generate
from mlx_vlm.utils import load_config

# ==============================================================================
# CONFIGURATION
# Specify the image path and the prompt (question/instruction) below.
# ==============================================================================
MODEL_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/models/llava-1.5-7b-4bit"
IMAGE_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/input/grid/coord_gridded_image_input_2.png"
PROMPT = """You are an intelligent seeing-eye assistant for a blind person.

User request: Find me an empty table with chairs.

The provided image has a coordinate grid overlaid on it. Every grid intersection is marked with a label in the format (x,y).

Analyze the image, find the target, and respond ONLY with a valid JSON Python dictionary using this exact structure. Do not output markdown code blocks, explanations, or any other text.

{
    "target_spotted": True,
    "target_name": "empty table with chairs",
    "grid_position": "x,y",
    "direction": "front-left",
    "distance_meters": 4,
    "confidence": 0.85,
    "next_action": "Walk forward and slightly left about 4 meters to the empty table near grid point x,y.",
    "checkpoint": {"id": 1, "x": 0, "y": 0, "z": 0},
    "alternative_suggestions": [],
    "clarification_question": None,
}

Rules:

    Look at the yellow (x,y) labels on the intersections to determine the target's exact location.

    Provide the "grid_position" as a string, e.g., "6,7". If the object is between points, you may use decimals like "6.5,7.2".

    Choose the closest truly empty table.

    Estimate "distance_meters" logically based on perspective."""
    
# ==============================================================================

def run_vision_model(image_path, prompt_text):
    print(f"Loading model from {MODEL_PATH}...")
    model, processor = mlx_vlm.load(MODEL_PATH)
    config = load_config(MODEL_PATH)
    
    # LLaVA specific processor configurations
    processor.patch_size = 14
    processor.vision_feature_select_strategy = "full"

    print("Formatting prompt...")
    # LLaVA formatting
    formatted_prompt = f"USER: <image>\n{prompt_text}\nASSISTANT:"

    print("Loading image...")
    img = Image.open(image_path).convert("RGB")

    print("Running inference...")
    output = generate(
        model,
        processor,
        image=[img],
        prompt=formatted_prompt,
        max_tokens=300,
        verbose=False
    )
    
    # Strip markdown escape characters that LLaVA sometimes adds to JSON keys
    output = output.replace("\\_", "_")
    
    print("\n" + "="*50)
    print("=== MODEL OUTPUT ===")
    print("="*50)
    print(output.strip())
    print("="*50 + "\n")

if __name__ == "__main__":
    if not os.path.exists(IMAGE_PATH):
        print(f"Error: Could not find image at '{IMAGE_PATH}'")
        sys.exit(1)
        
    start_time = time.time()
    run_vision_model(IMAGE_PATH, PROMPT)
    elapsed_time = time.time() - start_time
    print(f"⏱️ Total execution time: {elapsed_time:.2f} seconds\n")
