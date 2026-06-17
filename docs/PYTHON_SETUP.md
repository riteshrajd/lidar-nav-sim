# Python Backend Setup Guide

Complete setup for the Python VLM pipeline server.

---

## Requirements

- Python 3.8+ (Apple Silicon Mac recommended, but any OS should work)
- A Google Gemini API key (free tier available at [aistudio.google.com](https://aistudio.google.com))

---

## Installation

```bash
cd Python-Server

# Create virtual environment
python3 -m venv venv
source venv/bin/activate   # macOS/Linux
# venv\Scripts\activate    # Windows

# Install dependencies
pip install Pillow google-genai python-dotenv
```

> The `requirements.txt` contains the full frozen dependency list including optional `mlx` / local model deps. For the cloud-only pipeline you only need the three packages above.

---

## API Key Configuration

Create a `.env` file in the `Python-Server/` folder:
```env
GEMINI_API_KEY=your_google_gemini_api_key_here
```

> ⚠️ The `.env` file is gitignored and will never be committed. Never hardcode your API key in source files.

---

## Running the Server

```bash
cd Python-Server
source venv/bin/activate
python src/pipeline/pipeline_server.py
```

Expected output:
```
[HH:MM:SS] Pipeline Server running on port 8000...
Waiting for Unity to send images...
```

The server listens on `http://localhost:8000`. Unity sends POST requests to this address when you press `V`.

---

## What Happens When Unity Sends an Image

1. **`pipeline_server.py`** receives the HTTP POST with a PNG image and a `Direction` header.
2. It saves the raw image to `media/input/raw/`.
3. For the `front` view only, it calls `main_pipeline.py`.
4. **`step1_grid.py`** overlays a 10×10 coordinate grid on the image and saves it to `media/input/grid/`.
5. **`step2_vision.py`** sends the gridded image to the Gemini API with the navigation prompt.
6. **`step3_parser.py`** validates and extracts the structured JSON from the response.
7. The JSON is returned in the HTTP response body back to Unity.

---

## Modifying the Navigation Goal

Open `src/pipeline/main_pipeline.py` and edit the `PROMPT` constant:

```python
PROMPT = """You are an intelligent seeing-eye assistant for a blind person.

User request: take me to the washroom, find the door.
...
```

Change the "User request" line to whatever you want to find.

---

## Swapping to a Local Model

The VLM inference is isolated in `step2_vision.py`. To use a local model:

1. Replace the Gemini API call with your local inference call (LLaVA, MLX Gemma, etc.).
2. The input is a PIL image object. The output must be a JSON string with this schema:
```json
{
  "target_spotted": true,
  "target_name": "washroom door",
  "grid_position": "6.2, 4.8",
  "distance_meters": 7.5,
  "direction": "slightly left",
  "next_action": "Walk forward",
  "what_i_see": "A hallway with multiple doors"
}
```

---

## File Structure (Python-Server)

```
Python-Server/
├── requirements.txt           ← Full frozen dependency list
├── readme.md                  ← Older readme (superseded by docs/)
├── src/
│   ├── pipeline/
│   │   ├── pipeline_server.py ← HTTP server entry point
│   │   ├── main_pipeline.py   ← Orchestrator
│   │   ├── step1_grid.py      ← Grid overlay (PIL)
│   │   ├── step2_vision.py    ← Gemini API call
│   │   └── step3_parser.py    ← JSON parser + validator
│   ├── add_grid.py            ← Standalone: overlay grid on any image
│   └── manual_vision_sim.py   ← Standalone: test the VLM on a local image
├── media/
│   ├── input/raw/             ← Received images from Unity (gitignored)
│   ├── input/grid/            ← Gridded images (gitignored)
│   └── output/                ← Annotated output images (gitignored)
└── modules/                   ← Experimental/extra modules
```
