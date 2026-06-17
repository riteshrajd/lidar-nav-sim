pTo build this logically, you must separate **Reflexes** (Aura & Steer) from **Cognition** (The LLM). 

---

### 1. The Aura (Ambient Spatial Awareness)
**The Goal:** Give the user a subconscious "sixth sense" of the physical volume of the room.
* **Input:** Real-time depth map from a spatial camera (e.g., OAK-D Pro) or LiDAR.
* **Output:** Continuous, variable-intensity vibrations on a haptic waist belt.
* **Peak Specification:**
    * **Zero-Latency:** It must operate at hardware speeds (<20ms). 
    * **Subconscious Processing:** The user should not have to actively "think" about the vibrations. Just as you don't think about feeling the ground under your feet, the user should naturally feel a "pressure" on their left side as they walk past a wall. 
    * **Strict Thresholds:** It only maps objects within a strict 2.0m to 0.5m radius. Anything further is ignored; anything closer triggers maximum intensity.

### 2. The Pilot (Active Steer Assist)
**The Goal:** Fluid path routing. It doesn't just stop the user from hitting things; it actively bends their trajectory around obstacles so they never have to stop walking.
* **Input:** Depth data + local path-planning algorithm (calculating the "Safest Gap").
* **Output:** Distinct, sharp haptic "nudges" on the left and right shoulder straps.
* **Peak Specification:**
    * **Action-Oriented:** While Aura tells you *what is there*, Pilot tells you *what to do*. 
    * **Dynamic Avoidance:** If a person suddenly steps into the user's path, the system instantly calculates that the right side is clear and fires the left shoulder motor to "push" the user to the right.
    * **Vectoring:** If the user is navigating to a specific destination (e.g., the door), Pilot constantly corrects their heading, nudging them back on track after they bypass an obstacle.

### 3. The Jarvis (Semantic LLM Guide)
**The Goal:** High-value, contextual translation of the environment. The LLM is the "lookout," not the driver. 
* **Input:** Video frames sent to a local Vision-Language Model (VLM) running on the edge compute unit.
* **Output:** Concise, synthesized audio cues via a Bluetooth earbud.
* **Peak Specification:**
    * **Aggressive Filtering:** A bad LLM describes everything (*"I see a chair, a desk, a cup"*). A peak LLM only speaks when the information alters the user's immediate safety or objective (*"Caution: Wet floor sign in your path,"* or *"Stairs descending ahead"*).
    * **Target Acquisition:** The user can ask, "Where is the empty seat?" The VLM scans the room, finds it, and says, "Empty seat at your 2 o'clock, 4 meters away." The Pilot then engages to steer them to it.
    * **Asynchronous:** Because LLMs take time to process (1-2 seconds), Jarvis never handles collision avoidance. It only handles semantic understanding.

---

### The "Stress Test" Demo (The Investor Pitch)
To sell this, you don't use a clean, empty hallway. You use a chaotic, unstructured environment to prove the system handles edge cases flawlessly. 

**The Setup: The Coffee Shop Run**
1.  **The Environment:** A room set up like a busy cafe. Chairs pulled out randomly, a table in the center, a wet floor sign, and a specific target (a cup of coffee on a counter) at the far end.
2.  **The User:** You, blindfolded, wearing the sling bag and haptic rig. No cane.

**The Execution:**
* **Start:** You press a button and say, "Take me to the coffee cup."
* **The Run:** You hold "W" (walk forward at a steady pace).
* **Aura in Action:** The waist belt hums on your right side, letting you know you are tracking parallel to a wall.
* **Jarvis in Action:** The earbud says: *"Navigating to coffee cup. Caution: low hanging sign ahead."* (You duck).
* **Pilot in Action:** A person (assistant) suddenly steps right in front of you. Pilot instantly fires the left shoulder haptic. You veer right, smoothly bypassing them without stopping your momentum, then Pilot nudges your right shoulder to straighten you back out.
* **The Finish:** You arrive at the counter. Jarvis says, *"Target reached. Cup is directly in front of your right hand."* You reach out and grab it.

If you capture that 45-second unbroken sequence on video, the product is validated. 