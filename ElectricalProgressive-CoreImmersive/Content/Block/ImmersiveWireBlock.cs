using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using EPImmersive.Content.Block.EAccumulator;
using EPImmersive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EPImmersive.Content.Block
{
    public class ImmersiveWireBlock : Vintagestory.API.Common.Block
    {
        protected List<WireNode> _wireNodes; // точки крепления

        private WireConnectionData _currentConnectionData; // временные данные для текущих подключений

        




        /// <summary>
        /// Выделение
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var boxes = new List<Cuboidf>();

            if (api.Side == EnumAppSide.Client)
            {
                var capi = (ICoreClientAPI)api;

                // Показываем точки подключения когда игрок держит провод
                if (IsHoldingWireTool(capi.World.Player) || IsHoldingWrench(capi.World.Player))
                {
                    boxes.AddRange(GetNodeSelectionBoxes(blockAccessor, pos));
                    return boxes.ToArray();
                }
            }

            // Добавляем провода к выделению
            boxes.AddRange(base.GetSelectionBoxes(blockAccessor, pos));
            // boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));

            return boxes.ToArray();
        }



        /// <summary>
        /// Коллизии
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var boxes = new List<Cuboidf>();
            boxes.AddRange(base.GetCollisionBoxes(blockAccessor, pos));
            //boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));
            return boxes.ToArray();
        }




        /// <summary>
        /// Выделение нода при наведении
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public virtual Cuboidf[] GetNodeSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var boxes = new List<Cuboidf>();
            if (_wireNodes==null || _wireNodes.Count==0)
                return boxes.ToArray();

            for (int i = 0; i < _wireNodes.Count; i++)
            {
                var box = new Cuboidf(
                    (float)(_wireNodes[i].Position.X - _wireNodes[i].Radius),
                    (float)(_wireNodes[i].Position.Y - _wireNodes[i].Radius),
                    (float)(_wireNodes[i].Position.Z - _wireNodes[i].Radius),
                    (float)(_wireNodes[i].Position.X + _wireNodes[i].Radius),
                    (float)(_wireNodes[i].Position.Y + _wireNodes[i].Radius),
                    (float)(_wireNodes[i].Position.Z + _wireNodes[i].Radius)
                );
                boxes.Add(box);
            }


            return boxes.ToArray();
        }

        /// <summary>
        /// Считает простые коллизии проводам
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public virtual Cuboidf[] GetWireCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var boxes = new List<Cuboidf>();

            var behavior = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEPImmersive>();
            if (behavior == null) return boxes.ToArray();

            var connections = behavior.GetImmersiveConnections();
            if (connections.Count == 0)
                return boxes.ToArray();

            foreach (ConnectionData connection in connections)
            {
                var nodeHere = behavior.GetWireNode(connection.LocalNodeIndex);
                if (nodeHere == null) continue;

                var neighborEntity = blockAccessor.GetBlockEntity(connection.NeighborPos);
                if (neighborEntity == null) continue;

                var neighborBehavior = neighborEntity.GetBehavior<BEBehaviorEPImmersive>();
                if (neighborBehavior == null) continue;

                var nodeNeighbor = neighborBehavior.GetWireNode(connection.NeighborNodeIndex);
                if (nodeNeighbor == null) continue;

                // Создаем упрощенный коллайдер для провода
                var start = new Vec3d(
                    nodeHere.Position.X,
                    nodeHere.Position.Y,
                    nodeHere.Position.Z
                );

                var end = new Vec3d(
                    connection.NeighborPos.X - pos.X + nodeNeighbor.Position.X,
                    connection.NeighborPos.Y - pos.Y + nodeNeighbor.Position.Y,
                    connection.NeighborPos.Z - pos.Z + nodeNeighbor.Position.Z
                );

                // Простой кубоид вдоль провода
                var wireBox = CreateWireCollisionBox(start, end, 0.2f);
                boxes.Add(wireBox);
            }

            return boxes.ToArray();
        }


        /// <summary>
        /// Рисует собственно кубики коллизии большие
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="thickness"></param>
        /// <returns></returns>
        private Cuboidf CreateWireCollisionBox(Vec3d start, Vec3d end, float thickness)
        {
            var mid = new Vec3d(
                (start.X + end.X) / 2,
                (start.Y + end.Y) / 2,
                (start.Z + end.Z) / 2
            );

            float length = (float)start.DistanceTo(end);
            float height = thickness;
            float width = thickness;

            return new Cuboidf(
                (float)(mid.X - width / 2),
                (float)(mid.Y - height / 2),
                (float)(mid.Z - length / 2),
                (float)(mid.X + width / 2),
                (float)(mid.Y + height / 2),
                (float)(mid.Z + length / 2)
            );
        }



        /// <summary>
        /// Обновление точек крепления
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="nodes"></param>
        public void UpdateWireNodes(List<WireNode> nodes)
        {
            // обновляем точки крепления
            _wireNodes = nodes;
        }







        /// <summary>
        /// Начато взаимодействие с нодом
        /// </summary>
        /// <param name="world"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSel"></param>
        /// <returns></returns>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return true;
        }



        /// <summary>
        /// Окончание взаимодействия с блоком
        /// </summary>
        /// <param name="secondsUsed"></param>
        /// <param name="world"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSel"></param>
        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);

            ICoreClientAPI capi = null;

            if (api is ICoreClientAPI)
                capi = (ICoreClientAPI)api;

            // Если уже в процессе подключения - обрабатываем как вторую точку
            if (_currentConnectionData != null)
            {
                HandleSecondPointSelection(capi, byPlayer, blockSel);
                return;
            }



            // Если игрок держит кабель для подключения проводов
            if (IsHoldingWireTool(byPlayer))
            {
                var behavior = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();
                if (behavior != null && blockSel.SelectionBoxIndex < _wireNodes.Count)
                {
                    // Начинаем процесс подключения провода
                    HandleWireConnection(capi, byPlayer, blockSel, behavior);
                    return;
                }
            }

            // Если игрок держит гаечный ключ для отключения
            if (IsHoldingWrench(byPlayer))
            {
                BEBehaviorEPImmersive behavior = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();
                if (behavior != null)
                {
                    HandleWireDisconnection(byPlayer, blockSel, behavior);
                    return;
                }
            }
        }


        /// <summary>
        /// Игрок держит кабель?
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private bool IsHoldingWireTool(IPlayer player)
        {
            ItemSlot activeSlot = player.InventoryManager.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Block is BlockECable;
        }


        /// <summary>
        /// Игрок держит ключ?
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private bool IsHoldingWrench(IPlayer player)
        {
            ItemSlot activeSlot = player.InventoryManager.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Item?.Tool == EnumTool.Wrench;
        }



        /// <summary>
        /// Обработка первой точки для прокладки провода
        /// </summary>
        /// <param name="capi"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSel"></param>
        /// <param name="behavior"></param>
        private void HandleWireConnection(ICoreClientAPI capi, IPlayer byPlayer, BlockSelection blockSel, BEBehaviorEPImmersive behavior)
        {
            byte nodeIndex = (byte)blockSel.SelectionBoxIndex;


            // не позволяем к первой точке подключить более 8 проводов
            if (behavior.FindConnection(nodeIndex).Count >= 8)
            {
                if (capi != null)
                {
                    capi.ShowChatMessage("You cannot connect more than 8 wires to this point.");
                }

                return;
            }


            // Сохраняем информацию о первой точке подключения
            _currentConnectionData = new WireConnectionData
            {
                StartPos = blockSel.Position,
                StartNodeIndex = nodeIndex,
                StartBehavior = behavior,
                CableStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Clone()
            };


            if (capi != null)
            {
                // выберите вторую точку
                capi.ShowChatMessage("Select second connection point. Right-click to cancel.");

                // Устанавливаем таймаут для отмены операции
                capi.Event.RegisterCallback((dt) =>
                {
                    if (_currentConnectionData != null)
                    {
                        capi.ShowChatMessage("Wire connection cancelled.");
                        _currentConnectionData = null;
                    }
                }, 30000); // 30 секунд таймаут выбора второй точки
            }
        }



        /// <summary>
        /// Обработка второй точки для подключения
        /// </summary>
        /// <param name="capi"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSel"></param>
        private void HandleSecondPointSelection(ICoreClientAPI capi, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (_currentConnectionData == null)
                return;

            // Проверяем что в руках все еще тот же кабель
            if (!IsHoldingWireTool(byPlayer) || !byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Equals(api.World, _currentConnectionData.CableStack, GlobalConstants.IgnoredStackAttributes))
            {
                if (capi != null)
                    capi.ShowChatMessage("You must hold the same cable to complete connection");
                _currentConnectionData = null;
                return;
            }

            // Проверяем что вторая точка на другом блоке
            if (blockSel.Position.Equals(_currentConnectionData.StartPos))
            {
                if (capi != null)
                    capi.ShowChatMessage("Cannot connect wire to the same block");
                _currentConnectionData = null;
                return;
            }

            var endBehavior = api.World.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();

            // что-то не так со вторым подключением
            if (endBehavior == null || blockSel.SelectionBoxIndex >= endBehavior.GetWireNodes().Count)
            {
                if (capi != null)
                    capi.ShowChatMessage("Invalid connection point");
                _currentConnectionData = null;
                return;
            }

            // Такое подключение уже существует?
            if (_currentConnectionData.StartBehavior.FindConnection(_currentConnectionData.StartNodeIndex,
                    blockSel.Position, (byte)blockSel.SelectionBoxIndex) != null)
            {
                if (capi != null)
                    capi.ShowChatMessage("Such a connection already exists.");
                _currentConnectionData = null;
                return;
            }


            // У второго нода подключений слишком много
            if (endBehavior.FindConnection((byte)blockSel.SelectionBoxIndex).Count >= 8)
            {
                if (capi != null)
                    capi.ShowChatMessage("You cannot connect more than 8 wires to this point.");
                _currentConnectionData = null;
                return;
            }



            // Рассчитываем длину провода
            WireNode startNode = _currentConnectionData.StartBehavior.GetWireNode(_currentConnectionData.StartNodeIndex);
            WireNode endNode = endBehavior.GetWireNode((byte)blockSel.SelectionBoxIndex);

            var startWorldPos = new Vec3d(
                _currentConnectionData.StartPos.X + startNode.Position.X,
                _currentConnectionData.StartPos.Y + startNode.Position.Y,
                _currentConnectionData.StartPos.Z + startNode.Position.Z
            );

            var endWorldPos = new Vec3d(
                blockSel.Position.X + endNode.Position.X,
                blockSel.Position.Y + endNode.Position.Y,
                blockSel.Position.Z + endNode.Position.Z
            );

            double distance = startWorldPos.DistanceTo(endWorldPos);

            // округляем длину в большую сторону до целого
            int cableLength = (int)Math.Ceiling(distance);


            // ограничиваем максимальную длину провода
            if (cableLength > 32)
            {
                if (capi != null)
                    capi.ShowChatMessage("The maximum wire length cannot be more than 32 blocks.");
                _currentConnectionData = null;
                return;
            }

            // Проверяем достаточно ли кабеля у игрока
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative && activeSlot.StackSize < cableLength)
            {
                if (capi != null)
                    capi.ShowChatMessage($"Not enough cable. Need {cableLength} blocks, but only have {activeSlot.StackSize}");
                _currentConnectionData = null;
                return;
            }

            // Забираем кабель у игрока
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                activeSlot.TakeOut(cableLength);
                activeSlot.MarkDirty();
            }

            // Создаем электрические параметры кабеля
            EParams cableParams = CreateCableParams(_currentConnectionData.CableStack.Block);

            // Создаем соединение с параметрами кабеля
            _currentConnectionData.StartBehavior.AddImmersiveConnection(
                _currentConnectionData.StartNodeIndex,
                blockSel.Position,
                (byte)blockSel.SelectionBoxIndex
            );

            _currentConnectionData.StartBehavior.AddEparamsAt(cableParams, (byte)(_currentConnectionData.StartBehavior.GetImmersiveConnections().Count - 1));

            endBehavior.AddImmersiveConnection(
                (byte)blockSel.SelectionBoxIndex,
                _currentConnectionData.StartPos,
                _currentConnectionData.StartNodeIndex
            );

            endBehavior.AddEparamsAt(cableParams, (byte)(endBehavior.GetImmersiveConnections().Count - 1));




            // После создания соединения обновляем меши
            if (api.Side == EnumAppSide.Client)
            {
                ImmersiveWireBlock.InvalidateBlockMeshCache(_currentConnectionData.StartPos);
                ImmersiveWireBlock.InvalidateBlockMeshCache(blockSel.Position);
            }


            if (capi != null)
                capi.ShowChatMessage($"Wire connected successfully. Used {cableLength} blocks of cable.");

            _currentConnectionData = null;
        }


        /// <summary>
        /// Считываем параметры с кабеля в руках игрока
        /// </summary>
        /// <param name="cableBlock"></param>
        /// <returns></returns>
        private EParams CreateCableParams(Vintagestory.API.Common.Block cableBlock)
        {
            // Загружаем параметры кабеля из JSON атрибутов
            var voltage = BlockECable.VoltagesInvert[cableBlock.Variant["voltage"]];
            var maxCurrent = MyMiniLib.GetAttributeFloat(cableBlock, "maxCurrent", 5.0F);
            var isolated = cableBlock.Code.Path.Contains("isolated");
            var isolatedEnvironment = isolated;
            var res = MyMiniLib.GetAttributeFloat(cableBlock, "res", 1);
            var crosssectional = MyMiniLib.GetAttributeFloat(cableBlock, "crosssectional", 1);
            var material = MyMiniLib.GetAttributeString(cableBlock, "material", "");

            return new EParams(voltage, maxCurrent, material, res, 1, crosssectional, false, isolated, isolatedEnvironment);
        }



        /// <summary>
        /// Обработка отключения выбранного соединения
        /// </summary>
        /// <param name="byPlayer"></param>
        /// <param name="blockSel"></param>
        /// <param name="behavior"></param>
        private void HandleWireDisconnection(IPlayer byPlayer, BlockSelection blockSel, BEBehaviorEPImmersive behavior)
        {
            // Находим провод под курсором и удаляем его
            var connections = behavior.GetImmersiveConnections();

            if (connections.Count > 0 && blockSel.SelectionBoxIndex < _wireNodes.Count)
            {
                byte nodeIndex = (byte)blockSel.SelectionBoxIndex;
                var connectionToRemove = connections.FirstOrDefault(c => c.LocalNodeIndex == nodeIndex);

                if (connectionToRemove != null)
                {
                    // Рассчитываем длину провода для возврата
                    WireNode startNode = behavior.GetWireNode(connectionToRemove.LocalNodeIndex);
                    WireNode endNode = null;

                    var neighborEntity = api.World.BlockAccessor.GetBlockEntity(connectionToRemove.NeighborPos);
                    var neighborBehavior = neighborEntity?.GetBehavior<BEBehaviorEPImmersive>();
                    if (neighborBehavior != null)
                    {
                        endNode = neighborBehavior.GetWireNode(connectionToRemove.NeighborNodeIndex);
                    }

                    int cableLength = 1; // минимальная длина
                    if (startNode != null && endNode != null)
                    {
                        // стартовая позиция
                        var startWorldPos = new Vec3d(
                            blockSel.Position.X + startNode.Position.X,
                            blockSel.Position.Y + startNode.Position.Y,
                            blockSel.Position.Z + startNode.Position.Z
                        );

                        // конечная позиция
                        var endWorldPos = new Vec3d(
                            connectionToRemove.NeighborPos.X + endNode.Position.X,
                            connectionToRemove.NeighborPos.Y + endNode.Position.Y,
                            connectionToRemove.NeighborPos.Z + endNode.Position.Z
                        );

                        double distance = startWorldPos.DistanceTo(endWorldPos);
                        cableLength = (int)Math.Ceiling(distance);
                    }

                    // только на сервере
                    if (api is ICoreServerAPI)
                    {
                        // Возвращаем кабель игроку
                        ItemStack cableStack = CreateCableStack(api, connectionToRemove.Parameters);
                        cableStack.StackSize = cableLength;

                        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                        {
                            if (!byPlayer.InventoryManager.TryGiveItemstack(cableStack, true))
                            {
                                // Если не помещается в инвентарь, выбрасываем на землю
                                api.World.SpawnItemEntity(cableStack, blockSel.Position.ToVec3d());
                            }
                        }
                    }


                    // Удаляем соединение
                    behavior.RemoveConnection(
                        connectionToRemove.LocalNodeIndex,
                        connectionToRemove.NeighborPos,
                        connectionToRemove.NeighborNodeIndex
                    );

                    // Также удаляем соединение с соседней стороны
                    neighborBehavior?.RemoveConnection(
                        connectionToRemove.NeighborNodeIndex,
                        blockSel.Position,
                        connectionToRemove.LocalNodeIndex
                    );

                    // вывод сообщения о количестве выданных кабелей
                    if (api is ICoreClientAPI)
                        ((ICoreClientAPI)api).ShowChatMessage($"Wire disconnected. Returned {cableLength} blocks of cable.");

                    // После разрыва соединения обновляем меши
                    if (api.Side == EnumAppSide.Client)
                    {
                        ImmersiveWireBlock.InvalidateBlockMeshCache(blockSel.Position);
                        ImmersiveWireBlock.InvalidateBlockMeshCache(connectionToRemove.NeighborPos);
                    }


                }
            }
        }

        /// <summary>
        /// Ключ для кэширования мешей блока со всеми подключениями
        /// </summary>
        private struct WireMeshCacheKey : IEquatable<WireMeshCacheKey>
        {
            public readonly BlockPos Position;
            public readonly int ConnectionsHash;

            public WireMeshCacheKey(BlockPos position, List<ConnectionData> connections)
            {
                Position = position;
                ConnectionsHash = ComputeConnectionsHash(connections);
            }

            private static int ComputeConnectionsHash(List<ConnectionData> connections)
            {
                if (connections == null || connections.Count == 0)
                    return 0;

                int hash = 17;
                foreach (var conn in connections.OrderBy(c => c.LocalNodeIndex).ThenBy(c => c.NeighborPos.GetHashCode()))
                {
                    hash = hash * 31 + conn.LocalNodeIndex;
                    hash = hash * 31 + conn.NeighborPos.GetHashCode();
                    hash = hash * 31 + conn.NeighborNodeIndex;
                    // Также добавляем хеш параметров кабеля

                    hash = hash * 31 + conn.Parameters.voltage.GetHashCode();
                    hash = hash * 31 + conn.Parameters.maxCurrent.GetHashCode();
                    hash = hash * 31 + (conn.Parameters.material?.GetHashCode() ?? 0);
                    hash = hash * 31 + conn.Parameters.isolated.GetHashCode();
                    
                }
                return hash;
            }

            public bool Equals(WireMeshCacheKey other)
            {
                return Position.Equals(other.Position) && ConnectionsHash == other.ConnectionsHash;
            }

            public override bool Equals(object obj)
            {
                return obj is WireMeshCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Position.GetHashCode() * 397) ^ ConnectionsHash;
                }
            }
        }

        // Кэш для полных мешей блоков со всеми подключениями
        private static readonly Dictionary<WireMeshCacheKey, MeshData> WireMeshesCache = new();

        /// <summary>
        /// Основной метод тесселяции - с кэшированием только мешей проводов
        /// </summary>
        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos position, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            // Получаем BEBehavior
            var beh = api.World.BlockAccessor.GetBlockEntity(position)?.GetBehavior<BEBehaviorEPImmersive>();
            if (beh == null)
            {
                base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);
                return;
            }

            // Получаем подключенные провода
            var connections = beh.GetImmersiveConnections();

            // Создаем ключ для кэша проводов
            var cacheKey = new WireMeshCacheKey(position, connections);

            // Получаем базовый меш (генерируется каждый раз, но это дешево)
            MeshData baseMeshData = GetBaseMesh();
            MeshData finalMesh = baseMeshData?.Clone() ?? new MeshData();

            // Пытаемся получить меши проводов из кэша
            if (WireMeshesCache.TryGetValue(cacheKey, out MeshData cachedWiresMesh))
            {
                // Если нашли в кэше - просто добавляем провода к базовому мешу
                if (cachedWiresMesh != null)
                {
                    finalMesh.AddMeshData(cachedWiresMesh);
                }
            }
            else
            {
                // Если в кэше нет, генерируем меши проводов
                MeshData wiresMesh = null;

                if (connections != null && connections.Count > 0)
                {
                    var connectedWires = GetConnectedWires(position, beh);
                    if (connectedWires != null && connectedWires.Count > 0)
                    {
                        foreach (var wireConnection in connectedWires)
                        {
                            var wireMesh = CreateWireSegmentMesh(
                                wireConnection.StartPos,
                                wireConnection.EndPos,
                                wireConnection.Thickness,
                                wireConnection.Asset,
                                wireConnection.SagFactor,
                                wireConnection.IsReverse
                            );

                            if (wireMesh != null)
                            {
                                AddMeshData(ref wiresMesh, wireMesh);
                            }
                        }
                    }
                }

                // Сохраняем в кэш (даже если wiresMesh = null - это значит проводов нет)
                WireMeshesCache[cacheKey] = wiresMesh;

                // Добавляем провода к финальному мешу
                if (wiresMesh != null)
                {
                    finalMesh.AddMeshData(wiresMesh);
                }
            }

            sourceMesh = finalMesh;
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);
        }



        /// <summary>
        /// Очистка кэша при изменении подключений
        /// </summary>
        public static void InvalidateBlockMeshCache(BlockPos position)
        {
            if (WireMeshesCache == null || WireMeshesCache.Count == 0)
                return;

            try
            {
                var keysToRemove = WireMeshesCache.Keys.Where(key => key.Position.Equals(position)).ToList();
                foreach (var key in keysToRemove)
                {
                    WireMeshesCache.Remove(key);
                }
            }
            catch { }
        }



        /// <summary>
        /// Получает базовый меш блока
        /// </summary>
        private MeshData GetBaseMesh()
        {
            if (api is ICoreClientAPI clientApi)
            {
                var cachedShape = clientApi.TesselatorManager.GetCachedShape(this.Shape.Base);
                clientApi.Tesselator.TesselateShape(this, cachedShape, out MeshData baseMeshData);
                clientApi.TesselatorManager.ThreadDispose();
                return baseMeshData;
            }
            return null;
        }



        /// <summary>
        /// Очистка всех кэшей
        /// </summary>
        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            // Очищаем кэш для этого блока
            WireMeshesCache.Clear();
        }

        /// <summary>
        /// При удалении блока
        /// </summary>
        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);

            // Очищаем кэш для этого блока
            InvalidateBlockMeshCache(pos);
            WireMeshesCache.Clear();
        }


        /// <summary>
        /// Создает меш сегмента провода между двумя точками с синхронизированной ориентацией
        /// </summary>
        private MeshData? CreateWireSegmentMesh(Vec3f startPos, Vec3f endPos, float thickness, AssetLocation asset, float sagFactor, bool isReverse = false)
        {
            float dist = startPos.DistanceTo(endPos);
            if (dist < 0.001f)
                return null;

            // Центрируем координаты блока
            startPos = startPos.AddCopy(-0.5f, -0.5f, -0.5f);
            endPos = endPos.AddCopy(-0.5f, -0.5f, -0.5f);

            // Количество сегментов - зависит от длины провода
            int segments = Math.Max(4, (int)(dist * 4f));

            var wireVariant = GetWireVariant(asset, thickness);
            if (wireVariant?.MeshData == null)
                return null;

            var center = new Vec3f(0.5f, 0.5f, 0.5f);
            float segmentLength = dist * 1.2f / segments;
            float scaleZ = segmentLength * 2f; // базовый меш имеет длину 0.5

            MeshData? mesh = null;
            
            for (int i = 0; i < (segments / 2) + 1; i++)
            {
                float progress = (float)i / segments;
                float nextProgress = (float)(i + 1) / segments;

                // Позиция начала сегмента
                Vec3f segmentStartPos = CalculateSagPosition(startPos, endPos, progress, sagFactor);
                // Позиция конца сегмента  
                Vec3f segmentEndPos = CalculateSagPosition(startPos, endPos, nextProgress, sagFactor);

                // Направление сегмента
                Vec3f segmentDir = segmentEndPos - segmentStartPos;
                float segmentDist = segmentDir.Length();

                if (segmentDist < 0.001f)
                    continue;

                segmentDir.Normalize();

                var segmentMesh = wireVariant.MeshData.Clone();

                // Масштабируем по длине
                if (isReverse && i>=(segments / 2)-2)
                    segmentMesh.Scale(center, 0.99f, 0.99f, scaleZ); // стык посередине чуть меньше
                else
                {
                    segmentMesh.Scale(center, 1f, 1f, scaleZ);
                }

                // Используем общее направление провода для единообразного поворота
                Vec3f rotationDirection = isReverse ? -1 * segmentDir : segmentDir;

                // Используем кватернион для правильного поворота
                float[] quat = CalculateSegmentRotation(rotationDirection);
                float[] rotationMatrix = QuaternionToMatrix4x4(quat);

                // Применяем поворот через матрицу
                segmentMesh.MatrixTransform(rotationMatrix, new float[4], center);

                // Позиционируем в центр сегмента
                Vec3f segmentCenter = segmentStartPos + segmentDir * (segmentDist / 2f);
                segmentMesh.Translate(segmentCenter.X, segmentCenter.Y, segmentCenter.Z);

                AddMeshData(ref mesh, segmentMesh);
            }

            return mesh;
        }




        /// <summary>
        /// Вычисляет кватернион поворота для ориентации сегмента вдоль направления
        /// </summary>
        private float[] CalculateSegmentRotation(Vec3f direction)
        {
            // Базовое направление (вдоль оси Z)
            var baseDirection = new float[] { 0, 0, 1 };
            var targetDirection = new float[] { direction.X, direction.Y, direction.Z };

            // Вычисляем кватернион поворота от базового направления к целевому
            return Quaternionf.RotationTo(Quaternionf.Create(), baseDirection, targetDirection);
        }




        /// <summary>
        /// Преобразует кватернион в матрицу 4x4
        /// </summary>
        private float[] QuaternionToMatrix4x4(float[] quat)
        {
            float x = quat[0], y = quat[1], z = quat[2], w = quat[3];

            float[] matrix = new float[16];

            // Вычисляем элементы матрицы из кватерниона
            float xx = x * x;
            float yy = y * y;
            float zz = z * z;
            float xy = x * y;
            float xz = x * z;
            float yz = y * z;
            float wx = w * x;
            float wy = w * y;
            float wz = w * z;

            // Column-major order
            matrix[0] = 1.0f - 2.0f * (yy + zz);
            matrix[1] = 2.0f * (xy + wz);
            matrix[2] = 2.0f * (xz - wy);
            matrix[3] = 0.0f;

            matrix[4] = 2.0f * (xy - wz);
            matrix[5] = 1.0f - 2.0f * (xx + zz);
            matrix[6] = 2.0f * (yz + wx);
            matrix[7] = 0.0f;

            matrix[8] = 2.0f * (xz + wy);
            matrix[9] = 2.0f * (yz - wx);
            matrix[10] = 1.0f - 2.0f * (xx + yy);
            matrix[11] = 0.0f;

            matrix[12] = 0.0f;
            matrix[13] = 0.0f;
            matrix[14] = 0.0f;
            matrix[15] = 1.0f;

            return matrix;
        }



        /// <summary>
        /// Вычисляет позицию с учетом провисания
        /// </summary>
        private Vec3f CalculateSagPosition(Vec3f start, Vec3f end, float progress, float sagFactor)
        {
            var linear = start + (end - start) * progress;

            if (sagFactor <= 0.001f)
                return linear;

            // Горизонтальное расстояние
            float hDist = (float)Math.Sqrt((end.X - start.X) * (end.X - start.X) +
                                          (end.Z - start.Z) * (end.Z - start.Z));

            if (hDist < 0.001f)
                return linear;

            // Провисание по катеноиде (вниз)
            float a = hDist / (8f * sagFactor);
            float hProgress = progress * hDist;
            float sagY = a * ((float)Math.Cosh((hProgress - hDist / 2f) / a) -
                             (float)Math.Cosh(hDist / 2f / a));

            return new Vec3f(linear.X, linear.Y + sagY, linear.Z);
        }




        private static void AddMeshData(ref MeshData? sourceMesh, MeshData? meshData)
        {
            if (meshData != null)
            {
                if (sourceMesh != null)
                {
                    sourceMesh.AddMeshData(meshData);
                }
                else
                {
                    sourceMesh = meshData;
                }
            }
        }




        /// <summary>
        /// Получает вариант провода из системы BlockECable
        /// </summary>
        private BlockVariants GetWireVariant(AssetLocation asset, float thickness)
        {
            try
            {
                var block = api.World.GetBlock(asset);

                return new BlockVariants(api, block);

            }
            catch
            {
                return null;
            }
        }





        /// <summary>
        /// Создаем ItemStack кабеля на основе параметров
        /// </summary>
        /// <param name="api"></param>
        /// <param name="cableParams"></param>
        /// <returns></returns>
        public static ItemStack CreateCableStack(ICoreAPI api, EParams cableParams)
        {
            var cableBlock = api.World.GetBlock(CreateCableAsset(api, cableParams));

            if (cableBlock == null)
            {
                // Fallback на базовый кабель
                cableBlock = api.World.GetBlock(new AssetLocation("electricalprogressivebasics:ecable-32v-copper-single-part"));
            }

            return new ItemStack(cableBlock);
        }

         

        /// <summary>
        /// Создаем Asset кабеля на основе параметров
        /// </summary>
        /// <param name="api"></param>
        /// <param name="cableParams"></param>
        /// <returns></returns>
        public static AssetLocation CreateCableAsset(ICoreAPI api, EParams cableParams)
        {
            string voltage = cableParams.voltage == 32 ? "32v" : "128v";
            string material = cableParams.material;
            string isolation = cableParams.isolated ? "isolated" : "part";

            AssetLocation cable;

            if (material == null || material == "")
            {
                // Fallback на базовый кабель
                cable = new AssetLocation("electricalprogressivecoreimmersive:ecable1-32v-copper-single-part");
            }
            else
            {
                cable = new AssetLocation($"electricalprogressivecoreimmersive:ecable1-{voltage}-{material}-single-{isolation}");
            }

            return cable;
        }





        /// <summary>
        /// Возвращает список подключенных проводов с указанием направления
        /// </summary>
        public List<WireConnection> GetConnectedWires(BlockPos pos, BEBehaviorEPImmersive beh)
        {
            var conn = new List<WireConnection>();

            var connections = beh.GetImmersiveConnections();

            foreach (var connection in connections)
            {
                var nodeHere = beh.GetWireNode(connection.LocalNodeIndex);
                if (nodeHere == null)
                    continue;

                var neighborEntity = api.World.BlockAccessor.GetBlockEntity(connection.NeighborPos);
                if (neighborEntity == null)
                    continue;

                var neighborBehavior = neighborEntity.GetBehavior<BEBehaviorEPImmersive>();
                if (neighborBehavior == null)
                    continue;

                var nodeNeighbor = neighborBehavior.GetWireNode(connection.NeighborNodeIndex);
                if (nodeNeighbor == null)
                    continue;

                // Используем относительные координаты
                var startPos = new Vec3f(
                    (float)(nodeHere.Position.X),
                    (float)(nodeHere.Position.Y),
                    (float)(nodeHere.Position.Z)
                );

                var endPos = new Vec3f(
                    (float)(connection.NeighborPos.X - pos.X + nodeNeighbor.Position.X),
                    (float)(connection.NeighborPos.Y - pos.Y + nodeNeighbor.Position.Y),
                    (float)(connection.NeighborPos.Z - pos.Z + nodeNeighbor.Position.Z)
                );

                // Определяем, является ли этот блок "источником" для направления провода
                // Используем хеш позиции для детерминированного выбора
                bool isSource = pos.GetHashCode() < connection.NeighborPos.GetHashCode();

                conn.Add(new WireConnection
                {
                    StartPos = startPos,
                    EndPos = endPos,
                    Thickness = 0.015f,
                    Asset = CreateCableAsset(api, connection.Parameters),
                    SagFactor = 0.05f,
                    IsReverse = !isSource // Для обратного направления используем обратную ориентацию
                });
            }

            return conn;
        }






        /// <summary>
        /// Структура для временного хранения данных о подключении
        /// </summary>
        private class WireConnectionData
        {
            public BlockPos StartPos { get; set; }
            public byte StartNodeIndex { get; set; }
            public BEBehaviorEPImmersive StartBehavior { get; set; }
            public ItemStack CableStack { get; set; }
        }


        /// <summary>
        /// Структура для хранения информации о подключении провода
        /// </summary>
        public struct WireConnection
        {
            public Vec3f StartPos;
            public Vec3f EndPos;
            public float Thickness;
            public AssetLocation Asset;
            public float SagFactor;
            public bool IsReverse;
        }

        /// <summary>
        /// Структура для ключа кэша мешей
        /// </summary>
        public struct CacheDataKey : IEquatable<CacheDataKey>
        {
            public BlockPos Position;
            public List<ConnectionData> Connections;

            public CacheDataKey(BlockPos position, List<ConnectionData> connections)
            {
                Position = position;
                Connections = connections;
            }

            public bool Equals(CacheDataKey other)
            {
                if (!Position.Equals(other.Position) || Connections.Count != other.Connections.Count)
                    return false;

                for (int i = 0; i < Connections.Count; i++)
                {
                    if (!Connections[i].Equals(other.Connections[i]))
                        return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheDataKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + Position.GetHashCode();
                    foreach (ConnectionData conn in Connections)
                    {
                        hash = hash * 31 + conn.GetHashCode();
                    }
                    return hash;
                }
            }
        }
    }
}