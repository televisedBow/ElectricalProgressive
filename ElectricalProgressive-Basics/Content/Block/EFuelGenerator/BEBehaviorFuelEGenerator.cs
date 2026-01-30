﻿using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class BEBehaviorFuelEGenerator : BlockEntityBehavior, IElectricProducer
{
    private float _powerOrder;
    public const string PowerOrderKey = "electricalprogressive:powerOrder";
    private float _powerGive;
    private bool hasBurnout;
    private bool prepareBurnout;
    public const string PowerGiveKey = "electricalprogressive:powerGive";

    public new BlockPos Pos => Blockentity.Pos;

    public BEBehaviorFuelEGenerator(BlockEntity blockEntity) : base(blockEntity) { }

    public void Update()
    {
        if (Blockentity is not BlockEntityEFuelGenerator entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
        {
            return;
        }

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            if (!hasBurnout && eParam.burnout)
            {
                hasBurnout = true;
                entity.MarkDirty(true);
            }

            if (!prepareBurnout && eParam.ticksBeforeBurnout > 0)
            {
                prepareBurnout = true;
                entity.MarkDirty(true);
            }

            if (eParam.burnout)
                anyBurnout = true;

            if (eParam.ticksBeforeBurnout > 0)
                anyPrepareBurnout = true;
        }

        if (!anyBurnout && hasBurnout)
        {
            hasBurnout = false;
            entity.MarkDirty(true);
        }

        if (!anyPrepareBurnout && prepareBurnout)
        {
            prepareBurnout = false;
            entity.MarkDirty(true);
        }

        if (!hasBurnout)
        {
            if (entity.GenTemp > 200)
                entity.ElectricalProgressive.ParticlesType = 3;
            else
                entity.ElectricalProgressive.ParticlesType = 0;
        }
        else
        {
            entity.ElectricalProgressive.ParticlesType = 0;
        }
    }

    public float Produce_give()
    {
        if (Blockentity is not BlockEntityEFuelGenerator temp)
            return 0f;

        if (temp.GenTemp > 200 && !temp.WaterSlot.Empty)
            _powerGive = temp.Power;
        else
            _powerGive = 1f;
        
        return _powerGive;
    }

    public void Produce_order(float amount)
    {
        _powerOrder = amount;
    }

    public float getPowerGive() => _powerGive;
    public float getPowerOrder() => _powerOrder;

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Blockentity is not BlockEntityEFuelGenerator entity)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(_powerGive, _powerOrder) / Math.Max(1f, _powerGive) * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Production") + ": " + ((int)Math.Min(_powerGive, _powerOrder)) + "/" + Math.Max(1f, _powerGive) + " " + Lang.Get("W"));
        
        if (!entity.WaterSlot.Empty)
            stringBuilder.AppendLine("└ " + Lang.Get("Water") + ": " + entity.WaterAmount.ToString("0.0") + "/" + entity.WaterCapacity + " L");
        else
            stringBuilder.AppendLine("└ " + Lang.Get("No water") + " - " + Lang.Get("Reduced power"));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, _powerOrder);
        tree.SetFloat(PowerGiveKey, _powerGive);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _powerOrder = tree.GetFloat(PowerOrderKey);
        _powerGive = tree.GetFloat(PowerGiveKey);
    }
}