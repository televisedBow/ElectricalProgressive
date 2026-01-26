using System;
using System.Collections.Generic;
using System.Linq;
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
        
        private WorldInteraction[] _interactions;

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world,
            BlockSelection selection,
            IPlayer forPlayer)
        {
            if (_interactions == null)
            {
                // Ищем все предметы с кодом "wrench"
                var wrenchStacks = new List<ItemStack>();
        
                foreach (var obj in world.Collectibles)
                {
                    if (obj.FirstCodePart() == "wrench")
                    {
                        var stacks = obj.GetHandBookStacks(api as ICoreClientAPI);
                        if (stacks != null)
                        {
                            wrenchStacks.AddRange(stacks);
                        }
                    }
                }

                _interactions = new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "electricalprogressivetransport:blockhelp-pipe-switch-type",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = wrenchStacks.Count > 0 ? wrenchStacks.ToArray() : null
                    }
                }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
            }

            return _interactions;
        }
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null) return false;
    
            // Проверка прав доступа
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
                return false;
    
            // Получаем предмет в активной руке игрока
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            string toolCode = activeSlot.Itemstack?.Collectible?.FirstCodePart();
    
            // Логика для контейнера с проверкой на Shift

                // Для контейнеров - открываем только при зажатом Shift
                if (byPlayer.Entity.Controls.Sneak)
                {
                    var blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                    if (blockEntity is BlockEntityOpenableContainer openableContainer)
                    {
                        openableContainer.OnPlayerRightClick(byPlayer, blockSel);
                        return true;
                    }
                }

    
            // Логика для трубы с проверкой на ключ

                // Проверяем, что в руке ключ (проверяем по FirstCodePart)
                if (toolCode == "wrench")
                {
                    return TransformPipeType(world, blockSel.Position, byPlayer);
                }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
        
        protected virtual bool TransformPipeType(IWorldAccessor world, BlockPos pos, IPlayer player)
        {
            string currentType = this.Code.Path;
            string typeName = currentType.Split('/').Last().Split(':').Last();
    
            string newType = typeName == "pipe-normal" ? "pipe-insertion" : "pipe-normal";
    
            Block newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{newType}"));
            if (newBlock == null) return false;
    
            // Получаем текущую сущность и сохраняем её данные
            BlockEntity currentEntity = world.BlockAccessor.GetBlockEntity(pos);
            ITreeAttribute tree = null;
    
            if (currentEntity != null)
            {
                tree = new TreeAttribute();
                currentEntity.ToTreeAttributes(tree);
            }
    
            // Меняем блок
            world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
    
            // Восстанавливаем данные в новую сущность
            if (tree != null)
            {
                BlockEntity newEntity = world.BlockAccessor.GetBlockEntity(pos);
                if (newEntity != null)
                {
                    newEntity.FromTreeAttributes(tree, world);
                    newEntity.MarkDirty();
                }
            }
    
            // Обновляем и проигрываем звук
            world.BlockAccessor.MarkBlockDirty(pos);
            world.PlaySoundAt(new AssetLocation("sounds/effect/tooluse"), pos.X, pos.Y, pos.Z, player);
    
            return true;
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