using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport
{
    public class BlockInsertionPipe : BlockPipeBase
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection? blockSel)
        {
            if (blockSel is null)
                return false;

            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
                return false;

            blockSel.Block = this;

            var blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (blockEntity is null)
                return true;

            if (blockEntity is BlockEntityOpenableContainer openableContainer)
                openableContainer.OnPlayerRightClick(byPlayer, blockSel);

            return true;
        }
    }
}