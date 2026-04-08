## technical specs prompt

This most likely development I have to keep 3d 10 m local bubble using lidar and targeted, whose direction and distance from the user is detected intelligent by vision system, and that target is also mapped in the 3-D environment, and that might be out of the 10 m bubble and user use that bubble to surrounding praise the path, floor, and obstacles in low poly to learn the a star algorithm of towards the target. I want the whole processing and battery system on the backpack. And the input system on the head that will be lidar and visual cameras. So that will be light. but the backpack i can have 3kgs. i guess 3 kg backpack is acceptable. that will only have the processing unit and battery. 2.5kg of battery and 0.5kg of proseeing uning. now for this i need some heavy shit that can run the intelligent vision model very fast and simulate and track the 3d globe. so tell me the hardware requirements. also to save memory for the vision model i will delet everything else out of the 10m bubble in the 3d globe that i am taracking and just have the traced path left out of the bubble and delete the traced 3d map, cause that will be of no use. so out of the 10m bubble there will only be the target point in the globe and the previously traced paths. plus i am just thinking of adding a 

i am thinking if it works for 1-2h battery life, use the utmost max level of visual inteligence then that is good cause user can turn it off when sitting idle and use it only when moving and commute. i guess everything is doable but the only thing that will make it exceptional for a blind person is the extremely intelligent vision model that is smart and understands the user, almost like a human looking the world for them. and mark target or checkpoints to reach target intelligently. i hope you understand what im thinking. also just tell me the specs and cost. 

answer in very shrot. also i am also thinking of adding a surround sense uint that haptic or something belt around the waist that tells the direction and proximity(by changing intensity) of any very close obstacle like in 2 meter radious so taht the user can avoid it instinctively by sensing the world around it in real time. this is just an add on and i dont think it will take much processing. i just told yo so you know. now give cost estimations in short of the hardwares and total cost.



**Hardware cost estimates (April 2026):**

- **Max intelligence (AGX Thor 128GB)**: $3,499  
- **Balanced (AGX Orin 64GB)**: $1,999 – $2,367  

- LiDAR (Livox Mid-360): $800 – $1,000  
- Cameras + mount: $150 – $300  
- Battery (for 1-2h high-power): $400 – $800  

**Total system cost:**  
- **Premium (Thor)**: **$5,500 – $6,800**  
- **Balanced (Orin)**: **$3,800 – $5,000**  

Haptic waist belt add-on is simple (cheap vibration motors + Arduino-level controller), adds almost zero extra processing or cost (~$50-100). It runs independently for real-time 2m proximity feedback.  

Good for your blind-assist use case with on/off during movement.



# Haptic-Nav

Haptic-Nav is a wearable, AI-powered haptic navigation system for the visually impaired. 
**Hardware form-factor**: A tech-wear crossbody sling bag carrying the compute unit, cameras, and battery, connected to a haptic waist belt and haptic shoulder straps.

## 3-Layer Software Architecture

1. **The Aura (Ambient Haptics):** A 360° proximity sensor system running a strict 2.0m radius. Translates depth data into continuous waist-belt vibrations.
2. **The Pilot (Active Steering):** Live-vision dynamic obstacle avoidance using local path planning (Safest Gap logic) to calculate if the center path is blocked, triggering lateral haptic "nudges" on the left/right shoulder straps to steer the user.
3. **The Jarvis (Semantic LLM Guide):** A local Small Language Model (SLM) that reads object tags from the vision system and provides high-value audio cues via a Bluetooth earbud (e.g., "Descending stairs ahead," "Chair on your left").

 
Because we do not have the physical hardware yet, we are simulating the entire hardware/software loop on an Apple Silicon Mac M4 using a hybrid stack:

- **The World (Unity 3D):** Runs natively on Apple Silicon. Simulates the physical environment. A "Player" capsule uses C# scripts to fire 360° Raycasts (simulating LiDAR/Cameras) to detect distances and object tags.
- **The Brain (Python/Flask):** A local web server (`bridge.py`) that receives JSON payload frames from Unity at high speed. It computes the haptic motor intensities (Aura + Pilot).
- **The Voice (Apple MLX):** Runs within the Python environment. Uses `mlx-lm` to run a local AI model (Llama-3-8B or Gemma) to generate semantic audio alerts based on the tags sent from Unity.

## The V1 MVP: "The Last 50 Feet"
Smartphones already solve macro-navigation (Google Maps walking directions to the building). Your MVP solves **micro-navigation**—the hardest part for a visually impaired user. The MVP takes over the moment they pull the door handle.

**The V1 Feature Set:**
1. **The Shield (Aura):** A flawless 2.0m haptic collision bubble. They will not bump into tables, walls, or people. 
2. **The Local Router (Pilot):** Instead of navigating city blocks, Pilot navigates the room. It steers them down a hallway or through a crowded cafe without breaking stride.
3. **The Spotter (Jarvis):** Local visual search. The user asks, *"Where is an empty chair?"* or *"Where is the counter?"* Jarvis scans, locks the coordinates, and Pilot steers them to it.

**The Usable V1 Demo:**
The user walks into a completely unfamiliar coffee shop. They ask Jarvis to find the counter. Pilot steers them around two people standing in the way directly to the register. After ordering, they ask Jarvis to find an empty seat. Pilot steers them to the corner table. 

It is completely independent indoor spatial navigation. It is highly practical, relies strictly on the AI/physics you are already building, and requires zero external map APIs.
