using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using EPImmersive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EPImmersive.Content.Block
{
    public class ImmersiveWireBlock : Vintagestory.API.Common.Block
    {
        protected WireNode[] wireAnchors;
        private WireConnectionData currentConnectionData;
        private ImmersiveWireRenderer wireRenderer;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            LoadWireNodes(api);

            // Инициализируем рендерер на клиенте
            if (api.Side == EnumAppSide.Client)
            {
                wireRenderer = new ImmersiveWireRenderer((ICoreClientAPI)api);
                ((ICoreClientAPI)api).Event.RegisterRenderer(wireRenderer, EnumRenderStage.Opaque);
            }
        }



        private void LoadWireNodes(ICoreAPI api)
        {
            JsonObject[] wireNodesArray = Attributes?["wireNodes"]?.AsArray();
            if (wireNodesArray != null)
            {
                wireAnchors = new WireNode[wireNodesArray.Length];
                for (int i = 0; i < wireNodesArray.Length; i++)
                {
                    wireAnchors[i] = new WireNode
                    {
                        Index = (byte)(wireNodesArray[i]["index"]?.AsInt() ?? 0),
                        Voltage = wireNodesArray[i]["voltage"]?.AsInt() ?? 0,
                        Position = new Vec3d(
                            wireNodesArray[i]["x"]?.AsDouble() ?? 0,
                            wireNodesArray[i]["y"]?.AsDouble() ?? 0,
                            wireNodesArray[i]["z"]?.AsDouble() ?? 0
                        ),
                        Radius = wireNodesArray[i]["dxdydz"]?.AsFloat() ?? 0.1f
                    };
                }
            }
            else
            {
                wireAnchors = new WireNode[0];
            }
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();

            if (api.Side == EnumAppSide.Client)
            {
                ICoreClientAPI capi = (ICoreClientAPI)api;

                // Показываем точки подключения когда игрок держит провод
                if (IsHoldingWireTool(capi.World.Player) || IsHoldingWrench(capi.World.Player))
                {
                    boxes.AddRange(GetWireSelectionBoxes(blockAccessor, pos));
                    return boxes.ToArray();
                }
            }

            // Добавляем провода к выделению
            boxes.AddRange(base.GetSelectionBoxes(blockAccessor, pos));
            boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));

            return boxes.ToArray();
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();
            boxes.AddRange(base.GetCollisionBoxes(blockAccessor, pos));
            boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));
            return boxes.ToArray();
        }

        public virtual Cuboidf[] GetWireSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();
            for (int i = 0; i < wireAnchors.Length; i++)
            {
                Cuboidf box = new Cuboidf(
                    (float)(wireAnchors[i].Position.X - wireAnchors[i].Radius),
                    (float)(wireAnchors[i].Position.Y - wireAnchors[i].Radius),
                    (float)(wireAnchors[i].Position.Z - wireAnchors[i].Radius),
                    (float)(wireAnchors[i].Position.X + wireAnchors[i].Radius),
                    (float)(wireAnchors[i].Position.Y + wireAnchors[i].Radius),
                    (float)(wireAnchors[i].Position.Z + wireAnchors[i].Radius)
                );
                boxes.Add(box);
            }
            return boxes.ToArray();
        }

        public virtual Cuboidf[] GetWireCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();

            BEBehaviorEPImmersive behavior = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEPImmersive>();
            if (behavior == null) return boxes.ToArray();

            List<ConnectionData> connections = behavior.GetImmersiveConnections();
            if (connections.Count == 0) return boxes.ToArray();

            foreach (ConnectionData connection in connections)
            {
                WireNode nodeHere = behavior.GetWireNode(connection.LocalNodeIndex);
                if (nodeHere == null) continue;

                BlockEntity neighborEntity = blockAccessor.GetBlockEntity(connection.NeighborPos);
                if (neighborEntity == null) continue;

                BEBehaviorEPImmersive neighborBehavior = neighborEntity.GetBehavior<BEBehaviorEPImmersive>();
                if (neighborBehavior == null) continue;

                WireNode nodeNeighbor = neighborBehavior.GetWireNode(connection.NeighborNodeIndex);
                if (nodeNeighbor == null) continue;

                // Создаем упрощенный коллайдер для провода
                Vec3d start = new Vec3d(
                    pos.X + nodeHere.Position.X,
                    pos.Y + nodeHere.Position.Y,
                    pos.Z + nodeHere.Position.Z
                );

                Vec3d end = new Vec3d(
                    connection.NeighborPos.X + nodeNeighbor.Position.X,
                    connection.NeighborPos.Y + nodeNeighbor.Position.Y,
                    connection.NeighborPos.Z + nodeNeighbor.Position.Z
                );

                // Простой кубоид вдоль провода
                Cuboidf wireBox = CreateWireCollisionBox(start, end, 0.02f);
                boxes.Add(wireBox);
            }

            return boxes.ToArray();
        }

        private Cuboidf CreateWireCollisionBox(Vec3d start, Vec3d end, float thickness)
        {
            Vec3d mid = new Vec3d(
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

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos position, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);

            // Не добавляем провода здесь - они будут рендериться отдельно
        }

        // Добавляем метод для обновления меша проводов
        public void UpdateWireMeshes(BlockPos pos)
        {
            if (api.Side != EnumAppSide.Client || wireRenderer == null) return;

            BEBehaviorEPImmersive behavior = api.World.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEPImmersive>();
            if (behavior == null) return;

            List<ConnectionData> connections = behavior.GetImmersiveConnections();
            if (connections.Count == 0)
            {
                wireRenderer.RemoveWireMesh(pos);
                return;
            }

            // Генерируем меш для всех соединений этого блока
            MeshData wireMesh = GenerateWireMesh(pos, behavior, connections);
            wireRenderer.UpdateWireMesh(pos, wireMesh);
        }

      

        private MeshData GenerateWireMesh(BlockPos blockPos, BEBehaviorEPImmersive behavior, List<ConnectionData> connections)
        {
            MeshData combinedMesh = new MeshData(4 * connections.Count, 6 * connections.Count, false, true, true, true);
            combinedMesh.SetMode(EnumDrawMode.Triangles);

            foreach (ConnectionData connection in connections)
            {
                WireNode nodeHere = behavior.GetWireNode(connection.LocalNodeIndex);
                if (nodeHere == null) continue;

                BlockEntity neighborEntity = api.World.BlockAccessor.GetBlockEntity(connection.NeighborPos);
                if (neighborEntity == null) continue;

                BEBehaviorEPImmersive neighborBehavior = neighborEntity.GetBehavior<BEBehaviorEPImmersive>();
                if (neighborBehavior == null) continue;

                WireNode nodeNeighbor = neighborBehavior.GetWireNode(connection.NeighborNodeIndex);
                if (nodeNeighbor == null) continue;

                // Используем мировые координаты
                Vec3f startPos = new Vec3f(
                    (float)(nodeHere.Position.X),
                    (float)(nodeHere.Position.Y),
                    (float)(nodeHere.Position.Z)
                );

                Vec3f endPos = new Vec3f(
                    (float)(connection.NeighborPos.X - blockPos.X + nodeNeighbor.Position.X),
                    (float)(connection.NeighborPos.Y - blockPos.Y + nodeNeighbor.Position.Y),
                    (float)(connection.NeighborPos.Z - blockPos.Z + nodeNeighbor.Position.Z)
                );

                // Генерируем меш провода
                MeshData connectionMesh = WireMesh.MakeWireMesh(startPos, endPos, 0.015f);

                if (connectionMesh != null && connectionMesh.VerticesCount > 0)
                {
                    combinedMesh.AddMeshData(connectionMesh);
                }
            }

            return combinedMesh;
        }




        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return true;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);

            ICoreClientAPI capi = null;

            if (api is ICoreClientAPI)
                 capi = (ICoreClientAPI)api;

            // Если уже в процессе подключения - обрабатываем как вторую точку
            if (currentConnectionData != null)
            {
                HandleSecondPointSelection(capi, byPlayer, blockSel);
                return;
            }



            // Если игрок держит кабель для подключения проводов
            if (IsHoldingWireTool(byPlayer))
            {
                var behavior = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();
                if (behavior != null && blockSel.SelectionBoxIndex < wireAnchors.Length)
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


        private bool IsHoldingWireTool(IPlayer player)
        {
            ItemSlot activeSlot = player.InventoryManager.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Block is BlockECable;
        }

        private bool IsHoldingWrench(IPlayer player)
        {
            ItemSlot activeSlot = player.InventoryManager.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Item?.Tool == EnumTool.Wrench;
        }

        private void HandleWireConnection(ICoreClientAPI capi, IPlayer byPlayer, BlockSelection blockSel, BEBehaviorEPImmersive behavior)
        {
            byte nodeIndex = (byte)blockSel.SelectionBoxIndex;

            // Сохраняем информацию о первой точке подключения
            currentConnectionData = new WireConnectionData
            {
                StartPos = blockSel.Position,
                StartNodeIndex = nodeIndex,
                StartBehavior = behavior,
                CableStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Clone()
            };


            if (capi != null)
            {
                capi.ShowChatMessage("Select second connection point. Right-click to cancel.");

                // Устанавливаем таймаут для отмены операции
                capi.Event.RegisterCallback((dt) =>
                {
                    if (currentConnectionData != null)
                    {
                        capi.ShowChatMessage("Wire connection cancelled.");
                        currentConnectionData = null;
                    }
                }, 30000); // 30 секунд таймаут
            }
        }

        private void HandleSecondPointSelection(ICoreClientAPI capi, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (currentConnectionData == null)
                return;

            // Проверяем что в руках все еще тот же кабель
            if (!IsHoldingWireTool(byPlayer) || !byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Equals(api.World, currentConnectionData.CableStack, GlobalConstants.IgnoredStackAttributes))
            {
                if (capi!=null)
                    capi.ShowChatMessage("You must hold the same cable to complete connection");
                currentConnectionData = null;
                return;
            }

            // Проверяем что вторая точка на другом блоке
            if (blockSel.Position.Equals(currentConnectionData.StartPos))
            {
                if (capi != null)
                    capi.ShowChatMessage("Cannot connect wire to the same block");
                currentConnectionData = null;
                return;
            }

            var endBehavior = api.World.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();
            if (endBehavior == null || blockSel.SelectionBoxIndex >= endBehavior.GetWireNodes().Count)
            {
                if (capi != null)
                    capi.ShowChatMessage("Invalid connection point");
                currentConnectionData = null;
                return;
            }

            if (currentConnectionData.StartBehavior.FindConnection(currentConnectionData.StartNodeIndex,
                    blockSel.Position, (byte)blockSel.SelectionBoxIndex) != null)
            {
                if (capi != null)
                    capi.ShowChatMessage("Such a connection already exists.");
                currentConnectionData = null;
                return;
            }

            // Рассчитываем длину провода
            WireNode startNode = currentConnectionData.StartBehavior.GetWireNode(currentConnectionData.StartNodeIndex);
            WireNode endNode = endBehavior.GetWireNode((byte)blockSel.SelectionBoxIndex);

            Vec3d startWorldPos = new Vec3d(
                currentConnectionData.StartPos.X + startNode.Position.X,
                currentConnectionData.StartPos.Y + startNode.Position.Y,
                currentConnectionData.StartPos.Z + startNode.Position.Z
            );

            Vec3d endWorldPos = new Vec3d(
                blockSel.Position.X + endNode.Position.X,
                blockSel.Position.Y + endNode.Position.Y,
                blockSel.Position.Z + endNode.Position.Z
            );

            double distance = startWorldPos.DistanceTo(endWorldPos);
            int cableLength = (int)Math.Ceiling(distance);

            // Проверяем достаточно ли кабеля у игрока
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative && activeSlot.StackSize < cableLength)
            {
                if (capi != null)
                    capi.ShowChatMessage($"Not enough cable. Need {cableLength} blocks, but only have {activeSlot.StackSize}");
                currentConnectionData = null;
                return;
            }

            // Забираем кабель у игрока
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                activeSlot.TakeOut(cableLength);
                activeSlot.MarkDirty();
            }

            // Создаем электрические параметры кабеля
            EParams cableParams = CreateCableParams(currentConnectionData.CableStack.Block);

            // Создаем соединение с параметрами кабеля
            currentConnectionData.StartBehavior.AddImmersiveConnection(
                currentConnectionData.StartNodeIndex,
                blockSel.Position,
                (byte)blockSel.SelectionBoxIndex
            );

            currentConnectionData.StartBehavior.AddEparamsAt(cableParams, (byte)(currentConnectionData.StartBehavior.GetImmersiveConnections().Count-1));

            endBehavior.AddImmersiveConnection(
                (byte)blockSel.SelectionBoxIndex,
                currentConnectionData.StartPos,
                currentConnectionData.StartNodeIndex
            );

            endBehavior.AddEparamsAt(cableParams, (byte)(endBehavior.GetImmersiveConnections().Count - 1));




            // После создания соединения обновляем меши
            UpdateWireMeshes(currentConnectionData.StartPos);
            UpdateWireMeshes(blockSel.Position);

            if (capi != null)
                capi.ShowChatMessage($"Wire connected successfully. Used {cableLength} blocks of cable.");

            currentConnectionData = null;
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

            if (connections.Count > 0 && blockSel.SelectionBoxIndex < wireAnchors.Length)
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
            if (api.Side == EnumAppSide.Client && wireRenderer != null)
            {
                wireRenderer.Dispose();
                wireRenderer = null;
            }
            base.OnUnloaded(api);
        }


        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);

            // Удаляем меш при удалении блока
            if (world.Side == EnumAppSide.Client && wireRenderer != null)
            {
                wireRenderer.RemoveWireMesh(pos);
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