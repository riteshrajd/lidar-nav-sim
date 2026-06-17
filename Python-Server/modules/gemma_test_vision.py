import time
from mlx_vlm import load, generate

def test_gemma_vision():
    model_path = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/models/gemma-4-e4b-it-4bit"
    image_path = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/image_input_5.png"
    prompt = "Describe exactly what you see in this image in detail."

    print(f"Loading model from {model_path}...")
    model, processor = load(model_path)
    
    print("Formatting prompt...")
    formatted_prompt = processor.apply_chat_template(
        [{"role": "user", "content": f"<image>\n{prompt}"}],
        add_generation_prompt=True
    )

    print(f"Running inference on {image_path}...")
    start_time = time.time()
    
    # Ensure correct positional arguments for this version of mlx_vlm
    output = generate(
        model, 
        processor, 
        image_path,
        formatted_prompt, 
        verbose=True,
        max_tokens=200
    )
    
    elapsed = time.time() - start_time
    print("\n" + "="*50)
    print("📝 GEMMA OUTPUT:")
    print(output)
    print(f"⏱️ Generation took: {elapsed:.2f}s")
    print("="*50)

if __name__ == "__main__":
    test_gemma_vision()
