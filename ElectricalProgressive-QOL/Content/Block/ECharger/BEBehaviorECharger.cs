using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.ECharger;

public class BEBehaviorECharger : BEBehaviorBase, IElectricConsumer
{
    /// <summary>
    /// Мощность в заряднике
    /// </summary>
    public int PowerSetting { get; set; }


    public const string PowerSettingKey = "electricalprogressive:powersetting";


    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    public BEBehaviorECharger(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 200);
    }

    public bool Working
    {
        get
        {
            var working = false;
            int durability;         //текущая прочность
            int maxDurability;      //максимальная прочность

            if (Blockentity is not BlockEntityECharger entityECharger)
                return working;

            var entityStack = entityECharger.Inventory[0]?.Itemstack;

            // со стаком что - то не так?
            if (entityStack is null ||
                entityStack.StackSize == 0 ||
                entityStack.Collectible==null ||
                entityStack.Collectible.Attributes == null)
                return working = false;

            if (entityStack.Item != null &&
                entityStack.Collectible.Attributes["chargable"].AsBool(false)) //предмет?
            {
                durability = entityStack.Attributes.GetInt("durability");
                maxDurability = entityStack.Collectible.GetMaxDurability(entityStack);
                working = durability < maxDurability;
            }
            else if (entityStack.Block is IEnergyStorageItem) //блок?
            {
                durability = entityStack.Attributes.GetInt("durability");
                maxDurability = entityStack.Collectible.GetMaxDurability(entityStack);
                working = durability < maxDurability;
            }

            return working;
        }
    }

    public float AvgConsumeCoeff { get; set; }

    public void Consume_receive(float amount)
    {
        if (!Working)
            amount = 0;

        if (this.PowerSetting != amount)
            this.PowerSetting = (int)amount;
    }

    public float Consume_request()
    {
        if (Working)
            return _maxConsumption;

        return PowerSetting = 0;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityECharger entity)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("W"));

        stringBuilder.AppendLine();
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

    public void Update()
    {
        if (Blockentity is not BlockEntityECharger entity || entity.AllEparams == null)
            return;

        bool hasBurnout = false;
        bool prepareBurnout = false;

        // Однопроходная проверка всех условий
        foreach (var eParam in entity.AllEparams)
        {
            hasBurnout |= eParam.burnout;
            prepareBurnout |= eParam.ticksBeforeBurnout > 0;

            // Ранний выход если оба условия уже выполнены
            if (hasBurnout || prepareBurnout)
                break;
        }

        if (hasBurnout)
            ParticleManager.SpawnBlackSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

        if (prepareBurnout)
            ParticleManager.SpawnWhiteSlowSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

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
            new[] { stateType, sideType },
            new[] { burnedVariant, side }
        ));

        Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
    }





    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
    }
}