using ElectricalProgressive.Utils;

using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.EFence
{
    public class BlockEntityEFence : BlockEntityEBase
    {

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            var electricity = this.ElectricalProgressive;
            if (electricity == null)
                return;

            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this.Block, this);
        }
    }
}
