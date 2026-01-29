using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class BlockEFuelGenerator : BlockEBase
{
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
       BlockSelection blockSel, ref string failureCode)
    {
        var selection = new Selection(blockSel);
        var facing = Facing.None;

        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (FacingHelper.Faces(facing).First() is { } blockFacing &&
            !world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(blockFacing)).SideSolid[blockFacing.Opposite.Index])
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        if (byItemStack.Block.Variant["type"] == "burned")
            return false;

        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEFuelGenerator entity)
        {
            return false;
        }

        LoadEProperties.Load(this, entity);
        return true;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEFuelGenerator)
        {
            if (!world.BlockAccessor.GetBlock(pos.AddCopy(BlockFacing.DOWN)).SideSolid[4])
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel != null && !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var stack = slot?.Itemstack;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEFuelGenerator bef && stack != null)
        {
            if (byPlayer.Entity.Controls.CtrlKey) // Ctrl - топливо
            {
                if (stack.Collectible.CombustibleProps != null)
                {
                    var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
                    slot.TryPutInto(bef.FuelSlot, ref op);
                    if (op.MovedQuantity > 0)
                    {
                        (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
                        return true;
                    }
                }
            }
            else // Без Ctrl - вода
            {
                if (!slot.Empty && stack.Collectible is ILiquidInterface liquidInterface)
                {
                    CollectibleObject collectible = stack.Collectible;
                    bool shiftKey = byPlayer.WorldData.EntityControls.ShiftKey;
                    bool ctrlKey = byPlayer.WorldData.EntityControls.CtrlKey;
                    
                    ILiquidSource liquidSource = collectible as ILiquidSource;
                    if (liquidSource != null && !shiftKey && liquidSource.AllowHeldLiquidTransfer)
                    {
                        ItemStack content = liquidSource.GetContent(slot.Itemstack);
                        if (content != null && IsWaterLiquid(content, world))
                        {
                            float desiredLitres = ctrlKey ? liquidSource.TransferSizeLitres : liquidSource.CapacityLitres;
                            
                            // Рассчитываем сколько воды можем добавить
                            WaterTightContainableProps containableProps = BlockLiquidContainerBase.GetContainableProps(content);
                            if (containableProps != null)
                            {
                                float tankSpace = bef.WaterCapacity - bef.WaterAmount;
                                
                                // Ограничиваем желаемое количество доступным пространством
                                desiredLitres = Math.Min(desiredLitres, tankSpace);
                                
                                if (desiredLitres > 0)
                                {
                                    // Используем SplitStackAndPerformAction логику
                                    int moved = SplitStackAndPerformActionWater(byPlayer.Entity, slot, (itemStack) =>
                                    {
                                        ItemStack contentInStack = liquidSource.GetContent(itemStack);
                                        if (contentInStack == null) return 0;
                                        
                                        int itemsToTake = (int)(desiredLitres * containableProps.ItemsPerLitre);
                                        ItemStack taken = liquidSource.TryTakeContent(itemStack, itemsToTake);
                                        return taken?.StackSize ?? 0;
                                    });
                                    
                                    if (moved > 0)
                                    {
                                        // Добавляем воду в бак (пересчитываем из количества предметов в литры)
                                        float litresAdded = (float)moved / containableProps.ItemsPerLitre;
                                        bef.WaterAmount += litresAdded;
                                        
                                        // Эффекты
                                        DoLiquidMovedEffects(byPlayer, content, moved, EnumLiquidDirection.Fill, world, blockSel.Position);
                                        
                                        bef.MarkDirty();
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    // Проверка что жидкость является водой
    private bool IsWaterLiquid(ItemStack liquidStack, IWorldAccessor world)
    {
        if (liquidStack == null) return false;
        
        string code = liquidStack.Collectible.Code.Path.ToLower();
        return code.Contains("water") || code.Contains("freshwater");
    }

    // Метод для эффектов переливания жидкости (адаптированный из BlockLiquidContainerBase)
    private void DoLiquidMovedEffects(IPlayer player, ItemStack contentStack, int moved, EnumLiquidDirection dir, IWorldAccessor world, BlockPos pos)
    {
        if (player == null) return;
        
        WaterTightContainableProps containableProps = BlockLiquidContainerBase.GetContainableProps(contentStack);
        float litres = (float)moved / (containableProps != null ? containableProps.ItemsPerLitre : 1f);
        
        if (player is IClientPlayer clientPlayer)
            clientPlayer.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        
        AssetLocation soundLocation;
        
        if (dir == EnumLiquidDirection.Fill)
        {
            soundLocation = containableProps?.FillSound ?? new AssetLocation("sounds/effect/water-fill");
        }
        else
        {
            soundLocation = containableProps?.PourSound ?? new AssetLocation("sounds/effect/water-pour");
        }
        
        float volume = GameMath.Clamp(litres / 5f, 0.35f, 1f);
        world.PlaySoundAt(soundLocation, pos.X, pos.Y, pos.Z, player, volume, 16);
        
        // Спавним частицы (опционально)
        if (world.Side == EnumAppSide.Client)
        {
            world.SpawnCubeParticles(player.Entity.Pos.XYZ, contentStack, 0.75f, (int)litres * 2, 0.45f);
        }
    }

    // Получаем код пустой емкости
    private string GetEmptyContainerCode(string fullCode)
    {
        fullCode = fullCode.ToLower();
        
        if (fullCode.Contains("bucket") && fullCode.Contains("water"))
            return "bucket-empty";
        
        if (fullCode.Contains("waterskin") && fullCode.Contains("full"))
            return "waterskin-empty";
        
        if (fullCode.Contains("jug") && fullCode.Contains("water"))
            return "ceramicjug-empty";
        
        // Добавьте другие типы емкостей по необходимости
        return null;
    }

    // Замена пустого контейнера
    private void ReplaceWithEmptyContainer(Entity byEntity, ItemSlot slot)
    {
        if (slot.Itemstack == null) return;
        
        string emptyCode = GetEmptyContainerCode(slot.Itemstack.Collectible.Code.Path);
        if (!string.IsNullOrEmpty(emptyCode))
        {
            var emptyItem = byEntity.World.GetItem(new AssetLocation(emptyCode));
            if (emptyItem != null)
            {
                ItemStack emptyContainer = new ItemStack(emptyItem, 1);
                slot.Itemstack = emptyContainer;
                slot.MarkDirty();
            }
        }
    }

    // Адаптированный метод из BlockLiquidContainerBase для работы с водой
    private int SplitStackAndPerformActionWater(Entity byEntity, ItemSlot slot, Vintagestory.API.Common.Func<ItemStack, int> action)
    {
        if (slot.Itemstack == null || slot.Itemstack.StackSize == 0)
            return 0;
        
        // Если в стаке только 1 предмет
        if (slot.Itemstack.StackSize == 1)
        {
            int moved = action(slot.Itemstack);
            if (moved <= 0) return moved;
            
            // Помечаем слот как измененный
            slot.MarkDirty();
            
            // Если предмет полностью опустел, заменяем на пустой контейнер
            if (slot.Itemstack.Collectible is ILiquidSource liquidSource && 
                liquidSource.GetContent(slot.Itemstack) == null)
            {
                ReplaceWithEmptyContainer(byEntity, slot);
            }
            
            return moved;
        }
        
        // Если в стаке больше 1 предмета
        ItemStack singleStack = slot.Itemstack.Clone();
        singleStack.StackSize = 1; // Работаем с одним предметом
        
        int movedQuantity = action(singleStack);
        if (movedQuantity <= 0) return 0;
        
        // Убираем один предмет из стака
        slot.TakeOut(1);
        
        // Проверяем, опустел ли обработанный предмет
        if (singleStack.Collectible is ILiquidSource liquidSourceSingle && 
            liquidSourceSingle.GetContent(singleStack) == null)
        {
            // Создаем временный слот для замены
            DummySlot dummySlot = new DummySlot(singleStack);
            ReplaceWithEmptyContainer(byEntity, dummySlot);
            singleStack = dummySlot.Itemstack;
        }
        
        // Пытаемся вернуть обработанный предмет игроку
        if (byEntity is EntityPlayer entityPlayer)
        {
            if (!entityPlayer.Player.InventoryManager.TryGiveItemstack(singleStack, true))
            {
                byEntity.World.SpawnItemEntity(singleStack, byEntity.SidedPos.XYZ);
            }
        }
        else
        {
            byEntity.World.SpawnItemEntity(singleStack, byEntity.SidedPos.XYZ);
        }
        
        slot.MarkDirty();
        return movedQuantity;
    }

    // Вспомогательный класс для работы со слотами
    private class DummySlot : ItemSlot
    {
        public DummySlot(ItemStack stack) : base(null)
        {
            this.Itemstack = stack;
        }
        
        public override bool CanTake() => false;
        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
        public override bool CanHold(ItemSlot sourceSlot) => false;
    }

    // Перечисление направления жидкости
    private enum EnumLiquidDirection
    {
        Fill,
        Pour
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return new WorldInteraction[]
        {
            new()
            {
                ActionLangCode = "blockhelp-firepit-refuel",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "ctrl"
            },
            new()
            {
                ActionLangCode = "blockhelp-watergen-fillwater",
                MouseButton = EnumMouseButton.Right
            }
        }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("V"));
        dsc.AppendLine(Lang.Get("WResistance") + ": " + (MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false) ? Lang.Get("Yes") : Lang.Get("No")));
        dsc.AppendLine(Lang.Get("Water capacity") + ": 100 L");
    }
}