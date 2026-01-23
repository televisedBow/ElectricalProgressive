using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ESolarGenerator;

public class BlockEntityESolarGenerator : BlockEntityEFacingBase
{
    private Facing _facing = Facing.None;

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();


    /// <summary>
    /// Rэш для мэша топлива, где int - размер топлива в генераторе (от 0 до 8)
    /// </summary>
    private static readonly Dictionary<int, MeshData> MeshData = new();


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

  
    private long _listenerId;


    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        _listenerId = this.RegisterGameTickListener(new Action<float>(OnSunTick), 1000);
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
    }

    /// <summary>
    /// Обработчик тика горения топлива
    /// </summary>
    /// <param name="deltatime"></param>
    public void OnSunTick(float deltatime)
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

    }


    /// <summary>
    /// Сохраняет атрибуты
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
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