Step 1: Read GUI guide folder. You can ask me questions if you need to ask till 90 percent confident.

# 📜 Compiler & GUI Planning Blueprints

## 1. Core Directives
* **Ask Questions First**: Always ask clarifying questions until at least 90% confident in the user's intent. 
* **Auto-Save Everything**: Auto-save is always enabled. The system must save automatically on any interaction (movement, resizing, slider drag, typing) and before switching files.
* **Grid Snapping**: Grid snapping is always active on the canvas (using a strict **4-pixel** spacing increment), except for future special layout types like line graphs inside `Draw` components.
* **Count in Grid, Not Pixels**: Coordinates stored in the `.prop` file must be written in **grid units** (pixel coordinate divided by 4), NOT raw pixel counts! Upon loading, coordinates are multiplied by 4 to map back to canvas space.

### ❓ Question 1: Text-Input & Typing Behavior in Lists
* **Answer**: Typing in text fields for integer variables and lists is restricted to numeric characters and minus signs. PropLanguage enforces strict parenthesized types and semicolon termination (`var1(int) = 67;`).

### ❓ Question 2: Editing Lists directly in the Left Sidebar
* **Answer**: Option C. Left sidebar list and variable views are read-only. We do not need sidebar list editing since we edit them live via the sliders and text inputs on the main canvas, or manually inside the `.prop` file.

### ❓ Question 3: Switching Files & Auto-Saving
* **Answer**: The app automatically saves your current canvas and variables state to the old file before switching to a new file. Auto-saving is always active.

### ❓ Question 4: Grid Snapping Toggle
* **Answer**: Grid snapping is always active on the canvas by default, snapping strictly to **4-pixel** grid cell increments. No toggle button or G shortcut is required.
* **Grid Coordinate Translation**: The layout coordinates stored in the `.prop` file are written in **grid cells** (pixels / 4), NOT raw pixels. (The reason you saw multiples of tens in the previous rough draft is because the temporary draft hardcoded a 30-pixel grid and saved raw pixels to `.prop`, which we are correcting to grid units!).

---

## 2. Planning Decided Questions & Answers

### ❓ Question 1: Index Sorting / Component Placement inside Scroll
* **Answer**: Indexing inside a Scroll is determined dynamically using the spatial center coordinates of the `Fill` components. Top-left of the scroll is index `0`, the next right is `1`, and changing rows (vertical coordinates) changes the indices significantly while changing columns (horizontal coordinates) changes them slightly. No direct dragging of edits; sliders/texts are configured on Fills.
* **Infinite Scroll Row Expansion (`Scroll-to-Grow` Loop)**:
  * **Scrolling Beyond Max**: The scroll component's maximum scroll limit is calculated as `totalRows - limit + 1` (allowing scrolling exactly **one extra row** of empty space to be visible at the bottom).
  * **Placement to Expand**: The user can place a new component directly in this empty, scrolled-down space.
  * **Dynamic Growth**: Placing a new child in the empty row increases the list size, which automatically recalculates the max scroll height, allowing the scroll container to grow organically and infinitely as more components are placed! ("expanding the scroll therefore more fills therefore expanding the scroll therefore more fills...")
* **Scroll Auto-Naming & Instant List Creation**:
  * **Scroll Binding Mandatory**: Unlike standard decorator `Fills` (which can just be for visual alignment or backgrounds), a `Scroll` represents dynamic structured data and *must* always be bound to a list.
  * **Instant Auto-Linkage**: When drawing/creating a new `Scroll` on the canvas, the system automatically assigns it a unique default name: `scroll0`, or `scroll1`, `scroll2` if taken.
  * **Automated `.prop` Declaration**: In the same breath, the engine automatically instantiates a new empty list variable with that exact name inside the `.prop` file (e.g. `scroll0(list) = [];`), completely eliminating manual wiring friction!

### ❓ Question 2: Parent-Child Relative Movement
* **Answer**: Parent-child coordinates behave recursively. Moving a parent element (Base or Scroll) on the canvas automatically shifts all of its nested children relative to it, keeping their relative offsets perfectly intact.

### ❓ Question 3: Missing Semicolon Policy
* **Answer**: Semicolons are mandatory at the end of variable declarations. If a variable line does not end with `;`, the parser strictly throws a syntax error in the developer debug console and refuses to load that variable line.

### ❓ Question 4: Left Sidebar Overflow
* **Answer**: The left sidebar shows only list/variable names and values (sigma sidebar). When these items overflow, a vertical scrollbar automatically activates, allowing seamless scrolling via mouse wheel and visual scrollbars.

### ❓ Question 5: Variable Rename Propagation
* **Answer**: Connections are dynamic. Renaming a variable propagates immediately upon saving, updating the binding tags in the layout block of the `.prop` file automatically.

### ❓ Question 6: Unrecognized / Invalid Type Fallback
* **Answer**: Types are strict. Encountering an unrecognized or invalid type in the `.prop` file is treated as a language parse error and halts parsing (even if the grammar is valid).

### ❓ Question 7: Draw Component Scripting (Future Idea)
* **Answer**: The `Draw` component will eventually support a custom mini-scripting interface with programmable draw commands (e.g. line, rect, circle) for high-fidelity custom graphing and visualization. Kept simple for now.

### ❓ Question 8: Comment Preservation Policy
* **Answer**: Editing conflicts between GUI saving and manual text editor modifications must warn the user if an overwrite is about to happen, preventing concurrent changes from stomping each other.

### ❓ Question 9: Syntax Error Reporting
* **Answer**: Syntactical errors in files are logged quietly directly to the developer console/terminal.

### ❓ Question 10: Game Engine Integration
* **Answer**: The `.prop` system is a unified interactive engine handling dynamic layouts, render passes, parameters, and live event wiring across the entire Voxel World game.