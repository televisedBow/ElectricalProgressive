using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECable
{
    public class BEBehaviorECable : BlockEntityBehavior, IElectricConductor
    {
        public BEBehaviorECable(BlockEntity blockentity) : base(blockentity)
        {
        }

        public new BlockPos Pos => Blockentity.Pos;


        /// <summary>
        /// Подсказка при наведении на блок
        /// </summary>
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);


            //if (Blockentity is not BlockEntityECable entity)
            //    return;
            
            //stringBuilder.AppendLine("Заглушка");

        }

        /// <summary>
        /// Обновление блока кабеля
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Update()
        {
            if (Blockentity is not BlockEntityECable entity ||
                entity.ElectricalProgressive == null ||
                entity.ElectricalProgressive.AllEparams is null)
            {
                return;
            }

            var hasBurnout = false;
            var prepareBurnout = false;

            // Проверяем все параметры на наличие перегрева
            foreach (var eParam in entity.ElectricalProgressive.AllEparams)
            {
                hasBurnout |= eParam.burnout;
                prepareBurnout |= eParam.ticksBeforeBurnout > 0;

                if (hasBurnout || prepareBurnout)
                    break;
            }

            // Генерируем частицы черного дыма
            if (hasBurnout)
                ParticleManager.SpawnBlackSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

            // Генерируем частицы белого дыма
            if (prepareBurnout)
                ParticleManager.SpawnWhiteSlowSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

        }


    }
}
