using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace EPImmersive.Content.Block.CableDot
{
    internal class BlockEntityCableDot : BlockEntityEIBase
    {
        private BEBehaviorCableDot Behavior => GetBehavior<BEBehaviorCableDot>();


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetBytes(FacingKey, SerializerUtil.Serialize(this.Facing));
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            try
            {
                this.Facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
            }
            catch (Exception exception)
            {
                this.Api?.Logger.Error(exception.ToString());
            }
        }


        private Facing _facing = Facing.None;

        public Facing Facing
        {
            get => _facing;
            set
            {
                if (value == _facing)
                    return;

                _facing = value;
            }
        }

        public const string FacingKey = "electricalprogressive:facing";
    }
}

