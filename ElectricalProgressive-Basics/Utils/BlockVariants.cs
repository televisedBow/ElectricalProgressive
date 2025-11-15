using ElectricalProgressive.Content.Block.ECable;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils;

public class BlockVariants
{
    public readonly Cuboidf[] CollisionBoxes= new Cuboidf[0];
    public readonly MeshData? MeshData;
    public readonly Cuboidf[] SelectionBoxes= new Cuboidf[0];

    /// <summary>
    /// Извлекаем нужный вариант блока провода
    /// </summary>
    /// <param name="api"></param>
    /// <param name="baseBlock"></param>
    /// <param name="material"></param>
    /// <param name="indexQuantity"></param>
    /// <param name="indexType"></param>
    public BlockVariants(ICoreAPI api, CollectibleObject baseBlock, int indexVoltage, string material, int indexQuantity, int indexType)
    {


        var t = new string[4];
        var v = new string[4];

        t[0] = "voltage";
        t[1] = "material";
        t[2] = "quantity";
        t[3] = "type";

        v[0] = BlockECable.Voltages[indexVoltage];
        v[1] = material;
        v[2] = BlockECable.Quantitys[indexQuantity];
        v[3] = BlockECable.Types[indexType];

        var assetLocation = baseBlock.CodeWithVariants(t, v);
        var block = api.World.GetBlock(assetLocation);

        if (block == null)
            return;

        this.CollisionBoxes = block.CollisionBoxes;
        this.SelectionBoxes = block.SelectionBoxes;

        // Используем полученный блок для тесселяции, а не baseBlock!
        if (api is ICoreClientAPI clientApi)
        {
            var cachedShape = clientApi.TesselatorManager.GetCachedShape(block.Shape.Base);
            clientApi.Tesselator.TesselateShape(block, cachedShape, out this.MeshData);
            clientApi.TesselatorManager.ThreadDispose();  //обязательно!!
        }
    }



}
