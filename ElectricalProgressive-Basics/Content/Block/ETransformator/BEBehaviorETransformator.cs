using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.ETransformator;

public class BEBehaviorETransformator : BlockEntityBehavior, IElectricTransformator
{
    float maxCurrent; //максимальный ток
    float power;      //мощность


    public const string PowerKey = "electricalprogressive:power";


    public BEBehaviorETransformator(BlockEntity blockEntity) : base(blockEntity)
    {
        maxCurrent = MyMiniLib.GetAttributeFloat(this.Block, "maxCurrent", 5.0F);
    }

    public bool IsBurned => this.Block.Variant["state"] == "burned";
    public new BlockPos Pos => this.Blockentity.Pos;

    public int highVoltage => MyMiniLib.GetAttributeInt(this.Block, "voltage", 32);

    public int lowVoltage => MyMiniLib.GetAttributeInt(this.Block, "lowVoltage", 32);



    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityETransformator entity)
            return;



        if (IsBurned)
            return;

        //stringBuilder.AppendLine(StringHelper.Progressbar(getPower() / (lowVoltage * maxCurrent) * 100));
        //stringBuilder.AppendLine("└ " + Lang.Get("Power") + ": " + getPower() + " / " + lowVoltage * maxCurrent + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Power") + ": " + ((int)getPower()).ToString() + " " + Lang.Get("W"));
        stringBuilder.AppendLine();


    }


    public void Update()
    {
        if (Blockentity is not BlockEntityETransformator entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
        {
            return;
        }

        bool hasBurnout = false;
        bool prepareBurnout = false;

        // Однопроходная проверка всех условий
        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            hasBurnout |= eParam.burnout;
            prepareBurnout |= eParam.ticksBeforeBurnout > 0;

            // Ранний выход если оба условия уже выполнены
            if (hasBurnout || prepareBurnout)
                break;
        }

        // Обработка burnout
        if (hasBurnout)
        {
            ParticleManager.SpawnBlackSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

            // Проверяем и обновляем состояние блока если нужно
            if (entity.Block.Variant["state"] != "burned")
            {
                // Кэшируем блок для обмена
                var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
                Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
            }
        }

        // Обработка prepareBurnout
        if (prepareBurnout)
        {
            ParticleManager.SpawnWhiteSlowSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
        }

    }


    public float getPower()
    {
        return this.power;
    }

    public void setPower(float power)
    {
        this.power = power;
    }



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerKey, power);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        power = tree.GetFloat(PowerKey);
    }
}
