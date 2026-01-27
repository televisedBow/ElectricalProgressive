using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace EPImmersive.Content.Block.Wire
{
    public class BlockWire : Vintagestory.API.Common.Block
    {

        /// <summary>
        /// Не даем разрешение размещать провод на поверхности
        /// </summary>
        /// <param name="world"></param>
        /// <param name="byPlayer"></param>
        /// <param name="itemstack"></param>
        /// <param name="blockSel"></param>
        /// <param name="failureCode"></param>
        /// <returns></returns>
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel,
            ref string failureCode)
        {
            return false;
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
            var text = inSlot.Itemstack.Block.Variant["voltage"];
            dsc.AppendLine(Lang.Get("Voltage") + ": " + text.Substring(0, text.Length - 1) + " " + Lang.Get("V"));
            dsc.AppendLine(Lang.Get("Max. current") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "maxCurrent", 0) + " " + Lang.Get("A"));
            dsc.AppendLine(Lang.Get("Resistivity") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "res", 0) + " " + Lang.Get("units"));
            dsc.AppendLine(Lang.Get("WResistance") + ": " + (inSlot.Itemstack.Block.Code.Path.Contains("isolated") ? Lang.Get("Yes") : Lang.Get("No")));
        }

    }
}
