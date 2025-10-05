﻿using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EDrawing
{
    public class BlockEntityEDrawing : BlockEntityGenericTypedContainer
    {
        // Конфигурация
        internal InventoryDrawing inventory;
        private GuiDialogDrawing clientDialog;
        public override string InventoryClassName => "edrawing";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasCraftingLastTick;

        // Состояние крафта
        public DrawingRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;

        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot1 => inventory[2];
        public virtual string DialogTitle => Lang.Get("edrawing-title-gui");
        public override InventoryBase Inventory => inventory;

        private BlockEntityAnimationUtil animUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;

        //------------------------------------------------------------------------------------------------------------------
        // Электрические параметры
        private Facing facing = Facing.None;
        private BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

        public Facing Facing
        {
            get => this.facing;
            set
            {
                if (value != this.facing)
                {
                    this.ElectricalProgressive!.Connection =
                        FacingHelper.FullFace(this.facing = value);
                }
            }
        }

        public (EParams, int) Eparams
        {
            get => this.ElectricalProgressive?.Eparams ?? (new EParams(), 0);
            set => this.ElectricalProgressive!.Eparams = value;
        }

        public EParams[] AllEparams
        {
            get => this.ElectricalProgressive?.AllEparams ?? new EParams[]
            {
                new EParams(),
                new EParams(),
                new EParams(),
                new EParams(),
                new EParams(),
                new EParams()
            };
            set
            {
                if (this.ElectricalProgressive != null)
                {
                    this.ElectricalProgressive.AllEparams = value;
                }
            }
        }

        //----------------------------------------------------------------------------------------------------------------------------

        private ILoadedSound ambientSound;
        private AssetLocation centrifugeSound;

        public BlockEntityEDrawing()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventoryDrawing(3, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
            inventory.SlotModified += OnSlotModified;
        }



        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.RegisterGameTickListener(new Action<float>(this.Every1000ms), 1000);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;
                if (animUtil != null)
                {
                    animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
                }

                centrifugeSound = new AssetLocation("electricalprogressiveindustry:sounds/ecentrifuge/centrifuge.ogg");

                this.RegisterGameTickListener(new Action<float>(this.CheckAnimationFrame), 50);
            }
        }

        public int GetRotation()
        {
            string side = Block.Variant["side"];
            int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
            return adjustedIndex * 90;
        }

        private void OnSlotModified(int slotid)
        {
            if (Api is ICoreClientAPI)
                clientDialog?.Update(RecipeProgress);

            if (slotid < 2)
            {
                RecipeProgress = 0f;
                UpdateState(RecipeProgress);
            }

            MarkDirty();
        }

        #region Логика рецептов

        public static bool FindMatchingRecipe(ref DrawingRecipe currentRecipe, ref string currentRecipeName, InventoryDrawing inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;   

            foreach (DrawingRecipe recipe in ElectricalProgressiveRecipeManager.DrawingRecipes)
            {
                if (MatchesRecipe(recipe, inventory))
                {
                    currentRecipe = recipe;
                    currentRecipeName = recipe.Output.ResolvedItemstack?.GetName() ?? "Unknown";
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesRecipe(DrawingRecipe recipe, InventoryDrawing inventory)
        {
            List<int> usedSlots = new List<int>();

            for (int ingredIndex = 0; ingredIndex < recipe.Ingredients.Length && ingredIndex < 2; ingredIndex++)
            {
                var ingred = recipe.Ingredients[ingredIndex];
                bool foundSlot = false;

                for (int slotIndex = 0; slotIndex < 2; slotIndex++)
                {
                    if (usedSlots.Contains(slotIndex)) continue;

                    var slot = inventory[slotIndex];

                    if (ingred.Quantity == 0)
                    {
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
                    }
                    else if (ingred.Quantity > 0)
                    {
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack) &&
                            slot.Itemstack.StackSize >= ingred.Quantity)
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
                    }
                }

                if (!foundSlot) return false;
            }

            return true;
        }

        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;

            for (int i = 0; i < CurrentRecipe.Ingredients.Length && i < 2; i++)
            {
                var ingred = CurrentRecipe.Ingredients[i];
                var slot = GetInputSlot(i);

                if (ingred.Quantity == 0)
                {
                    if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack))
                        return false;
                }
                else if (ingred.Quantity > 0)
                {
                    if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack) ||
                        slot.Itemstack.StackSize < ingred.Quantity)
                        return false;
                }
            }

            return true;
        }

        private void ProcessCompletedCraft()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Output?.ResolvedItemstack == null)
                return;

            try
            {
                List<int> usedSlots = new List<int>();

                // Обработка основного выхода
                ItemStack outputStack1 = CurrentRecipe.Output.ResolvedItemstack.Clone();
                TryMergeOrSpawn(outputStack1, OutputSlot1);

                

                // Удаляем расходуемые ингредиенты
                foreach (var ingred in CurrentRecipe.Ingredients)
                {
                    if (ingred.Quantity <= 0) continue;

                    for (int slotIndex = 0; slotIndex < 2; slotIndex++)
                    {
                        if (usedSlots.Contains(slotIndex)) continue;

                        var slot = GetInputSlot(slotIndex);
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            slot.TakeOut(ingred.Quantity);
                            slot.MarkDirty();
                            usedSlots.Add(slotIndex);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Api.Logger.Error($"Crafting error in EDrawing at {Pos}: {ex}");
            }
        }

        private void TryMergeOrSpawn(ItemStack stack, ItemSlot targetSlot)
        {
            if (targetSlot.Empty)
            {
                targetSlot.Itemstack = stack;
            }
            else if (targetSlot.Itemstack.Collectible == stack.Collectible &&
                    targetSlot.Itemstack.StackSize < targetSlot.Itemstack.Collectible.MaxStackSize)
            {
                int freeSpace = targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.Itemstack.StackSize;
                int toAdd = Math.Min(freeSpace, stack.StackSize);

                targetSlot.Itemstack.StackSize += toAdd;
                stack.StackSize -= toAdd;

                if (stack.StackSize > 0)
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
            else
            {
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            targetSlot.MarkDirty();
        }

        private ItemSlot GetInputSlot(int index) => index switch
        {
            0 => InputSlot1,
            1 => InputSlot2,
            _ => throw new ArgumentOutOfRangeException()
        };
        #endregion

        #region Основной цикл работы
        private void Every1000ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorEDrawing>();
            if (beh == null)
            {
                StopAnimation();
                return;
            }

            bool hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            bool hasRecipe = !InputSlot1.Empty && !InputSlot2.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);
            bool isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

            
            if (isCraftingNow)
            {
                if (!_wasCraftingLastTick)
                {
                    startSound();
                }

                StartAnimation();

                RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
                UpdateState(RecipeProgress);

                if (RecipeProgress >= 1f)
                {
                    ProcessCompletedCraft();

                    
                    if (!HasRequiredItems())
                    {
                        StopAnimation();
                        stopSound();
                    }

                    // в любом случае сбрасываем прогресс
                    RecipeProgress = 0f;
                    UpdateState(RecipeProgress);
                }
            }
            else if (_wasCraftingLastTick)
            {
                StopAnimation();
                stopSound();
                MarkDirty(true);
            }



            _wasCraftingLastTick = isCraftingNow;
        }

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && clientDialog?.IsOpened() == true)
                clientDialog.Update(progress);

            MarkDirty(true);
        }
        #endregion



        /// <summary>
        /// Запуск звука
        /// </summary>
        public void startSound()
        {
            if (this.ambientSound != null)
                return;
            if ((Api != null ? (Api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
                return;
            this.ambientSound = (this.Api as ICoreClientAPI).World.LoadSound(new SoundParams()
            {
                Location = centrifugeSound,
                ShouldLoop = true,
                Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                DisposeOnFinish = false,
                Volume = 1f,
            });

            this.ambientSound.Start();
        }



        /// <summary>
        /// Остановка звука
        /// </summary>
        public void stopSound()
        {
            if (this.ambientSound == null)
                return;
            this.ambientSound.Stop();
            this.ambientSound.Dispose();
            this.ambientSound = (ILoadedSound)null;
        }




        #region Визуальные эффекты
        private void StartAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || animUtil == null || CurrentRecipe == null)
                return;

            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
            {
                animUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "Animation1",
                    Code = "craft",
                    AnimationSpeed = 1.0f,
                    EaseOutSpeed = 1.0f,
                    EaseInSpeed = 1f
                });
            }
        }

        private void StopAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || animUtil == null)
                return;

            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == true)
            {
                animUtil.StopAnimation("craft");
            }
        }

        private void CheckAnimationFrame(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || animUtil == null)
                return;

            const int startFrame = 20;
            if (animUtil.activeAnimationsByAnimCode.ContainsKey("craft"))
            {
                long currentTime = Api.World.ElapsedMilliseconds;
                _lastAnimationCheckTime = currentTime;

                var currentFrame = animUtil.animator.Animations[0].CurrentFrame;
                if (currentFrame >= startFrame && _lastSoundFrame != startFrame)
                {
                    PlayPressSound();
                    _lastSoundFrame = startFrame;
                }
                else if ((int)currentFrame < startFrame)
                {
                    _lastSoundFrame = -1;
                }
            }
            else
            {
                _lastSoundFrame = -1;
            }
        }

        private void PlayPressSound()
        {
            if (Api?.Side != EnumAppSide.Client)
                return;

            ICoreClientAPI capi = Api as ICoreClientAPI;
            capi.World.PlaySoundAt(
                centrifugeSound,
                Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
                null,
                false,
                32,
                1f
            );
        }
        #endregion

        #region GUI и взаимодействие
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    clientDialog = new GuiDialogDrawing(DialogTitle, Inventory, Pos, _capi);
                    clientDialog.Update(RecipeProgress);
                    return clientDialog;
                });
            }
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
            ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);
            ElectricalProgressive?.OnReceivedServerPacket(packetid, data);

            if (packetid != 1001)
                return;
            (this.Api.World as IClientWorldAccessor).Player.InventoryManager.CloseInventory((IInventory)this.Inventory);
            this.invDialog?.TryClose();
            this.invDialog?.Dispose();
            this.invDialog = (GuiDialogBlockEntity)null;
        }
        #endregion

        #region Сохранение состояния
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            RecipeProgress = tree.GetFloat("PowerCurrent");

            if (Api != null)
                Inventory.AfterBlocksLoaded(Api.World);

            if (Api?.Side == EnumAppSide.Client && clientDialog != null)
                clientDialog.Update(RecipeProgress);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invTree = new TreeAttribute();
            Inventory.ToTreeAttributes(invTree);
            tree["inventory"] = invTree;
            tree.SetFloat("PowerCurrent", RecipeProgress);
        }
        #endregion

        #region Жизненный цикл
        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (ElectricalProgressive == null || byItemStack == null)
                return;

            ElectricalProgressive.Connection = Facing.DownAll;

            var voltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "voltage", 32);
            var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 5.0F);
            var isolated = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolated", false);
            var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

            this.ElectricalProgressive.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 0);
            this.ElectricalProgressive.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 1);
            this.ElectricalProgressive.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 2);
            this.ElectricalProgressive.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 3);
            this.ElectricalProgressive.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 4);
            this.ElectricalProgressive.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 5);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (ElectricalProgressive != null)
            {
                ElectricalProgressive.Connection = Facing.None;
            }

            if (this.Api is ICoreClientAPI && this.clientDialog != null)
            {
                this.clientDialog.TryClose();
                this.clientDialog = null;
            }

            StopAnimation();

            if (this.Api.Side == EnumAppSide.Client && this.animUtil != null)
            {
                this.animUtil.Dispose();
            }

            if (this.ambientSound != null)
            {
                this.ambientSound.Stop();
                this.ambientSound.Dispose();
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this.clientDialog?.TryClose();
            if (this.ambientSound == null)
                return;
            this.ambientSound.Stop();
            this.ambientSound.Dispose();
            this.ambientSound = (ILoadedSound)null;
        }
        #endregion
    }
}
