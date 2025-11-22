using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace EPImmersive.Utils
{
    public struct FastPosKey : IEquatable<FastPosKey>
    {
        public int X, Y, Z, Dim;
        public BlockPos Pos;

        public FastPosKey(int x, int y, int z, int dim, BlockPos pos = null)
        {
            X = x;
            Y = y;
            Z = z;
            Dim = dim;
            Pos = pos;
        }

        public bool Equals(FastPosKey other) =>
            X == other.X && Y == other.Y && Z == other.Z && Dim == other.Dim;

        public override bool Equals(object obj) => obj is FastPosKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X;
                hash = (hash << 9) ^ (hash >> 23) ^ Y;
                hash = (hash << 9) ^ (hash >> 23) ^ Z;
                return hash ^ (Dim * 269023);
            }
        }
    }

    public struct NodeState : IEquatable<NodeState>
    {
        public FastPosKey Position;
        public byte NodeIndex;

        public NodeState(FastPosKey position, byte nodeIndex)
        {
            Position = position;
            NodeIndex = nodeIndex;
        }

        public bool Equals(NodeState other) =>
            Position.Equals(other.Position) && NodeIndex == other.NodeIndex;

        public override bool Equals(object obj) => obj is NodeState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Position.GetHashCode() * 397) ^ NodeIndex.GetHashCode();
            }
        }
    }

    public class ImmersivePathFinder
    {
        private PriorityQueue<NodeState, int> _queue = new();
        private Dictionary<NodeState, NodeState> _cameFrom = new();
        private Dictionary<NodeState, int> _costSoFar = new();
        private HashSet<NodeState> _visited = new();

        private List<NodeState> _neighborsBuffer = new(10);

        public void Clear()
        {
            _queue.Clear();
            _cameFrom.Clear();
            _costSoFar.Clear();
            _visited.Clear();
        }

        public static int Heuristic(BlockPos a, BlockPos b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

        public (BlockPos[], byte[]) FindShortestPath(
            BlockPos start, BlockPos end,
            ImmersiveNetwork immersiveNetwork,
            Dictionary<BlockPos, ImmersiveNetworkPart> parts)
        {
            Clear();

            var startKey = new FastPosKey(start.X, start.Y, start.Z, start.dimension, start);
            var endKey = new FastPosKey(end.X, end.Y, end.Z, end.dimension, end);

            if (!parts.TryGetValue(start, out var startPart) ||
                !parts.TryGetValue(end, out var endPart))
                return (null, null);

            // Добавляем все стартовые узлы в очередь
            foreach (var wireNode in startPart.WireNodes)
            {
                var startState = new NodeState(startKey, wireNode.Index);
                _queue.Enqueue(startState, 0);
                _cameFrom[startState] = new NodeState();
                _costSoFar[startState] = 0;
            }

            while (_queue.Count > 0)
            {
                var current = _queue.Dequeue();

                if (current.Position.Equals(endKey))
                {
                    return ReconstructPath(current);
                }

                if (_visited.Contains(current))
                    continue;

                _visited.Add(current);

                GetNeighbors(current, immersiveNetwork, parts, _neighborsBuffer);

                foreach (var neighbor in _neighborsBuffer)
                {
                    if (_visited.Contains(neighbor))
                        continue;

                    var newCost = _costSoFar[current] + 1; // Каждый шаг стоит 1

                    if (!_costSoFar.ContainsKey(neighbor) || newCost < _costSoFar[neighbor])
                    {
                        _costSoFar[neighbor] = newCost;
                        var priority = newCost + Heuristic(neighbor.Position.Pos, end);
                        _queue.Enqueue(neighbor, priority);
                        _cameFrom[neighbor] = current;
                    }
                }

                _neighborsBuffer.Clear();
            }

            return (null, null);
        }

        private void GetNeighbors(
            NodeState current,
            ImmersiveNetwork network,
            Dictionary<BlockPos, ImmersiveNetworkPart> parts,
            List<NodeState> neighbors)
        {
            if (!parts.TryGetValue(current.Position.Pos, out var currentPart))
                return;

            // Ищем соединения из текущего узла
            foreach (var connection in currentPart.Connections)
            {
                if (connection.LocalNodeIndex == current.NodeIndex)
                {
                    var neighborKey = new FastPosKey(
                        connection.NeighborPos.X, connection.NeighborPos.Y,
                        connection.NeighborPos.Z, connection.NeighborPos.dimension,
                        connection.NeighborPos);

                    var neighborState = new NodeState(neighborKey, connection.NeighborNodeIndex);

                    // Проверяем, что соседняя позиция находится в сети
                    if (network.PartPositions.Contains(connection.NeighborPos))
                    {
                        neighbors.Add(neighborState);
                    }
                }
            }

            // Также проверяем входящие соединения (когда текущий узел является NeighborNodeIndex)
            foreach (var networkConnection in network.ImmersiveConnections)
            {
                if (networkConnection.NeighborPos.Equals(current.Position.Pos) &&
                    networkConnection.NeighborNodeIndex == current.NodeIndex)
                {
                    var neighborKey = new FastPosKey(
                        networkConnection.LocalPos.X, networkConnection.LocalPos.Y,
                        networkConnection.LocalPos.Z, networkConnection.LocalPos.dimension,
                        networkConnection.LocalPos);

                    var neighborState = new NodeState(neighborKey, networkConnection.LocalNodeIndex);

                    if (network.PartPositions.Contains(networkConnection.LocalPos))
                    {
                        neighbors.Add(neighborState);
                    }
                }
            }
        }

        private (BlockPos[], byte[]) ReconstructPath(NodeState endState)
        {
            var path = new List<BlockPos>();
            var nodeIndices = new List<byte>();

            var current = endState;

            while (!current.Equals(new NodeState()))
            {
                path.Add(current.Position.Pos);
                nodeIndices.Add(current.NodeIndex);

                if (!_cameFrom.TryGetValue(current, out current))
                    break;
            }

            path.Reverse();
            nodeIndices.Reverse();

            return (path.ToArray(), nodeIndices.ToArray());
        }
    }
}