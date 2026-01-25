using ElectricalProgressive.Content.Block.EAccumulator;
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
   
    float _power;      //мощность


    public const string PowerKey = "electricalprogressive:_power";
    

    public BEBehaviorETransformator(BlockEntity blockEntity) : base(blockEntity)
    {
        HighVoltage = MyMiniLib.GetAttributeInt(this.Block, "voltage", 32);
        LowVoltage = MyMiniLib.GetAttributeInt(this.Block, "lowVoltage", 32);
    }

    public bool IsBurned => this.Block.Variant["state"] == "burned";
    public new BlockPos Pos => this.Blockentity.Pos;

    public int HighVoltage { get; set; }

    public int LowVoltage { get; set; }



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


    private readonly BurnoutTracker _burnoutTracker = new();


    public void Update()
    {
        if (Blockentity is not BlockEntityETransformator entity ||
            entity.ElectricalProgressive?.AllEparams is null)
        {
            return;
        }

        if (_burnoutTracker.Update(entity.ElectricalProgressive.AllEparams))
            entity.MarkDirty(true);

        // Swap to burned variant if burned
        if (_burnoutTracker.HasBurnout && entity.Block.Variant["state"] != "burned")
        {
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
            Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
        }
    }


    public float getPower()
    {
        return this._power;
    }

    public void setPower(float power)
    {
        this._power = power;
    }



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerKey, _power);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _power = tree.GetFloat(PowerKey);
    }
}
