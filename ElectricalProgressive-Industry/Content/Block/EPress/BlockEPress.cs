using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EPress;

public class BlockEPress : Vintagestory.API.Common.Block
{

    public static Dictionary<Item, ToolTextures> ToolTextureSubIds(ICoreAPI api)
    {
        Dictionary<Item, ToolTextures> result;

        if (api.ObjectCache.TryGetValue("pressToolTextureSubIds", out var obj)
            && obj is Dictionary<Item, ToolTextures> hammerToolTextureSubIds)
        {
            result = hammerToolTextureSubIds;
        }
        else
        {
            api.ObjectCache["pressToolTextureSubIds"] = result = new();
        }

        return result;
    }


    // Нужно, чтобы текстуры предметов также были в атласе блоков
    public override void OnCollectTextures(ICoreAPI api, ITextureLocationDictionary textureDict)
    {
        base.OnCollectTextures(api, textureDict);

        foreach (var item in api.World.Items)
        {
            // Здесь можно добавить условия для фильтрации предметов
            // Например, только те предметы, которые могут быть обработаны в молоте
            // if (item.Attributes?["processableInHammer"].AsBool() != true)
            //     continue;

            var toolTextures = new ToolTextures();

            if (item.Shape != null)
            {
                var asset = api.Assets.TryGet(item.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));

                if (asset != null)
                {
                    var shape = asset.ToObject<Shape>();

                    if (shape.Textures != null) // Нет текстур? 
                    {
                        foreach (var val in shape.Textures)
                        {
                            var ctex = new CompositeTexture(val.Value.Clone());
                            ctex.Bake(api.Assets);

                            textureDict.AddTextureLocation(new AssetLocationAndSource(ctex.Baked.BakedName,
                                "Shape code " + item.Shape.Base));
                            toolTextures.TextureSubIdsByCode[val.Key] = textureDict[new(ctex.Baked.BakedName)];
                        }
                    }
                }
            }

            foreach (var val in item.Textures)
            {
                val.Value.Bake(api.Assets);
                textureDict.AddTextureLocation(new(val.Value.Baked.BakedName, "Item code " + item.Code));
                toolTextures.TextureSubIdsByCode[val.Key] = textureDict[new(val.Value.Baked.BakedName)];
            }

            ToolTextureSubIds(api)[item] = toolTextures;
        }
    }




    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection? blockSel)
    {
        if (blockSel is null)
            return false;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        blockSel.Block = this;

        var blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (blockEntity is null)
            return true;

        if (blockEntity is BlockEntityOpenableContainer openableContainer)
            openableContainer.OnPlayerRightClick(byPlayer, blockSel);

        return true;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var blockCode = CodeWithVariant("side", "north");

        var block = world.BlockAccessor.GetBlock(blockCode);
        return new(block);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return new[] { OnPickBlock(world, pos) };
    }



    /// <summary>
    /// Получение информации о предмете в инвентаре
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("V"));
        dsc.AppendLine(Lang.Get("Consumption") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "maxConsumption", 0) + " " + Lang.Get("W"));
        dsc.AppendLine(Lang.Get("WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("Yes") : Lang.Get("No")));
    }



}