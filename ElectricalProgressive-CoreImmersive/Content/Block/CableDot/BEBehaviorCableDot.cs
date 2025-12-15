using EPImmersive.Interface;
using Vintagestory.API.Common;


namespace EPImmersive.Content.Block.CableDot
{
    public class BEBehaviorCableDot : BlockEntityBehavior, IEImmersiveConductor
    {

        private bool hasBurnout;
        private bool prepareBurnout;


        public BEBehaviorCableDot(BlockEntity blockEntity) : base(blockEntity)
        {

        }


        public void Update()
        {
            if (Blockentity is not BlockEntityCableDot entity ||
                entity.EPImmersive == null)
            {
                return;
            }

            //entity.MarkDirty();

            bool anyBurnout = false;
            bool anyPrepareBurnout = false;

            var eParam = entity.EPImmersive.MainEparams();
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


        }

    }
}
