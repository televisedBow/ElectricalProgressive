using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport
{
    public class BELiquidInsertionPipe : BlockEntityGenericTypedContainer
    {
        private long transferTimer;
        private int transferRate = 100; // В миллилитрах
        private BlockFacing outputFacing = null;
        private int debugCounter = 0;
        
        protected bool[] connectedSides = new bool[6];
        protected BlockPos?[] connectedPipes = new BlockPos?[6];
        protected PipeNetworkManager networkManager;
        
        // Собственный инвентарь (фильтры для жидкостей)
        internal InventoryLiquidInsertionPipe _inventory;
        private GuiDialogLiquidInsertionPipe _clientDialog;
        
        // Режимы работы фильтра
        public enum FilterMode
        {
            AllowList = 0,
            DenyList = 1,
        }
        
        private FilterMode currentFilterMode = FilterMode.AllowList;
        
        // Тайминги
        private Dictionary<BlockPos, long> lastTransferTime = new Dictionary<BlockPos, long>();
        private const long MinTransferInterval = 100;
        
        public override InventoryBase Inventory => _inventory;
        public override string InventoryClassName => "liquidinsertionpipe";
        
        public int TransferRate => transferRate;
        public bool[] ConnectedSides => connectedSides;
        public FilterMode CurrentFilterMode => currentFilterMode;
        
        public BELiquidInsertionPipe()
        {
            _inventory = new InventoryLiquidInsertionPipe(6, InventoryClassName, null, null, this);
        }
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            
            api.Logger.Notification($"=== Жидкостная труба Initialize на {Pos} ===");
            
            DetermineOutputDirection();
            
            networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
            networkManager?.AddPipe(Pos, this);
            
            UpdateConnections();
            
            if (api.Side == EnumAppSide.Server)
            {
                transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 200);
            }
        }
        
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                OpenGui(byPlayer as IClientPlayer);
            }
            return true;
        }
        
        private void OpenGui(IClientPlayer player)
        {
            if (_clientDialog == null || !_clientDialog.IsOpened())
            {
                _clientDialog = new GuiDialogLiquidInsertionPipe(
                    Lang.Get("electricalprogressivetransport:liquid-filter-pipe-title"),
                    Inventory,
                    Pos,
                    Api as ICoreClientAPI,
                    this
                );
                _clientDialog.TryOpen();
                player.InventoryManager.OpenInventory(Inventory);
            }
            else
            {
                _clientDialog.TryClose();
            }
        }
        
        private void DetermineOutputDirection()
        {
            if (Api == null) return;
    
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Определяем вывод для жидкостной трубы на {Pos} ===");
    
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);
        
                Block checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
                if (checkBlock is BlockPipeBase)
                {
                    continue;
                }
        
                // Ищем контейнеры, которые могут хранить жидкости
                BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(checkPos);
                
                if (be != null && HasLiquidSlot(be))
                {
                    outputFacing = facing;
                    if (debugCounter % 5 == 0)
                    {
                        Api.Logger.Notification($"=== НАЙДЕН КОНТЕЙНЕР С ЖИДКОСТНЫМ СЛОТОМ! {be.GetType().Name} на {checkPos} ===");
                    }
                    return;
                }
            }
    
            outputFacing = null;
        }
        
        private bool HasLiquidSlot(BlockEntity be)
        {
            if (be is BlockEntityContainer container)
            {
                if (container.Inventory != null)
                {
                    for (int i = 0; i < container.Inventory.Count; i++)
                    {
                        ItemSlot slot = container.Inventory[i];
                        if (slot is ItemSlotLiquidOnly)
                        {
                            if (debugCounter % 10 == 0)
                                Api.Logger.Notification($"=== Найден ItemSlotLiquidOnly в слоте #{i} ===");
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        
        private void OnTransferTick(float dt)
        {
            debugCounter++;
            
            if (Api == null || networkManager == null) return;

            // Обновляем направление вывода
            if (outputFacing == null || debugCounter % 20 == 0)
            {
                DetermineOutputDirection();
            }

            if (outputFacing == null)
                return;

            // Получаем целевой контейнер
            BlockPos containerPos = Pos.AddCopy(outputFacing);
            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(containerPos);
            
            if (be == null || !HasLiquidSlot(be))
                return;
    
            // Ищем и переносим предметы в жидкостные слоты
            FindAndTransferToLiquidSlots(be, containerPos);
        }
        
        private void FindAndTransferToLiquidSlots(BlockEntity targetBe, BlockPos targetPos)
        {
            if (!(targetBe is BlockEntityContainer targetContainer))
                return;
                
            IInventory targetInventory = targetContainer.Inventory;
            
            if (targetInventory == null) 
                return;
            
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Ищем предметы для жидкостного контейнера на {targetPos} ===");
            
            // Используем сеть для поиска источников
            var network = networkManager.GetNetwork(Pos);
            if (network == null) 
                return;

            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Размер сети: {network.Pipes.Count} труб ===");
            
            // Собираем все позиции для исключения
            var excludePositions = new HashSet<BlockPos>();
            excludePositions.Add(Pos);
            excludePositions.Add(targetPos);

            // Ищем источник предметов в сети
            foreach (var pipePos in network.Pipes)
            {
                if (excludePositions.Contains(pipePos)) continue;

                // Проверяем все стороны трубы
                for (int i = 0; i < 6; i++)
                {
                    BlockFacing facing = BlockFacing.ALLFACES[i];
                    BlockPos checkPos = pipePos.AddCopy(facing);

                    if (excludePositions.Contains(checkPos)) continue;
                    
                    // Пропускаем другие трубы
                    Block checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
                    if (checkBlock is BlockPipeBase)
                        continue;

                    // Проверяем, можно ли взять предмет из этого источника
                    if (TryTransferFromSourceToLiquidSlot(checkPos, targetInventory, targetContainer, targetPos))
                    {
                        if (debugCounter % 5 == 0)
                            Api.Logger.Notification($"=== Успешно перенесли предмет из {checkPos} ===");
                        return;
                    }
                }
            }
        }
        
        private bool TryTransferFromSourceToLiquidSlot(BlockPos sourcePos, IInventory targetInventory, 
            BlockEntityContainer targetContainer, BlockPos targetPos)
        {
            // Проверяем тайминг
            if (!CanTransferFrom(sourcePos)) 
                return false;
            
            BlockEntity sourceBe = Api.World.BlockAccessor.GetBlockEntity(sourcePos);
            if (sourceBe == null) 
                return false;
            
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Проверяем источник: {sourceBe.GetType().Name} на {sourcePos} ===");
            
            // Получаем инвентарь источника
            IInventory sourceInventory = GetInventoryFromBlockEntity(sourceBe);
            if (sourceInventory == null) 
                return false;
            
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Инвентарь источника получен. Слотов: {sourceInventory.Count} ===");
            
            // Ищем подходящий предмет в источнике
            ItemSlot sourceSlot = FindFirstItemForLiquidSlot(sourceInventory);
            
            if (sourceSlot == null || sourceSlot.Empty)
                return false;
            
            if (debugCounter % 5 == 0)
                Api.Logger.Notification($"=== Нашли предмет: {sourceSlot.Itemstack?.Collectible?.Code}, количество: {sourceSlot.StackSize} ===");
            
            // Находим жидкостной слот в цели
            ItemSlotLiquidOnly liquidSlot = FindLiquidSlot(targetInventory);
            if (liquidSlot == null)
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Не найден жидкостной слот в цели ===");
                return false;
            }
            
            // Проверяем фильтр (если фильтры настроены)
            if (!CheckItemAgainstLiquidFilter(sourceSlot.Itemstack))
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Предмет не прошел фильтр ===");
                return false;
            }
            
            // Проверяем, может ли жидкостной слот принять предмет
            if (!liquidSlot.CanHold(sourceSlot))
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Жидкостной слот не может принять этот предмет ===");
                return false;
            }
            
            // Выполняем перенос
            return ExecuteTransferToLiquidSlot(sourceSlot, liquidSlot, sourceBe, targetContainer, sourcePos);
        }
        
        private bool CanTransferFrom(BlockPos sourcePos)
        {
            if (!lastTransferTime.ContainsKey(sourcePos))
                return true;
                
            long elapsed = Api.World.ElapsedMilliseconds - lastTransferTime[sourcePos];
            return elapsed > MinTransferInterval;
        }
        
        // Ищет первый предмет, который может быть помещен в жидкостной слот
        private ItemSlot FindFirstItemForLiquidSlot(IInventory inventory)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot != null && !slot.Empty)
                {
                    // Логируем что нашли
                    if (debugCounter % 10 == 0)
                        Api.Logger.Notification($"=== Проверяем предмет в слоте #{i}: {slot.Itemstack?.Collectible?.Code} ===");
                    
                    // Проверяем, является ли это жидкостью (опционально)
                    // Но главное - этот предмет будет проверен жидкостным слотом через CanHold
                    return slot;
                }
            }
            
            return null;
        }
        
        // Находит жидкостной слот в инвентаре
        private ItemSlotLiquidOnly FindLiquidSlot(IInventory inventory)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot is ItemSlotLiquidOnly liquidSlot)
                {
                    if (debugCounter % 5 == 0)
                    {
                        string status = liquidSlot.Empty ? "пустой" : $"содержит {liquidSlot.Itemstack?.Collectible?.Code}";
                        Api.Logger.Notification($"=== Найден жидкостной слот #{i}: {status} ===");
                    }
                    return liquidSlot;
                }
            }
            
            return null;
        }
        
        // Проверяет предмет через фильтр (если фильтры настроены)
        private bool CheckItemAgainstLiquidFilter(ItemStack itemstack)
        {
            if (itemstack == null || itemstack.Collectible == null)
                return false;
            
            // Проверяем, есть ли хоть один фильтр
            bool hasAnyFilters = false;
            for (int i = 0; i < _inventory.Count; i++)
            {
                if (!_inventory[i].Empty)
                {
                    hasAnyFilters = true;
                    break;
                }
            }
            
            // Если фильтров нет - пропускаем всё
            if (!hasAnyFilters)
            {
                return true;
            }
            
            bool hasMatchingFilter = false;
            
            // Проверяем все слоты фильтра
            for (int i = 0; i < _inventory.Count; i++)
            {
                ItemSlot filterSlot = _inventory[i];
                if (!filterSlot.Empty)
                {
                    // Сравниваем коды предметов
                    if (itemstack.Collectible.Code.Equals(filterSlot.Itemstack.Collectible.Code))
                    {
                        hasMatchingFilter = true;
                        break;
                    }
                }
            }
            
            // Возвращаем результат в зависимости от режима
            return currentFilterMode == FilterMode.AllowList ? hasMatchingFilter : !hasMatchingFilter;
        }
        
        private bool ExecuteTransferToLiquidSlot(ItemSlot sourceSlot, ItemSlotLiquidOnly targetSlot, 
            BlockEntity sourceBe, BlockEntityContainer targetContainer, BlockPos sourcePos)
        {
            try
            {
                int transferAmount = Math.Min(transferRate, sourceSlot.StackSize);
                
                if (debugCounter % 5 == 0)
                {
                    Api.Logger.Notification($"=== Пытаемся перенести {transferAmount} предметов {sourceSlot.Itemstack?.Collectible?.Code} ===");
                }
                
                ItemStackMoveOperation op = new ItemStackMoveOperation(
                    Api.World,
                    EnumMouseButton.Left,
                    0,
                    EnumMergePriority.DirectMerge,
                    transferAmount
                );
                
                int transferred = sourceSlot.TryPutInto(targetSlot, ref op);
                
                if (transferred > 0)
                {
                    lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
                    sourceSlot.MarkDirty();
                    targetSlot.MarkDirty();
                    sourceBe.MarkDirty();
                    targetContainer.MarkDirty();
                    
                    Api.Logger.Notification($"=== УСПЕХ: Перенесено {transferred} предметов в жидкостной слот ===");
                    
                    return true;
                }
                else
                {
                    if (debugCounter % 5 == 0)
                    {
                        Api.Logger.Notification($"=== TryPutInto не смог перенести предметы ===");
                        if (!targetSlot.Empty)
                        {
                            Api.Logger.Notification($"=== Целевой слот уже содержит: {targetSlot.Itemstack.Collectible.Code}, количество: {targetSlot.StackSize} ===");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Api.Logger.Error($"Ошибка при переносе в жидкостной слот: {ex.Message}");
            }
            
            return false;
        }
        
        // Получаем инвентарь из BlockEntity
        private IInventory GetInventoryFromBlockEntity(BlockEntity be)
        {
            // 1. BlockEntityContainer
            if (be is BlockEntityContainer container)
            {
                return container.Inventory;
            }
            
            // 2. IBlockEntityContainer
            if (be is IBlockEntityContainer icon)
            {
                return icon.Inventory;
            }
            
            // 3. IInventory
            if (be is IInventory inventory)
            {
                return inventory;
            }
            
            // 4. Рефлексия для поиска Inventory
            try
            {
                var prop = be.GetType().GetProperty("Inventory");
                if (prop != null)
                {
                    return prop.GetValue(be) as IInventory;
                }
            }
            catch { }
            
            return null;
        }
        
        private BlockFacing GetFacingFromTo(BlockPos from, BlockPos to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            int dz = to.Z - from.Z;

            if (dx == 1 && dy == 0 && dz == 0) return BlockFacing.EAST;
            if (dx == -1 && dy == 0 && dz == 0) return BlockFacing.WEST;
            if (dx == 0 && dy == 1 && dz == 0) return BlockFacing.UP;
            if (dx == 0 && dy == -1 && dz == 0) return BlockFacing.DOWN;
            if (dx == 0 && dy == 0 && dz == 1) return BlockFacing.SOUTH;
            if (dx == 0 && dy == 0 && dz == -1) return BlockFacing.NORTH;

            if (Math.Abs(dx) > Math.Abs(dy) && Math.Abs(dx) > Math.Abs(dz))
            {
                return dx > 0 ? BlockFacing.EAST : BlockFacing.WEST;
            }
            else if (Math.Abs(dy) > Math.Abs(dx) && Math.Abs(dy) > Math.Abs(dz))
            {
                return dy > 0 ? BlockFacing.UP : BlockFacing.DOWN;
            }
            else if (Math.Abs(dz) > Math.Abs(dx) && Math.Abs(dz) > Math.Abs(dy))
            {
                return dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
            }

            return null;
        }
        
        public void UpdateFilterSettings(FilterMode mode)
        {
            this.currentFilterMode = mode;
            MarkDirty();
        }
        
        public void UpdateConnections()
        {
            for (int i = 0; i < 6; i++)
            {
                connectedSides[i] = false;
                connectedPipes[i] = null;
            }
            
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);
                
                Block neighborBlock = Api?.World.BlockAccessor.GetBlock(checkPos);
                
                if (neighborBlock is BlockPipeBase)
                {
                    connectedSides[i] = true;
                    connectedPipes[i] = checkPos.Copy();
                    
                    // Обновляем соединение у соседа
                    if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BEPipe neighborPipe)
                    {
                        neighborPipe.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BEInsertionPipe neighborInserter)
                    {
                        neighborInserter.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BELiquidInsertionPipe neighborLiquidInserter)
                    {
                        neighborLiquidInserter.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                }
            }
            
            MarkDirty();
        }
        
        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos)
        {
            int index = side.Index;
            connectedSides[index] = true;
            connectedPipes[index] = fromPos.Copy();
            MarkDirty();
        }
        
        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            connectedSides[index] = false;
            connectedPipes[index] = null;
            MarkDirty();
        }
        
        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            UpdateConnections();
        }
        
        public override void OnBlockRemoved()
        {
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i] && connectedPipes[i] != null)
                {
                    if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BEPipe neighborPipe)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborPipe.BreakConnection(facing.Opposite);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BEInsertionPipe neighborInserter)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborInserter.BreakConnection(facing.Opposite);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BELiquidInsertionPipe neighborLiquidInserter)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborLiquidInserter.BreakConnection(facing.Opposite);
                    }
                }
            }
            
            networkManager?.RemovePipe(Pos);
            
            if (Api?.Side == EnumAppSide.Server)
            {
                Api.World.UnregisterGameTickListener(transferTimer);
            }
            
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog.TryClose();
            }
            
            base.OnBlockRemoved();
        }
        
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            int connections = 0;
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i]) connections++;
            }
            
            sb.AppendLine(Lang.Get("electricalprogressivetransport:connections", connections));
            if (networkManager != null)
            {
                var network = networkManager.GetNetwork(Pos);
                if (network != null)
                {
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:network-size", network.Pipes.Count));
                }
            }
            
            string modeText = currentFilterMode switch
            {
                FilterMode.AllowList => Lang.Get("electricalprogressivetransport:filter-mode-allow"),
                FilterMode.DenyList => Lang.Get("electricalprogressivetransport:filter-mode-deny"),
                _ => "Unknown"
            };
            sb.AppendLine(Lang.Get("electricalprogressivetransport:filter-mode", modeText));
            
            sb.AppendLine(Lang.Get("electricalprogressivetransport:liquid-transfer-rate", transferRate));
            
            int activeFilters = 0;
            for (int i = 0; i < _inventory.Count; i++)
            {
                if (!_inventory[i].Empty) activeFilters++;
            }
            sb.AppendLine(Lang.Get("electricalprogressivetransport:active-liquid-filters", activeFilters, _inventory.Count));
        }
        
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
    
            byte[] connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    connectedSides[i] = connBytes[i] == 1;
                }
            }
    
            transferRate = tree.GetInt("transferRate", 100);
            currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
            transferRate = Math.Max(10, Math.Min(transferRate, 1000));
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
    
            byte[] connBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                connBytes[i] = (byte)(connectedSides[i] ? 1 : 0);
            }
            tree.SetBytes("connections", connBytes);
    
            tree.SetInt("transferRate", transferRate);
            tree.SetInt("filterMode", (int)currentFilterMode);
        }
        
        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
    
            if (packetid == 2001)
            {
                if (_clientDialog != null && _clientDialog.IsOpened())
                {
                    _clientDialog.TryClose();
                }
            }
            else if (packetid == 2002)
            {
                using (var ms = new System.IO.MemoryStream(data))
                using (var br = new System.IO.BinaryReader(ms))
                {
                    FilterMode mode = (FilterMode)br.ReadInt32();
                    UpdateFilterSettings(mode);
                }
            }
            else if (packetid == 2003)
            {
                var tree = new TreeAttribute();
                tree.FromBytes(data);
                int newRate = tree.GetInt("transferRate", 100);
                newRate = Math.Max(10, Math.Min(newRate, 1000));
                
                if (newRate != transferRate)
                {
                    transferRate = newRate;
                    MarkDirty();
                    Api.World.BlockAccessor.MarkBlockDirty(Pos);
                }
            }
        }
        
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog.TryClose();
            }
        }
        
        // Тестовый метод для отладки
        public void DebugLiquidPipe()
        {
            Api.Logger.Notification($"=== ДЕБАГ ЖИДКОСТНОЙ ТРУБЫ НА {Pos} ===");
            
            if (outputFacing != null)
            {
                BlockPos targetPos = Pos.AddCopy(outputFacing);
                Api.Logger.Notification($"=== Выходное направление: {outputFacing.Code} -> {targetPos} ===");
                
                BlockEntity target = Api.World.BlockAccessor.GetBlockEntity(targetPos);
                if (target != null)
                {
                    Api.Logger.Notification($"=== Целевой блок: {target.GetType().Name} ===");
                    
                    if (target is BlockEntityContainer container)
                    {
                        for (int i = 0; i < container.Inventory.Count; i++)
                        {
                            ItemSlot slot = container.Inventory[i];
                            if (slot is ItemSlotLiquidOnly)
                            {
                                string status = slot.Empty ? "пустой" : $"содержит {slot.Itemstack?.Collectible?.Code}";
                                Api.Logger.Notification($"=== Жидкостной слот #{i}: {status} ===");
                            }
                        }
                    }
                }
            }
            else
            {
                Api.Logger.Notification($"=== Выходное направление не определено ===");
            }
        }
    }
    
    public class InventoryLiquidInsertionPipe : InventoryGeneric
    {
        private BELiquidInsertionPipe _entity;
    
        public InventoryLiquidInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BELiquidInsertionPipe entity)
            : base(slots, className, instanceID, api)
        {
            _entity = entity;
        }
    
        private static ItemSlot CreateLiquidFilterSlot(int slotId, InventoryBase inventory)
        {
            return new LiquidFilterSlot(inventory);
        }
    
        protected override ItemSlot NewSlot(int i)
        {
            return CreateLiquidFilterSlot(i, this);
        }
    }
    
public class LiquidFilterSlot : ItemSlotWatertight
{
    public LiquidFilterSlot(InventoryBase inventory) : base(inventory, 1000f) // Большая емкость для фильтра
    {
        // capacityLitres уже установлен в базовом конструкторе
    }
    
    public override int MaxSlotStackSize => 1; // В фильтре только 1 предмет
    
    // Переопределяем CanHold для приема только жидкостей
    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.Empty)
            return true; // Всегда можно очистить слот
        
        ItemStack sourceStack = sourceSlot.Itemstack;
        if (sourceStack == null || sourceStack.Collectible == null)
            return false;
        
        // 1. Проверяем через IsLiquid() - самый прямой способ
        if (sourceStack.Collectible.IsLiquid())
            return true;
        
        // 2. Проверяем, является ли это BlockLiquidContainerBase (ведра)
        if (sourceStack.Block is BlockLiquidContainerBase)
            return true;
        
        // 3. Проверяем атрибуты контейнера с жидкостью
        if (sourceStack.ItemAttributes != null)
        {
            // Контейнеры с жидкостью имеют contentItemCode
            if (sourceStack.ItemAttributes["contentItemCode"].Exists)
            {
                string contentCode = sourceStack.ItemAttributes["contentItemCode"].AsString();
                if (!string.IsNullOrEmpty(contentCode))
                {
                    // Получаем предмет содержимого
                    AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceStack.Collectible.Code.Domain);
                    Item contentItem = this.inventory.Api.World.GetItem(contentAsset);
                    
                    if (contentItem != null && contentItem.IsLiquid())
                    {
                        return true;
                    }
                }
                return true; // Даже если не смогли проверить - предполагаем что это жидкость
            }
            
            // Или contentItem2BlockCodes (для бутылок)
            if (sourceStack.ItemAttributes["contentItem2BlockCodes"].Exists)
                return true;
                
            // Проверяем атрибут containerType на "liquid" или "portion"
            if (sourceStack.ItemAttributes["containerType"].Exists)
            {
                string containerType = sourceStack.ItemAttributes["containerType"].AsString();
                if (containerType?.ToLower() == "liquid" || containerType?.ToLower() == "portion")
                    return true;
            }
            
            // Проверяем атрибут liquidProps
            if (sourceStack.ItemAttributes["liquidProps"].Exists)
                return true;
        }
        
        // 5. Проверяем через ItemLadle (черпаки)
        if (sourceStack.Collectible.Code?.Path?.Contains("ladle") == true)
            return true;
        
        // 6. Для предметов с атрибутом "content" - предполагаем жидкость
        if (sourceStack.Attributes?.HasAttribute("content") == true)
            return true;
        
        // 7. Ни один из проверенных способов не подтвердил что это жидкость - НЕ допускаем
        return false;
    }
    
    // Переопределяем CanTake - в фильтре предметы нельзя брать обычным способом
    public override bool CanTake()
    {
        // В фильтре предметы можно брать только правым кликом для очистки
        return false;
    }
    
    // Переопределяем ActivateSlot для специальной обработки
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем пустой рукой - очищаем слот
        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (!this.Empty)
            {
                this.Itemstack = null;
                this.MarkDirty();
                op.MovedQuantity = 1;
            }
            return;
        }
        
        // Сначала проверяем, можно ли вообще положить этот предмет
        if (!CanHold(sourceSlot))
        {
            // Не жидкость - ничего не делаем
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }
        
        ItemStack sourceStack = sourceSlot.Itemstack;
        
        // Если пытаемся положить контейнер с жидкостью (ведро)
        if (sourceStack.Block is BlockLiquidContainerBase block)
        {
            HandleLiquidContainer(sourceSlot, block, ref op);
            return;
        }
        
        // Если предмет имеет contentItemCode (бутылки и т.д.)
        if (sourceStack.ItemAttributes?["contentItemCode"].Exists == true)
        {
            HandleContentItem(sourceSlot, ref op);
            return;
        }
        
        // Если это жидкость (portion)
        string itemCode = sourceStack.Collectible.Code?.ToString() ?? "";
        if (itemCode.ToLower().Contains("portion") || sourceStack.Collectible.IsLiquid())
        {
            HandleLiquidItem(sourceSlot, ref op);
            return;
        }
        
        // Для других разрешенных жидкостей - стандартная логика с ограничением количества
        if (this.Empty)
        {
            // Берем только 1 предмет
            this.Itemstack = sourceStack.Clone();
            this.Itemstack.StackSize = 1;
            
            if (sourceSlot.StackSize == 1)
            {
                sourceSlot.Itemstack = null;
            }
            else
            {
                sourceSlot.Itemstack.StackSize -= 1;
            }
            
            sourceSlot.MarkDirty();
            this.MarkDirty();
            
            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else
        {
            // Заменяем содержимое слота
            ItemStack temp = this.Itemstack;
            this.Itemstack = sourceStack.Clone();
            this.Itemstack.StackSize = 1;
            
            sourceSlot.Itemstack = temp;
            if (sourceSlot.Itemstack != null)
            {
                sourceSlot.Itemstack.StackSize = Math.Min(sourceSlot.Itemstack.StackSize, sourceSlot.MaxSlotStackSize);
            }
            
            sourceSlot.MarkDirty();
            this.MarkDirty();
            
            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
    }
    
    // Обрабатывает контейнер с жидкостью (ведро)
    private void HandleLiquidContainer(ItemSlot containerSlot, BlockLiquidContainerBase block, ref ItemStackMoveOperation op)
    {
        // Если слот фильтра пустой
        if (this.Empty)
        {
            // Создаем копию содержимого ведра
            ItemStack content = block.GetContent(containerSlot.Itemstack);
            if (content != null)
            {
                // Клонируем содержимое ведра
                this.Itemstack = content.Clone();
                this.Itemstack.StackSize = 1;
                this.MarkDirty();
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // Ведро пустое - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
            }
        }
        else
        {
            // Если в слоте уже есть что-то, заменяем
            ItemStack temp = this.Itemstack;
            
            ItemStack content = block.GetContent(containerSlot.Itemstack);
            if (content != null)
            {
                this.Itemstack = content.Clone();
                this.Itemstack.StackSize = 1;
            }
            else
            {
                this.Itemstack = null;
            }
            
            this.MarkDirty();
            
            // Возвращаем старую жидкость в контейнер
            if (temp != null)
            {
                // Пытаемся положить обратно
                containerSlot.Itemstack = temp;
                containerSlot.Itemstack.StackSize = 1;
                containerSlot.MarkDirty();
            }
            
            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
    }
    
    // Обрабатывает предметы с contentItemCode (бутылки и т.д.)
    private void HandleContentItem(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        IWorldAccessor world = this.inventory.Api.World;
        
        // Получаем код содержимого из атрибутов
        string contentCode = sourceSlot.Itemstack.ItemAttributes["contentItemCode"].AsString();
        if (contentCode == null)
        {
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }
        
        AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceSlot.Itemstack.Collectible.Code.Domain);
        Item contentItem = world.GetItem(contentAsset);
        
        if (contentItem == null)
        {
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }
        
        // Создаем стек содержимого
        ItemStack contentStack = new ItemStack(contentItem);
        
        if (this.Empty)
        {
            // Кладем содержимое в слот фильтра
            this.Itemstack = contentStack;
            this.Itemstack.StackSize = 1;
            this.MarkDirty();
            
            // Создаем пустой контейнер
            string emptiedBlockCode = sourceSlot.Itemstack.ItemAttributes["emptiedBlockCode"].AsString();
            if (emptiedBlockCode != null)
            {
                AssetLocation emptiedAsset = AssetLocation.Create(emptiedBlockCode, sourceSlot.Itemstack.Collectible.Code.Domain);
                Block emptiedBlock = world.GetBlock(emptiedAsset);
                
                if (emptiedBlock != null)
                {
                    ItemStack emptiedStack = new ItemStack(emptiedBlock);
                    
                    if (sourceSlot.StackSize == 1)
                    {
                        sourceSlot.Itemstack = emptiedStack;
                    }
                    else
                    {
                        sourceSlot.Itemstack.StackSize -= 1;
                        if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(emptiedStack))
                        {
                            world.SpawnItemEntity(emptiedStack, op.ActingPlayer.Entity.Pos.XYZ);
                        }
                    }
                    sourceSlot.MarkDirty();
                }
            }
            
            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else
        {
            // Заменяем содержимое слота
            ItemStack temp = this.Itemstack;
            this.Itemstack = contentStack;
            this.Itemstack.StackSize = 1;
            this.MarkDirty();
            
            // Возвращаем старую жидкость
            if (temp != null)
            {
                sourceSlot.Itemstack = temp;
                sourceSlot.Itemstack.StackSize = 1;
                sourceSlot.MarkDirty();
            }
            
            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
    }
    
    // Обрабатывает предметы жидкости (waterportion и т.д.)
    private void HandleLiquidItem(ItemSlot liquidSlot, ref ItemStackMoveOperation op)
    {
        if (this.Empty)
        {
            // Берем 1 предмет жидкости
            this.Itemstack = liquidSlot.Itemstack.Clone();
            this.Itemstack.StackSize = 1;
            
            if (liquidSlot.StackSize == 1)
            {
                liquidSlot.Itemstack = null;
            }
            else
            {
                liquidSlot.Itemstack.StackSize -= 1;
            }
            
            liquidSlot.MarkDirty();
            this.MarkDirty();
            
            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else if (this.Itemstack != null && liquidSlot.Itemstack != null)
        {
            // Проверяем, та же ли это жидкость
            if (AreLiquidsEqual(this.Itemstack, liquidSlot.Itemstack))
            {
                // Та же жидкость - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
            }
            else
            {
                // Разная жидкость - заменяем
                ItemStack temp = this.Itemstack;
                this.Itemstack = liquidSlot.Itemstack.Clone();
                this.Itemstack.StackSize = 1;
                
                liquidSlot.Itemstack = temp;
                if (liquidSlot.Itemstack != null)
                {
                    liquidSlot.Itemstack.StackSize = 1;
                }
                
                liquidSlot.MarkDirty();
                this.MarkDirty();
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
        }
    }
    
    // Проверяет, одинаковые ли жидкости
    private bool AreLiquidsEqual(ItemStack stack1, ItemStack stack2)
    {
        if (stack1 == null || stack2 == null)
            return false;
        
        // Сравниваем коды предметов
        if (!stack1.Collectible.Code.Equals(stack2.Collectible.Code))
            return false;
        
        // Для контейнеров с жидкостью сравниваем содержимое
        if (stack1.Attributes.HasAttribute("content") && stack2.Attributes.HasAttribute("content"))
        {
            string content1 = stack1.Attributes.GetString("content", "");
            string content2 = stack2.Attributes.GetString("content", "");
            return content1 == content2;
        }
        
        return true;
    }
    
    // Обработка правого клика для очистки слота
    protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем правой кнопкой с пустой рукой - очищаем слот
        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (!this.Empty)
            {
                this.Itemstack = null;
                this.MarkDirty();
                op.MovedQuantity = 1;
            }
            return;
        }
        
        // Для предметов с жидкостью используем специальную логику
        if (sourceSlot.Itemstack?.Block is BlockLiquidContainerBase ||
            sourceSlot.Itemstack?.ItemAttributes?["contentItemCode"].Exists == true ||
            sourceSlot.Itemstack?.Collectible.IsLiquid() == true)
        {
            // Используем левый клик для жидкостей
            ActivateSlot(sourceSlot, ref op);
            return;
        }
        
        // Для не-жидкостей - ничего не делаем
        op.MovedQuantity = 0;
        op.RequestedQuantity = 0;
    }
    
    // Переопределяем TryFlipWith для ограничения количества
    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        // Сначала проверяем, можно ли вообще поместить этот предмет
        if (!CanHold(itemSlot))
            return false;
            
        if (itemSlot != null && itemSlot.StackSize > 1)
        {
            // Если пытаются положить больше 1 предмета
            if (!this.Empty)
            {
                // Если слот не пуст, нельзя обменять
                return false;
            }
            
            // Берем только 1 предмет
            ItemStack singleStack = itemSlot.Itemstack.Clone();
            singleStack.StackSize = 1;
            this.Itemstack = singleStack;
            
            itemSlot.Itemstack.StackSize -= 1;
            if (itemSlot.Itemstack.StackSize <= 0)
                itemSlot.Itemstack = null;
                
            itemSlot.MarkDirty();
            this.MarkDirty();
            return true;
        }
        
        // Для 1 предмета - обычный обмен
        return base.TryFlipWith(itemSlot);
    }
    
    // Гарантируем, что в слоте не больше 1 предмета
    public override void OnItemSlotModified(ItemStack extractedStack = null)
    {
        if (!this.Empty && this.Itemstack.StackSize > 1)
        {
            this.Itemstack.StackSize = 1;
        }
        base.OnItemSlotModified(extractedStack);
    }
    

}
}