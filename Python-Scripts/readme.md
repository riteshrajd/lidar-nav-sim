# Haptic-Nav (Simulation V1 & Vision Targeter)

Haptic-Nav is a wearable, AI-powered haptic navigation system for the visually impaired. 
Because the physical hardware (cameras + haptic motors) is in concurrent development, this repository contains the **Simulation Stack** running natively on an Apple Silicon Mac bridging to Unity 3D.

## Architecture: Core Systems

### 1. The Spatial Lidar "Brain"
- **Unity 3D:** Natively runs a continuous 3D room. `Sensors.cs` attached to the Player fires 16 physical Raycasts to map depth distances and Semantic Tags.
- **Python Backend:** `bridge.py` server running on port 5050 catches Unity JSON payloads and calculates "safe gap steering" for the haptic pilot.

### 2. The Semantic Vision Targeter (MVP)
- **Unity Camera Catch:** Pressing `V` in Unity captures 360-degree directional views and pushes the raw camera feed to the vision server on port 8000.
- **Grid Annotation (`step1_grid.py`):** Instantly projects a discrete mathematical (X, Y) coordinate overlay grid onto the physical 2D scene.
- **VLM Semantic Inference (`step2_vision.py`):** Passes the gridded image to a Vision-Language Model with a conversational prompt ("Find an empty table"). The VLM calculates grid intersections and translates them into physical vectors.
- **Precision Parser (`step3_parser.py`):** Validates and extracts the strict JSON output, then physicalizes the target by drawing a UI crosshair over the exact image pixels.

> **Hardware Scaling Note (Cloud vs. Local)**  
> Currently, the vision pipeline utilizes the **Google Gemini 2.5 Flash API** (`modules/gemini_test_vision.py`) as a highly-efficient algorithmic placeholder. This allows us to rapidly prototype the routing logic, AST JSON parsing, and Unity visual feedback loops without waiting on VRAM hardware bottlenecks. The architecture (`main_pipeline.py -> MODEL_CHOICE`) features an instantaneous, native toggle designed to switch to massive, offline edge-hardware VLM implementations (like LLaVA 1.5 7B or MLX Gemma 4) when the physical prototype edge-compute drops natively!

---

## Setup & Running the Simulation

### 1. Requirements
Ensure you are on an Apple Silicon Mac.
1. Create a virtual environment: `source venv/bin/activate`
2. Install dependencies: `pip install -r requirements.txt mlx_vlm google-genai python-dotenv`
3. Setup Environment: Add a `.env` file to the root directory containing your Cloud API key (`GEMINI_API_KEY=your_key_here`).

### 2. Running The Python Servers
You have two concurrent listener servers depending on what you are testing:
- **Lidar Brain (Port 5050):** `python bridge.py` (Calculates collision physics for the haptic belt).
- **Vision Pipeline (Port 8000):** `python src/pipeline/pipeline_server.py` (Saves 4-directional images and natively runs the deep-vision crosshair analyzer).

### 3. Start The Unity World
1. Open your Unity 3D project.
2. Press **Play**.
3. **WASD / Arrows**: Move the Player manually around the physical room.
4. **Target Capture [V]**: Instantly pushes camera feeds to the Python Pipeline server for VLM analysis.

---
*Created for Haptic-Nav: Expanding Accessibility through Edge AI.*