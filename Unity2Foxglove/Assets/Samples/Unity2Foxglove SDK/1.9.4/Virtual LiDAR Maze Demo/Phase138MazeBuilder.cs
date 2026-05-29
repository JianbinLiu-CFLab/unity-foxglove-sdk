// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo

using System.Collections.Generic;
using UnityEngine;

namespace Unity.FoxgloveSDK.Samples.LidarMaze
{
    /// <summary>
    /// Procedural maze generator using iterative DFS on a 2D grid of cells.
    /// Walls are built from Cube primitives. Generation is exposed as a static
    /// method so it can run at runtime (Start) or be pre-baked from an editor
    /// tool. The maze is centred on the world origin.
    /// </summary>
    public class Phase138MazeBuilder : MonoBehaviour
    {
        [SerializeField] private int _cellsX = 8;
        [SerializeField] private int _cellsZ = 8;
        [SerializeField] private float _cellSize = 2f;
        [SerializeField] private float _wallHeight = 1.5f;
        [SerializeField] private float _wallThickness = 0.2f;
        [SerializeField] private int _seed = 42;

        private void Start()
        {
            Build(_cellsX, _cellsZ, _cellSize, _wallHeight, _wallThickness, _seed);
        }

        /// <summary>
        /// Build a maze centred on the origin and return its root GameObject.
        /// Safe to call from edit mode (uses DestroyImmediate when not playing).
        /// </summary>
        public static GameObject Build(int cellsX, int cellsZ, float cellSize,
            float wallHeight, float wallThickness, int seed)
        {
            var mazeRoot = new GameObject("Maze");
            var rng = new System.Random(seed);

            // Floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.parent = mazeRoot.transform;
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(
                cellsX * cellSize,
                0.1f,
                cellsZ * cellSize);

            // Outer perimeter walls
            var halfW = cellsX * cellSize * 0.5f;
            var halfD = cellsZ * cellSize * 0.5f;
            BuildWall(mazeRoot, new Vector3(0f, wallHeight * 0.5f, -halfD),
                new Vector3(cellsX * cellSize, wallHeight, wallThickness));
            BuildWall(mazeRoot, new Vector3(0f, wallHeight * 0.5f, halfD),
                new Vector3(cellsX * cellSize, wallHeight, wallThickness));
            BuildWall(mazeRoot, new Vector3(-halfW, wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, cellsZ * cellSize));
            BuildWall(mazeRoot, new Vector3(halfW, wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, cellsZ * cellSize));

            // Iterative DFS maze generation
            // Each wall is identified by (cellX, cellZ, side): 0=+X, 1=-X, 2=+Z, 3=-Z
            var walls = new HashSet<(int, int, int)>();
            for (var x = 0; x < cellsX; x++)
            {
                for (var z = 0; z < cellsZ; z++)
                {
                    if (x < cellsX - 1) walls.Add((x, z, 0)); // +X wall between (x,z) and (x+1,z)
                    if (x > 0)          walls.Add((x, z, 1)); // -X wall between (x,z) and (x-1,z)
                    if (z < cellsZ - 1) walls.Add((x, z, 2)); // +Z wall between (x,z) and (x,z+1)
                    if (z > 0)          walls.Add((x, z, 3)); // -Z wall between (x,z) and (x,z-1)
                }
            }

            var visited = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();

            // Start from (0,0)
            visited.Add((0, 0));
            stack.Push((0, 0));

            // Precompute neighbour offsets
            var dx = new int[] { 1, -1, 0, 0 };
            var dz = new int[] { 0, 0, 1, -1 };

            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Peek();

                // Collect unvisited neighbours
                var neighbours = new List<(int, int, int)>();
                for (var d = 0; d < 4; d++)
                {
                    var nx = cx + dx[d];
                    var nz = cz + dz[d];
                    if (nx >= 0 && nx < cellsX && nz >= 0 && nz < cellsZ && !visited.Contains((nx, nz)))
                        neighbours.Add((nx, nz, d));
                }

                if (neighbours.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var (nx2, nz2, dir) = neighbours[rng.Next(neighbours.Count)];
                visited.Add((nx2, nz2));
                stack.Push((nx2, nz2));

                // Remove the wall between cx,cz and nx2,nz2.
                // Each shared wall is registered with two symmetric keys
                // (one per cell) — must remove BOTH or the wall persists.
                switch (dir)
                {
                    case 0: // +X move: shared wall = (cx,cz,+X) and (nx2,nz2,-X)
                        walls.Remove((cx, cz, 0));
                        walls.Remove((nx2, nz2, 1));
                        break;
                    case 1: // -X move: shared wall = (cx,cz,-X) and (nx2,nz2,+X)
                        walls.Remove((cx, cz, 1));
                        walls.Remove((nx2, nz2, 0));
                        break;
                    case 2: // +Z move: shared wall = (cx,cz,+Z) and (nx2,nz2,-Z)
                        walls.Remove((cx, cz, 2));
                        walls.Remove((nx2, nz2, 3));
                        break;
                    case 3: // -Z move: shared wall = (cx,cz,-Z) and (nx2,nz2,+Z)
                        walls.Remove((cx, cz, 3));
                        walls.Remove((nx2, nz2, 2));
                        break;
                }
            }

            // Build remaining walls
            foreach (var (wx, wz, side) in walls)
            {
                var cellWorldX = (wx - (cellsX - 1) * 0.5f) * cellSize;
                var cellWorldZ = (wz - (cellsZ - 1) * 0.5f) * cellSize;
                Vector3 pos;
                Vector3 scale;

                switch (side)
                {
                    case 0: // +X
                        pos = new Vector3(cellWorldX + cellSize * 0.5f,
                            wallHeight * 0.5f, cellWorldZ);
                        scale = new Vector3(wallThickness, wallHeight, cellSize);
                        break;
                    case 1: // -X
                        pos = new Vector3(cellWorldX - cellSize * 0.5f,
                            wallHeight * 0.5f, cellWorldZ);
                        scale = new Vector3(wallThickness, wallHeight, cellSize);
                        break;
                    case 2: // +Z
                        pos = new Vector3(cellWorldX,
                            wallHeight * 0.5f, cellWorldZ + cellSize * 0.5f);
                        scale = new Vector3(cellSize, wallHeight, wallThickness);
                        break;
                    default: // 3 = -Z
                        pos = new Vector3(cellWorldX,
                            wallHeight * 0.5f, cellWorldZ - cellSize * 0.5f);
                        scale = new Vector3(cellSize, wallHeight, wallThickness);
                        break;
                }

                BuildWall(mazeRoot, pos, scale);
            }

            // Mark goal cell (cellsX-1, cellsZ-1) with green material
            var goalX = (cellsX - 1 - (cellsX - 1) * 0.5f) * cellSize;
            var goalZ = (cellsZ - 1 - (cellsZ - 1) * 0.5f) * cellSize;
            var goalMarker = GameObject.CreatePrimitive(PrimitiveType.Plane);
            goalMarker.name = "GoalMarker";
            goalMarker.transform.parent = mazeRoot.transform;
            goalMarker.transform.position = new Vector3(goalX, 0.01f, goalZ);
            goalMarker.transform.localScale = new Vector3(
                cellSize * 0.8f * 0.1f,
                1f,
                cellSize * 0.8f * 0.1f);
            var goalRenderer = goalMarker.GetComponent<Renderer>();
            if (goalRenderer != null)
                SetColor(goalRenderer, Color.green);

            // Front-wall sign on the -Z perimeter (the wall facing the overview camera).
            BuildWallLabel(mazeRoot, "CFLAB", halfD);

            return mazeRoot;
        }

        // 3x5 block-letter bitmaps for the front-wall sign.
        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['C'] = new[] { "###", "#..", "#..", "#..", "###" },
            ['F'] = new[] { "###", "#..", "###", "#..", "#.." },
            ['L'] = new[] { "#..", "#..", "#..", "#..", "###" },
            ['A'] = new[] { ".#.", "#.#", "###", "#.#", "#.#" },
            ['B'] = new[] { "##.", "#.#", "##.", "#.#", "##." },
        };

        /// <summary>
        /// Build a block-letter sign from cubes on the outward face of the -Z
        /// perimeter wall, facing the overview camera. Pipeline-safe (no fonts).
        /// </summary>
        private static void BuildWallLabel(GameObject parent, string text, float halfD)
        {
            const float pixel = 0.16f;     // size of one bitmap cell
            const float letterGap = 0.12f; // gap between letters
            const float centerY = 0.9f;    // vertical centre on the 1.5 m wall
            var letterW = 3 * pixel;
            var totalW = text.Length * letterW + (text.Length - 1) * letterGap;
            var startX = -totalW * 0.5f;
            var z = -halfD - 0.12f;        // just outside the wall, toward the camera

            var label = new GameObject("WallLabel");
            label.transform.SetParent(parent.transform, false);
            var color = new Color(0.98f, 0.85f, 0.1f);

            for (var i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[i]), out var rows))
                    continue;
                var letterOriginX = startX + i * (letterW + letterGap);
                for (var r = 0; r < rows.Length; r++)
                {
                    for (var c = 0; c < rows[r].Length; c++)
                    {
                        if (rows[r][c] != '#')
                            continue;
                        var px = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        px.name = "Px";
                        px.transform.SetParent(label.transform, false);
                        px.transform.position = new Vector3(
                            letterOriginX + c * pixel + pixel * 0.5f,
                            centerY + (2 - r) * pixel,
                            z);
                        px.transform.localScale = new Vector3(pixel * 0.9f, pixel * 0.9f, 0.06f);
                        var rb = px.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            if (Application.isPlaying) Destroy(rb); else DestroyImmediate(rb);
                        }
                        var col = px.GetComponent<Collider>();
                        if (col != null)
                        {
                            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
                        }
                        SetColor(px.GetComponent<Renderer>(), color);
                    }
                }
            }
        }

        private static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            // Clone the default material so the shader matches the active render
            // pipeline; a hard-coded "Standard" shader renders magenta under URP.
            var mat = new Material(renderer.sharedMaterial) { color = color };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            renderer.sharedMaterial = mat;
        }

        /// <summary>World position of a maze cell centre, with the maze centred on the origin.</summary>
        public static Vector3 CellCenter(int cellX, int cellZ, int cellsX, int cellsZ, float cellSize)
        {
            return new Vector3(
                (cellX - (cellsX - 1) * 0.5f) * cellSize,
                0f,
                (cellZ - (cellsZ - 1) * 0.5f) * cellSize);
        }

        private static void BuildWall(GameObject parent, Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.parent = parent.transform;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            var rb = wall.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (Application.isPlaying)
                    Destroy(rb);
                else
                    DestroyImmediate(rb);
            }
        }
    }
}
