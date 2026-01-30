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
                
                if (be != null && (be is ILiquidSink || HasLiquidSlot(be)))
                {
                    outputFacing = facing;
                    if (debugCounter % 5 == 0)
                    {
                        Api.Logger.Notification($"=== НАЙДЕН ЖИДКОСТНОЙ КОНТЕЙНЕР! {be.GetType().Name} на {checkPos} ===");
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
            
            if (be == null)
                return;
    
            // Пытаемся передать жидкость в контейнер
            TryTransferLiquidToContainer(be, containerPos);
        }
        
        private void TryTransferLiquidToContainer(BlockEntity targetBe, BlockPos targetPos)
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Пытаемся передать жидкость в контейнер на {targetPos} ===");
            
            // Если целевой блок реализует ILiquidSink
            if (targetBe is ILiquidSink liquidSink)
            {
                TransferToLiquidSink(liquidSink, targetPos);
                return;
            }
            
            // Если это BlockEntityContainer с жидкостными слотами
            if (targetBe is BlockEntityContainer container)
            {
                TransferToContainerSlots(container, targetPos);
                return;
            }
        }
        
        private void TransferToLiquidSink(ILiquidSink liquidSink, BlockPos targetPos)
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Цель реализует ILiquidSink ===");
            
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

            // Ищем источник жидкости в сети
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

                    // Проверяем, можно ли взять жидкость из этого источника
                    if (TryTransferFromSourceToSink(checkPos, liquidSink, targetPos))
                    {
                        if (debugCounter % 5 == 0)
                            Api.Logger.Notification($"=== Успешно перенесли жидкость из {checkPos} ===");
                        return;
                    }
                }
            }
        }
        
        private void TransferToContainerSlots(BlockEntityContainer container, BlockPos targetPos)
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Цель - BlockEntityContainer ===");
            
            IInventory targetInventory = container.Inventory;
            
            if (targetInventory == null) 
                return;
            
            // Используем сеть для поиска источников
            var network = networkManager.GetNetwork(Pos);
            if (network == null) 
                return;

            // Собираем все позиции для исключения
            var excludePositions = new HashSet<BlockPos>();
            excludePositions.Add(Pos);
            excludePositions.Add(targetPos);

            // Ищем источник жидкости в сети
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

                    // Проверяем, можно ли взять жидкость из этого источника
                    if (TryTransferFromSourceToContainerSlots(checkPos, container, targetPos))
                    {
                        if (debugCounter % 5 == 0)
                            Api.Logger.Notification($"=== Успешно перенесли жидкость из {checkPos} ===");
                        return;
                    }
                }
            }
        }
        
        private bool TryTransferFromSourceToSink(BlockPos sourcePos, ILiquidSink liquidSink, BlockPos targetPos)
        {
            // Проверяем тайминг
            if (!CanTransferFrom(sourcePos)) 
                return false;
            
            BlockEntity sourceBe = Api.World.BlockAccessor.GetBlockEntity(sourcePos);
            if (sourceBe == null) 
                return false;
            
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Проверяем источник: {sourceBe.GetType().Name} на {sourcePos} ===");
            
            // Проверяем, является ли источник ILiquidSource
            if (sourceBe is ILiquidSource liquidSource)
            {
                return TransferBetweenLiquidInterfaces(liquidSource, liquidSink, sourcePos, targetPos);
            }
            
            // Если источник - BlockEntityContainer с жидкостными контейнерами
            if (sourceBe is BlockEntityContainer sourceContainer)
            {
                return TransferFromContainerToSink(sourceContainer, liquidSink, sourcePos, targetPos);
            }
            
            return false;
        }
        
        private bool TryTransferFromSourceToContainerSlots(BlockPos sourcePos, BlockEntityContainer targetContainer, BlockPos targetPos)
        {
            // Проверяем тайминг
            if (!CanTransferFrom(sourcePos)) 
                return false;
            
            BlockEntity sourceBe = Api.World.BlockAccessor.GetBlockEntity(sourcePos);
            if (sourceBe == null) 
                return false;
            
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Проверяем источник: {sourceBe.GetType().Name} на {sourcePos} ===");
            
            // Если источник - ILiquidSource
            if (sourceBe is ILiquidSource liquidSource)
            {
                return TransferFromLiquidSourceToContainer(liquidSource, targetContainer, sourcePos, targetPos);
            }
            
            // Если источник - BlockEntityContainer с жидкостными контейнерами
            if (sourceBe is BlockEntityContainer sourceContainer)
            {
                return TransferBetweenContainers(sourceContainer, targetContainer, sourcePos, targetPos);
            }
            
            return false;
        }
        
        private bool TransferBetweenLiquidInterfaces(ILiquidSource source, ILiquidSink sink, BlockPos sourcePos, BlockPos targetPos)
        {
            // Получаем содержимое из источника
            var contentStack = source.GetContent(sourcePos);
            if (contentStack == null)
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Источник пуст ===");
                return false;
            }
    
            // Проверяем фильтр
            if (!CheckItemAgainstLiquidFilter(contentStack))
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Жидкость не прошла фильтр ===");
                return false;
            }
    
            // Вычисляем количество для передачи (в литрах)
            float litresToTransfer = Math.Min(transferRate / 1000f, source.GetCurrentLitres(sourcePos));
            if (litresToTransfer <= 0)
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Недостаточно жидкости для передачи ===");
                return false;
            }
    
            if (debugCounter % 5 == 0)
            {
                Api.Logger.Notification($"=== Пытаемся передать {litresToTransfer} литров жидкости {contentStack.Collectible?.Code} ===");
            }
    
            // Пытаемся передать жидкость
            int movedItems = sink.TryPutLiquid(targetPos, contentStack, litresToTransfer);
    
            if (movedItems > 0)
            {
                // Забираем переданное количество из источника
                ItemStack takenStack = source.TryTakeContent(sourcePos, movedItems);
        
                lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
                Api.World.BlockAccessor.GetBlockEntity(sourcePos)?.MarkDirty();
                Api.World.BlockAccessor.GetBlockEntity(targetPos)?.MarkDirty();
        
                Api.Logger.Notification($"=== УСПЕХ: Передано {movedItems} единиц жидкости ===");
                return true;
            }
    
            return false;
        }
        
        private bool TransferFromLiquidSourceToContainer(ILiquidSource source, BlockEntityContainer targetContainer, BlockPos sourcePos, BlockPos targetPos)
        {
            // Получаем содержимое из источника
            var contentStack = source.GetContent(sourcePos);
            if (contentStack == null)
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Источник пуст ===");
                return false;
            }
            
            // Проверяем фильтр
            if (!CheckItemAgainstLiquidFilter(contentStack))
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Жидкость не прошла фильтр ===");
                return false;
            }
            
            // Ищем подходящий слот в контейнере
            var targetSlot = FindSuitableLiquidSlot(targetContainer.Inventory, contentStack);
            if (targetSlot == null)
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Не найден подходящий слот в цели ===");
                return false;
            }
            
            // Вычисляем количество для передачи (в литрах)
            float litresToTransfer = Math.Min(transferRate / 1000f, source.GetCurrentLitres(sourcePos));
            if (litresToTransfer <= 0)
                return false;
            
            // Пытаемся передать жидкость
            int movedItems = 0;
            
            if (targetSlot is ItemSlotLiquidOnly liquidSlot)
            {
                // Передача в жидкостной слот
                movedItems = TransferToLiquidSlot(source, sourcePos, liquidSlot, litresToTransfer);
            }
            else if (targetSlot.Itemstack?.Block is BlockLiquidContainerBase containerBlock)
            {
                // Передача в жидкостной контейнер
                movedItems = TransferToLiquidContainer(source, sourcePos, targetSlot, containerBlock, litresToTransfer);
            }
            
            if (movedItems > 0)
            {
                lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
                Api.World.BlockAccessor.GetBlockEntity(sourcePos)?.MarkDirty();
                targetContainer.MarkDirty();
                targetSlot.MarkDirty();
                
                Api.Logger.Notification($"=== УСПЕХ: Передано {movedItems} единиц жидкости в контейнер ===");
                return true;
            }
            
            return false;
        }
        
        private bool TransferFromContainerToSink(BlockEntityContainer sourceContainer, ILiquidSink sink, BlockPos sourcePos, BlockPos targetPos)
        {
            // Ищем жидкость в контейнере-источнике
            var sourceSlot = FindLiquidSlot(sourceContainer.Inventory);
            if (sourceSlot == null || sourceSlot.Empty)
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== В источнике не найдена жидкость ===");
                return false;
            }
            
            var liquidStack = sourceSlot.Itemstack;
            
            // Проверяем фильтр
            if (!CheckItemAgainstLiquidFilter(liquidStack))
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Жидкость не прошла фильтр ===");
                return false;
            }
            
            // Вычисляем количество для передачи (в литрах)
            float litresToTransfer = transferRate / 1000f;
            
            if (debugCounter % 5 == 0)
            {
                Api.Logger.Notification($"=== Пытаемся передать {litresToTransfer} литров жидкости {liquidStack.Collectible?.Code} ===");
            }
            
            // Пытаемся передать жидкость в приемник
            int movedItems = sink.TryPutLiquid(targetPos, liquidStack, litresToTransfer);
            
            if (movedItems > 0)
            {
                // Забираем переданное количество из источника
                if (sourceSlot.Itemstack.StackSize == movedItems)
                {
                    sourceSlot.Itemstack = null;
                }
                else
                {
                    sourceSlot.Itemstack.StackSize -= movedItems;
                }
                
                sourceSlot.MarkDirty();
                sourceContainer.MarkDirty();
                lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
                Api.World.BlockAccessor.GetBlockEntity(targetPos)?.MarkDirty();
                
                Api.Logger.Notification($"=== УСПЕХ: Передано {movedItems} единиц жидкости ===");
                return true;
            }
            
            return false;
        }
        
private bool TransferBetweenContainers(BlockEntityContainer sourceContainer, BlockEntityContainer targetContainer, BlockPos sourcePos, BlockPos targetPos)
{
    // Ищем жидкость в контейнере-источнике
    var sourceSlot = FindLiquidSlot(sourceContainer.Inventory);
    if (sourceSlot == null || sourceSlot.Empty)
    {
        if (debugCounter % 5 == 0)
            Api.Logger.Notification($"=== В источнике не найдена жидкость ===");
        return false;
    }
    
    var liquidStack = sourceSlot.Itemstack;
    
    // Проверяем фильтр
    if (!CheckItemAgainstLiquidFilter(liquidStack))
    {
        if (debugCounter % 5 == 0)
            Api.Logger.Notification($"=== Жидкость не прошла фильтр ===");
        return false;
    }
    
    // Ищем подходящий слот в целевом контейнере
    var targetSlot = FindSuitableLiquidSlot(targetContainer.Inventory, liquidStack);
    if (targetSlot == null)
    {
        if (debugCounter % 5 == 0)
            Api.Logger.Notification($"=== Не найден подходящий слот в цели ===");
        return false;
    }
    
    // Вычисляем количество для передачи
    int maxAmount = Math.Min(transferRate, sourceSlot.StackSize);
    if (maxAmount <= 0)
        return false;
    
    if (debugCounter % 5 == 0)
    {
        Api.Logger.Notification($"=== Пытаемся передать {maxAmount} единиц жидкости {liquidStack.Collectible?.Code} ===");
    }
    
    // Простая логика переноса
    int transferred = 0;
    
    if (targetSlot is ItemSlotLiquidOnly liquidTargetSlot)
    {
        // Для жидкостных слотов
        if (liquidTargetSlot.Empty)
        {
            liquidTargetSlot.Itemstack = liquidStack.Clone();
            liquidTargetSlot.Itemstack.StackSize = maxAmount;
            transferred = maxAmount;
        }
        else if (liquidTargetSlot.Itemstack.Collectible.Code.Equals(liquidStack.Collectible.Code))
        {
            int availableSpace = liquidTargetSlot.MaxSlotStackSize - liquidTargetSlot.StackSize;
            transferred = Math.Min(maxAmount, availableSpace);
            liquidTargetSlot.Itemstack.StackSize += transferred;
        }
    }
    else
    {
        // Для обычных слотов
        ItemStackMoveOperation op = new ItemStackMoveOperation(
            Api.World,
            EnumMouseButton.Left,
            0,
            EnumMergePriority.DirectMerge,
            maxAmount
        );
        
        transferred = sourceSlot.TryPutInto(targetSlot, ref op);
    }
    
    if (transferred > 0)
    {
        // Убираем переданное количество из источника
        sourceSlot.Itemstack.StackSize -= transferred;
        if (sourceSlot.Itemstack.StackSize <= 0)
        {
            sourceSlot.Itemstack = null;
        }
        
        sourceSlot.MarkDirty();
        targetSlot.MarkDirty();
        sourceContainer.MarkDirty();
        targetContainer.MarkDirty();
        lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
        
        Api.Logger.Notification($"=== УСПЕХ: Передано {transferred} единиц жидкости ===");
        return true;
    }
    
    return false;
}

        private int TransferToLiquidSlot(ILiquidSource source, BlockPos sourcePos, ItemSlotLiquidOnly targetSlot, float litresToTransfer)
        {
            if (Api == null) return 0;
    
            // Получаем содержимое из источника
            var contentStack = source.GetContent(sourcePos);
            if (contentStack == null)
                return 0;
    
            // Вычисляем количество в единицах предмета
            var props = GetLiquidProps(contentStack);
            int itemsToTransfer = (int)(litresToTransfer * (props?.ItemsPerLitre ?? 1));
    
            if (itemsToTransfer <= 0)
                return 0;
    
            // Ограничиваем максимальное количество
            itemsToTransfer = Math.Min(itemsToTransfer, contentStack.StackSize);
    
            // Берем жидкость из источника
            ItemStack takenStack = source.TryTakeContent(sourcePos, itemsToTransfer);
    
            if (takenStack != null && takenStack.StackSize > 0)
            {
                int transferred = takenStack.StackSize;
        
                // Простая логика добавления жидкости в слот
                if (targetSlot.Empty)
                {
                    // Просто кладем в пустой слот
                    targetSlot.Itemstack = takenStack;
                    targetSlot.MarkDirty();
                    return transferred;
                }
                else if (targetSlot.Itemstack.Collectible.Code.Equals(takenStack.Collectible.Code))
                {
                    // Добавляем к существующей жидкости
                    targetSlot.Itemstack.StackSize += transferred;
                    targetSlot.MarkDirty();
                    return transferred;
                }
            }
    
            return 0;
        }

private int TransferToLiquidContainer(ILiquidSource source, BlockPos sourcePos, ItemSlot containerSlot, BlockLiquidContainerBase containerBlock, float litresToTransfer)
{
    // Получаем содержимое из источника
    var contentStack = source.GetContent(sourcePos);
    if (contentStack == null)
        return 0;
    
    // Создаем копию для передачи
    var liquidStack = contentStack.Clone();
    
    // Передаем жидкость в контейнер
    int transferred = containerBlock.TryPutLiquid(containerSlot.Itemstack, liquidStack, litresToTransfer);
    
    if (transferred > 0)
    {
        // Забираем переданное количество из источника
        source.TryTakeContent(sourcePos, transferred);
        containerSlot.MarkDirty();
        return transferred;
    }
    
    return 0;
}

// Вспомогательный метод для создания временного ItemSlot с содержимым
        private ItemSlot sourceSlotWithContent(ILiquidSource source, ItemStack content)
        {
            if (content == null) return null;

            // Создаем временный слот с содержимым источника
            var tempSlot = new DummySlot(content.Clone());
            return tempSlot;
        }
        

        private WaterTightContainableProps GetLiquidProps(ItemStack stack)
        {
            if (stack == null) return null;
    
            // 1. Пытаемся получить свойства из атрибутов предмета
            JsonObject obj = stack.ItemAttributes?["waterTightContainerProps"];
            if (obj != null && obj.Exists) 
                return obj.AsObject<WaterTightContainableProps>(null, stack.Collectible.Code.Domain);
    
            // 2. Если это жидкость из источника, она может быть BlockLiquidContainerBase
            if (stack.Block is BlockLiquidContainerBase containerBlock)
            {
                // Получаем свойства содержимого
                var content = containerBlock.GetContent(stack);
                if (content != null)
                {
                    JsonObject contentObj = content.ItemAttributes?["waterTightContainerProps"];
                    if (contentObj != null && contentObj.Exists)
                        return contentObj.AsObject<WaterTightContainableProps>(null, content.Collectible.Code.Domain);
                }
            }
    
            // 3. По умолчанию: 1 предмет = 1 литр
            return new WaterTightContainableProps { ItemsPerLitre = 1 };
        }
        

        private bool CanTransferFrom(BlockPos sourcePos)
        {
            if (!lastTransferTime.ContainsKey(sourcePos))
                return true;
                
            long elapsed = Api.World.ElapsedMilliseconds - lastTransferTime[sourcePos];
            return elapsed > MinTransferInterval;
        }
        
        // Находит жидкостной слот в инвентаре
        private ItemSlotLiquidOnly FindLiquidSlot(IInventory inventory)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot is ItemSlotLiquidOnly liquidSlot && !liquidSlot.Empty)
                {
                    if (debugCounter % 5 == 0)
                    {
                        Api.Logger.Notification($"=== Найден жидкостной слот #{i}: содержит {liquidSlot.Itemstack?.Collectible?.Code} ===");
                    }
                    return liquidSlot;
                }
            }
            
            return null;
        }
        
// Находит подходящий слот для жидкости в инвентаре
private ItemSlot FindSuitableLiquidSlot(IInventory inventory, ItemStack liquidStack)
{
    if (Api == null || liquidStack == null) return null;
    
    for (int i = 0; i < inventory.Count; i++)
    {
        ItemSlot slot = inventory[i];
        if (slot == null) continue;
        
        // Проверяем разные типы слотов
        if (slot is ItemSlotLiquidOnly liquidSlot)
        {
            // Проверяем, может ли этот слот принять жидкость
            // Простая проверка: если слот пустой или содержит ту же жидкость
            if (liquidSlot.Empty)
            {
                // Пустой слот всегда может принять жидкость (CanHold проверит тип)
                return liquidSlot;
            }
            else if (liquidSlot.Itemstack != null && 
                     liquidSlot.Itemstack.Collectible.Code.Equals(liquidStack.Collectible.Code))
            {
                // Слот уже содержит ту же жидкость
                return liquidSlot;
            }
        }
        else if (!slot.Empty && slot.Itemstack.Block is BlockLiquidContainerBase)
        {
            // Проверяем, можно ли добавить жидкость в контейнер
            var containerBlock = slot.Itemstack.Block as BlockLiquidContainerBase;
            var currentContent = containerBlock.GetContent(slot.Itemstack);
            
            if (currentContent == null || 
                currentContent.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes))
            {
                // Есть место для добавления жидкости
                float currentLitres = containerBlock.GetCurrentLitres(slot.Itemstack);
                if (currentLitres < containerBlock.CapacityLitres)
                {
                    if (debugCounter % 5 == 0)
                    {
                        Api.Logger.Notification($"=== Найден жидкостной контейнер #{i}: {containerBlock.Code}, заполнен на {currentLitres}/{containerBlock.CapacityLitres} литров ===");
                    }
                    return slot;
                }
            }
        }
        else if (slot.Empty && slot is ItemSlotLiquidOnly)
        {
            // Пустой жидкостной слот
            if (debugCounter % 5 == 0)
            {
                Api.Logger.Notification($"=== Найден пустой ItemSlotLiquidOnly #{i} ===");
            }
            return slot;
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
                            else if (!slot.Empty && slot.Itemstack.Block is BlockLiquidContainerBase)
                            {
                                string status = slot.Empty ? "пустой" : $"содержит {slot.Itemstack?.Collectible?.Code}";
                                Api.Logger.Notification($"=== Жидкостной контейнер #{i}: {status} ===");
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