using ElectricalProgressive.Interface;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EConnector
{
    public class BEBehaviorEConnector : BlockEntityBehavior, IElectricConductor
    {
        public BEBehaviorEConnector(BlockEntity blockentity) : base(blockentity)
        {
        }

        public new BlockPos Pos => Blockentity.Pos;




        /// <summary>
        /// Обновление блока кабеля
        /// </summary>
        public void Update()
        {
            // Blockentity.MarkDirty();
        }


        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            //if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEConnector entity)
            //    return;

           
        }
    }
}
