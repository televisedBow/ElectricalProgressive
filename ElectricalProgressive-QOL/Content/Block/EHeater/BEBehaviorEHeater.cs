using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EHeater
{
    public class BEBehaviorEHeater : BEBehaviorBase, IElectricConsumer
    {
        public int HeatLevel { get; private set; }


        public const string HeatLevelKey = "electricalprogressive:heatlevel";

        /// <summary>
        /// Максимальное потребление
        /// </summary>
        private readonly int _maxConsumption;

        public BEBehaviorEHeater(BlockEntity blockEntity) : base(blockEntity)
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 4);
        }

        public float AvgConsumeCoeff { get; set; }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);

            //проверяем не сгорел ли прибор
            if (Blockentity is not BlockEntityEHeater entity)
                return;

            if (IsBurned)
            {
                return;
            }

            stringBuilder.AppendLine(StringHelper.Progressbar(this.HeatLevel * 100.0f / _maxConsumption));
            stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + this.HeatLevel + "/" + _maxConsumption + " " + Lang.Get("W"));

            stringBuilder.AppendLine();
        }

        #region IElectricConsumer

        public float Consume_request()
        {
            return _maxConsumption;
        }

        public void Consume_receive(float amount)
        {
            if (this.Api is not { } api)
                return;

            var roundAmount = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
            if (roundAmount == this.HeatLevel || this.Block.Variant["state"] == "burned")
                return;

            // включаем если питание больше 1
            if (roundAmount >= 1 && this.Block.Variant["state"] == "disabled")
            {
                api.World.BlockAccessor.ExchangeBlock(api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
            }
            // гасим если питание меньше 1
            else if (roundAmount < 1 && this.Block.Variant["state"] == "enabled")
            {
                api.World.BlockAccessor.ExchangeBlock(api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
            }

            this.HeatLevel = roundAmount;
            this.Blockentity.MarkDirty(true);
        }

        public void Update()
        {
            if (Blockentity is not BlockEntityEHeater entity || entity.AllEparams == null)
                return;

            bool hasBurnout = false;
            bool prepareBurnout = false;

            // Однопроходная проверка всех условий
            foreach (var eParam in entity.AllEparams)
            {
                hasBurnout |= eParam.burnout;
                prepareBurnout |= eParam.ticksBeforeBurnout > 0;

                // Ранний выход если оба условия уже выполнены
                if (hasBurnout || prepareBurnout)
                    break;
            }

            // Кэшируем позицию для частиц
            var particlePos = Pos.ToVec3d().Add(0.1, 0, 0.1);

            if (hasBurnout)
                ParticleManager.SpawnBlackSmoke(Api.World, particlePos);

            if (prepareBurnout)
                ParticleManager.SpawnWhiteSlowSmoke(Api.World, particlePos);

            if (!hasBurnout || entity.Block.Variant["state"] == "burned")
                return;

            // Используем CodeWithVariant вместо CodeWithVariants для одного варианта
            // Это эффективнее, так как не требует создания массивов
            const string type = "state";
            const string variant = "burned";

            // Кэшируем блок для обмена
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant(type, variant));
            Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
        }

        public float getPowerReceive()
        {
            return this.HeatLevel;
        }

        public float getPowerRequest()
        {
            return _maxConsumption;
        }

        #endregion


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt(HeatLevelKey, HeatLevel);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            HeatLevel = tree.GetInt(HeatLevelKey);
        }


    }
}
