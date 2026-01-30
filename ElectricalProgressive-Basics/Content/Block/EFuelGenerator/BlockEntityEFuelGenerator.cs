﻿﻿using ElectricalProgressive.Utils;
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
    private const float WaterConsumptionRate = 0.1f;
    private float _waterAmount = 0f;

    private int _maxTemp;
    private float _fuelBurnTime;
    private float _maxBurnTime;
    
    // Поля для отслеживания последних значений GUI
    private float _lastGuiUpdateTemp = -1;
    private float _lastGuiUpdateBurnTime = -1;
    private float _lastGuiUpdateWater = -1;
    private long _lastGuiUpdateTime = 0;
    
    public float GenTemp => _genTemp;
    
    public float WaterAmount 
    { 
        get 
        {
            if (WaterSlot.Empty) return 0f;
            
            var props = BlockLiquidContainerBase.GetContainableProps(WaterSlot.Itemstack);
            if (props == null) return 0f;
            
            return (float)WaterSlot.Itemstack.StackSize / props.ItemsPerLitre;
        }
    }

    public float WaterCapacity => 100f; // 100 литров емкость бака

    public float Power
    {
        get
        {
            var envTemp = EnvironmentTemperature();
            if (_genTemp <= envTemp || _genTemp < 200 || WaterSlot.Empty)
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
        if (slotId == 0) // Слот топлива
        {
            if (!FuelSlot.Empty && FuelStack.Collectible.CombustibleProps != null && _fuelBurnTime == 0)
                CanDoBurn();
        }
        else if (slotId == 1) // Слот воды
        {
            CheckAnimationState();
        }

        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(Api.Side == EnumAppSide.Server, null);

        // Обновляем GUI с принудительным флагом при изменении слота
        UpdateGuiData(true);

        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    public void OnBurnTick(float deltatime)
    {
        if (_fuelBurnTime > 0f)
        {
            bool canProducePower = _genTemp > 200 && !WaterSlot.Empty;
            
            if (canProducePower)
            {
                StartAnimation();
                // Потребляем воду только если она есть и температура выше 200
                ConsumeWater(WaterConsumptionRate * deltatime);
            }
            else
            {
                // Если нет воды или температура упала ниже 200 - останавливаем анимацию
                StopAnimation();
            }

            _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
            _fuelBurnTime -= deltatime;
            
            if (_fuelBurnTime <= 0f)
            {
                _fuelBurnTime = 0f;
                _maxBurnTime = 0f;
                _maxTemp = 20;
                StopAnimation(); // Гарантируем остановку анимации при окончании топлива
                
                if (!FuelSlot.Empty)
                    CanDoBurn();
            }
        }
        else
        {
            // Останавливаем анимацию когда топливо кончилось
            StopAnimation();
            
            if (_genTemp != 20f)
                _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
                
            CanDoBurn();
        }

        MarkDirty();
        
        // Всегда обновляем GUI после каждого тика горения
        UpdateGuiData();
    }

    // Метод для проверки состояния анимации
    private void CheckAnimationState()
    {
        bool shouldBeAnimated = _fuelBurnTime > 0 && _genTemp > 200 && !WaterSlot.Empty;
        
        if (shouldBeAnimated)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    // Метод для добавления воды извне (вызывается из BlockEFuelGenerator)
    public bool AddWaterFromContainer(ItemStack waterStack, bool consumeFromSource = true)
    {
        if (waterStack == null) return false;
        
        var props = BlockLiquidContainerBase.GetContainableProps(waterStack);
        if (props == null || !IsWaterLiquid(waterStack))
            return false;
        
        // Конвертируем StackSize в литры
        float slotLitres = (float)waterStack.StackSize / props.ItemsPerLitre;
        float tankLitres = WaterAmount;
        float neededLitres = WaterCapacity - tankLitres;
        
        if (neededLitres > 0 && slotLitres > 0)
        {
            float takeLitres = Math.Min(neededLitres, slotLitres);
            int takeItems = (int)(takeLitres * props.ItemsPerLitre);
            
            if (takeItems <= 0) return false;
            
            // Создаем новый стак воды для бака
            ItemStack waterForTank = waterStack.Clone();
            waterForTank.StackSize = takeItems;
            
            // Добавляем воду в бак
            AddWaterToTank(waterForTank);
            
            // Убираем воду из исходного стака (если нужно)
            if (consumeFromSource)
            {
                waterStack.StackSize -= takeItems;
            }
            
            // Проверяем состояние анимации после добавления воды
            CheckAnimationState();
            
            MarkDirty();
            
            // Обновляем GUI после добавления воды
            UpdateGuiData(true);
            
            return true;
        }
        
        return false;
    }

    // Метод для добавления воды в бак
    private void AddWaterToTank(ItemStack waterStack)
    {
        if (waterStack == null || waterStack.StackSize <= 0) return;
        
        if (WaterSlot.Empty)
        {
            WaterSlot.Itemstack = waterStack;
        }
        else
        {
            // Проверяем, совпадает ли тип жидкости
            if (WaterStack.Collectible.Code == waterStack.Collectible.Code)
            {
                WaterStack.StackSize += waterStack.StackSize;
            }
            else
            {
                // Если тип жидкости другой, заменяем
                WaterSlot.Itemstack = waterStack;
            }
        }
        WaterSlot.MarkDirty();
    }

    // Метод потребления воды
    private void ConsumeWater(float litres)
    {
        if (WaterSlot.Empty) 
        {
            StopAnimation(); // Останавливаем анимацию если вода закончилась
            return;
        }

        var props = BlockLiquidContainerBase.GetContainableProps(WaterStack);
        if (props == null) 
        {
            StopAnimation();
            return;
        }
    
        int itemsToConsume = (int)(litres * props.ItemsPerLitre);
        if (itemsToConsume <= 0) return;
    
        WaterStack.StackSize -= itemsToConsume;
        if (WaterStack.StackSize <= 0)
        {
            WaterSlot.Itemstack = null;
            StopAnimation(); // Останавливаем анимацию когда вода закончилась
        }
    
        WaterSlot.MarkDirty();
    
        // Обновляем количество воды для GUI
        UpdateWaterAmount(WaterAmount);
    }
    
    // Метод для обновления количества воды
    public void UpdateWaterAmount(float newAmount)
    {
        if (Math.Abs(_waterAmount - newAmount) > 0.1f)
        {
            _waterAmount = newAmount;
            
            // Обновляем GUI при изменении количества воды
            UpdateGuiData();
            
            MarkDirty();
        }
    }

    // Проверка что жидкость является водой
    private bool IsWaterLiquid(ItemStack stack)
    {
        if (stack == null) return false;
        
        string code = stack.Collectible.Code.Path.ToLower();
        return code.Contains("water") || code.Contains("freshwater");
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
            
            // Обновляем GUI после начала горения с принудительным обновлением
            UpdateGuiData(true);
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
                // Сбрасываем кэш при открытии GUI и обновляем с актуальными значениями
                _lastGuiUpdateTemp = -1;
                _lastGuiUpdateBurnTime = -1;
                _lastGuiUpdateWater = -1;
                UpdateGuiData(true);
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

        // Проверяем состояние анимации при загрузке
        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            CheckAnimationState();
            
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                // Сбрасываем кэш при загрузке и обновляем GUI
                _lastGuiUpdateTemp = -1;
                _lastGuiUpdateBurnTime = -1;
                _lastGuiUpdateWater = -1;
                UpdateGuiData(true);
                MarkDirty();
            }
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        if (FuelStack != null)
            dsc.AppendLine(Lang.Get("Contents") + ": " + FuelStack.StackSize + "x" + FuelStack.GetName());

        dsc.AppendLine(Lang.Get("Water") + ": " + WaterAmount.ToString("0.0") + "/" + WaterCapacity + " L");
        if (WaterSlot.Empty)
            dsc.AppendLine(Lang.Get("No water - reduced power"));
            
        // Добавляем информацию о состоянии
        if (_fuelBurnTime > 0)
        {
            dsc.AppendLine(Lang.Get("Burn time") + ": " + (int)_fuelBurnTime + " " + Lang.Get("gui-word-seconds"));
            if (!WaterSlot.Empty && _genTemp > 200)
                dsc.AppendLine(Lang.Get("Working at full power"));
            else if (WaterSlot.Empty)
                dsc.AppendLine(Lang.Get("No water - generator stopped"));
        }
    }
    
    // Метод для получения времени горения (для GUI)
    public float GetFuelBurnTime()
    {
        return _fuelBurnTime;
    }
    
    // Единый метод для обновления GUI с оптимизацией
    private void UpdateGuiData(bool force = false)
    {
        if (_clientDialog == null || !_clientDialog.IsOpened())
            return;
            
        long currentTime = Api?.World?.ElapsedMilliseconds ?? 0;
        float currentWater = WaterAmount;
        float currentBurnTime = _fuelBurnTime;
        
        // Всегда обновляем при принудительном обновлении
        if (force)
        {
            _clientDialog.Update(_genTemp, currentBurnTime, currentWater);
            _lastGuiUpdateTemp = _genTemp;
            _lastGuiUpdateBurnTime = currentBurnTime;
            _lastGuiUpdateWater = currentWater;
            _lastGuiUpdateTime = currentTime;
            return;
        }
        
        // Для обычных обновлений - проверяем частоту
        if (currentTime - _lastGuiUpdateTime < 250)
            return;
            
        // Проверяем, изменились ли данные значительно
        bool tempChanged = Math.Abs(_genTemp - _lastGuiUpdateTemp) > 0.5f;
        bool burnTimeChanged = Math.Abs(currentBurnTime - _lastGuiUpdateBurnTime) > 0.5f;
        bool waterChanged = Math.Abs(currentWater - _lastGuiUpdateWater) > 0.1f;
        
        // Обновляем только если есть значимые изменения
        if (tempChanged || burnTimeChanged || waterChanged)
        {
            _clientDialog.Update(_genTemp, currentBurnTime, currentWater);
            
            // Сохраняем последние значения
            _lastGuiUpdateTemp = _genTemp;
            _lastGuiUpdateBurnTime = currentBurnTime;
            _lastGuiUpdateWater = currentWater;
            _lastGuiUpdateTime = currentTime;
        }
    }
}