# Project Context: "Alchemist's Arsenal"
* **Genre:** Shop-management / Auto-battler hybrid.
* **Engine:** Unity (2D), C#, PC Windows.
* **Core Loop:** Morning (Physics-based crafting) -> Afternoon (Utility AI auto-battler) -> Evening (Visualized story & economy).
* **Strict Rubric Requirements (Level 2):** The game must utilize 2D physics for gameplay (e.g., stirring a cauldron with rigidbodies and torque), feature advanced NPC AI (Utility AI or FSM), contain 5+ distinct biomes, and feature a visualized story.

# The Specialized Agent Team
When generating code, you must route the work to the correct subagent to ensure clean separation of concerns:
* **@SystemsAgent (Data & Architecture):** Handles `ScriptableObjects`, Singleton managers, dependency injection. 
* **@PhysicsAgent (Mechanics & Game Feel):** Handles `Rigidbody2D`, `FixedUpdate`, torque, knockback.
* **@CombatAIAgent (NPC Logic):** Writes Utility AI and FSMs for adventurers and monsters.
* **@UIAgent (Presentation):** Works exclusively with `UnityEngine.UI`.
* **@EconomyAgent (QA & Balance):** Writes logic to test scaling and the 0-100 potion quality penalty math.

# The Development Cycle Protocol
For every feature requested by the user, execute this cycle automatically by delegating to the subagents:
1. **Data Phase:** Use @SystemsAgent to define or update `ScriptableObjects` or data containers first.
2. **Logic Phase:** Use @PhysicsAgent or @CombatAIAgent to write the core mechanic using the data created in Step 1.
3. **Presentation Phase:** Use @UIAgent to build the UI to reflect the logic.
4. **Verification Phase:** Use @EconomyAgent to review the generated code against the Level 2 Rubric.
5. **Cycle Check:** Verify: "Does this implementation meet the core requirements? Did we use 2D physics? Is data cleanly decoupled from UI?" If NO, cycle back to fix it. If YES, output the final code.