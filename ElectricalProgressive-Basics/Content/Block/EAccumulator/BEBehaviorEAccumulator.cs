using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.EAccumulator;

public class BEBehaviorEAccumulator : BlockEntityBehavior, IElectricAccumulator
{
    public BEBehaviorEAccumulator(BlockEntity blockEntity) : base(blockEntity)
    {
    }






    public bool IsBurned => this.Block.Variant["state"] == "burned";

    /// <summary>
    /// Предыдущее значение емкости
    /// </summary>
    public float LastCapacity { get; set; }

    /// <summary>
    /// Текущая емкость (сохраняется)
    /// </summary>
    public float Capacity { get; set; }

    public const string CapacityKey = "electricalprogressive:capacity";

    public float MaxCapacity => MyMiniLib.GetAttributeInt(this.Block, "maxcapacity", 16000);

    float multFromDurab = 1.0F;

    public new BlockPos Pos => this.Blockentity.Pos;

    /// <summary>
    /// Мощность батареи!
    /// </summary>
    public float power => MyMiniLib.GetAttributeFloat(this.Block, "power", 128.0F);



    public float GetMaxCapacity()
    {
        return MaxCapacity * multFromDurab;
    }

    public float GetCapacity()
    {
        return Capacity;
    }

    /// <summary>
    /// Задает сразу емкость аккумулятору (вызывать только при установке аккумулятора)
    /// </summary>
    /// <returns></returns>
    public void SetCapacity(float value, float multDurab = 1.0F)
    {
        multFromDurab = multDurab;

        Capacity = value > GetMaxCapacity()
            ? GetMaxCapacity()
            : value;
    }

    public void Store(float amount)
    {
        var buf = Math.Min(Math.Min(amount, power), GetMaxCapacity() - Capacity);

        // не позволяем одним пакетом сохранить больше максимального тока.
        // В теории такого превышения и не должно случиться
        Capacity += buf;
    }

    public float Release(float amount)
    {
        var buf = Math.Min(Capacity, Math.Min(amount, power));
        Capacity -= buf;

        // выдаем пакет c учетом тока и запасов
        return buf;
    }

    public float canStore()
    {
        return Math.Min(power, GetMaxCapacity() - Capacity);
    }

    public float canRelease()
    {
        return Math.Min(Capacity, power);
    }


    public float GetLastCapacity()
    {
        return this.LastCapacity;
    }


    /// <summary>
    /// Обновление блока аккумулятора
    /// </summary>
    public void Update()
    {
        if (Blockentity is BlockEntityEAccumulator { AllEparams: not null } entity)
        {
            bool hasBurnout = false;
            bool prepareBurnout = false;

            // Однопроходная проверка условий
            foreach (var eParam in entity.AllEparams)
            {
                hasBurnout |= eParam.burnout;
                prepareBurnout |= eParam.ticksBeforeBurnout > 0;

                // Ранний выход если оба условия уже выполнены
                if (hasBurnout || prepareBurnout)
                    break;
            }

            // Кэшируем значения вариантов блока
            var tier = entity.Block.Variant["tier"];
            var state = entity.Block.Variant["state"];

            // Обработка burnout
            if (hasBurnout)
            {
                var posVec = Pos.ToVec3d().Add(0.1, 0, 0.1);

                if (tier == "tier2")
                    posVec = Pos.ToVec3d().Add(0.1, 1.0, 0.1);

                ParticleManager.SpawnBlackSmoke(Api.World, posVec);
            }

            // Обмен блока если нужно
            if (hasBurnout && state != "burned")
            {
                var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
                Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
            }

            // Обработка prepareBurnout
            if (prepareBurnout)
            {
                var posVec = Pos.ToVec3d().Add(0.1, 0, 0.1);

                if (tier == "tier2")
                    posVec = Pos.ToVec3d().Add(0.1, 1.0, 0.1);

                ParticleManager.SpawnWhiteSlowSmoke(Api.World, posVec);
            }
        }

        LastCapacity = Capacity;
    }


    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(CapacityKey, Capacity);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Capacity = tree.GetFloat(CapacityKey);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);
        
        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityEAccumulator entity)
            return;


        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(GetCapacity() * 100.0f / GetMaxCapacity()));
        stringBuilder.AppendLine("└ " + Lang.Get("Storage") + ": " + ((int)GetCapacity()).ToString() + "/" + ((int)GetMaxCapacity()).ToString() + " " + Lang.Get("J"));

        stringBuilder.AppendLine();
    }
}