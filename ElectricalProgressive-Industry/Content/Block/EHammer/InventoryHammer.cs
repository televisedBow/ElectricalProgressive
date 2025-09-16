using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EHammer;

public class InventoryHammer : InventoryGeneric
{
    
    public InventoryHammer(ICoreAPI api)
        : base(api)
    {

    }

    public InventoryHammer(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityEHammer entity)
        : base(slots, className, instanceID, api)
    {
 
    }


    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        // Слот 0 - только для входных предметов
        if (targetSlot == this[0])
        {
            return sourceSlot.Itemstack.Collectible.GrindingProps != null ? 4f : 0f;
        }
        
        // Слоты 1 и 2 - только для выходных предметов (нельзя вручную класть)
        return 0f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        // Автозаполнение только во входной слот
        return this[0];
    }


    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        //Автовывод сначала из основного выхода (слот 1), затем из дополнительного (слот 2)
        for (var i = 1; i < this.Count; i++)
        {
            if (!this[i].Empty)
                return this[i];
        }

        return null!;
    }


    // Методы для удобного доступа к слотам
    public ItemSlot InputSlot => this[0];
    public ItemSlot OutputSlot => this[1];
    public ItemSlot SecondaryOutputSlot => this[2];
}