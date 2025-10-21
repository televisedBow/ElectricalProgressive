using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ETermoGenerator;

public class BEBehaviorTermoEGenerator : BlockEntityBehavior, IElectricProducer
{
    private float PowerOrder;           // Просят столько энергии (сохраняется)
    public const string PowerOrderKey = "electricalprogressive:powerOrder";

    private float PowerGive;           // Отдаем столько энергии (сохраняется)
    public const string PowerGiveKey = "electricalprogressive:powerGive";



    private static bool IsBurned => false;



    public new BlockPos Pos => Blockentity.Pos;


    public BEBehaviorTermoEGenerator(BlockEntity blockEntity) : base(blockEntity)
    {

    }






    public void Update()
    {
        if (Blockentity is not BlockEntityETermoGenerator entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
        {
            return;
        }

        bool hasBurnout = false;

        // Проверяем наличие burnout без использования LINQ
        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            if (eParam.burnout)
            {
                hasBurnout = true;
                break; // Ранний выход при нахождении первого burnout
            }
        }

        if (hasBurnout)
        {
            ParticleManager.SpawnBlackSmoke(Api.World, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
        else if (entity.GenTemp > 20)
        {
            // Кэшируем вычисление позиции
            ParticleManager.SpawnWhiteSmoke(Api.World, Pos.ToVec3d().Add(0.4, entity.heightTermoplastin + 0.9, 0.4));
        }

    }



    public float Produce_give()
    {
        BlockEntityETermoGenerator? entity = null;
        if (Blockentity is BlockEntityETermoGenerator temp)
        {
            entity = temp;
            if (temp.GenTemp > 20)
            {
                PowerGive = temp.Power;
            }
            else
                PowerGive = 0;

        }

        return PowerGive;

    }



    public void Produce_order(float amount)
    {
        PowerOrder = amount;
    }



    public float getPowerGive() => PowerGive;


    public float getPowerOrder() => PowerOrder;



    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Blockentity is not BlockEntityETermoGenerator entity)
            return;

        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(PowerGive, PowerOrder) / entity.Power * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Production") + ": " + ((int)Math.Min(PowerGive, PowerOrder)).ToString() + "/" + ((int)entity.Power).ToString() + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:block-termoplastini") + ": " + entity.heightTermoplastin);
        stringBuilder.AppendLine("└ " + Lang.Get("kpd") + ": " + (entity.kpd * 100).ToString("F1") + " %");
    }



    /// <summary>
    /// Сохранение параметров в дерево атрибутов
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, PowerOrder);
        tree.SetFloat(PowerGiveKey, PowerGive);
    }



    /// <summary>
    /// Загрузка параметров из дерева атрибутов
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerOrder = tree.GetFloat(PowerOrderKey);
        PowerGive = tree.GetFloat(PowerGiveKey);
    }
}
