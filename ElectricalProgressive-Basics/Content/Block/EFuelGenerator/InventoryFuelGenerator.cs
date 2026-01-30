using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

/// <summary>
/// Инвентарь генератора на топливе.
/// Управляет слотами для топлива и воды.
/// Реализует ISlotProvider для интеграции с системой слотов.
/// </summary>
public class InventoryFuelGenerator : InventoryBase, ISlotProvider
{
    // === Поля ===
    private ItemSlot[] slots;
    private BlockPos _pos;
    private ICoreAPI _api;
    
    // === Свойства ===
    
    /// <summary>
    /// Массив всех слотов инвентаря
    /// </summary>
    public ItemSlot[] Slots => this.slots;
    
    /// <summary>
    /// Слот для топлива (индекс 0)
    /// </summary>
    public ItemSlot FuelSlot => this.slots[0];
    
    /// <summary>
    /// Слот для воды (индекс 1)
    /// </summary>
    public ItemSlot WaterSlot => this.slots[1];
    
    /// <summary>
    /// Количество слотов в инвентаре
    /// </summary>
    public override int Count => 2;
    
    /// <summary>
    /// Индексатор для доступа к слотам
    /// </summary>
    public override ItemSlot this[int slotId]
    {
        get 
        { 
            if (slotId < 0 || slotId >= 2) 
                return null; 
            return slots[slotId]; 
        }
        set
        {
            if (slotId < 0 || slotId >= 2)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    
    // === Конструкторы ===
    
    /// <summary>
    /// Конструктор с ID инвентаря и API
    /// </summary>
    public InventoryFuelGenerator(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        _api = api;
        slots = new ItemSlot[2];
        InitializeSlots();
    }
    
    /// <summary>
    /// Конструктор с именем класса и ID экземпляра
    /// </summary>
    public InventoryFuelGenerator(string className, string instanceID, ICoreAPI api)
        : base(className, instanceID, api)
    {
        _api = api;
        slots = new ItemSlot[2];
        InitializeSlots();
    }
    
    // Публичные методы для установки позиции и обновления емкости
    
    /// <summary>
    /// Установить позицию блока для получения емкости
    /// </summary>
    public void SetBlockPos(BlockPos pos)
    {
        _pos = pos;
        UpdateWaterSlotCapacity();
    }
    
    /// <summary>
    /// Обновить емкость водяного слота
    /// </summary>
    public void UpdateWaterSlotCapacity()
    {
        if (slots[1] is ItemSlotLiquidOnly waterSlot)
        {
            float capacity = GetWaterCapacityFromBlock();
            
            // Обновляем емкость через рефлексию
            var field = typeof(ItemSlotLiquidOnly).GetField("CapacityLitres",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(waterSlot, capacity);
                
                // Обновляем MaxSlotStackSize если слот уже содержит жидкость
                if (!waterSlot.Empty && waterSlot.Itemstack != null)
                {
                    var props = BlockLiquidContainerBase.GetContainableProps(waterSlot.Itemstack);
                    if (props != null)
                    {
                        // Если текущий стек превышает новую емкость
                        float maxStackSize = capacity * props.ItemsPerLitre;
                        if (waterSlot.StackSize > maxStackSize)
                        {
                            waterSlot.Itemstack.StackSize = (int)maxStackSize;
                            waterSlot.MarkDirty();
                        }
                    }
                }
            }
        }
    }
    
    // === Приватные методы ===
    
    /// <summary>
    /// Инициализация слотов инвентаря
    /// </summary>
    private void InitializeSlots()
    {
        for (int i = 0; i < 2; i++)
        {
            slots[i] = NewSlot(i);
        }
    }
    
    /// <summary>
    /// Получить емкость воды из блока
    /// </summary>
    private float GetWaterCapacityFromBlock()
    {
        if (_api == null || _pos == null) return 100f;
        
        // Пытаемся получить блок генератора
        Vintagestory.API.Common.Block block = _api.World.BlockAccessor.GetBlock(_pos);
        
        // Пробуем получить емкость через атрибуты
        if (block?.Attributes?["capacityLitres"].Exists == true)
        {
            return block.Attributes["capacityLitres"].AsFloat(100f);
        }
        
        // Проверяем, есть ли у блока свойство CapacityLitres
        var property = block?.GetType().GetProperty("CapacityLitres");
        if (property != null)
        {
            object value = property.GetValue(block);
            if (value is float floatValue)
                return floatValue;
            if (value is int intValue)
                return intValue;
        }
        
        return 100f; // Значение по умолчанию
    }
    
    // === Публичные методы ===
    
    /// <summary>
    /// Загрузка инвентаря из дерева атрибутов
    /// </summary>
    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loadedSlots = this.SlotsFromTreeAttributes(tree, this.slots);
        
        for (int i = 0; i < 2; i++)
        {
            if (i < loadedSlots.Length)
                slots[i] = loadedSlots[i];
        }
        
        // Обновляем емкость после загрузки
        UpdateWaterSlotCapacity();
    }
    
    /// <summary>
    /// Сохранение инвентаря в дерево атрибутов
    /// </summary>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
    }
    
    /// <summary>
    /// Создание нового слота по индексу
    /// </summary>
    protected override ItemSlot NewSlot(int i)
    {
        if (i == 1) // Слот для воды
        {
            // Используем значение по умолчанию, будет обновлено позже
            return new ItemSlotLiquidOnly(this, 100f);
        }
        return new ItemSlotSurvival(this); // Слот для топлива
    }
    
    /// <summary>
    /// Определение приоритета предмета для слота
    /// </summary>
    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == null || sourceSlot?.Itemstack == null) 
            return 0f;
        
        // Проверка для слота воды
        if (targetSlot == WaterSlot)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(sourceSlot.Itemstack);
            if (props != null && props.Containable)
            {
                return 4f; // Высокий приоритет для жидкостей
            }
            return 0f;
        }
        
        // Проверка для слота топлива
        if (targetSlot == FuelSlot && sourceSlot.Itemstack.Collectible.CombustibleProps != null)
            return 4f; // Высокий приоритет для горючего
        
        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }
    
    /// <summary>
    /// Получение слота для автоматического помещения предмета
    /// </summary>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Itemstack == null) 
            return null;
        
        // Автоматическое определение типа предмета
        var props = BlockLiquidContainerBase.GetContainableProps(fromSlot.Itemstack);
        if (props != null && props.Containable)
            return WaterSlot; // Жидкости идут в слот воды
        
        if (fromSlot.Itemstack.Collectible.CombustibleProps != null)
            return FuelSlot; // Горючее идет в слот топлива
        
        return null; // Другие предметы не принимаются
    }
}