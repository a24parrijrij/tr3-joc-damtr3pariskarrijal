# SDD Implementation Plan - Terrain

## Phase 1: Mesh Generation
- Implement `BuildMesh` and `BuildCollider`.
- Test with static seeds.

## Phase 2: Impact Detection
- Set up `ProjectileController` to call `DestroyTerrain`.
- Implement circular mask logic.

## Phase 3: Visual Polish
- Add color themes for different biomes.
- Implement `PlaceOnTerrain` snapping for tanks.
