using ElectricalProgressive.RecipeSystem;
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
    public class BlockEntityEPress : BlockEntityGenericTypedContainer
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

        // Слоты (2 входа, 1 выход)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot => inventory[2]; // Теперь слот 2 — выходной

        public virtual string DialogTitle => Lang.Get("epress-title-gui");
        public override InventoryBase Inventory => inventory;

        private BlockEntityAnimationUtil animUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;

        //------------------------------------------------------------------------------------------------------------------
        // Электрические параметры (без изменений)
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
            this.inventory = new InventoryPress(3, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
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
                if (animUtil != null)
                {
                    animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
                }

                soundPress = new AssetLocation("electricalprogressiveindustry:sounds/epress/press.ogg");

                // Регистрируем частый тикер для проверки анимации на клиенте
                this.RegisterGameTickListener(new Action<float>(this.CheckAnimationFrame), 50);
            }
        }

        public int GetRotation()
        {
            string side = Block.Variant["side"];
            int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
            return adjustedIndex * 90;
        }

        /// <summary>
        /// Слот изменен
        /// </summary>
        /// <param name="slotid"></param>
        private void OnSlotModified(int slotid)
        {
            if (Api is ICoreClientAPI)
                clientDialog?.Update(RecipeProgress);

            
            if (slotid != 2) // Если изменился не выходной слот, то сбрасываем прогресс 
            {
                RecipeProgress = 0f;
                UpdateState(RecipeProgress);
            }

            MarkDirty();
        }

        #region Логика рецептов (адаптирована под 2 слота)


        /// <summary>
        /// Ищем подходящий рецепт для текущих ингредиентов
        /// </summary>
        /// <returns></returns>
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
                    //MarkDirty(true);
                    return true;
                }
            }


            return false;
        }

        /// <summary>
        /// Проверка соответствия текущих слотов рецепту
        /// </summary>
        /// <param name="recipe"></param>
        /// <returns></returns>
        private static bool MatchesRecipe(PressRecipe recipe, InventoryPress inventory)
        {
            Dictionary<string, string> wildcardMatches = new Dictionary<string, string>();
            List<int> usedSlots = new List<int>();

            // Проверяем все ингредиенты рецепта (макс. 2)
            for (int ingredIndex = 0; ingredIndex < recipe.Ingredients.Length && ingredIndex < 2; ingredIndex++)
            {
                var ingred = recipe.Ingredients[ingredIndex];
                bool foundSlot = false;

                // Ищем подходящий слот (только 0 или 1)
                for (int slotIndex = 0; slotIndex < 2; slotIndex++)
                {
                    if (usedSlots.Contains(slotIndex)) continue;

                    var slot = inventory[slotIndex];

                    // Для quantity=0 проверяем только наличие
                    if (ingred.Quantity == 0)
                    {
                        if (!slot.Empty && SatisfiesIngredient(slot.Itemstack, ingred, ref wildcardMatches))
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
                    }
                    // Для quantity>0 проверяем количество
                    else if (ingred.Quantity > 0)
                    {
                        if (!slot.Empty && SatisfiesIngredient(slot.Itemstack, ingred, ref wildcardMatches) &&
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

            // Проверяем allowedVariants для wildcard-совпадений
            foreach (var match in wildcardMatches)
            {
                var ingred = recipe.Ingredients.FirstOrDefault(i => i.Name == match.Key);
                if (ingred?.AllowedVariants != null && !ingred.AllowedVariants.Contains(match.Value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Проверка наличия всех необходимых ингредиентов для текущего рецепта
        /// </summary>
        /// <returns></returns>
        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;

            // Проверяем только 2 слота
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


        /// <summary>
        /// Завершение крафта: создание выходного предмета и удаление ингредиентов
        /// </summary>
        private void ProcessCompletedCraft()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Output?.ResolvedItemstack == null)
                return;

            try
            {
                Dictionary<string, string> wildcardMatches = new Dictionary<string, string>();
                List<int> usedSlots = new List<int>();

                // Первый проход — находим совпадения (только 2 слота)
                foreach (var ingred in CurrentRecipe.Ingredients)
                {
                    for (int slotIndex = 0; slotIndex < 2; slotIndex++)
                    {
                        if (usedSlots.Contains(slotIndex)) continue;

                        var slot = GetInputSlot(slotIndex);
                        if (!slot.Empty && SatisfiesIngredient(slot.Itemstack, ingred, ref wildcardMatches))
                        {
                            usedSlots.Add(slotIndex);
                            break;
                        }
                    }
                }

                // Создаем выходной предмет
                ItemStack outputStack = CreateOutputStack(wildcardMatches);
                if (outputStack == null) return;

                // Помещаем результат в слот 2 (выходной)
                if (OutputSlot.Empty)
                {
                    OutputSlot.Itemstack = outputStack;
                }
                else if (OutputSlot.Itemstack.Collectible == outputStack.Collectible)
                {
                    int space = OutputSlot.Itemstack.Collectible.MaxStackSize - OutputSlot.Itemstack.StackSize;
                    if (space > 0)
                    {
                        int transfer = Math.Min(space, outputStack.StackSize);
                        OutputSlot.Itemstack.StackSize += transfer;
                        outputStack.StackSize -= transfer;
                    }
                    if (outputStack.StackSize > 0)
                        Api.World.SpawnItemEntity(outputStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
                else
                {
                    Api.World.SpawnItemEntity(outputStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                // Второй проход — удаляем расходуемые ингредиенты (только 2 слота)
                usedSlots.Clear();
                foreach (var ingred in CurrentRecipe.Ingredients)
                {
                    if (ingred.Quantity <= 0) continue;

                    for (int slotIndex = 0; slotIndex < 2; slotIndex++)
                    {
                        if (usedSlots.Contains(slotIndex)) continue;

                        var slot = GetInputSlot(slotIndex);
                        if (!slot.Empty && SatisfiesIngredient(slot.Itemstack, ingred, ref wildcardMatches))
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


        /// <summary>
        /// Создание выходного предмета с учетом wildcard-значений
        /// </summary>
        /// <param name="wildcardMatches"></param>
        /// <returns></returns>
        private ItemStack CreateOutputStack(Dictionary<string, string> wildcardMatches)
        {
            if (CurrentRecipe?.Output == null || Api == null) 
                return null;

            string outputCode = CurrentRecipe.Output.Code.Path;

            // Подставляем wildcard-значения в выходной код
            foreach (var match in wildcardMatches)
            {
                outputCode = outputCode.Replace("{" + match.Key + "}", match.Value);
            }

            // Создаем новый предмет
            AssetLocation outputLocation = new AssetLocation(outputCode);
            Item outputItem = Api.World.GetItem(outputLocation);
            if (outputItem == null) 
            {
                Api.Logger.Error($"EPress: Failed to find output item {outputCode}");
                return null;
            }

            return new ItemStack(outputItem, CurrentRecipe.Output.StackSize);
        }

        /// <summary>
        /// Проверка, удовлетворяет ли ItemStack требуемому ингредиенту (с учетом wildcard)
        /// </summary>
        /// <param name="stack"></param>
        /// <param name="ingred"></param>
        /// <param name="wildcardMatches"></param>
        /// <returns></returns>
        private static bool SatisfiesIngredient(ItemStack stack, CraftingRecipeIngredient ingred, ref Dictionary<string, string> wildcardMatches)
        {
            // Проверка wildcard-ингредиентов (например, "gear-*")
            if (ingred.Code.Path.Contains("*"))
            {
                string wildcardPart = GetWildcardMatch(ingred.Code, stack.Collectible.Code);
                if (wildcardPart == null) return false;

                if (!string.IsNullOrEmpty(ingred.Name))
                    wildcardMatches[ingred.Name] = wildcardPart;

                return true;
            }

            // Стандартная проверка (совпадение кода и свойств)
            return ingred.SatisfiesAsIngredient(stack);
        }


        /// <summary>
        /// Соответствие wildcard-шаблону
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="itemCode"></param>
        /// <returns></returns>
        private static string GetWildcardMatch(AssetLocation pattern, AssetLocation itemCode)
        {
            if (!WildcardUtil.Match(pattern, itemCode))
                return null;

            string patternStr = pattern.Path;
            string itemStr = itemCode.Path;

            int wildcardPos = patternStr.IndexOf('*');
            if (wildcardPos < 0) return null;

            string before = patternStr.Substring(0, wildcardPos);
            string after = patternStr.Substring(wildcardPos + 1);

            if (!itemStr.StartsWith(before)) return null;
            if (!itemStr.EndsWith(after)) return null;

            return itemStr.Substring(before.Length, itemStr.Length - before.Length - after.Length);
        }

        private ItemSlot GetInputSlot(int index) => index switch
        {
            0 => InputSlot1,
            1 => InputSlot2,
            _ => throw new ArgumentOutOfRangeException() // Теперь только 2 слота
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


            var stack = InputSlot1?.Itemstack;

            // со стаком что-то не так?
            if (stack is null ||
                stack.StackSize == 0 ||
                stack.Collectible == null ||
                stack.Collectible.Attributes == null)
                return;

            stack = InputSlot2?.Itemstack;

            // со стаком что-то не так?
            if (stack is null ||
                stack.StackSize == 0 ||
                stack.Collectible == null ||
                stack.Collectible.Attributes == null)
                return;



            bool hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            bool hasRecipe = !InputSlot1.Empty && !InputSlot2.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);
            bool isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

            // Проверяем, не изменилось ли состояние крафта
            if (isCraftingNow != _wasCraftingLastTick)
            {
                if (isCraftingNow)
                {
                    StartAnimation();
                }
                else
                {
                    RecipeProgress = 0f; // Сбрасываем прогресс при остановке
                    StopAnimation();
                }
            }

            if (isCraftingNow)
            {
                RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
                UpdateState(RecipeProgress);

                if (RecipeProgress >= 1f)
                {
                    ProcessCompletedCraft();

                    if (!HasRequiredItems()) // Проверяем возможность следующего цикла
                    {
                        StopAnimation();
                    }

                    // в любом случае сбрасываем прогресс
                    RecipeProgress = 0f;
                    UpdateState(RecipeProgress);
                }
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
        /// <summary>
        /// Старт анимации
        /// </summary>
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

        /// <summary>
        /// Стоп анимации
        /// </summary>
        private void StopAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || animUtil == null)
                return;

            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == true)
            {
               
                animUtil.StopAnimation("craft");
            }
         
        }



        /// <summary>
        /// Новый метод для проверки кадра анимации
        /// </summary>
        /// <param name="dt"></param>
        private void CheckAnimationFrame(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || animUtil == null)
                return;

            const int startFrame = 20; // Кадр, на котором нужно воспроизвести звук
            // Проверяем, активна ли анимация
            if (animUtil.activeAnimationsByAnimCode.ContainsKey("craft"))
            {
                // Получаем текущее время в миллисекундах
                long currentTime = Api.World.ElapsedMilliseconds;

                _lastAnimationCheckTime = currentTime;

                var currentFrame = animUtil.animator.Animations[0].CurrentFrame;
                // Воспроизводим звук на определенном кадре
                if (currentFrame >= startFrame && _lastSoundFrame != startFrame)
                {
                    PlayHammerSound();
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

        /// <summary>
        /// Метод для воспроизведения звука
        /// </summary>
        private void PlayHammerSound()
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
        

        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this.clientDialog?.TryClose();

        }
        #endregion
    }
}