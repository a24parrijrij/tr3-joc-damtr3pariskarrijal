# SDD Foundations - Terrain Destruction

This document outlines the theoretical basis for the terrain destruction and map creation feature in Tank Stars.

## 1. Feature Definition
The core "Spec-Driven Development" feature is the **Procedural Destructible Terrain**. 
- **Map Creation**: Utilizing Perlin Noise (FBM) to generate unique mountainous shapes.
- **Destruction**: Implementing a circular carving algorithm that modifies the terrain mesh and collider in real-time upon projectile impact.

## 2. Technical Approach
- **Mesh Generation**: Custom vertex/index arrays to build a 2D strip mesh.
- **Collision**: `PolygonCollider2D` that updates its path to match the visual mesh.
- **Physics Interaction**: Standard `OnCollisionEnter2D` callbacks to trigger the destruction at the impact point.

## 3. Map Themes
- **Desert**: High mountains with smooth transitions.
- **Snow**: Sharp peaks and lower base height.
- **Grassland**: Rolling hills (low frequency noise).
