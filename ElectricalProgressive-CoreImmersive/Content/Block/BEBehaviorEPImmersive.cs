using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using EPImmersive.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace EPImmersive.Content.Block;

public class WireNode
{
    public byte Index { get; set; }           // Индекс точки подключения
    public int Voltage { get; set; }          // Напряжение точки (максимальное)
    public Vec3d Position { get; set; }       // Локальная позиция относительно позиции блока
    public float Radius { get; set; }         // Радиус области подключения (для размеров рамки выделения вокрг точки)
}



public class ConnectionData
{
    public byte LocalNodeIndex { get; set; }      // Индекс точки подключения на ТЕКУЩЕМ устройстве
    public BlockPos NeighborPos { get; set; }     // Позиция СОСЕДНЕГО устройства
    public byte NeighborNodeIndex { get; set; }   // Индекс точки подключения на СОСЕДНЕМ устройстве
    public EParams Parameters;                      // Параметры этого конкретного соединения
}




public class BEBehaviorEPImmersive : BlockEntityBehavior
{
    // Единый список всех соединений
    private List<ConnectionData> _connections = new List<ConnectionData>();

    // Список точек подключения из JSON атрибутов
    private List<WireNode> _wireNodes = new List<WireNode>();

    public BEBehaviorEPImmersive(BlockEntity blockEntity)
        : base(blockEntity)
    {

    }

    public const int MyPacketIdForServer = 1122334457;
    public const int MyPacketIdForClient = 1122334458;

    public const string InterruptionKey = "electricalprogressive:interruption";
    public const string ConnectionKey = "electricalprogressive:connection";
    public const string IsLoadedKey = "electricalprogressive:isloaded";

    private IEImmersiveAccumulator? _accumulator;
    private IEImmersiveConsumer? _consumer;
    private IEImmersiveConductor? _conductor;
    private IEImmersiveProducer? _producer;
    private IEImmersiveTransformator? _transformator;

    public List<Vec3d> ParticlesOffsetPos = new List<Vec3d>(1);
    public List<int[]> ParticlesFramesAnim = new List<int[]>(1);
    public int ParticlesType = 0;
    private BlockEntityAnimationUtil AnimUtil;
    private BlockPos? mainPartPos;

    public global::EPImmersive.ElectricalProgressiveImmersive? System =>
        this.Api?.ModLoader.GetModSystem<global::EPImmersive.ElectricalProgressiveImmersive>();

    private bool _dirty = true;
    private bool _paramsSet = false;
    private (EParams param, byte index) _eparams;
    private bool _isLoaded;
    private EParams _mainEpar;

    /// <summary>
    /// Получает список точек подключения этого блока
    /// </summary>
    public List<WireNode> GetWireNodes()
    {
        return _wireNodes;
    }

    /// <summary>
    /// Получает точку подключения по индексу
    /// </summary>
    public WireNode? GetWireNode(byte index)
    {
        return _wireNodes.FirstOrDefault(node => node.Index == index);
    }

    /// <summary>
    /// Добавляет направление подключений иммерсивных проводов
    /// </summary>
    public void AddImmersiveConnection(byte indexHere, BlockPos neighborPos, byte indexNeighbor)
    {
        var newConnection = new ConnectionData
        {
            LocalNodeIndex = indexHere,
            NeighborPos = neighborPos,
            NeighborNodeIndex = indexNeighbor,
            Parameters = new EParams() // Параметры по умолчанию
        };

        _connections.Add(newConnection);
        this._dirty = true;
        this._paramsSet = false;

        this.Update();
    }

    /// <summary>
    /// Отдает список подключений этого блока
    /// </summary>
    public List<ConnectionData> GetImmersiveConnections()
    {
        return _connections;
    }

    /// <summary>
    /// Добавляет или обновляет параметры для соединения по индексу
    /// </summary>
    public void AddEparamsAt(EParams param, byte index)
    {
        if (_connections.Count > index)
        {
            _connections[index].Parameters = param;
        }
        else
        {
            // Если индекс выходит за границы, создаем новое соединение с параметрами
            var newConnection = new ConnectionData
            {
                LocalNodeIndex = index,
                NeighborPos = Blockentity.Pos, // Временная позиция, должна быть установлена позже
                NeighborNodeIndex = 0,
                Parameters = param
            };
            _connections.Add(newConnection);
        }

        this._dirty = true;
        this._paramsSet = true;
        this._eparams = (param, index);
        this.Update();
    }

    /// <summary>
    /// Добавляет или обновляет основыне параметры для текущего блока
    /// </summary>
    public void AddMainEparams(EParams param)
    {
        _mainEpar= param;

        this._dirty = true;
        this._paramsSet = false;

        this.Update();
    }






    /// <summary>
    /// Находит соединение по параметрам
    /// </summary>
    public ConnectionData? FindConnection(byte localIndex, BlockPos neighborPos, byte neighborIndex)
    {
        return _connections.FirstOrDefault(c =>
            c.LocalNodeIndex == localIndex &&
            c.NeighborPos.Equals(neighborPos) &&
            c.NeighborNodeIndex == neighborIndex);
    }

    /// <summary>
    /// Получает все соединения с указанным соседом
    /// </summary>
    public List<ConnectionData> GetConnectionsToNeighbor(BlockPos neighborPos)
    {
        return _connections.Where(c => c.NeighborPos.Equals(neighborPos)).ToList();
    }

    /// <summary>
    /// Удаляет соединение
    /// </summary>
    public void RemoveConnection(byte localIndex, BlockPos neighborPos, byte neighborIndex)
    {
        var connection = FindConnection(localIndex, neighborPos, neighborIndex);
        if (connection != null)
        {
            _connections.Remove(connection);
            this._dirty = true;
            this.Update();
        }
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        GetParticles();
        LoadWireNodes(); // Загружаем точки подключения из JSON

        AnimUtil = Blockentity.GetBehavior<BEBehaviorAnimatable>()?.animUtil!;

        if (api is ICoreClientAPI capi)
        {
            capi.Event.RegisterAsyncParticleSpawner(OnAsyncParticles);
        }

        InitMultiblock();

        this._isLoaded = true;
        this._dirty = true;
        this.Update();
    }

    /// <summary>
    /// Загружает точки подключения из JSON атрибутов блока
    /// </summary>
    private void LoadWireNodes()
    {
        _wireNodes.Clear();

        var wireNodesArray = this.Block?.Attributes?["wireNodes"]?.AsArray();
        if (wireNodesArray != null)
        {
            foreach (var nodeToken in wireNodesArray)
            {
                try
                {
                    var wireNode = new WireNode
                    {
                        Index = (byte)(nodeToken["index"]?.AsInt() ?? 0),
                        Voltage = nodeToken["voltage"]?.AsInt() ?? 0,
                        Position = new Vec3d(
                            nodeToken["x"]?.AsDouble() ?? 0,
                            nodeToken["y"]?.AsDouble() ?? 0,
                            nodeToken["z"]?.AsDouble() ?? 0
                        ),
                        Radius = nodeToken["dxdydz"]?.AsFloat() ?? 0.1f
                    };

                    _wireNodes.Add(wireNode);
                }
                catch (Exception ex)
                {
                    this.Api?.Logger.Warning($"Failed to load wire node for block {Block?.Code} at {Blockentity.Pos}: {ex.Message}");
                }
            }

            // Сортируем по индексу для удобства
            _wireNodes = _wireNodes.OrderBy(node => node.Index).ToList();
        }
    }

    private void GetParticles()
    {
        ParticlesType = MyMiniLib.GetAttributeInt(this.Block, "particlesType", 0);

        ParticlesOffsetPos.Clear();
        var arrayOffsetPos = MyMiniLib.GetAttributeArrayArrayFloat(this.Block, "particlesOffsetPos", new float[1][] { [0, 0, 0] });
        if (arrayOffsetPos != null)
        {
            for (int i = 0; i < arrayOffsetPos.Length; i++)
            {
                Vec3d buf = new Vec3d(
                    arrayOffsetPos[i][0],
                    arrayOffsetPos[i][1],
                    arrayOffsetPos[i][2]
                );
                ParticlesOffsetPos.Add(buf);
            }
        }

        ParticlesFramesAnim.Clear();
        var arrayFrames = MyMiniLib.GetAttributeArrayArrayInt(this.Block, "particlesFramesAnim", new int[1][] { [-1, -1] });
        if (arrayFrames != null)
        {
            for (int i = 0; i < arrayFrames.Length; i++)
            {
                int[] buf = [arrayFrames[i][0], arrayFrames[i][1]];
                ParticlesFramesAnim.Add(buf);
            }
        }
    }

    private bool OnAsyncParticles(float dt, IAsyncParticleManager manager)
    {
        if (!this._isLoaded || _connections.Count == 0)
            return true;

        // Логика частиц остается прежней
        return this._isLoaded;
    }

    private void InitMultiblock()
    {
        var blockEProperties = MyMiniLib.GetAttributeString(this.Block, "blockEProperties", "main");
        // Инициализация мультиблока
    }

    public void Update(bool force = false)
    {
        if (!this._dirty && !force)
            return;

        var system = this.System;
        if (system is null)
        {
            this._dirty = true;
            return;
        }

        this._dirty = false;

        this._consumer = null;
        this._conductor = null;
        this._producer = null;
        this._accumulator = null;
        this._transformator = null;

        foreach (var entityBehavior in this.Blockentity.Behaviors)
        {
            switch (entityBehavior)
            {
                case IEImmersiveConsumer { } consumer:
                    this._consumer = consumer;
                    break;
                case IEImmersiveProducer { } producer:
                    this._producer = producer;
                    break;
                case IEImmersiveAccumulator { } accumulator:
                    this._accumulator = accumulator;
                    break;
                case IEImmersiveTransformator { } transformator:
                    this._transformator = transformator;
                    break;
                case IEImmersiveConductor { } conductor:
                    this._conductor = conductor;
                    break;
            }
        }

        system.SetConductor(this.Blockentity.Pos, this._conductor);
        system.SetConsumer(this.Blockentity.Pos, this._consumer);
        system.SetProducer(this.Blockentity.Pos, this._producer);
        system.SetAccumulator(this.Blockentity.Pos, this._accumulator);
        system.SetTransformator(this.Blockentity.Pos, this._transformator);

        var currentEpar = this._paramsSet ? _eparams : (new(), 0);
        if (system.Update(this.Blockentity.Pos, _wireNodes, ref _connections, _mainEpar, currentEpar, _isLoaded))
        {
            try
            {
                this.Blockentity.MarkDirty(true);
            }
            catch { }
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        this._isLoaded = false;
        this.System?.Remove(this.Blockentity.Pos);
        AnimUtil?.Dispose();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._isLoaded = false;
        this._dirty = true;
        this.Update();
        AnimUtil?.Dispose();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        // Сохраняем соединения
        tree.SetInt("ConnectionsCount", _connections.Count);
        for (int i = 0; i < _connections.Count; i++)
        {
            var conn = _connections[i];
            tree.SetInt($"Conn_{i}_LocalIndex", conn.LocalNodeIndex);
            tree.SetInt($"Conn_{i}_NeighborX", conn.NeighborPos.X);
            tree.SetInt($"Conn_{i}_NeighborY", conn.NeighborPos.Y);
            tree.SetInt($"Conn_{i}_NeighborZ", conn.NeighborPos.Z);
            tree.SetInt($"Conn_{i}_NeighborIndex", conn.NeighborNodeIndex);

            // Сохраняем параметры соединения
            tree.SetBytes($"Conn_{i}_Params", EParamsSerializer.SerializeSingle(conn.Parameters));
        }

        

        tree.SetBool(IsLoadedKey, this._isLoaded);

        // Сохраняем параметры частиц
        tree.SetInt("ParticlesType", ParticlesType);

        tree.SetInt("ParticlesOffsetPosCount", ParticlesOffsetPos.Count);
        for (int i = 0; i < ParticlesOffsetPos.Count; i++)
        {
            tree.SetDouble($"ParticlesOffsetPosX_{i}", ParticlesOffsetPos[i].X);
            tree.SetDouble($"ParticlesOffsetPosY_{i}", ParticlesOffsetPos[i].Y);
            tree.SetDouble($"ParticlesOffsetPosZ_{i}", ParticlesOffsetPos[i].Z);
        }

        tree.SetInt("ParticlesFramesAnimCount", ParticlesFramesAnim.Count);
        for (int i = 0; i < ParticlesFramesAnim.Count; i++)
        {
            tree.SetInt($"ParticlesFramesAnimMin_{i}", ParticlesFramesAnim[i][0]);
            tree.SetInt($"ParticlesFramesAnimMax_{i}", ParticlesFramesAnim[i][1]);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        // Загружаем соединения
        _connections.Clear();
        int connectionsCount = tree.GetInt("ConnectionsCount", 0);
        for (int i = 0; i < connectionsCount; i++)
        {
            var localIndex = tree.GetInt($"Conn_{i}_LocalIndex", 0);
            var neighborX = tree.GetInt($"Conn_{i}_NeighborX", 0);
            var neighborY = tree.GetInt($"Conn_{i}_NeighborY", 0);
            var neighborZ = tree.GetInt($"Conn_{i}_NeighborZ", 0);
            var neighborIndex = tree.GetInt($"Conn_{i}_NeighborIndex", 0);

            var connection = new ConnectionData
            {
                LocalNodeIndex = (byte)localIndex,
                NeighborPos = new BlockPos(neighborX, neighborY, neighborZ),
                NeighborNodeIndex = (byte)neighborIndex
            };

            // Загружаем параметры соединения
            var paramsData = tree.GetBytes($"Conn_{i}_Params");
            if (paramsData != null)
            {
                connection.Parameters = EParamsSerializer.DeserializeSingle(paramsData);
            }
            else
            {
                connection.Parameters = new EParams();
            }

            _connections.Add(connection);
        }



        this._isLoaded = tree.GetBool(IsLoadedKey, false);

        // Загрузка параметров частиц
        ParticlesType = tree.GetInt("ParticlesType", 0);

        int count = tree.GetInt("ParticlesOffsetPosCount", 0);
        ParticlesOffsetPos = new(count);
        for (int i = 0; i < count; i++)
        {
            double x = tree.GetDouble($"ParticlesOffsetPosX_{i}", 0.0);
            double y = tree.GetDouble($"ParticlesOffsetPosY_{i}", 0.0);
            double z = tree.GetDouble($"ParticlesOffsetPosZ_{i}", 0.0);
            ParticlesOffsetPos.Add(new Vec3d(x, y, z));
        }

        count = tree.GetInt("ParticlesFramesAnimCount", 0);
        ParticlesFramesAnim = new(count);
        for (int i = 0; i < count; i++)
        {
            int min = tree.GetInt($"ParticlesFramesAnimMin_{i}", -1);
            int max = tree.GetInt($"ParticlesFramesAnimMax_{i}", -1);
            ParticlesFramesAnim.Add([min, max]);
        }

        this._dirty = true;
        this.Update();
    }
}