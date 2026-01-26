using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
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

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world,
            BlockSelection selection,
            IPlayer forPlayer)
        {
            return new WorldInteraction[1]
            {
                new WorldInteraction()
                {
                    ActionLangCode = Lang.Get("electricalprogressivetransport:blockhelp-filter-settings"), MouseButton = EnumMouseButton.Right
                }
            }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }
        
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-insertion-info"));
            BEInsertionPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BEInsertionPipe;
            if (pipe != null)
            {
                pipe.GetBlockInfo(forPlayer,sb);
            }
            return sb.ToString();
        }


    }
}