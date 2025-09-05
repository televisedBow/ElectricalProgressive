using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EFreezer2;

public class BEBehaviorEFreezer2 : BEBehaviorBase, IElectricConsumer
{
    public int PowerSetting { get; set; }

    public const string PowerSettingKey = "electricalprogressive:powersetting";

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;



    public BEBehaviorEFreezer2(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }

    public void Consume_receive(float amount)
    {
        if (PowerSetting != amount)
            PowerSetting = (int)amount;
    }

    public float Consume_request()
    {
        return _maxConsumption;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityEFreezer2 entity)
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
        return _maxConsumption;
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEFreezer2 entity || entity.AllEparams == null)
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

        // Кэшируем позицию для частиц (одинаковая для обоих типов дыма)
        var particlePos = Pos.ToVec3d().Add(0.1, 1.0, 0.1);

        if (hasBurnout)
            ParticleManager.SpawnBlackSmoke(Api.World, particlePos);

        if (prepareBurnout)
            ParticleManager.SpawnWhiteSlowSmoke(Api.World, particlePos);

        if (!hasBurnout || entity.Block.Variant["state"] == "burned")
            return;

        // Используем константы вместо создания новых строк
        const string type = "state";
        const string variant = "burned";

        // Кэшируем блок для обмена
        var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant(type, variant));
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