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
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);

                // Проверяем БЛОК на интерфейсы жидкостей
                Block checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
        
                if (checkBlock is ILiquidSink)
                {
                    // Блок реализует интерфейс приемника жидкостей
                    outputFacing = facing;
            
                    // Отладочная информация
                    Api.Logger.Notification($"=== Найден ILiquidSink: {facing.Code} -> {checkPos} ===");
                    Api.Logger.Notification($"=== Тип блока: {checkBlock.GetType().Name} ===");
            
                    // Проверим конкретный тип
                    if (checkBlock is BlockLiquidContainerBase liquidContainer)
                    {
                        Api.Logger.Notification($"=== Это BlockLiquidContainerBase, емкость: {liquidContainer.CapacityLitres}л ===");
                    }
            
                    return;
                }
            }
            outputFacing = null;
        }
        
        private void OnTransferTick(float dt)
        {
            debugCounter++;
    
            if (Api == null)
            {
                Api.Logger.Notification($"=== Api is null! ===");
                return;
            }
    
            if (networkManager == null)
            {
                Api.Logger.Notification($"=== networkManager is null! ===");
                return;
            }

            Api.Logger.Notification($"=== OnTransferTick #{debugCounter} на позиции {Pos} ===");

            // Обновляем направление вывода
            if (outputFacing == null || debugCounter % 20 == 0)
            {
                DetermineOutputDirection();
            }

            if (outputFacing == null)
            {
                Api.Logger.Notification($"=== outputFacing is null, пропускаем тик ===");
                return;
            }

            // Получаем целевой контейнер
            BlockPos targetPos = Pos.AddCopy(outputFacing);
            Api.Logger.Notification($"=== Целевая позиция: {targetPos} ===");
    
            Block targetBlock = Api.World.BlockAccessor.GetBlock(targetPos);
            Api.Logger.Notification($"=== Целевой блок: {targetBlock?.GetType().Name} ===");
    
            if (targetBlock is ILiquidSink)
            {
                Api.Logger.Notification($"=== Целевой блок реализует ILiquidSink ===");
            }
            else
            {
                Api.Logger.Notification($"=== Целевой блок НЕ реализует ILiquidSink! ===");
                outputFacing = null; // Сбросим направление
                return;
            }

            // Вызываем передачу напрямую через блок, а не через BlockEntity
            TryTransferLiquidToSinkDirect(targetBlock as ILiquidSink, targetPos);
        }
        
        // Новый метод: передача напрямую через блок
private void TryTransferLiquidToSinkDirect(ILiquidSink sink, BlockPos targetPos)
{
    if (sink == null) return;
    
    Api.Logger.Notification($"=== TryTransferLiquidToSinkDirect: цель на {targetPos} ===");
    
    // Используем сеть для поиска источников
    var network = networkManager.GetNetwork(Pos);
    if (network == null) 
    {
        Api.Logger.Notification($"=== СЕТЬ НЕ НАЙДЕНА ===");
        return;
    }

    Api.Logger.Notification($"=== Размер сети: {network.Pipes.Count} труб ===");
    
    // Собираем все позиции для исключения
    var excludePositions = new HashSet<BlockPos>();
    excludePositions.Add(Pos);
    excludePositions.Add(targetPos);

    int sourcesChecked = 0;
    
    // Ищем источник жидкости в сети
    foreach (var pipePos in network.Pipes)
    {
        if (excludePositions.Contains(pipePos)) continue;

        // Проверяем все стороны трубы
        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos sourcePos = pipePos.AddCopy(facing);

            if (excludePositions.Contains(sourcePos)) continue;
            
            // Пропускаем другие трубы
            Block sourceBlock = Api.World.BlockAccessor.GetBlock(sourcePos);
            if (sourceBlock is BlockPipeBase)
                continue;
                
            sourcesChecked++;
            Api.Logger.Notification($"=== Проверяем источник #{sourcesChecked}: {sourcePos} ===");
            Api.Logger.Notification($"=== Блок источника: {sourceBlock?.GetType().Name} ===");
            
            // Проверяем БЛОК на ILiquidSource
            if (sourceBlock is ILiquidSource liquidSource)
            {
                Api.Logger.Notification($"=== Блок источника реализует ILiquidSource! ===");
                
                // Пытаемся передать
                if (TryTransferFromBlockSourceToSink(sourcePos, liquidSource, sink, targetPos))
                {
                    Api.Logger.Notification($"=== Успешно перенесли жидкость из {sourcePos} ===");
                    return;
                }
            }
            else
            {
                Api.Logger.Notification($"=== Блок НЕ реализует ILiquidSource ===");
            }
        }
    }
    
    if (sourcesChecked == 0)
        Api.Logger.Notification($"=== НЕ НАЙДЕНО НИ ОДНОГО ИСТОЧНИКА ===");
}
        
// Новый метод: передача от блока-источника к приемнику
        private bool TryTransferFromBlockSourceToSink(BlockPos sourcePos, ILiquidSource source, ILiquidSink sink,
            BlockPos targetPos)
        {
            // Проверяем тайминг
            if (!CanTransferFrom(sourcePos))
            {
                Api.Logger.Notification($"=== Тайминг не позволяет передачу из {sourcePos} ===");
                return false;
            }

            Api.Logger.Notification($"=== TryTransferFromBlockSourceToSink ===");
            Api.Logger.Notification($"=== Источник (блок): {source.GetType().Name} на {sourcePos} ===");
            Api.Logger.Notification($"=== Приемник: {sink.GetType().Name} на {targetPos} ===");

            // Получаем содержимое из источника
            var contentStack = source.GetContent(sourcePos);
            if (contentStack == null)
            {
                Api.Logger.Notification($"=== Источник пуст (GetContent вернул null) ===");
                return false;
            }

            Api.Logger.Notification(
                $"=== Содержимое источника: {contentStack.Collectible?.Code}, StackSize: {contentStack.StackSize} ===");

            // Проверяем фильтр
            if (!CheckItemAgainstLiquidFilter(contentStack))
            {
                Api.Logger.Notification($"=== Жидкость не прошла фильтр ===");
                return false;
            }

            // Вычисляем количество для передачи (в литрах)
            float currentLitres = source.GetCurrentLitres(sourcePos);
            Api.Logger.Notification($"=== Текущее количество в источнике: {currentLitres} литров ===");

            float litresToTransfer = Math.Min(transferRate / 1000f, currentLitres);
            if (litresToTransfer <= 0)
            {
                Api.Logger.Notification($"=== Недостаточно жидкости для передачи ===");
                return false;
            }

            Api.Logger.Notification($"=== Пытаемся передать {litresToTransfer} литров жидкости ===");

            // Пытаемся передать жидкость
            int movedItems = sink.TryPutLiquid(targetPos, contentStack, litresToTransfer);

            Api.Logger.Notification($"=== TryPutLiquid вернул: {movedItems} единиц ===");

            if (movedItems > 0)
            {
                // Забираем переданное количество из источника
                ItemStack takenStack = source.TryTakeContent(sourcePos, movedItems);

                if (takenStack != null)
                    Api.Logger.Notification($"=== Изъято из источника: {takenStack.StackSize} единиц ===");

                lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;

                // Обновляем блоки
                Api.World.BlockAccessor.MarkBlockDirty(sourcePos);
                Api.World.BlockAccessor.MarkBlockDirty(targetPos);

                Api.Logger.Notification($"=== УСПЕХ: Передано {movedItems} единиц жидкости ===");
                return true;
            }
            else
            {
                Api.Logger.Notification($"=== НЕУДАЧА: TryPutLiquid вернул 0 ===");

                // Попробуем диагностику для приемника
                if (sink is BlockLiquidContainerBase container)
                {
                    BlockEntity targetBe = Api.World.BlockAccessor.GetBlockEntity(targetPos);
                    if (targetBe != null)
                    {
                        Api.Logger.Notification($"=== Сущность приемника: {targetBe.GetType().Name} ===");
                    }
                }
            }

            return false;
        }
        
        
        private bool CanTransferFrom(BlockPos sourcePos)
        {
            if (!lastTransferTime.ContainsKey(sourcePos))
                return true;
        
            long elapsed = Api.World.ElapsedMilliseconds - lastTransferTime[sourcePos];
            bool canTransfer = elapsed > MinTransferInterval;
    
            if (!canTransfer)
                Api.Logger.Notification($"=== Тайминг: {elapsed}мс из {MinTransferInterval}мс ===");
        
            return canTransfer;
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
                    
                    if (target is ILiquidSink liquidSink)
                    {
                        Api.Logger.Notification($"=== Реализует ILiquidSink ===");
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