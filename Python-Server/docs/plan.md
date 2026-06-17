# Haptic-Nav: Master Plan

## Phase 1: VLM Global Target Estimation (Current Focus)
*Goal: Systematically extract absolute 3D world coordinates from pure 2D semantic images without relying on hardware depth sensors.*
- [ ] 1.1 **Static Image Capture:** Pull an RGB sandbox image from disk (bypassing Unity/Hardware complexity).
- [ ] 1.2 **VLM Identification:** Pipe the image and prompt into LLaVA 1.5.
- [ ] 1.3 **Bounding Box Extraction:** Natively parse normalized `x1, y1, x2, y2` coordinates.
- [ ] 1.4 **Monocular Estimation Math:** Construct the mathematical bridge to estimate long-range physical distance using Ground Plane Trigonometry.
- [ ] 1.5 **Global Pin Deployment:** Transform the resulting vector (Angle + Distance) into an absolute $(X, Y)$ coordinate dropped into the unexplored A* tracking grid.
- [ ] 1.6 **Iterative Refinement:** Update the pin continuously as the user closes the distance, preparing for hardware handoff.

---

## Phase 2: Autonomous Obstacle Routing
* **Target:** Use a sliding 10m topological bubble.
* **Math:** Run standard A* algorithm inside the 10m radius to draw vectors toward the VLM global pin, bypassing local 'unseen' dead-ends organically.

## Phase 3: Hardware Integration
* **Target:** Ditch the simulated environments. Connect physical webcams, depth sensors, and the ESP32 Haptic vibration belt.

## Phase 4: Blindfolded Stress Test
* **Target:** Stand at the entrance of a coffee shop, request a seat, and let the system guide you flawlessly!