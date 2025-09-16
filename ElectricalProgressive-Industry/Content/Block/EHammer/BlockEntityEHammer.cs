using ElectricalProgressive.Content.Block.ECentrifuge;
using ElectricalProgressive.RicipeSystem;
using ElectricalProgressive.RicipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHammer;

public class BlockEntityEHammer : BlockEntityGenericTypedContainer
{
    
    internal InventoryHammer inventory;
    private GuiDialogHammer clientDialog;
    public override string InventoryClassName => "ehammer";
    public HammerRecipe CurrentRecipe;
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCraftingLastTick;
    public ItemSlot InputSlot => this.inventory[0];
    public ItemSlot OutputSlot => this.inventory[1];
    public ItemSlot SecondaryOutputSlot => this.inventory[2]; // Новый слот для дополнительного выхода

    public string CurrentRecipeName;
    public float RecipeProgress;
    private ILoadedSound ambientSound;

    public virtual string DialogTitle => Lang.Get("ehammer-title-gui");

    public override InventoryBase Inventory => (InventoryBase)this.inventory;

    
    private BlockEntityAnimationUtil animUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;


    //--------------------------------------------------------------------------------


    private BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

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

    //--------------------------------------------------------------------------------



    private Facing facing = Facing.None;

    public BlockEntityEHammer()
    { 
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        this.inventory = new InventoryHammer(3, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
        this.inventory.SlotModified += new Action<int>(this.OnSlotModifid);
    }


    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        this.inventory.LateInitialize(InventoryClassName +"-"+ this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
        this.RegisterGameTickListener(new Action<float>(this.Every500ms), 500);
        
        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            if (animUtil != null)
            {
                animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }
        }
    }
    
    public int GetRotation()
    {
        string side = Block.Variant["side"];
        int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }
    

    /// <summary>
    /// Слот модифицирован
    /// </summary>
    /// <param name="slotid"></param>
    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI && this.clientDialog!=null)
            this.clientDialog.Update(RecipeProgress);

        if (slotid != 0)
            return;

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
            FindMatchingRecipe();
            MarkDirty(true);
        }
    }
    



    /// <summary>
    /// Ищет подходящий рецепт
    /// </summary>
    /// <returns></returns>
    public bool FindMatchingRecipe()
    {
        ItemSlot[] inputSlots = new ItemSlot[] { inventory[0] };
        CurrentRecipe = null;
        CurrentRecipeName = string.Empty;

        foreach (HammerRecipe recipe in ElectricalProgressiveRecipeManager.HammerRecipes)
        {
            
            if (recipe.Matches(inputSlots, out int outsize))
            {
                CurrentRecipe = recipe;
                CurrentRecipeName = recipe.Output.ResolvedItemstack.GetName();
                MarkDirty(true);
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Тикер
    /// </summary>
    /// <param name="dt"></param>
    private void Every500ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null)
        {
            StopAnimation();
            stopSound();
            return;
        }

        bool hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        bool hasRecipe = !InputSlot.Empty && FindMatchingRecipe();
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
                else
                {
                    RecipeProgress = 0f; // Сбрасываем для нового цикла
                    UpdateState(RecipeProgress);
                }
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


    /// <summary>
    /// Обработка завершенного крафта
    /// </summary>
    private void ProcessCompletedCraft()
    {
        if (CurrentRecipe == null || Api == null || CurrentRecipe.Output?.ResolvedItemstack == null) 
        {
            return;
        }

        try
        {
            // Обработка основного выхода
            ItemStack outputItem = CurrentRecipe.Output.ResolvedItemstack.Clone();
            TryMergeOrSpawn(outputItem, OutputSlot);

            // Обработка дополнительного выхода с шансом
            if (CurrentRecipe.SecondaryOutput != null && 
                CurrentRecipe.SecondaryOutput.ResolvedItemstack != null &&
                Api.World.Rand.NextDouble() < CurrentRecipe.SecondaryOutputChance)
            {
                ItemStack secondaryOutput = CurrentRecipe.SecondaryOutput.ResolvedItemstack.Clone();
                TryMergeOrSpawn(secondaryOutput, SecondaryOutputSlot);
            }

            // Извлекаем ингредиенты
            InputSlot.TakeOut(CurrentRecipe.Ingredients[0].Quantity);
            InputSlot.MarkDirty();
        }
        catch (Exception ex)
        {
            Api?.Logger.Error($"Ошибка в обработке крафта: {ex}");
        }
    }


    /// <summary>
    /// Попытка сложить в слот или заспавнить в мир
    /// </summary>
    /// <param name="stack"></param>
    /// <param name="targetSlot"></param>
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


    /// <summary>
    /// Старт анимации
    /// </summary>
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || animUtil == null || CurrentRecipe == null) return;


        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            animUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "craft",
                Code = "craft",
                AnimationSpeed = 4.3f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }

    }

    /// <summary>
    /// Стоп анимации
    /// </summary>
    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || animUtil == null) return;

        try
        {
            animUtil.StopAnimation("craft");
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Error stopping animation: {ex}");
        }
    }    
    
    /// <summary>
    /// Включение звука
    /// </summary>
    public void startSound()
    {
        if (this.ambientSound != null)
            return;
        ICoreAPI api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
            return;
        this.ambientSound = (this.Api as ICoreClientAPI).World.LoadSound(new SoundParams()
        {
            Location = new AssetLocation("electricalprogressiveindustry:sounds/ehammer/hammer.ogg"),
            ShouldLoop = true,
            Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 0.75f
        });
        this.ambientSound.Start();
    }


    /// <summary>
    /// Выключение звука
    /// </summary>
    public void stopSound()
    {
        if (this.ambientSound == null)
            return;
        this.ambientSound.Stop();
        this.ambientSound.Dispose();
        this.ambientSound = (ILoadedSound) null;
    }

    /// <summary>
    /// Обновление состояния
    /// </summary>
    /// <param name="RecipeProgress"></param>
    protected virtual void UpdateState(float RecipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && clientDialog != null && clientDialog.IsOpened())
        {
            clientDialog.Update(RecipeProgress);
        }
        MarkDirty(true);
    }

    /// <summary>
    /// Игрок нажал ПКМ по блоку
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
                  new GuiDialogHammer(this.DialogTitle, this.Inventory, this.Pos, this.Api as ICoreClientAPI);
                this.clientDialog.Update(RecipeProgress);
                return (GuiDialogBlockEntity)this.clientDialog;
            }));
        return true;
    }



    /// <summary>
    /// Получение пакета с клиента
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
    /// Получение пакета с сервера
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
    /// Блок удален
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
    /// Выгрузка блока из памяти
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.clientDialog?.TryClose();
        if (this.ambientSound == null)
            return;
        this.ambientSound.Stop();
        this.ambientSound.Dispose();
        this.ambientSound = (ILoadedSound) null;
    }
}