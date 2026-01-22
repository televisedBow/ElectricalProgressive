using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ESolarGenerator;

public class BlockEntityESolarGenerator : BlockEntityEFacingBase
{
    private Facing _facing = Facing.None;

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public Facing Facing
    {
        get => this._facing;
        set
        {
            if (value != this._facing)
            {
                this.ElectricalProgressive!.Connection =
                    FacingHelper.FullFace(this._facing = value);
            }
        }
    }


    ICoreClientAPI? _capi;
    ICoreServerAPI? _sapi;

    //private float prevGenTemp = 20f;
    public float _genTemp = 20f;

    /// <summary>
    /// Rэш для мэша топлива, где int - размер топлива в генераторе (от 0 до 8)
    /// </summary>
    private static readonly Dictionary<int, MeshData> MeshData = new();


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
            return 100;
        }
    }

    /// <summary>
    /// КПД генератора в долях
    /// </summary>
    public float Kpd;

    /// <summary>
    /// Горизонтальные направления для смещения
    /// </summary>
    private static readonly BlockFacing[] OffsetsHorizontal = BlockFacing.HORIZONTALS;

    /// <summary>
    /// Сколько термопластин установлено в генераторе по высоте
    /// </summary>
    public int HeightTermoplastin = 0;

    /// <summary>
    /// Аниматор блока, используется для анимации открывания дверцы генератора
    /// </summary>
    private BlockEntityAnimationUtil AnimUtil
    {
        get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
    }


    /// <summary>
    /// Запускает анимацию открытия дверцы
    /// </summary>
    public new void OpenLid()
    {
        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("open") == false)
        {
            AnimUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "open",
                Code = "open",
                AnimationSpeed = 1.8f,
                EaseOutSpeed = 6,
                EaseInSpeed = 15
            });

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 11 };

            //добавляем звук
            _capi?.World.PlaySoundAt(new AssetLocation("game:sounds/block/cokeovendoor-open"), Pos.X, Pos.Y, Pos.Z,
                null, false, 8.0F, 0.4F);
        }
    }


    /// <summary>
    /// Закрывает дверцу генератора, останавливая анимацию открытия, если она запущена
    /// </summary>
    public new void CloseLid()
    {
        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("open") == true)
        {
            AnimUtil?.StopAnimation("open");

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 0 };

            //добавляем звук
            _capi?.World.PlaySoundAt(new AssetLocation("game:sounds/block/cokeovendoor-close"), Pos.X, Pos.Y, Pos.Z,
                null, false, 8.0F, 0.4F);
        }
    }


    private long _listenerId;


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
        }

        _listenerId = this.RegisterGameTickListener(new Action<float>(OnBurnTick), 1000);
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
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        MeshData.Clear(); //не забываем очищать кэш мэша при выгрузке блока

        this.ElectricalProgressive
            ?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

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
    /// Обработчик тика горения топлива
    /// </summary>
    /// <param name="deltatime"></param>
    public void OnBurnTick(float deltatime)
    {
        Calculate_kpd();
    }


    /// <summary>
    /// Расчет КПД генератора
    /// </summary>
    private void Calculate_kpd()
    {
        var accessor = Api.World.BlockAccessor;
        Kpd = accessor.GetLightLevel(Pos, EnumLightLevelType.TimeOfDaySunLight) / 32f;
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
        tree["inventory"] = invtree;
        tree.SetFloat("_genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
        tree.SetBytes("electricalprogressive:facing", SerializerUtil.Serialize(this._facing));
    }


    /// <summary>
    /// Загружает атрибуты 
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        try
        {
            this._facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes("electricalprogressive:facing"));
        }
        catch (Exception exception)
        {
            this.Api?.Logger.Error(exception.ToString());
        }
    }
}