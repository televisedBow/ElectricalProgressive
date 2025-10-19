using ElectricalProgressive.Content.Block.ECentrifuge;
using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Content.Block.EHammer;

public class BlockEntityEHammer : BlockEntityGenericTypedContainer, ITexPositionSource
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

    private static float maxTargetTemp = 1350f; //максимальная температура для нагрева

    public virtual string DialogTitle => Lang.Get("ehammer-title-gui");

    public override InventoryBase Inventory => (InventoryBase)this.inventory;

    private int _lastSoundFrame = -1;
    private long _lastAnimationCheckTime;
    private BlockEntityAnimationUtil animUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    // Новые поля для системы мешей (как в холодильнике)
    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;

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

    private AssetLocation soundHammer;
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
        this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);

        this.RegisterGameTickListener(new Action<float>(this.Every1000ms), 1000);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;

            // Инициализируем массив мешей как в холодильнике
            _meshes = new MeshData[this.inventory.Count];

            // Подписываемся на изменения инвентаря
            this.inventory.SlotModified += slotId =>
            {
                UpdateMeshes();
            };

            // Первоначальное создание мешей
            UpdateMeshes();

            if (animUtil != null)
            {
                animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }

            soundHammer = new AssetLocation("electricalprogressiveindustry:sounds/ehammer/hammer.ogg");

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
    /// Новый метод для проверки кадра анимации
    /// </summary>
    /// <param name="dt"></param>
    private void CheckAnimationFrame(float dt)
    {
        if (Api?.Side != EnumAppSide.Client || animUtil == null)
            return;

        const int startFrame = 27; // Кадр, на котором нужно воспроизвести звук
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
            soundHammer,
            Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
            null,
            false,
            32,
            1f
        );
    }

    /// <summary>
    /// Необходимо для отрисовки текстур
    /// </summary>
    /// <param name="textureCode"></param>
    /// <returns></returns>
    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            var assetLocation = default(AssetLocation?);

            // Пробуем получить текстуру из item.Textures
            if (_nowTesselatingObj is Vintagestory.API.Common.Item item)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }
            else if (_nowTesselatingObj is Vintagestory.API.Common.Block block)
            {
                if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (block.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }

            // Если не нашли, пробуем из shape.Textures
            if (assetLocation == null && _nowTesselatingShape != null)
            {
                _nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);
            }

            // Если все еще не нашли, используем домен предмета и предполагаемый путь
            if (assetLocation == null)
            {
                var domain = _nowTesselatingObj.Code.Domain;
                assetLocation = new(domain, "textures/item/" + textureCode);
                Api.World.Logger.Warning("Текстура {0} не найдена в текстурах предмета или формы, используется путь: {1}", textureCode, assetLocation);
            }

            return getOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition? getOrCreateTexPos(AssetLocation texturePath)
    {
        var textureAtlasPosition = _capi.BlockTextureAtlas[texturePath];
        if (textureAtlasPosition != null)
            return textureAtlasPosition;

        // берем только base текстуру (первую из кучи наваленных)
        var pos = texturePath.Path.IndexOf("++");
        if (pos >= 0)
            texturePath.Path = texturePath.Path.Substring(0, pos);

        var asset = _capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset != null)
        {
            _capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out var num, out textureAtlasPosition, null, 0.005f);
        }
        else
        {
            Api.World.Logger.Warning("Текстура не найдена по пути: {0}", texturePath);
        }

        return textureAtlasPosition;
    }

    public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;

    /// <summary>
    /// Обновляем mesh для конкретного слота
    /// </summary>
    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
            return;

        if (slotid >= this.inventory.Count)
            return;

        // В молоте отображаем только слот 0 (инструмент)
        if (slotid != 0)
        {
            _meshes[slotid] = null;
            return;
        }

        if (this.inventory[slotid].Empty)
        {
            _meshes[slotid] = null;
            return;
        }

        var meshData = GenMesh(this.inventory[slotid].Itemstack);
        if (meshData != null)
        {
            TranslateMesh(meshData, slotid);
            _meshes[slotid] = meshData;
        }
        else
        {
            _meshes[slotid] = null;
        }
    }

    /// <summary>
    /// Перемещаем mesh в нужную позицию для молота
    /// </summary>
    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null || slotId != 0)
            return;

        var stack = this.inventory[slotId].Itemstack;
        Vec3f origin = new Vec3f(0.5f, 0, 0.5f);

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

            meshData.Scale(origin, scaleX, scaleY, scaleZ);
            meshData.Translate(translateX, translateY + 0.95f, translateZ);
            meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);
        }
        else
        {
            meshData.Scale(origin, 0.3f, 0.3f, 0.3f);
            meshData.Translate(0f, 0.95f, 0f);
        }
    }

    /// <summary>
    /// Генерация меша для предмета (как в холодильнике)
    /// </summary>
    public MeshData? GenMesh(ItemStack stack)
    {
        if (stack == null)
            return null;

        MeshData meshData;
        try
        {
            var meshSource = stack.Collectible as IContainedMeshSource;

            if (meshSource != null)
            {
                meshData = meshSource.GenMesh(stack, _capi.BlockTextureAtlas, Pos);
                meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY * 0.0174532924f, 0f);
            }
            else
            {
                if (stack.Class == EnumItemClass.Block)
                {
                    meshData = _capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
                }
                else
                {
                    _nowTesselatingObj = stack.Collectible;
                    _nowTesselatingShape = null;

                    if (stack.Item.Shape != null)
                        _nowTesselatingShape = _capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);

                    _capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
            }
        }
        catch (Exception e)
        {
            Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Item.Code, e.Message);
            meshData = null;
        }

        return meshData;
    }

    /// <summary>
    /// Обновляет все meshы в инвентаре
    /// </summary>
    public void UpdateMeshes()
    {
        for (var i = 0; i < this.inventory.Count; i++)
            UpdateMesh(i);

        MarkDirty(true);
    }

    /// <summary>
    /// Слот модифицирован
    /// </summary>
    /// <param name="slotid"></param>
    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI && this.clientDialog != null)
            this.clientDialog.Update(RecipeProgress);

        if (slotid != 0)
            return;

        // защита от горячей смены стака
        if (slotid == 0 && RecipeProgress < 1f)
        {
            // в любом случае сбрасываем прогресс
            RecipeProgress = 0f;
            UpdateState(RecipeProgress);
        }

        // Обновляем меш при изменении входного слота
        if (slotid == 0 && Api.Side == EnumAppSide.Client)
        {
            UpdateMesh(0);
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
            FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);
            MarkDirty(true);
        }
    }

    /// <summary>
    /// Ищет подходящий рецепт
    /// </summary>
    /// <returns></returns>
    public static bool FindMatchingRecipe(ref HammerRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        ItemSlot[] inputSlots = new ItemSlot[] { inputSlot };
        currentRecipe = null;
        currentRecipeName = string.Empty;

        foreach (HammerRecipe recipe in ElectricalProgressiveRecipeManager.HammerRecipes)
        {
            if (recipe.Matches(inputSlots, out _))
            {
                currentRecipe = recipe;
                currentRecipeName = recipe.Output.ResolvedItemstack.GetName();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Тикер
    /// </summary>
    /// <param name="dt"></param>
    private void Every1000ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorEHammer>();
        // мало ли поведение не загрузилось еще
        if (beh == null)
        {
            StopAnimation();
            return;
        }

        var stack = InputSlot?.Itemstack;

        // со стаком что-то не так?
        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
            return;

        bool hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        bool hasRecipe = !InputSlot.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]); ;
        bool isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

        if (isCraftingNow) // крафтим?
        {
            // старт анимации
            StartAnimation();

            // меняем прогресс текущего крафта
            RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
            UpdateState(RecipeProgress);

            var temperature = stack.Collectible.GetTemperature(this.Api.World, stack);

            if (RecipeProgress < 0.5f)
            {
                stack.Collectible.SetTemperature(this.Api.World, stack, RecipeProgress * 2 * maxTargetTemp);
            }
            else
            {
                stack.Collectible.SetTemperature(this.Api.World, stack, maxTargetTemp);
            }

            if (RecipeProgress >= 1f)
            {
                ProcessCompletedCraft();

                // Проверяем возможность следующего цикла без лишних вызовов
                bool canContinueCrafting = hasPower && !InputSlot.Empty && CurrentRecipe != null &&
                                           InputSlot.Itemstack.StackSize >= CurrentRecipe.Ingredients[0].Quantity;

                if (!canContinueCrafting)
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

            outputItem.Collectible.SetTemperature(this.Api.World, outputItem, maxTargetTemp);

            TryMergeOrSpawn(outputItem, OutputSlot);

            // Обработка дополнительного выхода с шансом
            if (CurrentRecipe.SecondaryOutput != null &&
                CurrentRecipe.SecondaryOutput.ResolvedItemstack != null &&
                Api.World.Rand.NextDouble() < CurrentRecipe.SecondaryOutputChance)
            {
                ItemStack secondaryOutput = CurrentRecipe.SecondaryOutput.ResolvedItemstack.Clone();

                secondaryOutput.Collectible.SetTemperature(this.Api.World, secondaryOutput, maxTargetTemp);

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

            // учитываем температуру при объединении
            float stackTemp = stack.Collectible.GetTemperature(this.Api.World, stack);
            float targetstackTemp = targetSlot.Itemstack.Collectible.GetTemperature(this.Api.World, targetSlot.Itemstack);

            float stackCapacity = stackTemp * toAdd;
            float targetCapacity = targetstackTemp * targetSlot.Itemstack.StackSize;

            targetSlot.Itemstack.StackSize += toAdd;

            targetSlot.Itemstack.Collectible.SetTemperature(this.Api.World, targetSlot.Itemstack, (stackCapacity + targetCapacity) / targetSlot.Itemstack.StackSize);

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

    /// <summary>
    /// Вызывается при тесселяции блока
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator);

        // Отрисовываем меши предметов (как в холодильнике)
        if (_meshes != null)
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null)
                    mesher.AddMeshData(_meshes[i]);
            }
        }

        // если анимации нет, то рисуем блок базовый
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            return false;
        }

        return true;  // не рисует базовый блок, если есть анимация
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this.Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        this.RecipeProgress = tree.GetFloat("PowerCurrent");

        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);

        if (Api is ICoreClientAPI)
        {
            UpdateMeshes(); // обновляем меши при загрузке
        }

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

        //задаем электрические параметры блока/проводника
        LoadEProperties.Load(this.Block, this);
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

        // Очистка как в холодильнике
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
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

        // Очищаем ссылки как в холодильнике
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
        _capi = null;
    }
}