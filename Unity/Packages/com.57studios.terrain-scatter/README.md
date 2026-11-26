# 57Studios Terrain Prefab Scatter

Package ID: `com.57studios.terrain-scatter`

Copyright © 2026 57Studios. All rights reserved. Proprietary addon. See `LICENSE.md`.

Open **57Studios → Terrain Prefab Scatter** in the Unity Editor. Select a scene Terrain and one of its painted Terrain Layers. Add prefab assets to the list, enter the total count, and press **Scatter**. Each listed prefab has an equal chance of being selected for each placement.

The selected layer must have the highest paint weight at a location. Painted cells are sampled evenly, and each instance is placed on the terrain height. Terrain holes are skipped. Scale/height variation changes each original prefab scale axis by up to the entered percentage. Rotation variation uses up to ±180° at 100% per axis. Zero leaves that axis unchanged.

Slope limits are in degrees. Minimum spacing is a horizontal world-space distance between generated instances in the new batch. Set it to zero to disable spacing. The random seed repeats the same results when terrain, layers, prefabs, and settings are unchanged. If filters prevent the requested count, the window reports the actual count.

Running Scatter again replaces this addon's batch for the chosen Terrain. **Clear This Terrain** removes only that batch. Both actions support Unity Undo. Save the scene to keep the generated prefab instances.
