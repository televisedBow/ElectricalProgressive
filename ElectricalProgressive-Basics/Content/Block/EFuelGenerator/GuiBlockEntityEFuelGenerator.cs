﻿using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class GuiBlockEntityEFuelGenerator : GuiDialogBlockEntity
{
    private BlockEntityEFuelGenerator betestgen;
    private float _gentemp;
    private float _fuelBurntime;
    private float _waterAmount;

    public GuiBlockEntityEFuelGenerator(string dialogTitle, InventoryBase inventory, BlockPos blockEntityPos, ICoreClientAPI capi,
        BlockEntityEFuelGenerator bentity) : base(dialogTitle, inventory, blockEntityPos, capi)
    {
        if (IsDuplicate) return;

        capi.World.Player.InventoryManager.OpenInventory(inventory);
        betestgen = bentity;
        SetupDialog();
    }

    private void OnSlotModified(int slotid)
    {
        capi.Event.EnqueueMainThreadTask(SetupDialog, "termogen");
    }

    public void SetupDialog()
    {
        ElementBounds dialogBounds = ElementBounds.Fixed(250, 60);
        ElementBounds dialog = ElementBounds.Fill.WithFixedPadding(0);
        ElementBounds fuelGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 80, 50, 1, 1);
        ElementBounds stoveBounds = ElementBounds.Fixed(80, 70, 210, 150);
        
        // Просто задаем абсолютные координаты для индикатора
        // X = 100, Y = 50, ширина = 40, высота = 170 (оставляем место сверху и снизу)
        ElementBounds waterBounds = ElementBounds.Fixed(17, 40, 40, 150);
        
        ElementBounds textBounds = ElementBounds.Fixed(145, 50, 121, 100);
        
        // Не привязываем индикатор к dialog, он будет самостоятельным
        dialog.BothSizing = ElementSizing.FitToChildren;
        dialog.WithChildren(dialogBounds, fuelGrid, textBounds);
        
        ElementBounds window = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);
            
        if (capi.Settings.Bool["immersiveMouseMode"])
            window = window.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-12, 0);
        else
            window = window.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(20, 0);

        var outputText = CairoFont.WhiteDetailText().WithWeight(FontWeight.Normal);

        SingleComposer = capi.Gui.CreateCompo("termogen" + BlockEntityPosition, window)
            .AddShadedDialogBG(dialog, true, 5)
            .AddDialogTitleBar(Lang.Get("termogen"), OnTitleBarClose)
            .BeginChildElements(dialog)
            .AddDynamicCustomDraw(stoveBounds, OnBgDraw, "symbolDrawer")
            
            // Добавляем индикатор прямо в окно, а не в dialog
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, OnWaterDraw, "waterDrawer")
            
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[] { 0 }, fuelGrid, "fuelSlot")
            .AddDynamicText("", outputText, textBounds, "outputText")
            .EndChildElements()
            .Compose();
        
        // Обновляем данные при создании GUI
        Update(betestgen.GenTemp, betestgen.GetFuelBurnTime(), betestgen.WaterAmount);
    }

    private void SendInvPacket(object packet)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, packet);
    }

    private void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ctx.Save();
        var m = ctx.Matrix;
        m.Translate(GuiElement.scaled(5), GuiElement.scaled(53));
        m.Scale(GuiElement.scaled(0.25), GuiElement.scaled(0.25));
        ctx.Matrix = m;
        capi.Gui.Icons.DrawFlame(ctx);

        double dy = 210 - 210 * (_gentemp / 1300);
        ctx.Rectangle(0, dy, 200, 210 - dy);
        ctx.Clip();
        var gradient = new LinearGradient(0, GuiElement.scaled(250), 0, 0);
        gradient.AddColorStop(0, new Color(1, 1, 0, 1));
        gradient.AddColorStop(1, new Color(1, 0, 0, 1));
        ctx.SetSource(gradient);
        capi.Gui.Icons.DrawFlame(ctx, 0, false, false);
        gradient.Dispose();
        ctx.Restore();
    }

    private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ItemSlot liquidSlot = Inventory[1];
        if (liquidSlot.Empty)
            return;

        float itemsPerLitre = 1f;
        int capacity = 100; // Вместимость генератора

        WaterTightContainableProps containableProps = BlockLiquidContainerBase.GetContainableProps(liquidSlot.Itemstack);
        if (containableProps != null)
        {
            itemsPerLitre = containableProps.ItemsPerLitre;
        }

        float fullnessRelative = (float)liquidSlot.StackSize / itemsPerLitre / capacity;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);
        
        double y = (1.0 - fullnessRelative) * currentBounds.InnerHeight;

        ctx.Rectangle(0, y, currentBounds.InnerWidth, currentBounds.InnerHeight - y);

        CompositeTexture compositeTexture = containableProps?.Texture ?? 
            liquidSlot.Itemstack.Collectible.Attributes?["inContainerTexture"]
                .AsObject<CompositeTexture>(null, liquidSlot.Itemstack.Collectible.Code.Domain);

        if (compositeTexture != null)
        {
            ctx.Save();
            Matrix matrix = ctx.Matrix;
            matrix.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
            ctx.Matrix = matrix;

            AssetLocation textureLoc = compositeTexture.Base.Clone().WithPathAppendixOnce(".png");
            GuiElement.fillWithPattern(capi, ctx, textureLoc, true, false, compositeTexture.Alpha);

            ctx.Restore();
        }
    }

    public void Update(float gentemp, float burntime, float waterAmount)
    {
        if (!IsOpened()) return;

        _gentemp = gentemp;
        _fuelBurntime = burntime;
        _waterAmount = waterAmount;
        
        var newText = (int)gentemp + " °C\n" + 
                     (int)burntime + " " + Lang.Get("gui-word-seconds") + "\n" +
                     "Water: " + waterAmount.ToString("0.0") + " L";
        
        if (SingleComposer != null)
        {
            SingleComposer.GetDynamicText("outputText").SetNewText(newText);
            SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
            SingleComposer.GetCustomDraw("waterDrawer").Redraw();
        }
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnSlotModified;
    }

    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnSlotModified;
        SingleComposer.GetSlotGrid("fuelSlot").OnGuiClosed(capi);
        base.OnGuiClosed();
    }
}