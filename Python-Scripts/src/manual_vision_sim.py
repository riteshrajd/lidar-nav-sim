import os
import json
from PIL import Image, ImageDraw, ImageFont

def process_target(image_path, response_data, output_dir="media/output/grok_sim", grid_size=10.0):
    os.makedirs(output_dir, exist_ok=True)
    
    # Handle string vs dictionary
    data = json.loads(response_data) if isinstance(response_data, str) else response_data
    image = Image.open(image_path).convert("RGB")
    draw = ImageDraw.Draw(image)
    w, h = image.size

    try:
        font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 22)
    except:
        font = ImageFont.load_default()

    if data.get("target_spotted", False):
        name = data.get("target_name", "Target")
        distance = data.get("distance_meters", 0)
        grid_pos = data.get("grid_position", "")

        # Parse coordinates (handling decimals)
        try:
            gx, gy = map(float, grid_pos.split(','))
            cx = int((gx / grid_size) * w)
            cy = int((gy / grid_size) * h)
        except Exception as e:
            print(f"⚠️ Coordinate parsing error: {e}")
            cx, cy = w // 2, h // 2

        # --- 1. DRAW CROSSHAIR ---
        # Outer Ring
        draw.ellipse([cx-25, cy-25, cx+25, cy+25], outline="#00ff00", width=3)
        
        # Center red dot
        draw.ellipse([cx-3, cy-3, cx+3, cy+3], fill="red")
        
        # Crosshair lines (leaving a small gap near the center dot for visibility)
        draw.line([cx, cy-25, cx, cy-8], fill="#00ff00", width=3) # Top
        draw.line([cx, cy+8, cx, cy+25], fill="#00ff00", width=3) # Bottom
        draw.line([cx-25, cy, cx-8, cy], fill="#00ff00", width=3) # Left
        draw.line([cx+8, cy, cx+25, cy], fill="#00ff00", width=3) # Right

        # --- 2. DRAW LABEL ---
        label = f"{name} • {distance}m"
        text_bbox = draw.textbbox((0, 0), label, font=font)
        text_w = text_bbox[2] - text_bbox[0]
        
        draw.rectangle([cx - (text_w//2) - 5, cy + 30, cx + (text_w//2) + 5, cy + 60], fill="#00ff00")
        draw.text((cx - (text_w//2), cy + 32), label, fill="black", font=font)

        # --- 3. SAVE IMAGE FIRST ---
        out_name = "target_" + os.path.basename(image_path)
        out_path = os.path.join(output_dir, out_name)
        image.save(out_path)
        
        # --- 4. PRINT TERMINAL OUTPUT ---
        direction = data.get("direction", "ahead")
        next_action = data.get("next_action", "")
        
        print("\n" + "="*50)
        print(f"✅ TARGET MARKED AND SAVED: {out_path}")
        print(f"📍 Grid Coordinate: {grid_pos} --> Image Pixel: ({cx}, {cy})")
        print("-" * 50)
        print("🤖 ASSISTANT SAYS:")
        print(f'   "Found {name}, {direction}, about {distance} meters."')
        if next_action:
            print(f'   "{next_action}"')
        print("="*50 + "\n")

    else:
        print("\n❌ Target not spotted. Assistant says: \"No target found.\"")
        
        # Save unmodified image if nothing is found
        out_name = "target_" + os.path.basename(image_path)
        out_path = os.path.join(output_dir, out_name)
        image.save(out_path)

    return out_path

# ====================== TEST ======================
if __name__ == "__main__":
    IMAGE_PATH = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/image_input_6.png"   # Change if needed

    # Paste the new response dictionary here
    response = {
        "target_spotted": True,
        "target_name": "metro car door",
        "grid_position": "8.5,4.5",
        "direction": "front-right",
        "distance_meters": 5,
        "confidence": 0.75,
        "next_action": "Walk forward and slightly to the right about 5 meters toward the door area near grid point 8.5,4.5.",
        "checkpoint": {"id": 1, "x": 0, "y": 0, "z": 0},
        "alternative_suggestions": None,
        "clarification_question": None
    }   

    process_target(IMAGE_PATH, response)





"""
You are an intelligent seeing-eye assistant for a blind person.

User request: FIND ME THE EXIT DOOR.

The provided image has a coordinate grid overlaid on it. Every grid intersection is marked with a label in the format (x,y).

Analyze the image, find the target, and respond ONLY with a strictly valid JSON document using this exact structure. Do not output markdown code blocks, explanations, or any other text. USE ONLY VALID JSON BOOLEANS (true, false, None), not Python capitalized keywords!

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
"""