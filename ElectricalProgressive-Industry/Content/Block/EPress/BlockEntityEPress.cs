﻿using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EPress
{
    public class BlockEntityEPress : BlockEntityGenericTypedContainer, ITexPositionSource
    {
        // Конфигурация
        internal InventoryPress inventory;
        private GuiDialogPress clientDialog;
        public override string InventoryClassName => "epress";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasCraftingLastTick;

        // Состояние крафта
        public PressRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;

        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot1 => inventory[2];
        public ItemSlot OutputSlot2 => inventory[3];
        public virtual string DialogTitle => Lang.Get("epress-title-gui");
        public override InventoryBase Inventory => inventory;

        private BlockEntityAnimationUtil animUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;

        private MeshData toolMesh;
        private CollectibleObject tmpItem;

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

        private AssetLocation soundPress;

        public BlockEntityEPress()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventoryPress(4, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
            inventory.SlotModified += OnSlotModified;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.RegisterGameTickListener(new Action<float>(this.Every500ms), 500);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;

                // Загружаем мэш
                LoadToolMesh();

                if (animUtil != null)
                {
                    animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
                }


                soundPress = new AssetLocation("electricalprogressiveindustry:sounds/epress/press.ogg");

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


            if (slotid == 1 && Api.Side == EnumAppSide.Client)
            {
                LoadToolMesh(); // Обновляем меш при изменении входного слота
                MarkDirty(true);
            }

            MarkDirty();
        }


        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (BlockEPress.ToolTextureSubIds(Api).TryGetValue((Item)tmpItem!, out var toolTextures))
                {
                    if (toolTextures.TextureSubIdsByCode.TryGetValue(textureCode, out var textureSubId))
                        return ((ICoreClientAPI)Api).BlockTextureAtlas.Positions[textureSubId];

                    return ((ICoreClientAPI)Api).BlockTextureAtlas.Positions[toolTextures.TextureSubIdsByCode.First().Value];
                }

                return null!;
            }
        }

        public Size2i AtlasSize => ((ICoreClientAPI)Api).BlockTextureAtlas.Size;



        #region Логика рецептов

        public static bool FindMatchingRecipe(ref PressRecipe currentRecipe, ref string currentRecipeName, InventoryPress inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (PressRecipe recipe in ElectricalProgressiveRecipeManager.PressRecipes)
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

        private static bool MatchesRecipe(PressRecipe recipe, InventoryPress inventory)
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

                // Обработка дополнительного выхода с шансом
                if (CurrentRecipe.SecondaryOutput != null &&
                    CurrentRecipe.SecondaryOutput.ResolvedItemstack != null &&
                    Api.World.Rand.NextDouble() < CurrentRecipe.SecondaryOutputChance)
                {
                    ItemStack secondaryOutput = CurrentRecipe.SecondaryOutput.ResolvedItemstack.Clone();
                    TryMergeOrSpawn(secondaryOutput, OutputSlot2);
                }

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
                Api.Logger.Error($"Crafting error in EPress at {Pos}: {ex}");
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
        private void Every500ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorEPress>();
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
                StartAnimation();

                RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
                UpdateState(RecipeProgress);

                if (RecipeProgress >= 1f)
                {
                    ProcessCompletedCraft();

                    
                    if (!HasRequiredItems())
                    {
                        StopAnimation();
                    }

                    // в любом случае сбрасываем прогресс
                    RecipeProgress = 0f;
                    UpdateState(RecipeProgress);
                }
            }
            else if (_wasCraftingLastTick)
            {
                StopAnimation();
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
                    AnimationSpeed = 2.0f,
                    EaseOutSpeed = 2.0f,
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
                soundPress,
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
                    clientDialog = new GuiDialogPress(DialogTitle, Inventory, Pos, _capi);
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

            if (Api is ICoreClientAPI)
            {
                LoadToolMesh(); // обновляем меш при загрузке
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }

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



        /// <summary>
        /// Загружает меш предмета для отображения в молоте
        /// </summary>
        void LoadToolMesh()
        {
            toolMesh = null!; // сбрасываем предыдущий меш
            tmpItem = null!;

            var stack = InputSlot2?.Itemstack;
            if (stack == null || stack.Collectible==null || !stack.Collectible.Code.Path.Contains("pressform")) // пустой стак не отображаем и не прессформу
                return;


            tmpItem = stack.Collectible;

            Vec3f origin = new Vec3f(0.5f, 0, 0.5f);
            var clientApi = (ICoreClientAPI)Api;

            if (stack.Class == EnumItemClass.Item)
                clientApi.Tesselator.TesselateItem(stack.Item, out toolMesh, this);
            else
                clientApi.Tesselator.TesselateBlock(stack.Block, out toolMesh);

            clientApi.TesselatorManager.ThreadDispose(); // обязательно

            if (stack.Class == EnumItemClass.Item)
            {
                var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 0.8F);
                var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 0.8F);
                var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 0.8F);
                var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0F);
                var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0F);
                var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", 0F);
                var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
                var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
                var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

                toolMesh.Scale(origin, scaleX, scaleY, scaleZ);
                toolMesh.Translate(translateX, translateY + 0.95f, translateZ);
                toolMesh.Rotate(origin, rotateX, rotateY, rotateZ);
            }
            else
            {
                toolMesh.Scale(origin, 0.9f, 0.9f, 0.9f);
                toolMesh.Translate(0f, 0.9f, 0f);
            }
        }



        /// <summary>
        /// Вызывается при тесселяции блока
        /// </summary>
        /// <param name="mesher"></param>
        /// <param name="tesselator"></param>
        /// <returns></returns>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            base.OnTesselation(mesher, tesselator); // вызываем базовую логику тесселяции

            if (toolMesh != null)
                try
                {
                    mesher.AddMeshData(toolMesh);
                }
                catch
                {
                    // мэш поврежден
                }

            // если анимации нет, то рисуем блок базовый
            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
            {
                return false;
            }

            return true;  // не рисует базовый блок, если есть анимация

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

            // Очистка мусора
            toolMesh = null!;
            tmpItem = null!;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this.clientDialog?.TryClose();

            // Очистка мусора
            toolMesh = null!;
            tmpItem = null!;
        }


        #endregion
    }
}
