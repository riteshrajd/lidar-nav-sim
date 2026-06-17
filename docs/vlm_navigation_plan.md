# VLM Navigation Game Plan & Vision

This document outlines the strategy for implementing an "Agentic Checkpoint Compass" navigation system, combining high-level Vision-Language Model (VLM) guidance with low-level LiDAR safety.

## Core Philosophy
1. **LiDAR is the Safety Bubble**: Maintaining a 2-5m proximal safety zone. It provides immediate haptic feedback for local obstacle avoidance (walls, people, furniture).
2. **VLM is the Macro Compass**: Providing high-level direction toward "checkpoints" or "milestones" using a "Snapshot-and-Walk" approach.
3. **Agentic Checkpoints**: The system identifies visible milestones (e.g., "base of the escalator," "end of the hallway") even if the final target is not visible.

---

## Phase 1: Snapshot-Based Target Projection (Current Focus)
**Goal**: Accurately project a VLM-identified target from a 2D image into 3D world space, accounting for player movement during inference.

### Steps:
1. **Record Snapshot Pose**: In Unity, when the user presses 'V', capture the `SnapshotPosition` and `SnapshotRotation` alongside the image.
2. **VLM Inference**: Python server processes the image, identifies the target via coordinate grid, and returns:
    - `grid_position` (x,y)
    - `distance_meters` (estimate)
    - `checkpoint_description` (text)
3. **3D Projection**:
    - Convert `grid_position` to a Camera Ray using the recorded `SnapshotRotation`.
    - Place a 3D marker at `SnapshotPosition + (RayDirection * distance_meters)`.
4. **Haptic Feedback**: Trigger the belt motors corresponding to the target's relative direction.

---

## Phase 2: Refinement & Continuous Guidance
**Goal**: Keep the user on track as they move toward the checkpoint.

### Steps:
1. **Continuous Compass**: Maintain the 3D marker's position in world space. As the user walks/rotates, update the belt haptics to always point toward the marker.
2. **Contextual Updates**: When the user gets closer to the checkpoint (detected by distance or another 'V' press), tell the VLM about the *past* target and ask for a refinement or the *next* checkpoint.
3. **State Management**: Keep a "Short-term Memory" of navigation goals so the VLM knows we are in the middle of a multi-step task (e.g., "Finding the elevator").

---

## Phase 3: Advanced Scene Understanding
**Goal**: Resolve complex navigation scenarios (e.g., 90-degree turns, multiple floors).

### Steps:
1. **Agentic Recovery**: If the target is lost or hidden, the VLM identifies a "Search Checkpoint" (e.g., "Walk around this corner to see the exit").
2. **Multi-View Synthesis**: Use all 4 captured views (Front, Back, Left, Right) to build a more complete spatial understanding if the front view is insufficient.
3. **Dynamic Re-planning**: If LiDAR detects a major blockage, trigger a VLM "Eyes-on" request to find an alternate path.

---

## Technical Challenges & Solutions
> [!IMPORTANT]
> **Latency & Drift**: Inference takes 2-5 seconds. The player will move during this time.
> **Solution**: Use the saved **Snapshot Pose** to project the target relative to the *capture* frame, then transform it to the *current* frame.

> [!TIP]
> **Distance Estimation**: VLM distance estimates are approximate ($ \pm 1-2m $).
> **Solution**: Treat checkpoints as "zones" rather than exact points. Once the user is within 2m, move to the next checkpoint or ask for a fresh snapshot.



/*
### My prompt for the above
okey this is done now what. on pressing v. the target. pointing is working. now i guess i can test it in different environment. and then build a physical prototype. cause i guess the modern cloud based vlms are smart enough to handle target spotting intelligently like if there are several doors then  they can intelligently tell the user and also tell which one seems most probable. and mark it. so i guess the hard part is done. now i just gotta polish it a little and test in different scinarios like in malls, metros, stations, roads. sidewalks, etc and then after that build a real world prototype(i guess this will be hard i guess but doable cause there is nothing that is uncertain, like it was when i was doing the intelligence part but it is working now so i guess the road from here is completely diterministic. so i want you to update the docs/vlm_navigation_plan. shorten the already done phases or steps and then add the next steps and phases. keep it concise). 
*/