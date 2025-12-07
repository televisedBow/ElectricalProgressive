using ElectricalProgressive.Utils;
using EPImmersive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace EPImmersive.Content.Block.HVSFonar
{
    internal class BlockEntityHVSFonar : BlockEntityEIBase
    {
        private BEBehaviorHVSFonar Behavior => this.GetBehavior<BEBehaviorHVSFonar>();

        public bool IsEnabled
        {
            get
            {
                if (this.Behavior == null)
                    return false;

                return this.Behavior.LightLevel >= 1;
            }
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (this.EPImmersive == null || byItemStack == null)
                return;

            //задаем электрические параметры блока/проводника
            LoadImmersiveEProperties.Load(this.Block, this);
        }


    }
}

