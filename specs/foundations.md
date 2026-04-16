# Tank Stars - Foundations

## Project Overview

Tank Stars is a multiplayer tank battle game built with Unity. The core features include procedural terrain generation, destructible environment, and turn-based combat between players or vs AI.

## Technology Stack

- **Engine**: Unity 6 with Universal Render Pipeline (URP)
- **UI System**: UI Toolkit (UIDocument, UXML, USS)
- **Physics**: Unity 2D Physics with Rigidbody2D and Colliders
- **Networking**: REST API with WebSocket for real-time updates
- **ML**: Unity ML-Agents for AI tank behavior

## Key Systems

### Terrain System
- Procedural mesh generation with custom shape per biome
- Destructible terrain - craters form on projectile impact
- Tanks snap to terrain surface after terrain changes
- Different visual styles for each map type (desert, snow, grassland, canyon, volcanic)

### Tank System
- Turn-based movement and shooting
- Health system with damage calculation
- Projectile physics with arc trajectory
- Barrel angle and power controls

### AI System
- ML-trained agent for Vs AI mode
- Neural network makes shooting decisions
- Trained with PPO algorithm over millions of steps

### Multiplayer System
- Room-based matchmaking
- Server-authoritative terrain state
- Real-time shot synchronization
- Turn management between players

## Architecture

```
Tank Stars/
├── client/UnityTankStar/     # Unity game client
├── server/                   # Node.js backend
├── ml-agents/                # ML training
└── docker-compose.yml        # Container setup
```

## Design Principles

1. **Single responsibility**: Each system handles one specific feature
2. **Server authority**: Server validates all game state in multiplayer
3. **Deterministic physics**: Same inputs always produce same outputs
4. **Graceful degradation**: AI works without trained model using heuristics