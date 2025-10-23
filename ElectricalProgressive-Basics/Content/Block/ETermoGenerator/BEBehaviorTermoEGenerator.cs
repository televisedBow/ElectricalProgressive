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
    private float _powerOrder;           // Просят столько энергии (сохраняется)
    public const string PowerOrderKey = "electricalprogressive:powerOrder";

    private float _powerGive;           // Отдаем столько энергии (сохраняется)
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

        var hasBurnout = false;

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
        // отсекаем внештатные ситуации
        if (Blockentity is not BlockEntityETermoGenerator temp)
        {
            return 0f;
        }

        // отдаём энергию только если температура генератора выше 20 градусов
        if (temp.GenTemp > 20)
            _powerGive = temp.Power;
        else
            _powerGive = 0;


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

        if (Blockentity is not BlockEntityETermoGenerator entity)
            return;

        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(_powerGive, _powerOrder) / entity.Power * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Production") + ": " + ((int)Math.Min(_powerGive, _powerOrder)).ToString() + "/" + ((int)entity.Power).ToString() + " " + Lang.Get("W"));
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
