using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class GuiBlockEntityEFuelGenerator : GuiDialogBlockEntity
{
    private BlockEntityEFuelGenerator betestgen;
    private float _gentemp;
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
        ElementBounds fuelGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 17, 50, 1, 1);
        ElementBounds waterGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 17, 100, 1, 1);
        ElementBounds stoveBounds = ElementBounds.Fixed(17, 50, 210, 150);
        ElementBounds waterBounds = ElementBounds.Fixed(17, 100, 210, 50);
        ElementBounds textBounds = ElementBounds.Fixed(115, 60, 121, 100);
        
        dialog.BothSizing = ElementSizing.FitToChildren;
        dialog.WithChildren(dialogBounds, fuelGrid, waterGrid, waterBounds, textBounds);
        
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
            .AddDynamicCustomDraw(waterBounds, OnWaterDraw, "waterDrawer")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[] { 0 }, fuelGrid, "fuelSlot")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[] { 1 }, waterGrid, "waterSlot")
            .AddDynamicText("", outputText, textBounds, "outputText")
            .EndChildElements()
            .Compose();
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
        ctx.Save();
        
        // Рамка бака
        ctx.SetSourceRGB(0.3, 0.3, 0.3);
        ctx.Rectangle(0, 0, currentBounds.OuterWidth, currentBounds.OuterHeight);
        ctx.Stroke();
        
        // Уровень воды
        float waterPercentage = _waterAmount / 100f;
        float waterHeight = (float)(currentBounds.OuterHeight * waterPercentage);
        
        // Вода
        ctx.Rectangle(2, currentBounds.OuterHeight - waterHeight, currentBounds.OuterWidth - 4, waterHeight);
        var gradient = new LinearGradient(0, currentBounds.OuterHeight - waterHeight, 0, currentBounds.OuterHeight);
        gradient.AddColorStop(0, new Color(0.2, 0.4, 1.0, 0.8));
        gradient.AddColorStop(1, new Color(0.1, 0.2, 0.8, 0.8));
        ctx.SetSource(gradient);
        ctx.Fill();
        gradient.Dispose();
        
        // Текст
        ctx.SetSourceRGB(1, 1, 1);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        string waterText = $"{_waterAmount:0.0}/100 L";
        var extents = ctx.TextExtents(waterText);
        ctx.MoveTo((currentBounds.OuterWidth - extents.Width) / 2, currentBounds.OuterHeight / 2);
        ctx.ShowText(waterText);
        
        ctx.Restore();
    }

    public void Update(float gentemp, float burntime, float waterAmount)
    {
        if (!IsOpened()) return;

        _gentemp = gentemp;
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
        SingleComposer.GetSlotGrid("waterSlot").OnGuiClosed(capi);
        base.OnGuiClosed();
    }
}