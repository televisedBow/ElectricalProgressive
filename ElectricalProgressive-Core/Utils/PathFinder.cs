using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Utils;

public readonly struct FastPosKey : IEquatable<FastPosKey>
{
    public readonly int X, Y, Z, Dim;
    public FastPosKey(int x, int y, int z, int dim)
    {
        X = x; Y = y; Z = z; Dim = dim;
    }

    public bool Equals(FastPosKey other) =>
        X == other.X && Y == other.Y && Z == other.Z && Dim == other.Dim;

    public override bool Equals(object obj) => obj is FastPosKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((391 + this.X) * 23 + this.Y) * 23 + this.Z + this.Dim * 269023;
        }
    }
}

public class PathFinder
{
    private static readonly Facing[] faceMasks =
    {
        Facing.NorthAll, Facing.EastAll, Facing.SouthAll,
        Facing.WestAll, Facing.UpAll, Facing.DownAll
    };

    // Переиспользуемые коллекции и буферы
    private List<FastPosKey> neighborsFast = new(27);
    private List<byte> NeighborsFace = new(27);
    private bool[] NowProcessed = new bool[6];
    private Queue<byte> queue2 = new();
    private bool[] processFacesBuf = new bool[6];
    private List<BlockFacing> bufForDirections = new(6);
    private List<BlockFacing> bufForFaces = new(6);

    private List<byte> startBlockFacing = new();
    private List<byte> endBlockFacing = new();
    private PriorityQueue<(FastPosKey, byte), int> queue = new();
    private Dictionary<(FastPosKey, byte), (FastPosKey, byte)> cameFrom = new();
    private Dictionary<FastPosKey, bool[]> processedFaces = new();
    private Dictionary<(FastPosKey, byte), byte> facingFrom = new();
    private Dictionary<(FastPosKey, byte), bool[]> nowProcessedFaces = new();
    private HashSet<BlockPos> networkPositions = new();
    private List<FastPosKey> buf1 = new();
    private List<byte> buf2 = new();
    private bool[]? buf3;
    private bool[]? buf4;

    // lookupPos для поиска в parts без создания новых объектов
    private  BlockPos lookupPos = new BlockPos(0, 0, 0, 0);

    private bool TryGetPart(Dictionary<BlockPos, NetworkPart> parts, FastPosKey key, out NetworkPart part)
    {
        lookupPos.X = key.X;
        lookupPos.Y = key.Y;
        lookupPos.Z = key.Z;
        lookupPos.dimension = key.Dim;
        return parts.TryGetValue(lookupPos, out part);
    }

    private BlockPos ToBlockPosKey(FastPosKey key)
    {
        lookupPos.X = key.X;
        lookupPos.Y = key.Y;
        lookupPos.Z = key.Z;
        lookupPos.dimension = key.Dim;
        return lookupPos;
    }

    public void Clear() => processedFaces.Clear();

    private static int Heuristic(BlockPos a, BlockPos b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    /// <summary>
    /// Основной поиск пути
    /// </summary>
    public (BlockPos[], byte[], bool[][], Facing[]) FindShortestPath(
        BlockPos start, BlockPos end, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        startBlockFacing.Clear();
        endBlockFacing.Clear();
        queue.Clear();
        cameFrom.Clear();
        facingFrom.Clear();
        nowProcessedFaces.Clear();
        buf1.Clear();
        buf2.Clear();
        buf3 = Array.Empty<bool>();
        buf4 = Array.Empty<bool>();

        networkPositions = network.PartPositions;

        var startKey = new FastPosKey(start.X, start.Y, start.Z, start.dimension);
        var endKey = new FastPosKey(end.X, end.Y, end.Z, end.dimension);

        if (!networkPositions.Contains(start) ||
            !networkPositions.Contains(end) ||
            Heuristic(start, end) >= ElectricalProgressive.maxDistanceForFinding ||
            start.Equals(end))
            return (null!, null!, null!, null!);

        foreach (var face in FacingHelper.Faces(parts[start].Connection))
            startBlockFacing.Add((byte)face.Index);
        foreach (var face in FacingHelper.Faces(parts[end].Connection))
            endBlockFacing.Add((byte)face.Index);

        foreach (var sFace in startBlockFacing)
        {
            queue.Enqueue((startKey, sFace), 0);
            cameFrom[(startKey, sFace)] = (default, 0);
            facingFrom[(startKey, sFace)] = sFace;
            var buffer = new bool[6]; buffer[sFace] = true;
            nowProcessedFaces[(startKey, sFace)] = buffer;
        }

        foreach (var pos in networkPositions)
        {
            if (!processedFaces.TryGetValue(new FastPosKey(pos.X, pos.Y, pos.Z, pos.dimension), out var val))
                processedFaces.Add(new FastPosKey(pos.X, pos.Y, pos.Z, pos.dimension), new bool[6]);
            else Array.Fill(val, false);
        }

        FastPosKey currentKey = default;
        byte currentFace = 0;

        while (queue.Count > 0)
        {
            (currentKey, currentFace) = queue.Dequeue();
            if (currentKey.Equals(endKey))
                break;

            (buf1, buf2, buf3, buf4) = GetNeighbors(currentKey,
                processedFaces[currentKey], facingFrom[(currentKey, currentFace)], network, parts);

            processedFaces[currentKey] = buf4;

            int i = 0;
            foreach (var neighbor in buf1)
            {
                var state = (neighbor, buf2[i]);
                int priority = Math.Abs(neighbor.X - end.X) +
                               Math.Abs(neighbor.Y - end.Y) +
                               Math.Abs(neighbor.Z - end.Z);

                if (priority < ElectricalProgressive.maxDistanceForFinding &&
                    !cameFrom.ContainsKey(state) &&
                    !processedFaces[neighbor][buf2[i]])
                {
                    queue.Enqueue(state, priority);
                    cameFrom[state] = (currentKey, facingFrom[(currentKey, currentFace)]);
                    facingFrom[state] = buf2[i];

                    // тут только копировать
                    var buf3copy = new bool[6];
                    Array.Copy(buf3, buf3copy, 6);
                    nowProcessedFaces.Add(state, buf3copy);
                }
                i++;
            }
        }

        var (fastPath, faces) = ReconstructFastPath(startKey, endKey, endBlockFacing, cameFrom);
        if (fastPath == null)
            return (null!, null!, null!, null!);

        var path = new BlockPos[fastPath.Length];
        for (int i = 0; i < fastPath.Length; i++)
        {
            var p = fastPath[i];
            path[i] = new BlockPos(p.X, p.Y, p.Z, p.Dim);
        }

        int len = path.Length;
        Facing[] nowProcessingFaces = new Facing[len];
        bool[][] nowProcessedFacesList = new bool[len][];
        byte[] facingFromList = new byte[len];

        facingFromList[0] = facingFrom[(fastPath[0], faces[0])];

        for (int i = 1; i < len; i++)
        {
            facingFromList[i] = facingFrom[(fastPath[i], faces[i])];
            var npf = nowProcessedFaces[(fastPath[i], faces[i])];
            nowProcessedFacesList[i - 1] = npf;

            var facing = parts[path[i - 1]].Connection &
                ((npf[0] ? Facing.NorthAll : Facing.None) |
                 (npf[1] ? Facing.EastAll : Facing.None) |
                 (npf[2] ? Facing.SouthAll : Facing.None) |
                 (npf[3] ? Facing.WestAll : Facing.None) |
                 (npf[4] ? Facing.UpAll : Facing.None) |
                 (npf[5] ? Facing.DownAll : Facing.None));

            nowProcessingFaces[i - 1] = facing;
        }

        var lastNpf = new bool[6];
        lastNpf[endBlockFacing[0]] = true;
        nowProcessedFacesList[len - 1] = lastNpf;
        nowProcessingFaces[len - 1] = parts[path[len - 1]].Connection &
            ((lastNpf[0] ? Facing.NorthAll : Facing.None) |
             (lastNpf[1] ? Facing.EastAll : Facing.None) |
             (lastNpf[2] ? Facing.SouthAll : Facing.None) |
             (lastNpf[3] ? Facing.WestAll : Facing.None) |
             (lastNpf[4] ? Facing.UpAll : Facing.None) |
             (lastNpf[5] ? Facing.DownAll : Facing.None));

        return (path, facingFromList, nowProcessedFacesList, nowProcessingFaces);
    }

    private (FastPosKey[]?, byte[]?) ReconstructFastPath(
        FastPosKey start, FastPosKey end, List<byte> endFacing,
        Dictionary<(FastPosKey, byte), (FastPosKey, byte)> cameFrom)
    {
        int length = 0;
        var current = (end, endFacing[0]);

        while (!current.Item1.Equals(default(FastPosKey)))
        {
            length++;
            if (current.Item1.Equals(end))
            {
                bool valid = false;
                foreach (var eFace in endFacing)
                {
                    current = (end, eFace);
                    if (cameFrom.TryGetValue(current, out current)) { valid = true; break; }
                }
                if (!valid) return (null, null);
            }
            else if (!cameFrom.TryGetValue(current, out current))
                return (null, null);
        }

        var pathArray = new FastPosKey[length];
        var faceArray = new byte[length];
        current = (end, endFacing[0]);

        for (int i = length - 1; i >= 0; i--)
        {
            pathArray[i] = current.Item1;
            faceArray[i] = current.Item2;
            if (!cameFrom.TryGetValue(current, out current))
                break;
        }

        return pathArray[0].Equals(start) ? (pathArray, faceArray) : (null, null);
    }

    private (List<FastPosKey>, List<byte>, bool[], bool[]) GetNeighbors(
    FastPosKey pos, bool[] processFaces, byte startFace,
    Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        neighborsFast.Clear();
        NeighborsFace.Clear();
        NowProcessed.Fill(false);
        queue2.Clear();
        processFacesBuf.Fill(false);

        if (!TryGetPart(parts, pos, out var part))
            return (neighborsFast, NeighborsFace, NowProcessed, processFaces);

        var Connections = part.Connection;
        Facing hereConnections = Facing.None;

        for (byte i = 0; i < 6; i++)
            if (part.Networks[i] == network && !processFaces[i])
                hereConnections |= Connections & faceMasks[i];

        queue2.Enqueue(startFace);
        processFaces.CopyTo(processFacesBuf, 0);
        processFacesBuf[startFace] = true;

        while (queue2.Count > 0)
        {
            int faceIndex = queue2.Dequeue();
            var face = FacingHelper.BlockFacingFromIndex(faceIndex);
            var mask = FacingHelper.FromFace(face);
            var connections = hereConnections & mask;

            FacingHelper.FillDirections(connections, bufForDirections);
            foreach (var dir in bufForDirections)
            {
                byte idx = (byte)dir.Index;
                if (!processFacesBuf[idx] &&
                    (hereConnections & FacingHelper.From(dir, face)) != 0)
                {
                    processFacesBuf[idx] = true;
                    queue2.Enqueue(idx);
                }
            }
        }

        Facing validMask = Facing.None;
        for (byte i = 0; i < 6; i++)
            if (processFacesBuf[i])
                validMask |= FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i));
        hereConnections &= validMask;

        int px = part.Position.X, py = part.Position.Y, pz = part.Position.Z, dim = part.Position.dimension;
        FacingHelper.FillDirections(hereConnections, bufForDirections);

        foreach (var dir in bufForDirections)
        {
            var dv = dir.Normali;
            int nx = px + dv.X, ny = py + dv.Y, nz = pz + dv.Z;
            var neighborKey = new FastPosKey(nx, ny, nz, dim);

            if (TryGetPart(parts, neighborKey, out var neighborPart))
            {
                FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), bufForFaces);
                foreach (var face in bufForFaces)
                {
                    var opp = dir.Opposite;
                    if ((neighborPart.Connection & FacingHelper.From(face, opp)) != 0)
                    {
                        neighborsFast.Add(neighborKey);
                        NeighborsFace.Add((byte)face.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(opp, face)) != 0)
                    {
                        neighborsFast.Add(neighborKey);
                        NeighborsFace.Add((byte)opp.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }

            // ищем соседей по ребрам
            FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), bufForFaces);
            foreach (var face in bufForFaces)
            {
                dv = dir.Normali;
                var fv = face.Normali;
                nx = px + dv.X + fv.X;
                ny = py + dv.Y + fv.Y;
                nz = pz + dv.Z + fv.Z;

                neighborKey = new FastPosKey(nx, ny, nz, dim);

                if (TryGetPart(parts, neighborKey, out neighborPart))
                {
                    var oppDir = dir.Opposite;
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(oppDir, oppFace)) != 0)
                    {
                        neighborsFast.Add(neighborKey);
                        NeighborsFace.Add((byte)oppDir.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, oppDir)) != 0)
                    {
                        neighborsFast.Add(neighborKey);
                        NeighborsFace.Add((byte)oppFace.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }

            // ищем соседей по перпендикулярной грани
            FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), bufForFaces);
            foreach (var face in bufForFaces)
            {
                var fv = face.Normali;
                nx = px + fv.X;
                ny = py + fv.Y;
                nz = pz + fv.Z;

                neighborKey = new FastPosKey(nx, ny, nz, dim);

                if (TryGetPart(parts, neighborKey, out neighborPart))
                {
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(dir, oppFace)) != 0)
                    {
                        neighborsFast.Add(neighborKey);
                        NeighborsFace.Add((byte)dir.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, dir)) != 0)
                    {
                        neighborsFast.Add(neighborKey);
                        NeighborsFace.Add((byte)oppFace.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }
        }

        return (neighborsFast, NeighborsFace, NowProcessed, processFaces);
    }
}
