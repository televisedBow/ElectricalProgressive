using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;


namespace ElectricalProgressive.Content.Block.EOven;

public class BEBehaviorEOven : BEBehaviorBase, IElectricConsumer
{
    public int PowerSetting { get; set; }


    public const string PowerSettingKey = "electricalprogressive:powersetting";
    public const string OvenTemperatureKey = "electricalprogressive:oventemperature";

    /// <summary>
    /// Температура печи
    /// </summary>
    private float _ovenTemperature;

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    public BEBehaviorEOven(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }

    public float AvgConsumeCoeff { get; set; }


    public bool Working
    {
        get
        {
            var working = false;
            if (Blockentity is not BlockEntityEOven entity)
                return working;

            _ovenTemperature = (int)entity.OvenTemperature;

            //проверяем количество занятых слотов и готовой еды
            var stack_count = 0;
            var stack_count_perfect = 0;

            for (var index = 0; index < entity.BakeableCapacity; ++index)
            {
                var itemstack = entity.OvenInv[index].Itemstack;
                if (itemstack == null)
                    continue;

                if (ElectricalProgressiveQOL.IsFinished(itemstack.Collectible))
                {
                    stack_count_perfect++;
                }

                stack_count++;
            }
            

            if (stack_count_perfect == stack_count)   // если все готово - не работаем
                return false;
            

            if (stack_count > 0)
                return true;

            return working;
        }
    }


    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityEOven)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("├ " + Lang.Get("Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Temperature") + ": " + ((int)_ovenTemperature).ToString() + "°");

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        if (Working)
            return _maxConsumption;

        return PowerSetting = 0;
    }

    public void Consume_receive(float amount)
    {
        if (!Working)
            amount = 0;

        if (PowerSetting != amount)
            PowerSetting = (int)amount;
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEOven entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
        {
            return;
        }

        var hasBurnout = false;
        var prepareBurnout = false;

        // Однопроходная проверка всех условий
        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            hasBurnout |= eParam.burnout;
            prepareBurnout |= eParam.ticksBeforeBurnout > 0;

            // Ранний выход если оба условия уже выполнены
            if (hasBurnout || prepareBurnout)
                break;
        }

        // Кэшируем позицию для частиц
        var particlePos = Pos.ToVec3d().Add(0.1, 0, 0.1);

        if (hasBurnout)
            ParticleManager.SpawnBlackSmoke(Api.World, particlePos);

        if (prepareBurnout)
            ParticleManager.SpawnWhiteSlowSmoke(Api.World, particlePos);

        if (!hasBurnout || entity.Block.Variant["state"] == "burned")
            return;

        // Кэшируем значение side
        var side = entity.Block.Variant["side"];

        // Используем предварительно созданные массивы для избежания аллокаций
        const string stateType = "state";
        const string sideType = "side";
        const string burnedVariant = "burned";

        // Получаем блок только один раз
        var burnedBlock = Api.World.GetBlock(Block.CodeWithVariants(
            [stateType, sideType],
            [burnedVariant, side]
        ));

        Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
    }

    public float getPowerReceive()
    {
        return this.PowerSetting;
    }

    public float getPowerRequest()
    {
        if (Working)
            return _maxConsumption;

        return PowerSetting = 0;
    }

    #endregion



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat(OvenTemperatureKey, _ovenTemperature);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _ovenTemperature = tree.GetFloat(OvenTemperatureKey, 0f);
    }
}