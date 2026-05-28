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
    /// Walls are built from Cube primitives at runtime — no prefabs or scenes needed.
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
            BuildMaze();
        }

        private void BuildMaze()
        {
            var mazeRoot = new GameObject("Maze");
            var rng = new System.Random(_seed);

            // Floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.parent = mazeRoot.transform;
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(
                _cellsX * _cellSize,
                0.1f,
                _cellsZ * _cellSize);

            // Outer perimeter walls
            var halfW = _cellsX * _cellSize * 0.5f;
            var halfD = _cellsZ * _cellSize * 0.5f;
            BuildWall(mazeRoot, new Vector3(0f, _wallHeight * 0.5f, -halfD),
                new Vector3(_cellsX * _cellSize, _wallHeight, _wallThickness));
            BuildWall(mazeRoot, new Vector3(0f, _wallHeight * 0.5f, halfD),
                new Vector3(_cellsX * _cellSize, _wallHeight, _wallThickness));
            BuildWall(mazeRoot, new Vector3(-halfW, _wallHeight * 0.5f, 0f),
                new Vector3(_wallThickness, _wallHeight, _cellsZ * _cellSize));
            BuildWall(mazeRoot, new Vector3(halfW, _wallHeight * 0.5f, 0f),
                new Vector3(_wallThickness, _wallHeight, _cellsZ * _cellSize));

            // Iterative DFS maze generation
            // Each wall is identified by (cellX, cellZ, side): 0=+X, 1=-X, 2=+Z, 3=-Z
            var walls = new HashSet<(int, int, int)>();
            for (var x = 0; x < _cellsX; x++)
            {
                for (var z = 0; z < _cellsZ; z++)
                {
                    if (x < _cellsX - 1) walls.Add((x, z, 0)); // +X wall between (x,z) and (x+1,z)
                    if (x > 0)           walls.Add((x, z, 1)); // -X wall between (x,z) and (x-1,z)
                    if (z < _cellsZ - 1) walls.Add((x, z, 2)); // +Z wall between (x,z) and (x,z+1)
                    if (z > 0)           walls.Add((x, z, 3)); // -Z wall between (x,z) and (x,z-1)
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
                    if (nx >= 0 && nx < _cellsX && nz >= 0 && nz < _cellsZ && !visited.Contains((nx, nz)))
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

                // Remove the wall between cx,cz and nx2,nz2
                // The direction dir tells us which neighbour we moved to:
                // dir=0 +X, dir=1 -X, dir=2 +Z, dir=3 -Z
                // Wall for +X: (cx,cz,0) — remove it
                // Wall for -X: (nx2,nz2,0) — that's the +X wall from nx2 to cx
                // Wall for +Z: (cx,cz,2)
                // Wall for -Z: (nx2,nz2,2)
                switch (dir)
                {
                    case 0: walls.Remove((cx, cz, 0)); break;
                    case 1: walls.Remove((nx2, nz2, 0)); break;
                    case 2: walls.Remove((cx, cz, 2)); break;
                    case 3: walls.Remove((nx2, nz2, 2)); break;
                }
            }

            // Build remaining walls
            foreach (var (wx, wz, side) in walls)
            {
                var cellWorldX = (wx - (_cellsX - 1) * 0.5f) * _cellSize;
                var cellWorldZ = (wz - (_cellsZ - 1) * 0.5f) * _cellSize;
                Vector3 pos;
                Vector3 scale;

                switch (side)
                {
                    case 0: // +X
                        pos = new Vector3(cellWorldX + _cellSize * 0.5f,
                            _wallHeight * 0.5f, cellWorldZ);
                        scale = new Vector3(_wallThickness, _wallHeight, _cellSize);
                        break;
                    case 1: // -X
                        pos = new Vector3(cellWorldX - _cellSize * 0.5f,
                            _wallHeight * 0.5f, cellWorldZ);
                        scale = new Vector3(_wallThickness, _wallHeight, _cellSize);
                        break;
                    case 2: // +Z
                        pos = new Vector3(cellWorldX,
                            _wallHeight * 0.5f, cellWorldZ + _cellSize * 0.5f);
                        scale = new Vector3(_cellSize, _wallHeight, _wallThickness);
                        break;
                    default: // 3 = -Z
                        pos = new Vector3(cellWorldX,
                            _wallHeight * 0.5f, cellWorldZ - _cellSize * 0.5f);
                        scale = new Vector3(_cellSize, _wallHeight, _wallThickness);
                        break;
                }

                BuildWall(mazeRoot, pos, scale);
            }

            // Mark goal cell (cellsX-1, cellsZ-1) with green material
            var goalX = (_cellsX - 1 - (_cellsX - 1) * 0.5f) * _cellSize;
            var goalZ = (_cellsZ - 1 - (_cellsZ - 1) * 0.5f) * _cellSize;
            var goalMarker = GameObject.CreatePrimitive(PrimitiveType.Plane);
            goalMarker.name = "GoalMarker";
            goalMarker.transform.parent = mazeRoot.transform;
            goalMarker.transform.position = new Vector3(goalX, 0.01f, goalZ);
            goalMarker.transform.localScale = new Vector3(
                _cellSize * 0.8f * 0.1f,
                1f,
                _cellSize * 0.8f * 0.1f);
            var goalRenderer = goalMarker.GetComponent<Renderer>();
            if (goalRenderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = Color.green;
                goalRenderer.material = mat;
            }
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
                Destroy(rb);
        }
    }
}
