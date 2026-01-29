using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class InventoryFuelGenerator : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;

    public ItemSlot[] Slots => this.slots;

    // Слоты
    public ItemSlot FuelSlot => this.slots[0];
    public ItemSlot WaterSlot => this.slots[1];

    public InventoryFuelGenerator(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        slots = new ItemSlot[2];
        InitializeSlots();
    }

    public InventoryFuelGenerator(string className, string instanceID, ICoreAPI api)
        : base(className, instanceID, api)
    {
        slots = new ItemSlot[2];
        InitializeSlots();
    }

    private void InitializeSlots()
    {
        for (int i = 0; i < 2; i++)
        {
            slots[i] = NewSlot(i);
        }
    }

    public override int Count => 2;

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

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loadedSlots = this.SlotsFromTreeAttributes(tree, this.slots);
        
        // Просто копируем загруженные слоты
        for (int i = 0; i < 2; i++)
        {
            if (i < loadedSlots.Length)
                slots[i] = loadedSlots[i];
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
        if (i == 1) // Слот для воды
            return new ItemSlotLiquidOnly(this, 100);
        return new ItemSlotSurvival(this); // Слот для топлива
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == null || sourceSlot?.Itemstack == null) 
            return 0f;
        
        if (targetSlot == WaterSlot)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(sourceSlot.Itemstack);
            if (props != null && props.Containable)
            {
                if (sourceSlot.Itemstack.Collectible.Code.Path.ToLower().Contains("water"))
                    return 4f;
            }
            return 0f;
        }
        
        if (targetSlot == FuelSlot && sourceSlot.Itemstack.Collectible.CombustibleProps != null)
            return 4f;
        
        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Itemstack == null) 
            return null;
        
        var props = BlockLiquidContainerBase.GetContainableProps(fromSlot.Itemstack);
        if (props != null && props.Containable && fromSlot.Itemstack.Collectible.Code.Path.ToLower().Contains("water"))
            return WaterSlot;
        
        if (fromSlot.Itemstack.Collectible.CombustibleProps != null)
            return FuelSlot;
        
        return null;
    }
}