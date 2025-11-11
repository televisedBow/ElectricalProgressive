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
    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    
    ICoreClientAPI? _capi;
    ICoreServerAPI? _sapi;
    private InventoryFuelGenerator _inventory;
    private GuiBlockEntityEFuelGenerator? _clientDialog;


    private float _genTemp = 20f;

    /// <summary>
    /// Максимальная температура топлива
    /// </summary>
    private int _maxTemp;

    /// <summary>
    /// Текущее время горения топлива
    /// </summary>
    private float _fuelBurnTime;

    /// <summary>
    /// Максимальное время горения топлива
    /// </summary>
    private float _maxBurnTime;

    /// <summary>
    /// Температура в генераторе
    /// </summary>
    public float GenTemp => _genTemp;


    /// <summary>
    /// Собственно выходная максимальная мощность
    /// </summary>
    public float Power
    {
        get
        {
            var envTemp = EnvironmentTemperature(); //температура окружающей среды

            if (_genTemp <= envTemp) //окружающая среда теплее? 
            {
                return 1f;
            }
            else
            {
                return (_genTemp - envTemp);  //учитываем разницу температур с окружающей средой 
            }


        }
    }




    /// <summary>
    /// Слот для топлива в инвентаре генератора
    /// </summary>
    public ItemSlot FuelSlot => this._inventory[0];





    /// <summary>
    /// Стак для топлива в генераторе
    /// </summary>
    public ItemStack FuelStack
    {
        get { return this._inventory[0].Itemstack; }
        set
        {
            this._inventory[0].Itemstack = value;
            this._inventory[0].MarkDirty();
        }
    }

    /// <summary>
    /// Аниматор блока, используется для анимации открывания дверцы генератора
    /// </summary>
    private BlockEntityAnimationUtil AnimUtil
    {
        get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
    }





    private long _listenerId;

    public override InventoryBase Inventory => _inventory;

    public override string DialogTitle => Lang.Get("fuelgen");

    public override string InventoryClassName => "fuelgen";

    public BlockEntityEFuelGenerator()
    {
        this._inventory = new InventoryFuelGenerator(null!, null!);
        this._inventory.SlotModified += OnSlotModified;
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
            _sapi = api as ICoreServerAPI;
        }
        else
        {
            _capi = api as ICoreClientAPI;

            // инициализируем аниматор
            if (AnimUtil != null)
            {
                AnimUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }

        }

        this._inventory.Pos = this.Pos;
        this._inventory.LateInitialize(InventoryClassName + "-" + Pos, api);

        _listenerId = this.RegisterGameTickListener(new Action<float>(OnBurnTick), 1000);

        CanDoBurn();
    }


    /// <summary>
    /// Получает угол поворота блока в градусах
    /// </summary>
    /// <returns></returns>
    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
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
        return Math.Max((float)(((float)this._genTemp - 20.0F) / ((float)1300F - 20.0F) * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)), 0.0f);
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

        

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        // закрываем диалоговое окно, если оно открыто
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog.TryClose();
            this._clientDialog = null;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(_listenerId);

        // отключаем аниматор, если он есть
        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil.Dispose();
        }

        // очищаем ссылки на API
        _capi = null;
        _sapi = null;

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
                if (_fuelBurnTime == 0)
                    CanDoBurn();
            }
        }

        base.Block = this.Api.World.BlockAccessor.GetBlock(this.Pos);
        this.MarkDirty(this.Api.Side == EnumAppSide.Server, null);

        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            _clientDialog.Update(_genTemp, _fuelBurnTime);
        }

        var chunkatPos = this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos);
        if (chunkatPos == null)
            return;

        chunkatPos.MarkModified();
    }





    /// <summary>
    /// Обработчик тика горения топлива
    /// </summary>
    /// <param name="deltatime"></param>
    public void OnBurnTick(float deltatime)
    {

        if (_fuelBurnTime > 0f)
        {
            if (_genTemp>200)
                StartAnimation();

            _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
            _fuelBurnTime -= deltatime;
            if (_fuelBurnTime <= 0f)
            {
                _fuelBurnTime = 0f;
                _maxBurnTime = 0f;
                _maxTemp = 20; // важно
                if (!Inventory[0].Empty)
                    CanDoBurn();
            }
        }
        else
        {
            if (_genTemp < 200)
                StopAnimation();

            if (_genTemp != 20f)
                _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
            CanDoBurn();
        }



        MarkDirty(true, null);


        

        // обновляем диалоговое окно на клиенте
        if (this.Api != null && this.Api.Side == EnumAppSide.Client)
        {
            if (this._clientDialog != null)
                _clientDialog.Update(_genTemp, _fuelBurnTime);

        }

    }



    /// <summary>
    /// Запуск анимации
    /// </summary>
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client
            || AnimUtil == null)
            return;

        

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
        {
            this.Block.LightHsv = new byte[] { 0, 0, 14 };


            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 2f,
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
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
        {
            this.Block.LightHsv = new byte[] { 0, 0, 0 };

            AnimUtil.StopAnimation("work-on");
        }

    }



    /// <summary>
    /// Проверяет, можно ли сжечь топливо в генераторе
    /// </summary>
    private void CanDoBurn()
    {
        var fuelProps = FuelSlot.Itemstack?.Collectible?.CombustibleProps ?? null!;
        if (fuelProps == null)
            return;

        if (_fuelBurnTime > 0)
            return;

        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            _maxBurnTime = _fuelBurnTime = fuelProps.BurnDuration;
            _maxTemp = fuelProps.BurnTemperature;
            FuelStack.StackSize--;
            if (FuelStack.StackSize <= 0)
            {
                FuelStack = null!;
            }

            FuelSlot.MarkDirty();
            //MarkDirty(true);
        }
    }


    /// <summary>
    /// Изменяет температуру в зависимости от времени и разницы температур
    /// </summary>
    /// <param name="fromTemp"></param>
    /// <param name="toTemp"></param>
    /// <param name="deltaTime"></param>
    /// <returns></returns>
    private static float ChangeTemperature(float fromTemp, float toTemp, float deltaTime)
    {
        var diff = Math.Abs(fromTemp - toTemp);
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
                this._clientDialog =
                    new GuiBlockEntityEFuelGenerator(DialogTitle, Inventory, this.Pos, this._capi!, this);
                _clientDialog.Update(_genTemp, _fuelBurnTime);
                return this._clientDialog;
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


        // закрываем диалоговое окно, если оно открыто
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog.TryClose();
            this._clientDialog = null;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(_listenerId);

        // отключаем аниматор, если он есть
        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil.Dispose();
        }

        // очищаем ссылки на API
        _capi = null;
        _sapi = null;
    }



    /// <summary>
    /// Сохраняет атрибуты
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        this._inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("_genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
    }


    /// <summary>
    /// Загружает атрибуты 
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this._inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
            Inventory.AfterBlocksLoaded(this.Api.World);
        _genTemp = tree.GetFloat("_genTemp", 0);
        _maxTemp = tree.GetInt("maxTemp", 0);
        _fuelBurnTime = tree.GetFloat("fuelBurnTime", 0);

        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            if (this._clientDialog != null)
                _clientDialog.Update(_genTemp, _fuelBurnTime);
            MarkDirty(true, null);
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