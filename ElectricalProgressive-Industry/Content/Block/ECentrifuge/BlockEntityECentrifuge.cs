using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ECentrifuge;

public class BlockEntityECentrifuge : BlockEntityGenericTypedContainer
{

    internal InventoryCentrifuge inventory;
    private GuiDialogCentrifuge clientDialog;
    public override string InventoryClassName => "ecentrifuge";
    public CentrifugeRecipe CurrentRecipe;
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCraftingLastTick;

    public string CurrentRecipeName;
    public float RecipeProgress;
    private ILoadedSound ambientSound;

    public virtual string DialogTitle => Lang.Get("ecentrifuge-title-gui");

    public override InventoryBase Inventory => this.inventory;

    private BlockEntityAnimationUtil animUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;


    //-------------------------------------------------------------------------------------------------------
    private BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();


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

    //передает значения из Block в BEBehaviorElectricalProgressive
    public (EParams, int) Eparams
    {
        get => this.ElectricalProgressive?.Eparams ?? (new EParams(), 0);
        set => this.ElectricalProgressive!.Eparams = value;
    }

    //передает значения из Block в BEBehaviorElectricalProgressive
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


    //-------------------------------------------------------------------------------------------------------


    private Facing facing = Facing.None;
    private AssetLocation centrifugeSound;

    public BlockEntityECentrifuge()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        this.inventory = new InventoryCentrifuge(2, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
        this.inventory.SlotModified += new Action<int>(this.OnSlotModifid);
    }



    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        this.inventory.LateInitialize(
            InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);

        this.RegisterGameTickListener(new Action<float>(this.Every500ms), 500);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            if (animUtil != null)
            {
                animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }

            centrifugeSound = new AssetLocation("electricalprogressiveindustry:sounds/ecentrifuge/centrifuge.ogg");
        }
    }

    public int GetRotation()
    {
        string side = Block.Variant["side"];
        int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }

    /// <summary>
    /// При модификации слота
    /// </summary>
    /// <param name="slotid"></param>
    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI && this.clientDialog!=null)
            this.clientDialog.Update(RecipeProgress);

        if (slotid != 0)
            return;

        // защита от горячей смены стака
        if (slotid == 0 && RecipeProgress<1f)
        {
            // в любом случае сбрасываем прогресс
            RecipeProgress = 0f;
            UpdateState(RecipeProgress);
        }

        if (this.InputSlot.Empty)
        {
            RecipeProgress = 0;
            StopAnimation();
        }
        this.MarkDirty();

        if (this.clientDialog == null || !this.clientDialog.IsOpened())
            return;

        this.clientDialog.SingleComposer.ReCompose();

        if (Api?.Side == EnumAppSide.Server)
        {
            BlockEntityECentrifuge.FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]);
            MarkDirty(true);
        }
    }

    /// <summary>
    /// Ищем рецепт для текущего стака
    /// </summary>
    /// <returns></returns>
    public static bool FindMatchingRecipe(ref CentrifugeRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        ItemSlot[] inputSlots = new ItemSlot[] { inputSlot };
        currentRecipe = null;
        currentRecipeName = string.Empty;

        foreach (CentrifugeRecipe recipe in ElectricalProgressiveRecipeManager.CentrifugeRecipes)
        {
            if (recipe.Matches(inputSlots, out _))
            {
                currentRecipe = recipe;
                currentRecipeName = recipe.Output.ResolvedItemstack.GetName();
                //MarkDirty(true);
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Ищем свойства порчи для стака и создаем рецепт на лету
    /// </summary>
    /// <returns></returns>
    public static bool FindPerishProperties(ref CentrifugeRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        var transProps = inputSlot.Itemstack.Collectible.TransitionableProps;
        if (transProps != null)
        {
            foreach (var prop in transProps)
            {
                if (prop.Type == EnumTransitionType.Perish) // может гнить?
                {
                    int inputSize = 1;
                    int outputSize = 1;
                    double coeff = 0;

                    if (prop.TransitionedStack.Code.Path == "rot") // гниль?
                    {
                        // здесь считаем входные и выходные количества
                        coeff = Math.Ceiling(8.0f / (prop.TransitionedStack.StackSize* prop.TransitionRatio));
                        inputSize = (int)coeff;
                        if (coeff < 1)
                        {
                            outputSize = (int)Math.Floor((prop.TransitionedStack.StackSize * prop.TransitionRatio) / 8.0f);
                        }
                    }
                    else
                    {
                        continue;
                    }



                    foreach (CentrifugeRecipe recipe in ElectricalProgressiveRecipeManager.CentrifugeRecipes)
                    {
                        if (recipe.Code == "default_perish" && inputSlot.StackSize >= inputSize) // нашли универсальный шаблон для гниения
                        {
                            recipe.Ingredients[0].Quantity=inputSize;
                            recipe.Output.StackSize = outputSize;

                            currentRecipe = recipe;
                            currentRecipeName = recipe.Output.ResolvedItemstack.GetName();
                            return true;
                        }
                    }


                }
            }
        }

        return false;
    }


    /// <summary>
    /// Серверный тикер
    /// </summary>
    /// <param name="dt"></param>
    private void Every500ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorECentrifuge>();
        if (beh == null)
        {
            StopAnimation();
            return;
        }


        if (this.AllEparams.Any(e => e.burnout))
            return;


        var stack = InputSlot?.Itemstack;
        
        // со стаком что-то не так?
        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
            return;





        bool hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        bool hasRecipe = !InputSlot.Empty
                         && (BlockEntityECentrifuge.FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]) || FindPerishProperties(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]));
        bool isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

        if (isCraftingNow)
        {
            if (!_wasCraftingLastTick)
            {
                StartAnimation();
                startSound();
            }

            RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
            UpdateState(RecipeProgress);

            // Обработка закончена?
            if (RecipeProgress >= 1f)
            {
                ProcessCompletedCraft();

                // Проверяем возможность следующего цикла без лишних вызовов
                bool canContinueCrafting = hasPower && !InputSlot.Empty && CurrentRecipe != null &&
                                           InputSlot.Itemstack.StackSize >= CurrentRecipe.Ingredients[0].Quantity;

                if (!canContinueCrafting)
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


    private void ProcessCompletedCraft()
    {
        // Проверяем наличие рецепта и API
        if (CurrentRecipe == null
            || Api == null
            || CurrentRecipe.Output?.ResolvedItemstack == null)
        {
            return;
        }

        try
        {
            // Создаем копию выходного предмета
            ItemStack outputItem = CurrentRecipe.Output.ResolvedItemstack.Clone();

            // Проверяем ингредиенты и слоты
            if (CurrentRecipe.Ingredients == null || CurrentRecipe.Ingredients.Length == 0 || InputSlot == null)
            {
                Api.Logger.Error("Ошибка в рецепте: отсутствуют ингредиенты или входной слот");
                return;
            }

            // Обработка выходного слота
            if (OutputSlot == null)
            {
                Api.Logger.Error("Ошибка: выходной слот не существует");
                return;
            }

            if (OutputSlot.Empty)
            {
                OutputSlot.Itemstack = outputItem;
            }
            else if (OutputSlot.Itemstack != null &&
                    outputItem.Collectible != null &&
                    OutputSlot.Itemstack.Collectible == outputItem.Collectible &&
                    OutputSlot.Itemstack.StackSize < OutputSlot.Itemstack.Collectible.MaxStackSize)
            {
                int freeSpace = OutputSlot.Itemstack.Collectible.MaxStackSize - OutputSlot.Itemstack.StackSize;
                int toAdd = Math.Min(freeSpace, outputItem.StackSize);

                OutputSlot.Itemstack.StackSize += toAdd;
                outputItem.StackSize -= toAdd;

                if (outputItem.StackSize > 0)
                {
                    Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
            else
            {
                Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            // Извлекаем ингредиенты из входного слота
            InputSlot.TakeOut(CurrentRecipe.Ingredients[0].Quantity);
            InputSlot.MarkDirty();
        }
        catch (Exception ex)
        {
            Api?.Logger.Error($"Ошибка в обработке крафта: {ex}");
        }
    }

    /// <summary>
    /// Запуск анимации
    /// </summary>
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client
            || animUtil == null
            || CurrentRecipe == null)
            return;


        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            animUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "craft",
                Code = "craft",
                AnimationSpeed = 1f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }

    }

    /// <summary>
    /// Остановка анимации
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



    protected virtual void UpdateState(float RecipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && clientDialog != null && clientDialog.IsOpened())
        {
            clientDialog.Update(RecipeProgress);
        }
        MarkDirty(true);
    }

    /// <summary>
    /// Нажатие ПКМ по блоку
    /// </summary>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (this.Api.Side == EnumAppSide.Client)
            this.toggleInventoryDialogClient(byPlayer, (CreateDialogDelegate)(() =>
            {
                this.clientDialog =
                  new GuiDialogCentrifuge(this.DialogTitle, this.Inventory, this.Pos, this.Api as ICoreClientAPI);
                this.clientDialog.Update(RecipeProgress);
                return (GuiDialogBlockEntity)this.clientDialog;
            }));
        return true;
    }



    /// <summary>
    /// Получен пакет от клиента
    /// </summary>
    /// <param name="player"></param>
    /// <param name="packetid"></param>
    /// <param name="data"></param>
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
    }

    /// <summary>
    /// Получен пакет от сервера
    /// </summary>
    /// <param name="packetid"></param>
    /// <param name="data"></param>
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

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this.Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        this.RecipeProgress = tree.GetFloat("PowerCurrent");
        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);
        ICoreAPI api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0 || this.clientDialog == null)
            return;
        this.clientDialog.Update(RecipeProgress);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute tree1 = (ITreeAttribute)new TreeAttribute();
        this.Inventory.ToTreeAttributes(tree1);
        tree["inventory"] = (IAttribute)tree1;
        tree.SetFloat("PowerCurrent", this.RecipeProgress);
    }


    /// <summary>
    /// Блок установлен
    /// </summary>
    /// <param name="byItemStack"></param>
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (ElectricalProgressive == null || byItemStack == null)
            return;

        ElectricalProgressive.Connection = Facing.AllAll;

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
    /// Блок уничтожен
    /// </summary>
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

    public ItemSlot InputSlot => this.inventory[0];
    public ItemSlot OutputSlot => this.inventory[1];

    public ItemStack InputStack
    {
        get => this.inventory[0].Itemstack;
        set
        {
            this.inventory[0].Itemstack = value;
            this.inventory[0].MarkDirty();
        }
    }

    public ItemStack OutputStack
    {
        get => this.inventory[1].Itemstack;
        set
        {
            this.inventory[1].Itemstack = value;
            this.inventory[1].MarkDirty();
        }
    }


    /// <summary>
    /// Блок выгружен из памяти
    /// </summary>
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
}