using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace EPImmersive.Content.Block.CableDot
{
    internal class BlockEntityCableDotRoof : BlockEntityEIBase
    {
        private BEBehaviorCableDot Behavior => GetBehavior<BEBehaviorCableDot>();



        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RotationCache = CreateRotationCache();
        }


        private static Dictionary<Facing, RotationData> CreateRotationCache()
        {
            return new Dictionary<Facing, RotationData>
            {
                { Facing.UpNorth, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.UpEast, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.UpSouth, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.UpWest, new RotationData(0.0f, 90.0f, 0.0f) },
            };
        }


    }
}

