from flask import Flask, request, jsonify
import sys
import threading
import queue
import base64
from io import BytesIO
from PIL import Image, ImageDraw
import re
import os
import logging

app = Flask(__name__)

# Silence the Flask HTTP request spam so we can clearly see the AI logs
log = logging.getLogger('werkzeug')
log.setLevel(logging.ERROR)

# --- VLM GLOBAL ENGINE ---
# We keep these globally cached to prevent loading the entire heavy VLM on every snapshot!
vlm_model = None
vlm_processor = None

# --- JARVIS ASYNC WORKER ---
# A queue with maxsize=1 ensures that if Jarvis is busy speaking, 
# it drops the spammy tag frames until it's ready to alert again.
jarvis_queue = queue.Queue(maxsize=1)

def jarvis_worker():
    print("\n[JARVIS] Loading MLX Model into Unified Memory... (This may take roughly 5-15 seconds)")
    try:
        import os
        from mlx_lm import load, generate
        # Using Llama 3.2 3B 4-bit for blazing fast Apple Silicon performance and low RAM profile
        local_model_path = "../models/Llama-3.2-3B-Instruct-4bit"
        if os.path.exists(local_model_path) and os.listdir(local_model_path):
            print(f"\n[JARVIS] Loading from local browser-downloaded folder: {local_model_path} ...")
            model, tokenizer = load(local_model_path)
        else:
            model, tokenizer = load("mlx-community/Llama-3.2-3B-Instruct-4bit")
        print("\n[JARVIS] MLX SLM Loaded & Ready for Semantic Audio Cues!")
        
        while True:
            context_strings = jarvis_queue.get()
            if context_strings == "QUIT":
                break
                
            prompt = f"You are a very very concise, helpful navigation assistant for a blind user. The camera just detected: {', '.join(context_strings)}. Provide a swift, direct 1-sentence warning."
            
            messages = [{"role": "user", "content": prompt}]
            try:
                prompt_text = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
            except Exception:
                # Fallback manual Llama-3 template if tokenizer_config.json is missing from browser download
                prompt_text = f"<|begin_of_text|><|start_header_id|>user<|end_header_id|>\n\n{prompt}<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n"
            
            # Fast local Apple Silicon generation
            response = generate(model, tokenizer, prompt=prompt_text, max_tokens=40, verbose=False)
            
            # Clean the text so quotes don't break the Mac terminal command
            clean_text = response.strip().replace("'", "").replace('"', "")
            
            print(f"\n🔊 [JARVIS AUDIO CUE]: {clean_text}\n")
            
            # THE MAGIC MAC COMMAND: Speak it out loud!
            os.system(f"say '{clean_text}'")
            
    except Exception as e:
        print(f"\n[JARVIS ERROR] Failed to load or run model: {e}")


# Spin up the background AI thread so Flask handles Unity physics instantaneously
threading.Thread(target=jarvis_worker, daemon=True).start()


def calculate_aura(distances):
    """ The Aura (Ambient Haptics) """
    intensities = []
    danger_max = 3.0
    danger_min = 1.0
    for d in distances:
        if d >= danger_max:
            intensities.append(0)
        elif d <= danger_min:
            intensities.append(10)
        else:
            ratio = (danger_max - d) / (danger_max - danger_min)
            intensities.append(int(ratio * 10))
    return intensities

def calculate_pilot(distances):
    """ The Pilot (Active Steering) """
    if not distances or len(distances) != 16:
        return {"action": "Invalid Data", "turn_value": 0.0}
        
    front_dist = distances[0]
    front_right_dist = distances[1]
    front_left_dist = distances[15]
    
    threshold = 2.5
    
    if front_dist < threshold:
        if front_left_dist > front_right_dist:
            return {"action": "Push Right Shoulder (Steer Left)", "turn_value": -1.0}
        else:
            return {"action": "Push Left Shoulder (Steer Right)", "turn_value": 1.0}
    elif front_left_dist < threshold:
        return {"action": "Push Left Shoulder (Steer Right)", "turn_value": 1.0}
    elif front_right_dist < threshold:
        return {"action": "Push Right Shoulder (Steer Left)", "turn_value": -1.0}
        
    return {"action": "Straight (Path Clear)", "turn_value": 0.0}

@app.route('/sensordata', methods=['POST'])
def receive_sensordata():
    try:
        data = request.get_json()
        if not data:
            return jsonify({'status': 'error', 'message': 'No payload'}), 400
        
        distances = data.get('distances', [])
        tags = data.get('tags', [])
        
        aura_intensities = calculate_aura(distances)
        pilot_data = calculate_pilot(distances)
        steering_command = pilot_data["action"]
        
        # Terminal visual output filter
        if distances:
            # We hid the continuous 10Hz Pilot data terminal spam to prioritize the Visual AI Target feedback!
            # print(f"Front Dist: {distances[0]:.1f}m | Aura: {aura_intensities[0]}/10 | Steering: {steering_command}")
            
            # Map semantic tags to spatial context for Jarvis
            zone_names = [
                "directly in front of you", "slightly to your front-right", "to your front-right", "far to your front-right",
                "to your right", "behind you to the right", "behind you to the right", "behind you to the right",
                "directly behind you", "behind you to the left", "behind you to the left", "behind you to the left",
                "to your left", "far to your front-left", "to your front-left", "slightly to your front-left"
            ]
            
            tag_closest = {}
            for i in range(16):
                t = tags[i]
                d = distances[i]
                if t != "None" and t != "Untagged":
                    if t not in tag_closest or d < tag_closest[t]['dist']:
                        tag_closest[t] = {'dist': d, 'dir': zone_names[i]}
                        
            if tag_closest:
                context_strings = []
                for t, info in tag_closest.items():
                    context_strings.append(f"a {t} {info['dir']} ({info['dist']:.1f}m away)")
                    
                print(f"  --> Identified: {', '.join(context_strings)}")
                
                # If Jarvis background thread is idle, throw it the perfectly formatted spatial tags!
                if jarvis_queue.empty():
                    jarvis_queue.put(context_strings)
        
        response_payload = {
            'status': 'success',
            'haptic_commands': {
                'ambient_belt_intensities': aura_intensities,
                'steering_push': pilot_data
            }
        }
        return jsonify(response_payload), 200
        
    except Exception as e:
        print(f"Error processing sensor data: {e}", file=sys.stderr)
        return jsonify({'status': 'error', 'message': str(e)}), 500

@app.route('/vlm_target', methods=['POST'])
def vlm_target_endpoint():
    print("\n=======================================================")
    print("[VISION] Unity Target Lock Capture Received ('T' Pressed)!")
    print("=======================================================")

    global vlm_model, vlm_processor
    
    # 1. Lazy-load the heavy MLX Vision Model (LLAVA)
    if vlm_model is None:
        import mlx_vlm
        import os
        
        local_llava_path = "../models/llava-1.5-7b-4bit"
        
        if os.path.exists(local_llava_path) and os.listdir(local_llava_path):
            print(f"[VISION] Loading LLaVA weights locally from '{local_llava_path}'...")
            vlm_model, vlm_processor = mlx_vlm.load(local_llava_path)
        else:
            print("[VISION] Lazy Loading LLaVA-1.5 via Apple MLX...")
            vlm_model, vlm_processor = mlx_vlm.load("mlx-community/llava-1.5-7b-4bit")
            
        # THE FIX: Brutally inject the missing variables so it stops crashing
        vlm_processor.patch_size = 14
        vlm_processor.vision_feature_select_strategy = "default"
            
        print("[VISION] LLaVA Loaded Successfully! Processing inference...\n")

    data = request.get_json()
    if not data or 'images_b64' not in data:
        return jsonify({'status': 'error', 'message': 'Missing base64 images payload'}), 400
        
    prompt_text = data.get('prompt', 'Find the object.')
    images_b64 = data.get('images_b64', [])
    
    # 2. Decode the four 90-degree 512x512 viewports from the C# bridge
    imgs = []
    for b64 in images_b64:
        img_data = base64.b64decode(b64)
        imgs.append(Image.open(BytesIO(img_data)).convert('RGB'))
    
    if len(imgs) != 4:
         return jsonify({'status': 'error', 'message': 'Requires exactly 4 Base64 images for surround rig.'})
         
    # 3. Mathematically stitch into a 2x2 Matrix Collage (Preserves exactly 0 lens distortion!)
    w, h = imgs[0].width, imgs[0].height
    collage = Image.new('RGB', (w*2, h*2))
    collage.paste(imgs[0], (0, 0))     # Quad 0: FRONT
    collage.paste(imgs[1], (w, 0))     # Quad 1: RIGHT
    collage.paste(imgs[2], (0, h))     # Quad 2: BACK
    collage.paste(imgs[3], (w, h))     # Quad 3: LEFT
    
    # 4. Prompt Engineering for LLaVA
    llava_prompt = f"Find the {prompt_text} in this image. Output ONLY the bounding box coordinates in the format [ymin, xmin, ymax, xmax]."

    from mlx_vlm import generate

    # THE FIX 1: Lock resolution to standard ViT size to guarantee exactly 576 patches
    safe_collage = collage.resize((336, 336))

    # THE FIX 2: Try native formatting, fallback to space-isolated <image> tags
    try:
        from mlx_vlm.prompt_utils import apply_chat_template
        from mlx_vlm.utils import load_config
        config = load_config("../models/llava-1.5-7b-4bit")
        prompt_formatted = apply_chat_template(vlm_processor, config, llava_prompt, num_images=1)
    except Exception:
        prompt_formatted = f"USER: <image> \n {llava_prompt} \nASSISTANT:"
        
    print(f"[VISION] Analyzing 360 Collage for: '{prompt_text}'...")
    output = generate(vlm_model, vlm_processor, prompt=prompt_formatted, image=[safe_collage], verbose=False)
    print(f"[VISION] Raw AI Response: {output}")

    # 5. Extract Explicit Numbers
    numbers = re.findall(r'\d+', output)
    if len(numbers) >= 4:
        ymin, xmin, ymax, xmax = [int(n) for n in numbers[:4]]
        
        # De-normalize backend 1000-scale returning to explicit True Collage pixel geometry!
        pixel_ymin = int((ymin / 1000.0) * (h*2))
        pixel_xmin = int((xmin / 1000.0) * (w*2))
        pixel_ymax = int((ymax / 1000.0) * (h*2))
        pixel_xmax = int((xmax / 1000.0) * (w*2))
        
        center_y = (pixel_ymin + pixel_ymax) / 2.0
        center_x = (pixel_xmin + pixel_xmax) / 2.0
        
        # 6. Quadrant Router: Slice localized pixels isolating exactly which Unity camera the AI is tracing!
        camera_index = 0
        local_x, local_y = center_x, center_y
        
        if center_x < w and center_y < h:
            camera_index = 0 # Front Camera
        elif center_x >= w and center_y < h:
            camera_index = 1 # Right Camera
            local_x -= w
        elif center_x < w and center_y >= h:
            camera_index = 2 # Back Camera
            local_y -= h
        else:
            camera_index = 3 # Left Camera
            local_x -= w
            local_y -= h
            
        # 7. Render Hardcoded Artifact Bounding Box for Verification! (Saves to Media block exactly as requested)
        draw = ImageDraw.Draw(collage)
        draw.rectangle([pixel_xmin, pixel_ymin, pixel_xmax, pixel_ymax], outline="#00FF00", width=6)
        
        # Draw explicit red crosshair on the center of mass dictating our physical vector trajectory:
        cr = 10
        draw.ellipse([center_x-cr, center_y-cr, center_x+cr, center_y+cr], fill="#FF0000")
        
        os.makedirs("../media", exist_ok=True)
        filename = "../media/target_image_annotated.jpg"
        collage.save(filename)
        
        print(f"[VISION] Identified object in Camera [{camera_index}]. Local Cartesian X:{local_x:.1f} Y:{local_y:.1f}")
        print(f"[VISION] Saved debug visualization bounding box to '{filename}'. Assisting Unity Raycaster Link...\n")
        
        return jsonify({
            "status": "success",
            "camera_index": camera_index,
            "target_x": local_x,
            "target_y": local_y,
            "message": output
        })
    else:
        print("[VISION ERROR] VLM Failed to ground physical target location! Raw Output: " + output)
        return jsonify({
            "status": "error",
            "message": f"Could not map bounding pixels mathematically. Model raw return: {output}"
        })

@app.route('/health', methods=['GET'])
def health_check():
    return jsonify({'status': 'healthy', 'system': 'Haptic-Nav Brain'}), 200

if __name__ == '__main__':
    print("Starting Haptic-Nav Brain (Flask bridge) on port 5050...")
    # use_reloader=False is REQUIRED so the threaded MLX model doesn't load twice into VRAM
    app.run(host='0.0.0.0', port=5050, debug=True, threaded=True, use_reloader=False)
