using ElectricalProgressive.Utils;
using EPImmersive.Interface;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace EPImmersive.Content.Block.HVTower
{
    public class BEBehaviorHVTower : BlockEntityBehavior, IEImmersiveConductor
    {
       
        private bool hasBurnout;
        private bool prepareBurnout;

        public BEBehaviorHVTower(BlockEntity blockEntity) : base(blockEntity)
        {
          
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            if (Blockentity is BlockEntityHVTower entity &&
                entity.EPImmersive != null)
            {
                // вычисляем высоту для частиц дыма
                /*
                var heightStr = entity.Block.Variant["height"];
                var height = heightStr.ToFloat() - 1;
                entity.EPImmersive.ParticlesOffsetPos.Clear();
                entity.EPImmersive.ParticlesOffsetPos.Add(new Vec3d(0.1, height, 0.1));
                */
            }
        }


        public void Update()
        {
            if (Blockentity is not BlockEntityHVTower entity ||
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
