import os
import sys
import time
from step1_grid import apply_grid
from step2_vision import run_vision_model
from step3_parser import process_target

# ==============================================================================
# CONFIGURATION
# ==============================================================================
IMAGE_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/image_input_6.png"

# Switch between models: "llava", "gemma", or "gemini"
MODEL_CHOICE = "gemini"

PROMPT = """You are an intelligent seeing-eye assistant for a blind person.

User request: take me to the washroom, find the door.

The provided image has a coordinate grid overlaid on it. Every grid intersection is marked with a label in the format (x,y).

Analyze the image, find the target, and respond ONLY with a strictly valid JSON document using this exact structure. Do not output markdown code blocks, explanations, or any other text. USE ONLY VALID JSON BOOLEANS (true, false, null), not Python capitalized keywords!

{
    "target_spotted": true or false,
    "target_name": "empty table with chairs" or "exit door" or "stair entry point" or "escalator",
    "grid_position": "x,y",
    "direction": "front-left",
    "distance_meters": 4,
    "confidence": 0.85,
    "next_action": "Walk forward and slightly left about 4 meters to the empty table near grid point x,y." or "the stair entry point is around 11 meters away in front of you",
    "checkpoint": {"id": 1, "x": 0, "y": 0, "z": 0},
    "alternative_suggestions": null or "i dont see exit door but i might see if you go to the end of this hallway" or "theres no escalator but i see an elevator" or "theres no elevator and no escalator",
    "clarification_question": null or "Do you want me to find the exit door?"
}

Rules:
    Look at the yellow (x,y) labels on the intersections to determine the target's exact location.
    Provide the "grid_position" as a string, e.g., "1,8", "3,9","6,7", can any coordinate from the grid. If the object is between points, you may use decimals like "6.5,7.2".
    Choose the closest truly empty table.
    Estimate "distance_meters" logically based on perspective.
    find the request target and if visible then use the closest grid coordinate lable to it to give target coordinates or use few of the closest lables that enclose the target and take average this way the accuracy is maintained. 
    also one thing more and its very important that is give the coordinate that is exactly pointing to the target object and not something in front of the object or covering it. if the grid coordinates are poniting to the target but are coverd by something infront then adjust the coordinate's decimal precision to adjust the point to the target object. 

    SELF-CORRECTION RULE: You may see a small orange sphere in the image. This is your PREVIOUS estimation of the target location. 
    - If the orange sphere is already perfectly on the target, maintain its coordinates. 
    - If the orange sphere is offset, floating, or on the wrong object, provide the CORRECTED grid coordinates to move the target to the right spot.
"""

# ==============================================================================

def run_pipeline(image_path=None, output_dir_base="src/pipeline/media"):
    target_image_path = image_path if image_path else IMAGE_PATH
    
    if not os.path.exists(target_image_path):
        print(f"Error: Could not find base image at '{target_image_path}'")
        sys.exit(1)

    print("\n" + "#"*60)
    print("🚀 STARTING VISION PIPELINE")
    print(f"   Input Image : {target_image_path}")
    print("#"*60 + "\n")
    
    total_start = time.time()

    # Determine dynamic folders based on where this is called
    grid_dir = os.path.join(output_dir_base, "input/grid")
    output_dir = os.path.join(output_dir_base, "output/pipeline_sim")

    # --- Step 1: Draw Grid ---
    print(f">>> STEP 1: Appending Coordinate Grid to {os.path.basename(target_image_path)}...")
    t1_start = time.time()
    gridded_image_path = apply_grid(target_image_path, output_dir=grid_dir, grid_size=10)
    t1_end = time.time()
    t1_elapsed = t1_end - t1_start
    print(f"⏱️ Step 1 Time: {t1_elapsed:.2f}s\n")

    # --- Step 2: VLM Inference ---
    print(f">>> STEP 2: Running {MODEL_CHOICE.upper()} Vision Inference...")
    t2_start = time.time()
    json_response = run_vision_model(gridded_image_path, PROMPT, model_choice=MODEL_CHOICE)
    t2_end = time.time()
    t2_elapsed = t2_end - t2_start
    print(f"\n[Raw VLM Output]:\n{json_response}\n")
    print(f"⏱️ Step 2 Time: {t2_elapsed:.2f}s\n")

    # --- Step 3: Parsing and Mark Target ---
    print(">>> STEP 3: Parsing JSON and Marking Target...")
    t3_start = time.time()
    # We use the original clean image to draw the target on, avoiding grid clutter
    final_output_path = process_target(target_image_path, json_response, output_dir=output_dir)
    t3_end = time.time()
    t3_elapsed = t3_end - t3_start
    print(f"⏱️ Step 3 Time: {t3_elapsed:.2f}s\n")

    total_time = time.time() - total_start

    print("#"*60)
    print("🎉 PIPELINE COMPLETE")
    print(f"   Total Execution Time : {total_time:.2f}s")
    print(f"   Image Marking Output : {final_output_path}")
    print("#"*60 + "\n")
    
    return json_response

if __name__ == "__main__":
    run_pipeline()
