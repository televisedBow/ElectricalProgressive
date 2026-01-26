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
    public class BEInsertionPipe : BlockEntityGenericTypedContainer
    {
        private long transferTimer;
        private int transferRate = 1;
        private BlockFacing outputFacing = null; // Направление вывода
        private int debugCounter = 0;
        
        protected bool[] connectedSides = new bool[6];
        protected BlockPos?[] connectedPipes = new BlockPos?[6];
        protected PipeNetworkManager networkManager;
        
        // Собственный инвентарь (фильтры)
        internal InventoryInsertionPipe _inventory;
        private GuiDialogInsertionPipe _clientDialog;
        
        // Режимы работы фильтра
        public enum FilterMode
        {
            AllowList = 0,    // Разрешать только указанные предметы
            DenyList = 1,     // Запрещать указанные предметы
        }
        
        private FilterMode currentFilterMode = FilterMode.AllowList;
        private bool matchMod = false; // Совпадать по мод-идентификатору
        private bool matchType = true; // Совпадать по типу предмета
        private bool matchAttributes = false; // Совпадать по атрибутам
        
        // Тайминги для предотвращения спама
        private Dictionary<BlockPos, long> lastTransferTime = new Dictionary<BlockPos, long>();
        private const long MinTransferInterval = 500; // 500 мс между переносами
        
        // Реализация свойств BlockEntityContainer
        public override InventoryBase Inventory => _inventory;
        public override string InventoryClassName => "insertionpipe";
        
        public int TransferRate => transferRate;
        public bool[] ConnectedSides => connectedSides;
        public FilterMode CurrentFilterMode => currentFilterMode;
        public bool MatchMod => matchMod;
        public bool MatchType => matchType;
        public bool MatchAttributes => matchAttributes;
        
        public BEInsertionPipe()
        {
            // 12 слотов для фильтров (6x2 в GUI)
            _inventory = new InventoryInsertionPipe(12, InventoryClassName, null, null, this);
        }
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            
            api.Logger.Notification($"=== Фильтрующая труба Initialize на {Pos} ===");
            
            // Определяем направление вывода
            DetermineOutputDirection();
            
            // Регистрируем трубу в сети
            networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
            networkManager?.AddPipe(Pos, this);
            
            UpdateConnections();
            
            if (api.Side == EnumAppSide.Server)
            {
                api.Logger.Notification($"=== Регистрируем таймер на {Pos} ===");
                transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 1000);
            }
        }
        
        // Открытие GUI при клике ПКМ
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
                _clientDialog = new GuiDialogInsertionPipe(
                    Lang.Get("electricalprogressivetransport:filter-pipe-title"),
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
        
        // Определяем направление, куда будем выводить предметы (КАК У ЖЕЛОБА!)
        private void DetermineOutputDirection()
        {
            if (Api == null) return;
    
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Определяем вывод для {Pos} ===");
    
            // Ищем контейнер в соседних блоках (как желоб)
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);
        
                // Пропускаем позиции с трубами
                Block checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
                if (checkBlock is BlockPipeBase)
                {
                    continue;
                }
        
                // Используем ТОЧНО ТАКОЙ ЖЕ подход как желоб!
                BlockEntityContainer container = checkBlock.GetBlockEntity<BlockEntityContainer>(checkPos);
                
                if (container != null)
                {
                    outputFacing = facing;
                    if (debugCounter % 5 == 0)
                    {
                        Api.Logger.Notification($"=== НАЙДЕН КОНТЕЙНЕР! Направление: {facing.Code} на {checkPos} ===");
                        Api.Logger.Notification($"=== Тип контейнера: {container.GetType().Name} ===");
                    }
                    return;
                }
            }
    
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Контейнер не найден для {Pos} ===");
            outputFacing = null;
        }
        
        // Основной метод для получения инвентаря из позиции (КАК У ЖЕЛОБА!)
        private IInventory GetInventoryAt(BlockPos pos)
        {
            // Используем ТОЧНО ТАКОЙ ЖЕ подход как желоб!
            Block block = Api.World.BlockAccessor.GetBlock(pos);
            
            // 1. Пробуем получить BlockEntityContainer через блок (как желоб)
            BlockEntityContainer container = block.GetBlockEntity<BlockEntityContainer>(pos);
            if (container != null)
            {
                if (debugCounter % 10 == 0)
                    Api.Logger.Notification($"=== Получен BlockEntityContainer из {container.GetType().Name} на {pos} ===");
                return container.Inventory;
            }
            
            return null;
        }
        
        // Получаем инвентарь из BlockEntity (для источников)
        private IInventory GetInventoryFromBlockEntity(BlockEntity be)
        {
            // 1. BlockEntityContainer (самый надежный)
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
        
        // Проверяет, проходит ли предмет через фильтр
        public bool CheckItemAgainstFilter(ItemStack itemstack)
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
                    bool matches = CheckItemMatchesFilter(itemstack, filterSlot.Itemstack);
                    if (matches)
                    {
                        hasMatchingFilter = true;
                        break;
                    }
                }
            }
            
            // Возвращаем результат в зависимости от режима
            return currentFilterMode == FilterMode.AllowList ? hasMatchingFilter : !hasMatchingFilter;
        }
        
        // Проверяет соответствие предмета фильтру
        private bool CheckItemMatchesFilter(ItemStack item, ItemStack filter)
        {
            if (item == null || filter == null || item.Collectible == null || filter.Collectible == null)
                return false;
            
            AssetLocation itemCode = item.Collectible.Code;
            AssetLocation filterCode = filter.Collectible.Code;
            
            Api?.Logger?.Debug($"Проверка: {itemCode} против {filterCode}");
            Api?.Logger?.Debug($"Настройки: Mod={matchMod}, Type={matchType}, Attrs={matchAttributes}");
            
            // 1. Проверка по мод-идентификатору (домену)
            if (matchMod)
            {
                if (itemCode?.Domain != filterCode?.Domain)
                {
                    Api?.Logger?.Debug($"Не совпадает мод: {itemCode?.Domain} != {filterCode?.Domain}");
                    return false;
                }
            }
            
            // 2. Проверка по типу (КЛЮЧЕВОЕ ИЗМЕНЕНИЕ!)
            if (matchType)
            {
                // Разбиваем путь на части
                string itemPath = itemCode?.Path ?? "";
                string filterPath = filterCode?.Path ?? "";
                
                // Сравниваем только первую часть пути (например "crushed", "ingot", "plate")
                string[] itemParts = itemPath.Split('-');
                string[] filterParts = filterPath.Split('-');
                
                if (itemParts.Length == 0 || filterParts.Length == 0)
                    return false;
                    
                // Сравниваем базовую часть (например "crushed" в "crushed-copper" и "crushed-iron")
                if (itemParts[0] != filterParts[0])
                {
                    Api?.Logger?.Debug($"Не совпадает тип: {itemParts[0]} != {filterParts[0]}");
                    return false;
                }
                
                // Если есть вторая часть и она не пустая
                if (itemParts.Length > 1 && filterParts.Length > 1)
                {
                    // Здесь можно добавить дополнительную логику
                    // Например, если обе части содержат материал
                }
            }
            
            // 3. Проверка по атрибутам/вариантам
            if (matchAttributes)
            {
                try
                {
                    // Сравниваем варианты
                    if (item.Collectible.Variant != null && filter.Collectible.Variant != null)
                    {
                        foreach (var key in filter.Collectible.Variant.Keys)
                        {
                            if (item.Collectible.Variant.ContainsKey(key))
                            {
                                string itemValue = item.Collectible.Variant[key]?.ToString();
                                string filterValue = filter.Collectible.Variant[key]?.ToString();
                                
                                if (itemValue != filterValue)
                                {
                                    Api?.Logger?.Debug($"Не совпадает атрибут {key}: {itemValue} != {filterValue}");
                                    return false;
                                }
                            }
                            else
                            {
                                // Если в фильтре есть атрибут, которого нет в предмете
                                return false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Api?.Logger?.Error($"Ошибка при проверке атрибутов: {ex.Message}");
                    return false;
                }
            }
            
            // 4. Если НЕ включена ни одна из настроек сравнения
            // Проверяем, включена ли хотя бы одна настройка сравнения
            bool hasComparisonSetting = matchMod || matchType || matchAttributes;
            
            if (!hasComparisonSetting)
            {
                // Если не включено ни одной настройки сравнения, 
                // то предмет должен точно совпадать с фильтром
                if (!itemCode?.Equals(filterCode) ?? false)
                {
                    Api?.Logger?.Debug($"Нет настроек сравнения, коды не совпадают");
                    return false;
                }
            }
            
            Api?.Logger?.Debug($"Предмет прошел фильтр!");
            return true;
        }  
        
        // Метод обновления настроек фильтра (вызывается из GUI)
        public void UpdateFilterSettings(FilterMode mode, bool matchMod, bool matchType, bool matchAttributes)
        {
            this.currentFilterMode = mode;
            this.matchMod = matchMod;
            this.matchType = matchType;
            this.matchAttributes = matchAttributes;
            
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
        
        private void OnTransferTick(float dt)
        {
            debugCounter++;
            if (debugCounter % 10 == 0)
            {
                Api.Logger.Notification($"=== OnTransferTick #{debugCounter} на {Pos} ===");
            }

            if (Api == null || networkManager == null) return;

            // Обновляем направление вывода, если нужно
            if (outputFacing == null || debugCounter % 20 == 0)
            {
                DetermineOutputDirection();
            }

            if (outputFacing == null)
            {
                if (debugCounter % 20 == 0)
                    Api.Logger.Notification($"=== Нет вывода на {Pos} ===");
                return;
            }

            // Получаем целевой контейнер (куда будем выводить) - КАК У ЖЕЛОБА!
            BlockPos containerPos = Pos.AddCopy(outputFacing);
            Block block = Api.World.BlockAccessor.GetBlock(containerPos);
            
            // Используем ТОЧНО ТАКОЙ ЖЕ подход как желоб!
            BlockEntityContainer targetContainer = block.GetBlockEntity<BlockEntityContainer>(containerPos);
            
            if (targetContainer == null)
            {
                if (debugCounter % 20 == 0)
                    Api.Logger.Error($"=== BlockEntityContainer не найден на {containerPos} ===");
                return;
            }
            
            if (debugCounter % 5 == 0)
                Api.Logger.Notification($"=== Целевой контейнер: {targetContainer.GetType().Name} на {containerPos} ===");
    
            // Ищем и переносим предметы
            FindAndTransferItems(targetContainer, containerPos);
        }
        
private void FindAndTransferItems(BlockEntityContainer targetContainer, BlockPos targetPos)
{
    IInventory targetInventory = targetContainer?.Inventory;
    
    if (targetInventory == null) 
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Error($"=== Не удалось получить инвентарь из контейнера на {targetPos} ===");
        return;
    }
    
    if (debugCounter % 10 == 0)
        Api.Logger.Notification($"=== Получен инвентарь цели. Количество слотов: {targetInventory.Count} ===");
    
    // Используем сеть для поиска источников
    var network = networkManager.GetNetwork(Pos);
    if (network == null) 
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Нет сети для трубы на {Pos} ===");
        return;
    }

    if (debugCounter % 20 == 0)
        Api.Logger.Notification($"=== Размер сети: {network.Pipes.Count} труб, {network.Inserters.Count} инсертеров ===");
    
    // Собираем все позиции для исключения
    var excludePositions = new HashSet<BlockPos>();
    excludePositions.Add(Pos);
    excludePositions.Add(targetPos);

    // Исключаем другие фильтрующие трубы и их цели
    foreach (var inserterPos in network.Inserters)
    {
        if (!inserterPos.Equals(Pos))
        {
            excludePositions.Add(inserterPos);
            BEInsertionPipe otherPipe = Api.World.BlockAccessor.GetBlockEntity(inserterPos) as BEInsertionPipe;
            if (otherPipe != null && otherPipe.outputFacing != null)
            {
                excludePositions.Add(inserterPos.AddCopy(otherPipe.outputFacing));
            }
        }
    }

    // Ищем источник предметов в сети
    bool foundSource = false;
    foreach (var pipePos in network.Pipes)
    {
        if (excludePositions.Contains(pipePos)) continue;

        // Проверяем все стороны трубы
        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos checkPos = pipePos.AddCopy(facing);

            if (excludePositions.Contains(checkPos)) continue;

            // Проверяем, можно ли взять предмет из этого источника
            if (TryTransferFromSource(checkPos, targetInventory, targetContainer, targetPos))
            {
                if (debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Успешно нашли и перенесли предмет из {checkPos} ===");
                return; // Успешно перенесли предмет
            }
            else
            {
                foundSource = true; // Нашли источник, но не смогли взять предмет
            }
        }
    }
    
    if (!foundSource && debugCounter % 10 == 0)
        Api.Logger.Notification($"=== Не найдено подходящих источников в сети ===");
}
        
private bool TryTransferFromSource(BlockPos sourcePos, IInventory targetInventory, 
    BlockEntityContainer targetContainer, BlockPos targetPos)
{
    // Проверяем тайминг
    if (!CanTransferFrom(sourcePos)) 
    {
        if (debugCounter % 30 == 0)
            Api.Logger.Notification($"=== Слишком рано для переноса из {sourcePos} ===");
        return false;
    }
    
    BlockEntity sourceBe = Api.World.BlockAccessor.GetBlockEntity(sourcePos);
    if (sourceBe == null) 
    {
        if (debugCounter % 30 == 0)
            Api.Logger.Notification($"=== Источник не найден на {sourcePos} ===");
        return false;
    }
    
    if (debugCounter % 20 == 0)
        Api.Logger.Notification($"=== Проверяем источник: {sourceBe.GetType().Name} на {sourcePos} ===");
    
    // Получаем инвентарь источника
    IInventory sourceInventory = GetInventoryFromBlockEntity(sourceBe);
    if (sourceInventory == null) 
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Не удалось получить инвентарь из источника {sourceBe.GetType().Name} ===");
        return false;
    }
    
    if (debugCounter % 20 == 0)
        Api.Logger.Notification($"=== Инвентарь источника получен. Слотов: {sourceInventory.Count} ===");
    
    // Определяем направление от источника к трубе
    BlockFacing directionFromSource = GetFacingFromTo(sourcePos, Pos);
    if (directionFromSource == null) 
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Не удалось определить направление от источника ===");
        return false;
    }
    
    // ВАЖНОЕ ИЗМЕНЕНИЕ: Ищем ВСЕ подходящие слоты, а не только первый
    ItemSlot sourceSlot = FindFirstSuitableSlot(sourceInventory, directionFromSource.Opposite);
    
    if (sourceSlot == null || sourceSlot.Empty) 
    {
        if (debugCounter % 30 == 0)
            Api.Logger.Notification($"=== В источнике нет подходящих предметов ===");
        return false;
    }
    
    // Определяем направление к цели
    BlockFacing directionToTarget = GetFacingFromTo(Pos, targetPos);
    if (directionToTarget == null) 
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Не удалось определить направление к цели ===");
        return false;
    }
    
    // Запрашиваем у цели разрешение на вставку
    ItemSlot targetSlot = null;
    if (targetInventory is InventoryBase targetInventoryBase)
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Запрашиваем GetAutoPushIntoSlot у цели ===");
        
        targetSlot = targetInventoryBase.GetAutoPushIntoSlot(directionToTarget.Opposite, sourceSlot);
        
        if (targetSlot != null)
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== GetAutoPushIntoSlot вернул целевой слот ===");
        }
    }
    
    // Если не получили целевой слот через GetAutoPushIntoSlot, ищем подходящий
    if (targetSlot == null)
    {
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Ищем подходящий слот в цели вручную ===");
        
        targetSlot = FindSuitableTargetSlot(targetInventory, sourceSlot);
        
        if (targetSlot == null)
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Не найден подходящий слот в цели ===");
            return false;
        }
    }
    
    // Проверяем, может ли целевой слот принять предмет
    if (!targetSlot.CanHold(sourceSlot))
    {
        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Целевой слот не может принять предмет ===");
        return false;
    }
    
    // Выполняем перенос
    return ExecuteTransfer(sourceSlot, targetSlot, sourceBe, targetContainer, sourcePos);
}

// НОВЫЙ МЕТОД: Ищет первый подходящий слот в источнике
private ItemSlot FindFirstSuitableSlot(IInventory inventory, BlockFacing pullDirection)
{
    // ищем по всем слотам вручную
    for (int i = 0; i < inventory.Count; i++)
    {
        ItemSlot slot = inventory[i];
        if (slot != null && !slot.Empty && CheckItemAgainstFilter(slot.Itemstack))
        {
            return slot;
        }
    }
    
    return null;
}
        

        
        private ItemSlot FindSuitableTargetSlot(IInventory targetInventory, ItemSlot sourceSlot)
        {
            // 1. Сначала ищем слот с таким же предметом
            for (int i = 0; i < targetInventory.Count; i++)
            {
                ItemSlot targetSlot = targetInventory[i];
                if (targetSlot != null && 
                    !targetSlot.Empty && 
                    targetSlot.CanHold(sourceSlot) &&
                    targetSlot.Itemstack.Equals(Api.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
                {
                    // Проверяем, есть ли свободное место
                    int freeSpace = targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.StackSize;
                    if (freeSpace > 0)
                    {
                        return targetSlot;
                    }
                }
            }
            
            // 2. Ищем пустой слот, который может принять предмет
            for (int i = 0; i < targetInventory.Count; i++)
            {
                ItemSlot targetSlot = targetInventory[i];
                if (targetSlot != null && targetSlot.Empty && targetSlot.CanHold(sourceSlot))
                {
                    return targetSlot;
                }
            }
            
            return null;
        }
        
private bool ExecuteTransfer(ItemSlot sourceSlot, ItemSlot targetSlot, BlockEntity sourceBe, 
    BlockEntityContainer targetContainer, BlockPos sourcePos)
{
    try
    {
        // Создаем операцию переноса (как в желобе)
        ItemStackMoveOperation op = new ItemStackMoveOperation(
            Api.World,
            EnumMouseButton.Left,
            0,
            EnumMergePriority.DirectMerge,  // DirectMerge вместо AutoMerge
            Math.Min(transferRate, sourceSlot.StackSize)
        );
        
        if (debugCounter % 5 == 0)
            Api.Logger.Notification($"=== Пытаемся перенести {Math.Min(transferRate, sourceSlot.StackSize)} предметов ===");
        
        // Используем TryPutInto (как в желобе)
        int transferred = sourceSlot.TryPutInto(targetSlot, ref op);
        
        if (transferred > 0)
        {
            // Успешно перенесли
            lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
            
            // Помечаем слоты как измененные
            sourceSlot.MarkDirty();
            targetSlot.MarkDirty();
            
            // Помечаем BlockEntity как измененные
            sourceBe.MarkDirty();
            targetContainer.MarkDirty();
            
            if (debugCounter % 2 == 0)
                Api.Logger.Notification($"=== Успешно перенесено {transferred} предметов через TryPutInto ===");
            
            return true;
        }
        else
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== TryPutInto не смог перенести предметы (transferred = 0) ===");
        }
    }
    catch (Exception ex)
    {
        Api.Logger.Error($"Ошибка при выполнении переноса: {ex.Message}");
        Api.Logger.Error($"Stack trace: {ex.StackTrace}");
    }
    
    return false;
}
        
        private bool CanTransferFrom(BlockPos sourcePos)
        {
            if (!lastTransferTime.ContainsKey(sourcePos))
                return true;
                
            long elapsed = Api.World.ElapsedMilliseconds - lastTransferTime[sourcePos];
            return elapsed > MinTransferInterval;
        }
        
        private BlockFacing GetFacingFromTo(BlockPos from, BlockPos to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            int dz = to.Z - from.Z;
    
            // Простая проверка соседних блоков
            if (dx == 1 && dy == 0 && dz == 0) return BlockFacing.EAST;
            if (dx == -1 && dy == 0 && dz == 0) return BlockFacing.WEST;
            if (dx == 0 && dy == 1 && dz == 0) return BlockFacing.UP;
            if (dx == 0 && dy == -1 && dz == 0) return BlockFacing.DOWN;
            if (dx == 0 && dy == 0 && dz == 1) return BlockFacing.SOUTH;
            if (dx == 0 && dy == 0 && dz == -1) return BlockFacing.NORTH;
    
            // Если блоки не соседние, попробуем определить основное направление
            // (для случаев, когда между ними другие трубы)
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
    
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Не удалось определить направление от {from} к {to} (dx={dx}, dy={dy}, dz={dz}) ===");
    
            return null;
        }
        
        // Отображение информации о блоке
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
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:inserters", network.Inserters.Count));
                }
            }
            sb.AppendLine("══════════════════════════════════════════");
            
            // Информация о режиме фильтра
            string modeText = currentFilterMode switch
            {
                FilterMode.AllowList => Lang.Get("electricalprogressivetransport:filter-mode-allow"),
                FilterMode.DenyList => Lang.Get("electricalprogressivetransport:filter-mode-deny"),
                _ => "Unknown"
            };
            sb.AppendLine(Lang.Get("electricalprogressivetransport:filter-mode", modeText));
            
            
            // Информация о настройки сравнения
            List<string> filters = new List<string>();
            if (matchMod) filters.Add(Lang.Get("electricalprogressivetransport:filter-match-mod"));
            if (matchType) filters.Add(Lang.Get("electricalprogressivetransport:filter-match-type"));
            if (matchAttributes) filters.Add(Lang.Get("electricalprogressivetransport:filter-match-attrs"));
            sb.AppendLine("└ " +string.Join(", ", filters));
            sb.AppendLine(Lang.Get("electricalprogressivetransport:transfer-rate", transferRate));
            // Показываем информацию о фильтрах
            int activeFilters = 0;
            for (int i = 0; i < _inventory.Count; i++)
            {
                if (!_inventory[i].Empty) activeFilters++;
            }
            sb.AppendLine(Lang.Get("electricalprogressivetransport:active-filters", activeFilters, _inventory.Count));
            
        } 
        
        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            UpdateConnections();
        }
        
        public override void OnBlockRemoved()
        {
            // Сначала обрабатываем разрыв соединений
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
                }
            }
            
            // Удаляем трубу из сети
            networkManager?.RemovePipe(Pos);
            
            if (Api?.Side == EnumAppSide.Server)
            {
                Api.World.UnregisterGameTickListener(transferTimer);
            }
            
            // Закрываем GUI если открыт
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog.TryClose();
            }
            
            base.OnBlockRemoved();
        }
        
        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            connectedSides[index] = false;
            connectedPipes[index] = null;
            MarkDirty();
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
    
            // Загружаем настройки фильтра
            transferRate = tree.GetInt("transferRate", 1);
            currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
            matchMod = tree.GetBool("matchMod", false);
            matchType = tree.GetBool("matchType", true);
            matchAttributes = tree.GetBool("matchAttributes", false);
    
            // Ограничиваем значение скорости
            transferRate = System.Math.Max(1, System.Math.Min(transferRate, 64));
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
    
            // Сохраняем настройки фильтра
            tree.SetInt("transferRate", transferRate);
            tree.SetInt("filterMode", (int)currentFilterMode);
            tree.SetBool("matchMod", matchMod);
            tree.SetBool("matchType", matchType);
            tree.SetBool("matchAttributes", matchAttributes);
        }
        
        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
    
            // Обработка пакетов от GUI
            if (packetid == 1001) // Закрытие GUI
            {
                if (_clientDialog != null && _clientDialog.IsOpened())
                {
                    _clientDialog.TryClose();
                }
            }
            else if (packetid == 1002) // Обновление настроек фильтра
            {
                using (var ms = new System.IO.MemoryStream(data))
                using (var br = new System.IO.BinaryReader(ms))
                {
                    FilterMode mode = (FilterMode)br.ReadInt32();
                    bool modMatch = br.ReadBoolean();
                    bool typeMatch = br.ReadBoolean();
                    bool attrMatch = br.ReadBoolean();
            
                    UpdateFilterSettings(mode, modMatch, typeMatch, attrMatch);
                }
            }
            else if (packetid == 1003) // Обновление скорости передачи
            {
                try
                {
                    var tree = new TreeAttribute();
                    tree.FromBytes(data);
            
                    int newRate = tree.GetInt("transferRate", 1);
            
                    // Ограничиваем значение
                    newRate = System.Math.Max(1, System.Math.Min(newRate, 64));
            
                    if (newRate != transferRate)
                    {
                        transferRate = newRate;
                
                        Api.Logger.Notification($"=== Скорость передачи обновлена: {transferRate} на {Pos} ===");
                
                        // Помечаем как измененное для сохранения
                        MarkDirty();
                
                        // Отправляем обновление клиентам
                        Api.World.BlockAccessor.MarkBlockDirty(Pos);
                    }
                }
                catch (Exception ex)
                {
                    Api?.Logger?.Error($"Ошибка при обновлении скорости передачи: {ex.Message}");
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
    }
    
    // Класс инвентаря для фильтрующей трубы
    public class InventoryInsertionPipe : InventoryGeneric
    {
        private BEInsertionPipe _entity;
    
        public InventoryInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BEInsertionPipe entity)
            : base(slots, className, instanceID, api)
        {
            _entity = entity;
        }
    
        // Фабричный метод для создания специальных слотов
        private static ItemSlot CreateFilterSlot(int slotId, InventoryBase inventory)
        {
            return new FilterSlot(inventory);
        }
    
        // Переопределяем метод, чтобы использовать наши слоты
        protected override ItemSlot NewSlot(int i)
        {
            return CreateFilterSlot(i, this);
        }
    
        // Автопуш из соседних контейнеров в инвентарь трубы
        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            // Фильтрующая труба может принимать предметы для фильтрации
            // Проверяем, есть ли пустые слоты фильтра
            for (int i = 0; i < Count; i++)
            {
                if (this[i] != null && this[i].Empty)
                {
                    // Можно принимать предметы для фильтров
                    return this[i];
                }
            }
            return null;
        }
    
        // Автопулл из инвентаря трубы в соседние контейнеры
        public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
        {
            // Фильтрующая труба не отдает предметы автоматически
            // (фильтры должны оставаться в трубе)
            return null;
        }
    }
    
    public class FilterSlot : ItemSlotSurvival
    {
        public FilterSlot(InventoryBase inventory) : base(inventory)
        {
        }
        
        // Ограничиваем количество предметов до 1
        public override int MaxSlotStackSize => 1;

        
        // Обработка при перетаскивании
        public override bool TryFlipWith(ItemSlot itemSlot)
        {
            if (itemSlot.StackSize > 1)
            {
                // Если в itemSlot больше 1 предмета, берем только 1
                if (!Empty)
                {
                    // Если наш слот не пуст, нельзя обменять
                    return false;
                }
                
                // Берем только 1 предмет из itemSlot
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
        
        // Обработка при изменении слота
        public override void OnItemSlotModified(ItemStack extractedStack = null)
        {
            // Гарантируем, что в слоте не больше 1 предмета
            if (!Empty && Itemstack.StackSize > 1)
            {
                Itemstack.StackSize = 1;
            }
            base.OnItemSlotModified(extractedStack);
        }
        
        // Проверка при активации слота
        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (!Empty && sourceSlot.Empty)
            {
                // При взятии из слота берем все (1 предмет)
                op.RequestedQuantity = StackSize;
            }
            else if (Empty && !sourceSlot.Empty)
            {
                // При попытке положить в пустой слот - ограничиваем количество
                if (sourceSlot.StackSize > 0)
                {
                    // Разрешаем положить, но ограничим количество до 1
                    if (op.RequestedQuantity == -1)
                        op.RequestedQuantity = 1;
                    else
                        op.RequestedQuantity = Math.Min(1, op.RequestedQuantity);
                }
            }
            
            base.ActivateSlot(sourceSlot, ref op);
        }
    }
}