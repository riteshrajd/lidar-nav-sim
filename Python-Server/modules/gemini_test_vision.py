import os
from PIL import Image
from dotenv import load_dotenv
from google import genai

# Load environment variables from .env file
load_dotenv()

def test_gemini_vision():
    image_path = "/Users/ritesh/Desktop/vscode/dev/haptic-nav/media/image_input_5.png"
    prompt = "Describe exactly what you see in this image in detail."

    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        print("❌ Error: GEMINI_API_KEY environment variable not found!")
        print("Please create a file named '.env' in your haptic-nav folder and add:")
        print("GEMINI_API_KEY=your_copied_api_key_here")
        return

    print("Initializing Gemini API client...")
    client = genai.Client(api_key=api_key)
    
    print("Loading image...")
    img = Image.open(image_path).convert("RGB")

    print(f"Running inference on {image_path}...")
    import time
    wait_time = 4
    for attempt in range(6):
        try:
            response = client.models.generate_content(
                model='gemini-2.5-flash-lite',
                contents=[img, prompt]
            )

            print("\n" + "="*50)
            print("📝 GEMINI OUTPUT:")
            print(response.text)
            print("="*50)
            break
        except Exception as e:
            print(f"⚠️ Attempt {attempt+1}/6 Failed: {e}")
            if attempt < 5:
                print(f"   Retrying in {wait_time}s to bypass the temporary server spike...")
                time.sleep(wait_time)
                wait_time += 2
            else:
                print("❌ Final API Request permanently failed.")

if __name__ == "__main__":
    test_gemini_vision()
