using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EHeatCannon
{
    public class BlockEHeatCannon : BlockEBase
    {
        



        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, selection.Direction);

            if (FacingHelper.Faces(facing).FirstOrDefault() is BlockFacing blockFacing)
            {
                var neighborPos = blockSel.Position.AddCopy(blockFacing);
                var neighborBlock = world.BlockAccessor.GetBlock(neighborPos);

                if (!neighborBlock.SideSolid[blockFacing.Opposite.Index])
                {
                    return false;
                }
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, selection.Direction);

            if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEHeatCannon entity)
            {
                return false;
            }

            entity.Facing = facing;
            LoadEProperties.Load(this, entity, selection.Face.Index);
            return true;
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            var newState = Variant["state"] switch
            {
                "enabled" => "disabled",
                _ => "disabled"
            };

            var blockCode = CodeWithVariants(new Dictionary<string, string>
            {
                { "state", newState }
            });
            var block = world.BlockAccessor.GetBlock(blockCode);

            return new ItemStack(block);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return new[] { OnPickBlock(world, pos) };
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEHeatCannon entity)
            {
                var blockFacing = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
                var selectedFacing = FacingHelper.FromFace(blockFacing);

                if ((entity.Facing & ~selectedFacing) == Facing.None)
                    world.BlockAccessor.BreakBlock(pos, null);
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var block = inSlot.Itemstack.Block;

            dsc.AppendLine(Lang.Get("Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("V"));
            dsc.AppendLine(Lang.Get("Consumption") + ": " + MyMiniLib.GetAttributeFloat(block, "maxConsumption", 0) + " " + Lang.Get("W"));
            dsc.AppendLine(Lang.Get("WResistance") + ": " + (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("Yes") : Lang.Get("No")));
        }

    }
}