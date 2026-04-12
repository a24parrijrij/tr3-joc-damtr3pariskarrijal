# SDD Specification - Tank Stars Terrain

## 1. Requirement Specification
- The terrain must be generated from a seed so both players see the same map.
- Projectiles must destroy the terrain in a roughly 1-unit radius.
- Tanks must re-ground themselves after terrain is destroyed beneath them.

## 2. Interface Definition
`public void GenerateTerrain(int seed, string mapType)`
- `seed`: Integer for deterministic random.
- `mapType`: "Desert", "Snow", "Canyon", "Grassland", "Volcanic".

## 3. Calculation Logic
- `worldY = impactY - radius * depth`: The formula to carve the heights array.
- `Mathf.Min(heights[i], carved)` ensures we only remove terrain, never add it.
