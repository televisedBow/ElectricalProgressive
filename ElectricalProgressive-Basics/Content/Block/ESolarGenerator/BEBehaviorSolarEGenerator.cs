using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolarGenerator;

public class BEBehaviorSolarEGenerator : BlockEntityBehavior, IElectricProducer
{
    private float _powerOrder; // Просят столько энергии (сохраняется)
    public const string PowerOrderKey = "electricalprogressive:powerOrder";

    private float _powerGive; // Отдаем столько энергии (сохраняется)
    public const string PowerGiveKey = "electricalprogressive:powerGive";

    public new BlockPos Pos => Blockentity.Pos;


    public BEBehaviorSolarEGenerator(BlockEntity blockEntity) : base(blockEntity)
    {
    }


    public void Update()
    {
        // No burn for solar panels at this time
    }


    public float Produce_give()
    {
        if (Blockentity is not BlockEntityESolarGenerator temp)
        {
            return 0f;
        }
        // Only give
        _powerGive = temp.Power * temp.Kpd;
        return _powerGive;
    }


    public void Produce_order(float amount)
    {
        _powerOrder = amount;
    }


    public float getPowerGive() => _powerGive;


    public float getPowerOrder() => _powerOrder;


    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Blockentity is not BlockEntityESolarGenerator entity)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(_powerGive, _powerOrder) / entity.Power * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Production") + ": " +
                                 ((int)Math.Min(_powerGive, _powerOrder)).ToString() + "/" +
                                 ((int)entity.Power).ToString() + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("kpd") + ": " + (entity.Kpd * 100).ToString("F1") + " %");
    }


    /// <summary>
    /// Сохранение параметров в дерево атрибутов
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, _powerOrder);
        tree.SetFloat(PowerGiveKey, _powerGive);
    }


    /// <summary>
    /// Загрузка параметров из дерева атрибутов
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _powerOrder = tree.GetFloat(PowerOrderKey);
        _powerGive = tree.GetFloat(PowerGiveKey);
    }
}