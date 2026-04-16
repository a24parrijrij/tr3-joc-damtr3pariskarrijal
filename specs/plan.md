# Tank Stars - Implementation Plan

## Phase 1: Terrain Generation

1. Set up terrain game object with mesh and collider components
2. Create height array based on map type
3. Apply noise algorithm to create natural terrain variations
4. Build mesh from height array
5. Update collider to match mesh surface

## Phase 2: Terrain Destruction

1. Detect projectile collision with terrain
2. Calculate crater shape at impact point
3. Modify height array to create depression
4. Rebuild mesh with new terrain shape
5. Update collider to match new surface
6. Make tanks fall to new surface level

## Phase 3: Tank Movement

1. Set up tank game object with physics
2. Implement horizontal movement within bounds
3. Add slope detection to prevent climbing steep areas
4. Snap tank to terrain surface after movement
5. Restrict movement to player's turn only

## Phase 4: Shooting System

1. Create projectile prefab with physics
2. Calculate projectile trajectory from angle and power
3. Detect collision with tanks or terrain
4. Apply damage: direct hit 35 HP, near miss 15 HP
5. Trigger terrain destruction on ground hit

## Phase 5: Game Flow

1. Implement turn-based system (player vs player or player vs AI)
2. Add turn timer (15 seconds)
3. Track HP for both tanks
4. Detect win condition when HP reaches zero
5. Show game over screen with results

## Phase 6: AI Integration

1. Set up ML-Agent component on AI tank
2. Configure observations (tank positions, terrain, HP)
3. Train model with PPO algorithm
4. Add fallback heuristic when no model is loaded

## Phase 7: Multiplayer

1. Create server to handle game state
2. Implement room creation and joining
3. Synchronize terrain between all players
4. Broadcast shots and impacts in real-time
5. Validate all actions server-side