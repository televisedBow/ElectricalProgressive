using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static HarmonyLib.Code;

namespace EPImmersive.Content.Block
{
    public class ImmersiveWireBlock : Vintagestory.API.Common.Block, IMultiBlockColSelBoxes
    {
        //protected List<WireNode> _wireNodes; // точки крепления (ненадежно)

        public MeshData? _CustomMeshData;
        public bool _drawBaseMesh = true;

        public Cuboidf[]? _CustomSelBoxes;

        public bool _skipNonCenterCollisions = false;

        // Кэш для полных мешей блоков со всеми подключениями
        private static readonly Dictionary<WireMeshCacheKey, MeshData> WireMeshesCache = new();

        // Ключи для атрибутов
        private const string WIRE_CONNECTION_PREFIX = "epImmersiveWireConnection_";

        /// <summary>
        /// Сохраняет данные подключения в атрибуты игрока
        /// </summary>
        private void SaveConnectionToAttributes(IPlayer player, WireConnectionData data)
        {
            var attributes = player.Entity.Attributes;

            attributes.SetBlockPos($"{WIRE_CONNECTION_PREFIX}startPos", data.StartPos);
            attributes.SetInt($"{WIRE_CONNECTION_PREFIX}startNodeIndex", data.StartNodeIndex);
            attributes.SetString($"{WIRE_CONNECTION_PREFIX}asset", data.Asset.ToString());


            player.Entity.Attributes.MarkAllDirty();
        }

        /// <summary>
        /// Загружает данные подключения из атрибутов игрока
        /// </summary>
        private WireConnectionData LoadConnectionFromAttributes(IPlayer player)
        {
            var attributes = player.Entity.Attributes;

            if (!attributes.HasAttribute($"{WIRE_CONNECTION_PREFIX}startPosX"))
                return null;

            var startPos = attributes.GetBlockPos($"{WIRE_CONNECTION_PREFIX}startPos");
            var startNodeIndex = (byte)attributes.GetInt($"{WIRE_CONNECTION_PREFIX}startNodeIndex");
            var asset = new AssetLocation(attributes.GetString($"{WIRE_CONNECTION_PREFIX}asset") ?? "game:air");

            // Восстанавливаем StartBehavior из мира
            var startBlockEntity = api.World.BlockAccessor.GetBlockEntity(startPos);
            var startBehavior = startBlockEntity?.GetBehavior<BEBehaviorEPImmersive>();

            if (startBehavior == null)
            {
                ClearConnectionAttributes(player);
                return null;
            }

            return new WireConnectionData
            {
                StartPos = startPos,
                StartNodeIndex = startNodeIndex,
                StartBehavior = startBehavior,
                Asset = asset
            };
        }

        /// <summary>
        /// Очищает атрибуты подключения
        /// </summary>
        private void ClearConnectionAttributes(IPlayer player)
        {
            var attributes = player.Entity.Attributes;

            attributes.RemoveAttribute($"{WIRE_CONNECTION_PREFIX}startPosX");
            attributes.RemoveAttribute($"{WIRE_CONNECTION_PREFIX}startPosY");
            attributes.RemoveAttribute($"{WIRE_CONNECTION_PREFIX}startPosZ");
            attributes.RemoveAttribute($"{WIRE_CONNECTION_PREFIX}startNodeIndex");
            attributes.RemoveAttribute($"{WIRE_CONNECTION_PREFIX}asset");

            player.Entity.Attributes.MarkAllDirty();
        }

        /// <summary>
        /// Проверяет, есть ли активное подключение у игрока
        /// </summary>
        private bool HasActiveConnection(IPlayer player)
        {
            return player.Entity.Attributes.HasAttribute($"{WIRE_CONNECTION_PREFIX}startPosX");
        }


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

            // Добавляем выделение самого блока
            // нет кастомного выделения?
            if (_CustomSelBoxes == null)
            {
                boxes.AddRange(base.GetSelectionBoxes(blockAccessor, pos));
            }
            else
            {
                boxes.AddRange(_CustomSelBoxes);
            }

            // boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));

            return boxes.ToArray();
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            var boxes = new List<Cuboidf>();

            if (_skipNonCenterCollisions && (Math.Abs(offset.X) > 0 || Math.Abs(offset.Z) > 0))
                return boxes.ToArray();

            boxes.AddRange(base.GetCollisionBoxes(blockAccessor, pos));
            //boxes.AddRange(GetWireCollisionBoxes(blockAccessor, pos));
            return boxes.ToArray();
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            var boxes = new List<Cuboidf>();

            if (api.Side == EnumAppSide.Client)
            {
                var capi = (ICoreClientAPI)api;

                // Показываем точки подключения когда игрок держит провод
                if (IsHoldingWireTool(capi.World.Player) || IsHoldingWrench(capi.World.Player))
                {
                    var coll = GetNodeSelectionBoxes(blockAccessor, pos.AddCopy(offset));
                    
                    foreach (var col in coll)
                    {
                        col.X1 = col.X1 + offset.X;
                        col.Y1 = col.Y1 + offset.Y;
                        col.Z1 = col.Z1 + offset.Z;
                        col.X2 = col.X2 + offset.X;
                        col.Y2 = col.Y2 + offset.Y;
                        col.Z2 = col.Z2 + offset.Z;
                    }

                    boxes.AddRange(coll);
                    return boxes.ToArray();
                }
            }

            if (_skipNonCenterCollisions && (Math.Abs(offset.X) > 0 || Math.Abs(offset.Z) > 0))
                return boxes.ToArray();

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

            var entity = api.World.BlockAccessor.GetBlockEntity(pos);

            if(entity==null)
                return boxes.ToArray();

            var wireNodes = entity.GetBehavior<BEBehaviorEPImmersive>().GetWireNodes();

            if (wireNodes == null || wireNodes.Count == 0)
                return boxes.ToArray();

            float rotateY = 0;


            var entity2 = entity as BlockEntityEIBase;
            if (entity2 != null && entity2.Facing != Facing.None && entity2.RotationCache != null)
            {
                if (entity2.RotationCache.TryGetValue(entity2.Facing, out var rotation))
                {
                    rotateY = rotation.Y;
                }
            }

            for (int i = 0; i < wireNodes.Count; i++)
            {
                var x = wireNodes[i].Position.X;
                var y = wireNodes[i].Position.Y;
                var z = wireNodes[i].Position.Z;

                // небольшой костыль с вращением дотов
                (x, y, z) = RotateCoords(rotateY, x, y, z);

                var box = new Cuboidf(
                    (float)(x - wireNodes[i].Radius),
                    (float)(y - wireNodes[i].Radius),
                    (float)(z - wireNodes[i].Radius),
                    (float)(x + wireNodes[i].Radius),
                    (float)(y + wireNodes[i].Radius),
                    (float)(z + wireNodes[i].Radius)
                );

                boxes.Add(box);
            }


            return boxes.ToArray();
        }


        private static (double x, double y, double z) RotateCoords(float rotateY, double x, double y, double z)
        {
            // Конвертируем угол поворота в радианы
            double angleRad = (360 - rotateY) * GameMath.DEG2RAD;
            double cosAngle = Math.Cos(angleRad);
            double sinAngle = Math.Sin(angleRad);

            // Центр блока для поворота (0.5, 0.5, 0.5 в локальных координатах)
            double centerX = 0.5;
            double centerZ = 0.5;

            // Применяем поворот вокруг центра блока
            if (rotateY != 0)
            {
                // Смещаем координаты относительно центра
                double xRel = x - centerX;
                double zRel = z - centerZ;

                // Поворачиваем координаты
                double xRotated = xRel * cosAngle - zRel * sinAngle;
                double zRotated = xRel * sinAngle + zRel * cosAngle;

                // Возвращаем обратно в систему координат блока
                x = xRotated + centerX;
                z = zRotated + centerZ;
            }

            return (x, y, z);
        }



        /*
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

        */








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

            // Проверяем через атрибуты игрока
            if (HasActiveConnection(byPlayer))
            {
                HandleSecondPointSelection(capi, byPlayer, blockSel);
                return;
            }


            // Если игрок держит кабель для подключения проводов
            if (IsHoldingWireTool(byPlayer))
            {
                var behavior = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();
                var wireNodes = behavior.GetWireNodes();
                if (behavior != null && wireNodes != null && blockSel.SelectionBoxIndex < wireNodes.Count)
                {
                    // Начинаем процесс подключения провода
                    HandleWireConnection(capi, byPlayer, blockSel, behavior);
                    return;
                }
            }

            // Если игрок держит гаечный ключ для отключения
            if (IsHoldingWrench(byPlayer))
            {
                var behavior = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();
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
        public bool IsHoldingWireTool(IPlayer player)
        {
            ItemSlot activeSlot = player.InventoryManager.ActiveHotbarSlot;
            return activeSlot?.Itemstack?.Block?.Code.ToString().Contains("cable1")==true;
        }


        /// <summary>
        /// Игрок держит ключ?
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public bool IsHoldingWrench(IPlayer player)
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

            if (behavior.FindConnection(nodeIndex).Count >= 8)
            {
                if (capi != null)
                {
                    capi.ShowChatMessage("You cannot connect more than 8 wires to this point.");
                }
                return;
            }

            // Сохраняем в атрибуты игрока
            var connectionData = new WireConnectionData
            {
                StartPos = blockSel.Position,
                StartNodeIndex = nodeIndex,
                StartBehavior = behavior,
                Asset = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Block.Code.ToString()
            };

            SaveConnectionToAttributes(byPlayer, connectionData);

            if (capi != null)
            {
                capi.ShowChatMessage("Select second connection point. Right-click to cancel.");

                // Таймаут - очищаем атрибуты через 30 секунд
                capi.Event.RegisterCallback((dt) =>
                {
                    if (HasActiveConnection(byPlayer))
                    {
                        capi.ShowChatMessage("Wire connection cancelled.");
                        ClearConnectionAttributes(byPlayer);
                    }
                }, 30000);
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
            // Загружаем из атрибутов игрока
            var connectionData = LoadConnectionFromAttributes(byPlayer);
            if (connectionData == null)
                return;

            // Проверяем что в руках все еще тот же кабель
            if (!IsHoldingWireTool(byPlayer) || !byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Block.Code.ToString().Equals(connectionData.Asset))
            {
                if (capi != null)
                    capi.ShowChatMessage("You must hold the same cable to complete connection");
                ClearConnectionAttributes(byPlayer);
                return;
            }

            // Проверяем что вторая точка на другом блоке
            if (blockSel.Position.Equals(connectionData.StartPos))
            {
                if (capi != null)
                    capi.ShowChatMessage("Cannot connect wire to the same block");
                ClearConnectionAttributes(byPlayer);
                return;
            }

            var endBehavior = api.World.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorEPImmersive>();

            if (endBehavior == null || blockSel.SelectionBoxIndex >= endBehavior.GetWireNodes().Count)
            {
                if (capi != null)
                    capi.ShowChatMessage("Invalid connection point");
                ClearConnectionAttributes(byPlayer);
                return;
            }

            // Проверяем существующее подключение
            if (connectionData.StartBehavior.FindConnection(connectionData.StartNodeIndex,
                    blockSel.Position, (byte)blockSel.SelectionBoxIndex) != null)
            {
                if (capi != null)
                    capi.ShowChatMessage("Such a connection already exists.");
                ClearConnectionAttributes(byPlayer);
                return;
            }

            // Проверяем ограничение на 8 подключений у второго нода
            if (endBehavior.FindConnection((byte)blockSel.SelectionBoxIndex).Count >= 8)
            {
                if (capi != null)
                    capi.ShowChatMessage("You cannot connect more than 8 wires to this point.");
                ClearConnectionAttributes(byPlayer);
                return;
            }

            var currentConnectionData = LoadConnectionFromAttributes(byPlayer);

            // Рассчитываем длину провода
            WireNode startNode = currentConnectionData.StartBehavior.GetWireNode(currentConnectionData.StartNodeIndex);
            WireNode endNode = endBehavior.GetWireNode((byte)blockSel.SelectionBoxIndex);

            var startWorldPos = new Vec3d(
                currentConnectionData.StartPos.X + startNode.Position.X,
                currentConnectionData.StartPos.Y + startNode.Position.Y,
                currentConnectionData.StartPos.Z + startNode.Position.Z
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
                ClearConnectionAttributes(byPlayer);
                return;
            }

            // Проверяем достаточно ли кабеля у игрока
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative && activeSlot.StackSize < cableLength)
            {
                if (capi != null)
                    capi.ShowChatMessage($"Not enough cable. Need {cableLength} blocks, but only have {activeSlot.StackSize}");
                ClearConnectionAttributes(byPlayer);
                return;
            }

            // Забираем кабель у игрока
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                activeSlot.TakeOut(cableLength);
                activeSlot.MarkDirty();
            }



            // Создаем электрические параметры кабеля
            EParams cableParams = CreateCableParams(api.World.GetBlock(currentConnectionData.Asset));

            if (api.Side == EnumAppSide.Server)
            {
                // Создаем соединение с параметрами кабеля
                currentConnectionData.StartBehavior.AddImmersiveConnection(
                    currentConnectionData.StartNodeIndex,
                    blockSel.Position,
                    (byte)blockSel.SelectionBoxIndex
                );

                currentConnectionData.StartBehavior.AddEparamsAt(cableParams,
                    (byte)(currentConnectionData.StartBehavior.GetImmersiveConnections().Count - 1));

                endBehavior.AddImmersiveConnection(
                    (byte)blockSel.SelectionBoxIndex,
                    currentConnectionData.StartPos,
                    currentConnectionData.StartNodeIndex
                );

                endBehavior.AddEparamsAt(cableParams, (byte)(endBehavior.GetImmersiveConnections().Count - 1));

            }


            // После создания соединения обновляем меши
            if (api.Side == EnumAppSide.Client)
            {
                ImmersiveWireBlock.InvalidateBlockMeshCache(currentConnectionData.StartPos);
                ImmersiveWireBlock.InvalidateBlockMeshCache(blockSel.Position);
            }


            if (capi != null)
                capi.ShowChatMessage($"Wire connected successfully. Used {cableLength} blocks of cable.");

            ClearConnectionAttributes(byPlayer);
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
            var wireNodes = behavior.GetWireNodes();
            if (connections.Count > 0 && wireNodes != null && blockSel.SelectionBoxIndex < wireNodes.Count)
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


                    int cableLength = (int)Math.Ceiling(connectionToRemove.WireLength);


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
                    }



                    // После разрыва соединения обновляем меши
                    if (api.Side == EnumAppSide.Client)
                    {
                        // вывод сообщения о количестве выданных кабелей
                        ((ICoreClientAPI)api).ShowChatMessage($"Wire disconnected. Returned {cableLength} blocks of cable.");

                        ImmersiveWireBlock.InvalidateBlockMeshCache(blockSel.Position);
                        ImmersiveWireBlock.InvalidateBlockMeshCache(connectionToRemove.NeighborPos);
                    }


                }
            }
        }





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
            MeshData baseMeshData = null;
            if (_CustomMeshData == null)
                //baseMeshData = GetBaseMesh();
                baseMeshData = sourceMesh;
            else
            {
                baseMeshData = _CustomMeshData;
            }

            if (!_drawBaseMesh)
                baseMeshData = null;


            MeshData finalMesh = baseMeshData?.Clone() ?? new MeshData(4,6);

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



                // Добавляем провода к финальному мешу
                if (wiresMesh != null)
                {
                    WireMeshesCache[cacheKey] = wiresMesh;
                    finalMesh.AddMeshData(wiresMesh);
                }
            }

            sourceMesh = finalMesh;
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);
        }



        /// <summary>
        /// Очистка кэша при изменении подключений (оптимизированная версия)
        /// </summary>
        public static void InvalidateBlockMeshCache(BlockPos position)
        {
            // Быстрая проверка на пустоту
            if (WireMeshesCache == null || WireMeshesCache.Count == 0)
                return;

            try
            {
                // Используем список с предварительным выделением памяти
                // Предполагаем, что в среднем у блока не более 8 подключений
                var keysToRemove = new List<WireMeshCacheKey>(8);

                // Собираем ключи для удаления напрямую из словаря без LINQ
                foreach (var kvp in WireMeshesCache)
                {
                    // Используем прямое сравнение координат вместо Equals для производительности
                    var keyPos = kvp.Key.Position;
                    if (keyPos.X == position.X &&
                        keyPos.Y == position.Y &&
                        keyPos.Z == position.Z &&
                        keyPos.dimension == position.dimension)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                // Удаляем собранные ключи
                int count = keysToRemove.Count;
                for (int i = 0; i < count; i++)
                {
                    WireMeshesCache.Remove(keysToRemove[i]);
                }

                // Быстрая очистка списка (предотвращает утечку памяти при частых вызовах)
                keysToRemove.Clear();
            }
            catch
            {
                // Быстрое подавление исключения без дополнительной нагрузки
            }
        }

        /*

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
        */


        /// <summary>
        /// Очистка всех кэшей
        /// </summary>
        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            // Очищаем кэш для этого блока

            //WireMeshesCache.Clear();
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
            int segmentCount = (segments / 2) + 1;

            var wireVariant = GetWireVariant(asset, thickness);
            if (wireVariant?.MeshData == null)
                return null;

            // Получаем шаблонный меш для расчета размеров
            var templateMesh = wireVariant.MeshData;

            // Вычисляем общее количество вершин и индексов
            int totalVertices = segmentCount * templateMesh.VerticesCount;
            int totalIndices = segmentCount * templateMesh.IndicesCount;

            // Создаем меш с заранее рассчитанным размером
            MeshData mesh = new MeshData(
                totalVertices,
                totalIndices,
                withNormals: templateMesh.Normals != null,
                withUv: templateMesh.Uv != null,
                withRgba: templateMesh.Rgba != null,
                withFlags: templateMesh.Flags != null
            );

            var center = new Vec3f(0.5f, 0.5f, 0.5f);
            float segmentLength = dist * 1.2f / segments;
            float scaleZ = segmentLength * 2f; // базовый меш имеет длину 0.5

            Random rnd = new Random();

            for (int i = 0; i < segmentCount; i++)
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

                var segmentMesh = templateMesh.Clone();

                // Масштабируем по длине
                if (isReverse && i >= (segments / 2) - 2)
                    segmentMesh.Scale(center, 0.99f, 0.99f, scaleZ); // стык посередине чуть меньше
                else
                {
                    float buf = (float)(rnd.NextDouble() / 1000d);
                    segmentMesh.Scale(center, 1f + buf, 1f + buf, scaleZ);
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

                // Добавляем к основному мешу
                mesh.AddMeshData(segmentMesh);

                // Освобождаем временный меш
                segmentMesh.Dispose();
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



        /// <summary>
        /// Складываем мэши
        /// </summary>
        /// <param name="sourceMesh"></param>
        /// <param name="meshData"></param>
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
            string type = cableParams.isolated ? "isolated" : "part";

            // если кабель сгорел
            if (cableParams.burnout)
                type = "burned";


            AssetLocation cable;

            // материал неизвестен?
            if (material == null || material == "")
            {
                // Fallback на базовый кабель
                cable = new AssetLocation("electricalprogressivecoreimmersive:ecable1-32v-copper-part");
            }
            else
            {
                cable = new AssetLocation($"electricalprogressivecoreimmersive:ecable1-{voltage}-{material}-{type}");
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

                // далле костыль с поворотом нодов, если они заданы с помощью Facing
                float rotateY = 0;

                // берем энтити
                var entity = beh.Blockentity as BlockEntityEIBase;
                if (entity != null && entity.Facing != Facing.None && entity.RotationCache != null)
                {
                    if (entity.RotationCache.TryGetValue(entity.Facing, out var rotation))
                    {
                        rotateY = rotation.Y;
                    }
                }


                var x = nodeHere.Position.X;
                var y = nodeHere.Position.Y;
                var z = nodeHere.Position.Z;

                (x, y, z) = RotateCoords(rotateY, x, y, z);
                

                var startPos = new Vec3f(
                    (float)(x),
                    (float)(y),
                    (float)(z)
                );

                rotateY = 0;

                entity = api.World.BlockAccessor.GetBlockEntity(connection.NeighborPos) as BlockEntityEIBase;
                if (entity != null && entity.Facing != Facing.None && entity.RotationCache != null)
                {
                    if (entity.RotationCache.TryGetValue(entity.Facing, out var rotation))
                    {
                        rotateY = rotation.Y;
                    }
                }


                x = connection.NeighborNodeLocalPos.X;
                y = connection.NeighborNodeLocalPos.Y;
                z = connection.NeighborNodeLocalPos.Z;

                (x, y, z) = RotateCoords(rotateY, x, y, z);


                
                var endPos = new Vec3f(
                    (float)(connection.NeighborPos.X - pos.X + x),
                    (float)(connection.NeighborPos.Y - pos.Y + y),
                    (float)(connection.NeighborPos.Z - pos.Z + z)
                );

                // Используем хеш позиции для детерминированного выбора направления
                bool isSource = pos.GetHashCode() < connection.NeighborPos.GetHashCode();

                conn.Add(new WireConnection
                {
                    StartPos = startPos,
                    EndPos = endPos,
                    Thickness = 0.015f,
                    Asset = CreateCableAsset(api, connection.Parameters),
                    SagFactor = 0.05f,
                    IsReverse = !isSource
                });
            }

            return conn;
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
                return Position.Equals(other.Position)
                       && ConnectionsHash == other.ConnectionsHash;
            }

            public override bool Equals(object obj)
            {
                return obj is WireMeshCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + Position.GetHashCode();
                    hash = hash * 31 + ConnectionsHash;
                    return hash;
                }
            }
        }


        /// <summary>
        /// Структура для временного хранения данных о подключении
        /// </summary>
        private class WireConnectionData
        {
            public BlockPos StartPos { get; set; }
            public byte StartNodeIndex { get; set; }
            public BEBehaviorEPImmersive StartBehavior { get; set; }
            public AssetLocation Asset { get; set; }
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