using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class BlockEntityEFuelGenerator : BlockEntityGenericTypedContainer, IHeatSource
{
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    
    ICoreClientAPI _capi;
    ICoreServerAPI _sapi;
    private InventoryFuelGenerator _inventory;
    private GuiBlockEntityEFuelGenerator _clientDialog;

    private float _genTemp = 20f;
    private float _waterAmount = 0f;
    private const float WaterConsumptionRate = 0.1f;

    private int _maxTemp;
    private float _fuelBurnTime;
    private float _maxBurnTime;
    public float GenTemp => _genTemp;
    
    public float WaterAmount 
    { 
        get => _waterAmount; 
        set 
        { 
            _waterAmount = Math.Min(Math.Max(value, 0), WaterCapacity);
            MarkDirty(); 
        } 
    }

    public float WaterCapacity => 100f; // 100 литров емкость бака

    public float Power
    {
        get
        {
            var envTemp = EnvironmentTemperature();
            if (_genTemp <= envTemp || _genTemp < 200 || _waterAmount <= 0)
                return 1f;
            return (_genTemp - envTemp) * 2f;
        }
    }

    public ItemSlot FuelSlot => _inventory[0];
    public ItemSlot WaterSlot => _inventory[1];

    public ItemStack FuelStack
    {
        get => _inventory[0].Itemstack;
        set
        {
            _inventory[0].Itemstack = value;
            _inventory[0].MarkDirty();
        }
    }

    public ItemStack WaterStack
    {
        get => _inventory[1].Itemstack;
        set
        {
            _inventory[1].Itemstack = value;
            _inventory[1].MarkDirty();
        }
    }

    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    private long _listenerId;
    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("fuelgen");
    public override string InventoryClassName => "fuelgen";
    
    

    public BlockEntityEFuelGenerator()
    {
        _inventory = new InventoryFuelGenerator(null, null);
        _inventory.SlotModified += OnSlotModified;
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
            _sapi = api as ICoreServerAPI;
        else
        {
            _capi = api as ICoreClientAPI;
            if (AnimUtil != null)
                AnimUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
        }

        _inventory.Pos = Pos;
        _inventory.LateInitialize(InventoryClassName + "-" + Pos, api);

        _listenerId = RegisterGameTickListener(OnBurnTick, 1000);
        CanDoBurn();
    }

    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);
        ElectricalProgressive?.OnReceivedServerPacket(packetid, data);
    }

    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return Math.Max(((_genTemp - 20.0f) / (1300f - 20.0f) * MyMiniLib.GetAttributeFloat(Block, "maxHeat", 0.0f)), 0.0f);
    }

    protected virtual int EnvironmentTemperature()
    {
        return (int)Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, Api.World.Calendar.TotalDays).Temperature;
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        ElectricalProgressive?.OnBlockUnloaded();
        
        if (_clientDialog != null)
        {
            _clientDialog.TryClose();
            _clientDialog = null;
        }

        UnregisterGameTickListener(_listenerId);

        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil.Dispose();

        _capi = null;
        _sapi = null;
    }

    public void OnSlotModified(int slotId)
    {
        if (slotId == 0)
        {
            if (!FuelSlot.Empty && FuelStack.Collectible.CombustibleProps != null && _fuelBurnTime == 0)
                CanDoBurn();
        }
        else if (slotId == 1)
        {
            TryFillWaterFromSlot();
        }

        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(Api.Side == EnumAppSide.Server, null);

        if (_clientDialog != null)
            _clientDialog.Update(_genTemp, _fuelBurnTime, _waterAmount);

        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    public void OnBurnTick(float deltatime)
    {
        if (_fuelBurnTime > 0f)
        {
            if (_genTemp > 200)
            {
                StartAnimation();
                if (_waterAmount > 0)
                {
                    _waterAmount -= WaterConsumptionRate * deltatime;
                    if (_waterAmount < 0) _waterAmount = 0;
                }
            }

            _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
            _fuelBurnTime -= deltatime;
            if (_fuelBurnTime <= 0f)
            {
                _fuelBurnTime = 0f;
                _maxBurnTime = 0f;
                _maxTemp = 20;
                if (!FuelSlot.Empty)
                    CanDoBurn();
            }
        }
        else
        {
            if (_genTemp < 200)
                StopAnimation();
            if (_genTemp != 20f)
                _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
            CanDoBurn();
        }

        if (!WaterSlot.Empty && _waterAmount < WaterCapacity)
            TryFillWaterFromSlot();

        MarkDirty();

        if (_clientDialog != null)
            _clientDialog.Update(_genTemp, _fuelBurnTime, _waterAmount);
    }

    private void TryFillWaterFromSlot()
    {
        if (WaterSlot.Empty) return;
        
        var waterStack = WaterStack;
        var props = BlockLiquidContainerBase.GetContainableProps(waterStack);
        if (props == null || !waterStack.Collectible.Code.Path.ToLower().Contains("water"))
            return;
        
        float availableLitres = waterStack.StackSize / props.ItemsPerLitre;
        float neededLitres = WaterCapacity - _waterAmount;
        
        if (neededLitres > 0 && availableLitres > 0)
        {
            float takeLitres = Math.Min(neededLitres, availableLitres);
            int takeItems = (int)(takeLitres * props.ItemsPerLitre);
            
            waterStack.StackSize -= takeItems;
            _waterAmount += takeLitres;
            
            if (waterStack.StackSize <= 0)
                WaterSlot.Itemstack = null;
            
            WaterSlot.MarkDirty();
            MarkDirty();
        }
    }

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null) return;

        if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            Block.LightHsv = new byte[] { 0, 0, 14 };
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 2f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }
    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null) return;

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            Block.LightHsv = new byte[] { 0, 0, 0 };
            AnimUtil.StopAnimation("work-on");
        }
    }

    private void CanDoBurn()
    {
        if (FuelSlot.Empty) return;
        
        var fuelProps = FuelStack.Collectible.CombustibleProps;
        if (fuelProps == null || _fuelBurnTime > 0) return;

        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            _maxBurnTime = _fuelBurnTime = fuelProps.BurnDuration;
            _maxTemp = fuelProps.BurnTemperature;
            FuelStack.StackSize--;
            if (FuelStack.StackSize <= 0)
                FuelStack = null;
            FuelSlot.MarkDirty();
        }
    }

    private static float ChangeTemperature(float fromTemp, float toTemp, float deltaTime)
    {
        var diff = Math.Abs(fromTemp - toTemp);
        deltaTime += deltaTime * (diff / 28f);
        if (diff < deltaTime) return toTemp;
        if (fromTemp > toTemp) deltaTime = -deltaTime;
        if (Math.Abs(fromTemp - toTemp) < 1f) return toTemp;
        return fromTemp + deltaTime;
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiBlockEntityEFuelGenerator(DialogTitle, Inventory, Pos, _capi, this);
                _clientDialog.Update(_genTemp, _fuelBurnTime, _waterAmount);
                return _clientDialog;
            });
        }
        return true;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        ElectricalProgressive.Connection = Facing.None;
        
        if (_clientDialog != null)
        {
            _clientDialog.TryClose();
            _clientDialog = null;
        }

        UnregisterGameTickListener(_listenerId);

        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil.Dispose();

        _capi = null;
        _sapi = null;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        _inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("_genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
        tree.SetFloat("waterAmount", _waterAmount);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        
        if (tree.HasAttribute("inventory"))
            _inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        
        if (Api != null)
            Inventory.AfterBlocksLoaded(Api.World);
            
        _genTemp = tree.GetFloat("_genTemp", 20);
        _maxTemp = tree.GetInt("maxTemp", 20);
        _fuelBurnTime = tree.GetFloat("fuelBurnTime", 0);
        _waterAmount = tree.GetFloat("waterAmount", 0);

        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null)
        {
            _clientDialog.Update(_genTemp, _fuelBurnTime, _waterAmount);
            MarkDirty();
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        if (FuelStack != null)
            dsc.AppendLine(Lang.Get("Contents") + ": " + FuelStack.StackSize + "x" + FuelStack.GetName());

        dsc.AppendLine(Lang.Get("Water") + ": " + _waterAmount.ToString("0.0") + "/" + WaterCapacity + " L");
        if (_waterAmount <= 0)
            dsc.AppendLine(Lang.Get("No water - reduced power"));
    }
}