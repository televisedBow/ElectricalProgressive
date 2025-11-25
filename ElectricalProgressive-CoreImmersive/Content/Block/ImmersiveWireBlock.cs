using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
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

        private ImmersiveWireRenderer _wireRenderer; // рендерер проводов


        public override void OnEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed,
            bool isImpact)
        {
            base.OnEntityCollide(world, entity, pos, facing, collideSpeed, isImpact);

            //if(world.Api is ICoreClientAPI capi)
            //    capi.ShowChatMessage("Collide!");
        }


        /// <summary>
        /// Загрузка блока
        /// </summary>
        /// <param name="api"></param>
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Инициализируем рендерер на клиенте
            if (api.Side == EnumAppSide.Client)
            {
                _wireRenderer = new ImmersiveWireRenderer((ICoreClientAPI)api);
                ((ICoreClientAPI)api).Event.RegisterRenderer(_wireRenderer, EnumRenderStage.Opaque);
            }
        }
        

        /// <summary>
        /// Выделение
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();

            if (api.Side == EnumAppSide.Client)
            {
                ICoreClientAPI capi = (ICoreClientAPI)api;

                // Показываем точки подключения когда игрок держит провод
                if (IsHoldingWireTool(capi.World.Player) || IsHoldingWrench(capi.World.Player))
                {
                    boxes.AddRange(GetNodeSelectionBoxes(blockAccessor, pos));
                    return boxes.ToArray();
                }
            }

            // Добавляем провода к выделению
            boxes.AddRange(base.GetSelectionBoxes(blockAccessor, pos));
            boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));

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
            List<Cuboidf> boxes = new List<Cuboidf>();
            boxes.AddRange(base.GetCollisionBoxes(blockAccessor, pos));
            boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));
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



        // Добавляем метод для обновления меша проводов
        private List<ImmersiveWireRenderer.WireMeshData> GenerateWireMesh(BlockPos blockPos, BEBehaviorEPImmersive behavior, List<ConnectionData> connections)
        {
            var meshDataList = new List<ImmersiveWireRenderer.WireMeshData>();

            foreach (ConnectionData connection in connections)
            {
                var nodeHere = behavior.GetWireNode(connection.LocalNodeIndex);
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

                // Используем мировые координаты
                var startPos = new Vec3f(
                    (float)(nodeHere.Position.X),
                    (float)(nodeHere.Position.Y),
                    (float)(nodeHere.Position.Z)
                );

                var endPos = new Vec3f(
                    (float)(connection.NeighborPos.X - blockPos.X + nodeNeighbor.Position.X),
                    (float)(connection.NeighborPos.Y - blockPos.Y + nodeNeighbor.Position.Y),
                    (float)(connection.NeighborPos.Z - blockPos.Z + nodeNeighbor.Position.Z)
                );

                // Генерируем меш провода
                MeshData connectionMesh = WireMesh.MakeWireMesh(startPos, endPos, 0.015f);

                if (connectionMesh != null && connectionMesh.VerticesCount > 0)
                {
                    // Получаем материал из параметров кабеля
                    string material = (connection.Parameters.material=="")? "copper": connection.Parameters.material;
                    // Если кабель изолирован
                    if (connection.Parameters.isolated)
                        material = "liquid/dye/gray";

                    meshDataList.Add(new ImmersiveWireRenderer.WireMeshData
                    {
                        Mesh = connectionMesh,
                        Material = material
                    });
                }
            }

            return meshDataList;
        }

        

        /// <summary>
        /// Обновление точек крепления
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="nodes"></param>
        public void UpdateWireNodes(List<WireNode> nodes)
        {
            // обновляем точки крепления
            _wireNodes= nodes;
        }


        /// <summary>
        /// Обновляем мэши
        /// </summary>
        /// <param name="pos"></param>
        public void UpdateWireMeshes(BlockPos pos)
        {
            if (api.Side != EnumAppSide.Client || _wireRenderer == null)
                return;

            var behavior = api.World.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEPImmersive>();
            if (behavior == null)
                return;

            var connections = behavior.GetImmersiveConnections();
            if (connections.Count == 0)
            {
                _wireRenderer.RemoveWireMesh(pos);
                return;
            }

            // Генерируем список мешей для всех соединений этого блока
            List<ImmersiveWireRenderer.WireMeshData> wireMeshes = GenerateWireMesh(pos, behavior, connections);
            _wireRenderer.UpdateWireMesh(pos, wireMeshes);
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
                if (capi!=null)
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
            if (endBehavior.FindConnection((byte)blockSel.SelectionBoxIndex).Count>=8)
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

            _currentConnectionData.StartBehavior.AddEparamsAt(cableParams, (byte)(_currentConnectionData.StartBehavior.GetImmersiveConnections().Count-1));

            endBehavior.AddImmersiveConnection(
                (byte)blockSel.SelectionBoxIndex,
                _currentConnectionData.StartPos,
                _currentConnectionData.StartNodeIndex
            );

            endBehavior.AddEparamsAt(cableParams, (byte)(endBehavior.GetImmersiveConnections().Count - 1));




            // После создания соединения обновляем меши
            UpdateWireMeshes(_currentConnectionData.StartPos);
            UpdateWireMeshes(blockSel.Position);

            if (capi != null)
                capi.ShowChatMessage($"Wire connected successfully. Used {cableLength} blocks of cable.");

            _currentConnectionData = null;
        }



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




        private void HandleWireDisconnection(IPlayer byPlayer, BlockSelection blockSel, BEBehaviorEPImmersive behavior)
        {
            // Находим провод под курсором и удаляем его
            List<ConnectionData> connections = behavior.GetImmersiveConnections();

            if (connections.Count > 0 && blockSel.SelectionBoxIndex < _wireNodes.Count)
            {
                byte nodeIndex = (byte)blockSel.SelectionBoxIndex;
                var connectionToRemove = connections.FirstOrDefault(c => c.LocalNodeIndex == nodeIndex);

                if (connectionToRemove != null)
                {
                    // Рассчитываем длину провода для возврата
                    WireNode startNode = behavior.GetWireNode(connectionToRemove.LocalNodeIndex);
                    WireNode endNode = null;

                    BlockEntity neighborEntity = api.World.BlockAccessor.GetBlockEntity(connectionToRemove.NeighborPos);
                    BEBehaviorEPImmersive neighborBehavior = neighborEntity?.GetBehavior<BEBehaviorEPImmersive>();
                    if (neighborBehavior != null)
                    {
                        endNode = neighborBehavior.GetWireNode(connectionToRemove.NeighborNodeIndex);
                    }

                    int cableLength = 1; // минимальная длина
                    if (startNode != null && endNode != null)
                    {
                        var startWorldPos = new Vec3d(
                            blockSel.Position.X + startNode.Position.X,
                            blockSel.Position.Y + startNode.Position.Y,
                            blockSel.Position.Z + startNode.Position.Z
                        );

                        var endWorldPos = new Vec3d(
                            connectionToRemove.NeighborPos.X + endNode.Position.X,
                            connectionToRemove.NeighborPos.Y + endNode.Position.Y,
                            connectionToRemove.NeighborPos.Z + endNode.Position.Z
                        );

                        double distance = startWorldPos.DistanceTo(endWorldPos);
                        cableLength = (int)Math.Ceiling(distance);
                    }

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

                    if (api is ICoreClientAPI)
                        ((ICoreClientAPI)api).ShowChatMessage($"Wire disconnected. Returned {cableLength} blocks of cable.");

                    // После разрыва соединения обновляем меши
                    UpdateWireMeshes(blockSel.Position);
                    UpdateWireMeshes(connectionToRemove.NeighborPos);

                }
            }
        }



        public static ItemStack CreateCableStack(ICoreAPI api, EParams cableParams)
        {
            // Создаем ItemStack кабеля на основе параметров
            // Здесь нужно создать соответствующий BlockECable на основе параметров
            // Это упрощенная реализация - в реальности нужно маппить параметры на конкретный блок кабеля

            string voltage = cableParams.voltage == 32 ? "32v" : "128v";
            string material = cableParams.material;
            string isolation = cableParams.isolated ? "isolated" : "part";

            AssetLocation cableCode = new AssetLocation($"electricalprogressivebasics:ecable-{voltage}-{material}-single-{isolation}");
            Vintagestory.API.Common.Block cableBlock = api.World.GetBlock(cableCode);

            if (cableBlock == null)
            {
                // Fallback на базовый кабель
                cableBlock = api.World.GetBlock(new AssetLocation("electricalprogressivebasics:ecable-32v-copper-single-part"));
            }

            return new ItemStack(cableBlock);
        }




        // Добавляем очистку рендерера
        public override void OnUnloaded(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Client && _wireRenderer != null)
            {
                _wireRenderer.Dispose();
                _wireRenderer = null;
            }
            base.OnUnloaded(api);
        }


        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);

            // Удаляем меш при удалении блока
            if (world.Side == EnumAppSide.Client && _wireRenderer != null)
            {
                _wireRenderer.RemoveWireMesh(pos);
            }

        }


        


        // Структура для временного хранения данных о подключении
        private class WireConnectionData
        {
            public BlockPos StartPos { get; set; }
            public byte StartNodeIndex { get; set; }
            public BEBehaviorEPImmersive StartBehavior { get; set; }
            public ItemStack CableStack { get; set; }
        }

        // Структура для ключа кэша мешей
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