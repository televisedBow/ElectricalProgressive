using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class BlockEntityEFuelGenerator : BlockEntityGenericTypedContainer, IHeatSource
{

    private Facing facing = Facing.None;

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




    ICoreClientAPI? capi;
    ICoreServerAPI? sapi;
    private InventoryFuelGenerator inventory;
    private GuiBlockEntityEFuelGenerator? clientDialog;


    //private float prevGenTemp = 20f;
    public float genTemp = 20f;

    /// <summary>
    /// Rэш для мэша топлива, где int - размер топлива в генераторе (от 0 до 8)
    /// </summary>
    private readonly static Dictionary<int, MeshData> MeshData = new();




    /// <summary>
    /// Максимальная температура топлива
    /// </summary>
    private int maxTemp;

    /// <summary>
    /// Текущее время горения топлива
    /// </summary>
    private float fuelBurnTime;

    /// <summary>
    /// Максимальное время горения топлива
    /// </summary>
    private float maxBurnTime;

    /// <summary>
    /// Температура в генераторе
    /// </summary>
    public float GenTemp => genTemp;


    /// <summary>
    /// Собственно выходная максимальная мощность
    /// </summary>
    public float Power
    {
        get
        {
            int envTemp = EnvironmentTemperature(); //температура окружающей среды

            if (genTemp <= envTemp) //окружающая среда теплее? 
            {
                return 1f;
            }
            else
            {
                return (genTemp - envTemp);  //учитываем разницу температур с окружающей средой 
            }


        }
    }




    /// <summary>
    /// Слот для топлива в инвентаре генератора
    /// </summary>
    public ItemSlot FuelSlot => this.inventory[0];





    /// <summary>
    /// Стак дял топлива в генераторе
    /// </summary>
    public ItemStack FuelStack
    {
        get { return this.inventory[0].Itemstack; }
        set
        {
            this.inventory[0].Itemstack = value;
            this.inventory[0].MarkDirty();
        }
    }

    /// <summary>
    /// Аниматор блока, используется для анимации открывания дверцы генератора
    /// </summary>
    private BlockEntityAnimationUtil animUtil
    {
        get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
    }





    private long listenerId;

    public override InventoryBase Inventory => inventory;

    public override string DialogTitle => Lang.Get("fuelgen");

    public override string InventoryClassName => "fuelgen";

    public BlockEntityEFuelGenerator()
    {
        this.inventory = new InventoryFuelGenerator(null!, null!);
        this.inventory.SlotModified += OnSlotModified;
    }


    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
        {
            sapi = api as ICoreServerAPI;
        }
        else
        {
            capi = api as ICoreClientAPI;

            // инициализируем аниматор
            if (animUtil != null)
            {
                animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }

        }

        this.inventory.Pos = this.Pos;
        this.inventory.LateInitialize(InventoryClassName + "-" + Pos, api);

        listenerId = this.RegisterGameTickListener(new Action<float>(OnBurnTick), 1000);

        CanDoBurn();
    }


    /// <summary>
    /// Получает угол поворота блока в градусах
    /// </summary>
    /// <returns></returns>
    public int GetRotation()
    {
        string side = Block.Variant["side"];
        int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
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
    }



    /// <summary>
    /// При ломании блока
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer byPlayer = null!)
    {
        base.OnBlockBroken(null);
    }




    /// <summary>
    /// Отвечает за тепло отдаваемое в окружающую среду
    /// </summary>
    /// <param name="world"></param>
    /// <param name="heatSourcePos"></param>
    /// <param name="heatReceiverPos"></param>
    /// <returns></returns>
    public float GetHeatStrength(
      IWorldAccessor world,
      BlockPos heatSourcePos,
      BlockPos heatReceiverPos)
    {
        return Math.Max((float)(((float)this.genTemp - 20.0F) / ((float)1300F - 20.0F) * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)), 0.0f);
    }




    /// <summary>
    /// Получает температуру окружающей среды
    /// </summary>
    /// <returns></returns>
    protected virtual int EnvironmentTemperature()
    {
        return (int)this.Api.World.BlockAccessor.GetClimateAt(this.Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, this.Api.World.Calendar.TotalDays).Temperature;
    }






    /// <summary>
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        MeshData.Clear(); //не забываем очищать кэш мэша при выгрузке блока

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        // закрываем диалоговое окно, если оно открыто
        if (this.Api is ICoreClientAPI && this.clientDialog != null)
        {
            this.clientDialog.TryClose();
            this.clientDialog = null;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(listenerId);

        // отключаем аниматор, если он есть
        if (this.Api.Side == EnumAppSide.Client && this.animUtil != null)
        {
            this.animUtil.Dispose();
        }

        // очищаем ссылки на API
        capi = null;
        sapi = null;

    }

    /// <summary>
    /// Обработчик изменения слота инвентаря
    /// </summary>
    /// <param name="slotId"></param>
    public void OnSlotModified(int slotId)
    {
        if (slotId == 0)
        {
            if (Inventory[0].Itemstack != null && !Inventory[0].Empty &&
                Inventory[0].Itemstack.Collectible.CombustibleProps != null)
            {
                if (fuelBurnTime == 0)
                    CanDoBurn();
            }
        }

        base.Block = this.Api.World.BlockAccessor.GetBlock(this.Pos);
        this.MarkDirty(this.Api.Side == EnumAppSide.Server, null);

        if (this.Api is ICoreClientAPI && this.clientDialog != null)
        {
            clientDialog.Update(genTemp, fuelBurnTime);
        }

        IWorldChunk chunkatPos = this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos);
        if (chunkatPos == null)
            return;

        chunkatPos.MarkModified();
    }

    /// <summary>
    /// Обработчик тесселяции блока, добавляет мэш блока и мэш топлива, если он есть
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator); // вызываем базовую логику тесселяции


        var stack = Inventory[0].Itemstack;
        int sizeFuel = 0; // размер топлива в генераторе

        if (stack != null && stack.Collectible.CombustibleProps != null)
        {
            // смотрим сколько топлива в генераторе
            sizeFuel = (int)(stack.StackSize * 8.0F / stack.Collectible.MaxStackSize) + 1;
            sizeFuel = Math.Clamp(sizeFuel, 1, 8); // ограничиваем размер топлива от 1 до 8
        }


        if (!MeshData.TryGetValue(sizeFuel, out var fuelMesh))
        {
            // если есть топливо, то добавляем его в мэш

            capi?.Tesselator.TesselateShape(this.Block, Vintagestory.API.Common.Shape.TryGet(Api, "electricalprogressivebasics:shapes/block/termogenerator/toplivo/toplivo-" + sizeFuel + ".json"), out fuelMesh);

            capi?.TesselatorManager.ThreadDispose(); //обязательно

            MeshData.TryAdd(sizeFuel, fuelMesh!);

        }

        if (fuelMesh != null)
        {
            mesher.AddMeshData(fuelMesh);
        }


        // если анимации нет, то рисуем блок базовый
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
        {
            return false;
        }


        return true;  // не рисует базовый блок, если есть анимация
    }

    /// <summary>
    /// Обработчик тика горения топлива
    /// </summary>
    /// <param name="deltatime"></param>
    public void OnBurnTick(float deltatime)
    {

        if (fuelBurnTime > 0f)
        {
            StartAnimation();

            genTemp = ChangeTemperature(genTemp, maxTemp, deltatime);
            fuelBurnTime -= deltatime;
            if (fuelBurnTime <= 0f)
            {
                fuelBurnTime = 0f;
                maxBurnTime = 0f;
                maxTemp = 20; // важно
                if (!Inventory[0].Empty)
                    CanDoBurn();
            }
        }
        else
        {
            StopAnimation();

            if (genTemp != 20f)
                genTemp = ChangeTemperature(genTemp, 20f, deltatime);
            CanDoBurn();
        }



        MarkDirty(true, null);


        

        // обновляем диалоговое окно на клиенте
        if (this.Api != null && this.Api.Side == EnumAppSide.Client)
        {
            if (this.clientDialog != null)
                clientDialog.Update(genTemp, fuelBurnTime);

        }

    }



    /// <summary>
    /// Запуск анимации
    /// </summary>
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client
            || animUtil == null)
            return;

        

        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
        {
            this.Block.LightHsv = new byte[] { 0, 0, 14 };

            animUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
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

        

        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
        {
            this.Block.LightHsv = new byte[] { 0, 0, 0 };

            animUtil.StopAnimation("work-on");
        }

    }



    /// <summary>
    /// Проверяет, можно ли сжечь топливо в генераторе
    /// </summary>
    private void CanDoBurn()
    {
        CombustibleProperties fuelProps = FuelSlot.Itemstack?.Collectible?.CombustibleProps ?? null!;
        if (fuelProps == null)
            return;

        if (fuelBurnTime > 0)
            return;

        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            maxBurnTime = fuelBurnTime = fuelProps.BurnDuration;
            maxTemp = fuelProps.BurnTemperature;
            FuelStack.StackSize--;
            if (FuelStack.StackSize <= 0)
            {
                FuelStack = null!;
            }

            FuelSlot.MarkDirty();
            MarkDirty(true);
        }
    }


    /// <summary>
    /// Изменяет температуру в зависимости от времени и разницы температур
    /// </summary>
    /// <param name="fromTemp"></param>
    /// <param name="toTemp"></param>
    /// <param name="deltaTime"></param>
    /// <returns></returns>
    private float ChangeTemperature(float fromTemp, float toTemp, float deltaTime)
    {
        float diff = Math.Abs(fromTemp - toTemp);
        deltaTime += deltaTime * (diff / 28f);
        if (diff < deltaTime)
        {
            return toTemp;
        }

        if (fromTemp > toTemp)
        {
            deltaTime = -deltaTime;
        }

        if (Math.Abs(fromTemp - toTemp) < 1f)
        {
            return toTemp;
        }
        return fromTemp + deltaTime;
    }








    /// <summary>
    /// Обработчик нажатия правой кнопкой мыши по блоку, открывает диалоговое окно
    /// </summary>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {

        // открываем диалоговое окно
        if (this.Api.Side == EnumAppSide.Client)
        {
            base.toggleInventoryDialogClient(byPlayer, delegate
            {
                this.clientDialog =
                    new GuiBlockEntityEFuelGenerator(DialogTitle, Inventory, this.Pos, this.capi!, this);
                clientDialog.Update(genTemp, fuelBurnTime);
                return this.clientDialog;
            });
        }
        return true;
    }




    /// <summary>
    /// При удалении блока, закрывает диалоговое окно и отключает электричество
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        var electricity = ElectricalProgressive;

        if (electricity != null)
        {
            electricity.Connection = Facing.None;
        }


        MeshData.Clear(); //не забываем очищать кэш мэша при выгрузке блока

        // закрываем диалоговое окно, если оно открыто
        if (this.Api is ICoreClientAPI && this.clientDialog != null)
        {
            this.clientDialog.TryClose();
            this.clientDialog = null;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(listenerId);

        // отключаем аниматор, если он есть
        if (this.Api.Side == EnumAppSide.Client && this.animUtil != null)
        {
            this.animUtil.Dispose();
        }

        // очищаем ссылки на API
        capi = null;
        sapi = null;
    }



    /// <summary>
    /// Сохраняет атрибуты
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        this.inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("genTemp", genTemp);
        tree.SetInt("maxTemp", maxTemp);
        tree.SetFloat("fuelBurnTime", fuelBurnTime);
        tree.SetBytes("electricalprogressive:facing", SerializerUtil.Serialize(this.facing));
    }


    /// <summary>
    /// Загружает атрибуты 
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this.inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
            Inventory.AfterBlocksLoaded(this.Api.World);
        genTemp = tree.GetFloat("genTemp", 0);
        maxTemp = tree.GetInt("maxTemp", 0);
        fuelBurnTime = tree.GetFloat("fuelBurnTime", 0);

        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            if (this.clientDialog != null)
                clientDialog.Update(genTemp, fuelBurnTime);
            MarkDirty(true, null);
        }

        try
        {
            this.facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes("electricalprogressive:facing"));
        }
        catch (Exception exception)
        {
            this.Api?.Logger.Error(exception.ToString());
        }
    }


    /// <summary>
    /// Получение информации о блоке 
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="dsc"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        if (this.FuelStack == null)
            return;

        dsc.AppendLine(Lang.Get("Contents") + ": " + (object)this.FuelStack.StackSize + "x" + (object)this.FuelStack.GetName());

    }

}