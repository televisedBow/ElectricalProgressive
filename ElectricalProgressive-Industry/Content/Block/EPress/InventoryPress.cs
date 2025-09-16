using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EPress;

public class InventoryPress : InventoryGeneric
{


    public InventoryPress(ICoreAPI api)
        : base(api)
    {

    }

    public InventoryPress(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityEPress entity)
        : base(slots, className, instanceID, api)
    {

    }


    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        return targetSlot == this[0] && sourceSlot.Itemstack.Collectible.GrindingProps != null
            ? 3f
            : base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return this.slots[0];
    }
}