import os
from PIL import Image
from dotenv import load_dotenv

try:
    import mlx_vlm
    from mlx_vlm import generate
    from mlx_vlm.utils import load_config
except ImportError:
    pass

# Available Models Dictionary
MODELS = {
    "llava": "/Users/ritesh/Desktop/vscode/dev/haptic-nav/models/llava-1.5-7b-4bit",
    "gemma": "/Users/ritesh/Desktop/vscode/dev/haptic-nav/models/gemma-4-e4b-it-4bit",
    "gemini": "gemini-2.5-flash-lite"
}

def run_vision_model(image_path, prompt_text, model_choice="gemini"):
    """
    Runs the selected vision model (Local or Cloud API) on the given image and prompt.
    Returns the string representing the generated text.
    """
    model_choice = model_choice.lower()
    
    if model_choice == "gemini":
        print(f"Loading Cloud Interface for {MODELS['gemini']}...")
        load_dotenv()
        api_key = os.getenv("GEMINI_API_KEY")
        if not api_key:
            print("❌ GEMINI_API_KEY not found in .env file!")
            return '{"target_spotted": false, "error": "Missing key"}'
            
        from google import genai
        client = genai.Client(api_key=api_key)
        
        print("Loading image...")
        img = Image.open(image_path).convert("RGB")
        
        import time
        print("Running Cloud Inference (Gemini)...")
        output = '{"target_spotted": false, "error": "API failed due to high demand."}'
        
        wait_time = 4
        for attempt in range(6):
            try:
                response = client.models.generate_content(
                    model=MODELS["gemini"],
                    contents=[img, prompt_text]
                )
                output = response.text
                break
            except Exception as e:
                print(f"   ⚠️ Gemini API Overload (Attempt {attempt+1}/6), waiting {wait_time}s before retry...")
                if attempt < 5:
                    time.sleep(wait_time)
                    wait_time += 2
                else:
                    print(f"❌ Failed after 6 retries. Final Error: {e}")

    else:
        model_path = MODELS.get(model_choice, MODELS["llava"])

        print(f"Loading {model_choice.upper()} model from {model_path}...")
        model, processor = mlx_vlm.load(model_path)
        
        if model_choice == "gemma":
            print("Formatting prompt for Gemma...")
            formatted_prompt = processor.apply_chat_template(
                [{"role": "user", "content": f"<image>\n{prompt_text}"}],
                add_generation_prompt=True
            )
            print("Running inference (this may take a bit)...")
            # Generates output using positional arguments matching mlx_vlm
            output = generate(
                model,
                processor,
                image_path,
                formatted_prompt,
                max_tokens=300,
                verbose=False
            )
        else:
            # LLaVA specific logic
            config = load_config(model_path)
            processor.patch_size = 14
            processor.vision_feature_select_strategy = "full"

            print("Formatting prompt for LLaVA...")
            formatted_prompt = f"USER: <image>\n{prompt_text}\nASSISTANT:"

            print("Loading image...")
            img = Image.open(image_path).convert("RGB")

            print("Running inference (this may take a bit)...")
            output = generate(
                model,
                processor,
                image=[img],
                prompt=formatted_prompt,
                max_tokens=300,
                verbose=False
            )
    
    # Strip markdown escape characters that models sometimes add to JSON keys
    output = output.replace("\\_", "_")
    output = output.replace("```json", "").replace("```", "").replace("```python", "")
    
    return output.strip()
