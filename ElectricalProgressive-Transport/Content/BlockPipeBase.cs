using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport
{
    public class BlockPipeBase : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null) return false;
            
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
                return false;
            
            // Shift+ПКМ меняет тип трубы
            if (byPlayer.Entity.Controls.Sneak)
            {
                return TransformPipeType(world, blockSel.Position, byPlayer);
            }
            
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
        
        protected virtual bool TransformPipeType(IWorldAccessor world, BlockPos pos, IPlayer player)
        {
            string currentType = this.Code.Path;
            string newType = currentType == "pipe-normal" ? "pipe-insertion" : "pipe-normal";
            
            if (newType != currentType)
            {
                Block newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{newType}"));
                if (newBlock != null)
                {
                    BEPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
                    if (pipe != null)
                    {
                        ITreeAttribute tree = new TreeAttribute();
                        pipe.ToTreeAttributes(tree);
                        
                        world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
                        
                        BEPipe newPipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
                        if (newPipe != null)
                        {
                            newPipe.FromTreeAttributes(tree, world);
                            newPipe.MarkDirty();
                        }
                        
                        world.PlaySoundAt(new AssetLocation("sounds/effect/tooluse"), pos.X, pos.Y, pos.Z, player);
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            
            BEPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
            pipe?.UpdateConnections();
        }
        
        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
            
            BEPipe pipe = world.BlockAccessor.GetBlockEntity(blockPos) as BEPipe;
            if (pipe != null)
            {
                world.RegisterCallback((dt) => 
                {
                    pipe.UpdateConnections();
                }, 50);
            }
        }
        
    }
}