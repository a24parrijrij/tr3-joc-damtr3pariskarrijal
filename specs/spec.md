# Tank Stars - Technical Specification

---

## 1. Terrain Generation

### 1.1 GenerateTerrain
Creates procedural terrain based on map type (desert, snow, grassland, canyon, volcanic).

**Behavior:**
- Single-player mode: Generate terrain using noise algorithms with unique parameters per biome
- Each biome has its own visual character (dunes for desert, peaks for volcanic, etc.)
- Each game uses a random seed so terrain is different each time
- Multiplayer mode: Load terrain heights from server to ensure all players see the same map
- Terrain always stays within defined height bounds
- Tanks stick to the surface after terrain changes

### 1.2 DestroyTerrain
Creates craters when projectiles hit the ground.

**Behavior:**
- Smooth, rounded crater shape (not sharp V-shape)
- Updates mesh and collision after each impact
- Tanks fall to new surface level after destruction

### 1.3 GetHeightAtX
Returns the Y position of terrain at a given X coordinate.

---

## 2. Tank Controller

### 2.1 Tank Properties
- Health points (HP)
- Movement speed
- Barrel angle (aiming)
- Player identification

### 2.2 Movement
- Move left or right within bounds
- Cannot move during opponent's turn
- Cannot move on steep slopes

### 2.3 Aiming
- Barrel angle adjustable 0-90 degrees
- Power level adjustable 10-100

### 2.4 Shooting
- Fires projectile with given angle and power
- Projectile follows physics trajectory
- Damage on hit: 35 HP (direct), 15 HP (near miss)

---

## 3. Projectile

### 3.1 Launch
- Launches at specified angle and power
- Speed proportional to power value

### 3.2 Collision
- Detects collision with terrain (ground hit)
- Detects collision with tanks (direct hit)
- Calls damage callback on impact

---

## 4. Game States

### 4.1 Turn System
- Player turn: Aim and fire
- AI turn: AI makes decision and fires
- Turn timer: 15 seconds per turn

### 4.2 Win Condition
- First tank to reduce opponent's HP to 0 wins

### 4.3 Game Over
- Show winner
- Display final HP for both tanks
- Show match duration
- Button to return to menu