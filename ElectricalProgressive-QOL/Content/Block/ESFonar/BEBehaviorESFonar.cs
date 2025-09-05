using ElectricalProgressive.Utils;
using System;
using System.Text;
using ElectricalProgressive.Interface;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using System.Linq;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ESFonar
{
    public class BEBehaviorESFonar : BEBehaviorBase, IElectricConsumer
    {
        /// <summary>
        /// Уровень света
        /// </summary>
        public int LightLevel { get; private set; }

        /// <summary>
        /// Ключ для сохранения уровня света в дереве атрибутов
        /// </summary>
        public const string LightLevelKey = "electricalprogressive:LightLevel";

        /// <summary>
        /// Максимальное потребление
        /// </summary>
        private readonly int _maxConsumption;

        public BEBehaviorESFonar(BlockEntity blockEntity) : base(blockEntity)
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 4);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt(LightLevelKey, LightLevel);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            LightLevel = tree.GetInt(LightLevelKey);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);

            //проверяем не сгорел ли прибор
            if (Blockentity is not BlockEntityESFonar entity)
                return;

            if (IsBurned)
            {
                return;
            }

            stringBuilder.AppendLine(StringHelper.Progressbar(this.LightLevel * 100.0f / _maxConsumption));
            stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + this.LightLevel + "/" + _maxConsumption + " " + Lang.Get("W"));

            stringBuilder.AppendLine();
        }

        #region IElectricConsumer

        public float Consume_request()
        {
            return _maxConsumption;
        }

        public void Consume_receive(float amount)
        {
            if (Api is null)
                return;

            int roundAmount = (int)Math.Round(Math.Min(amount, _maxConsumption), MidpointRounding.AwayFromZero);

            if (roundAmount == LightLevel || Block.Variant["state"] == "burned")
                return;

            //включаем если питание больше 25 %
            if (roundAmount * 4 >= _maxConsumption && Block.Variant["state"] == "disabled") // включаем если питание больше 25%
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                Blockentity.MarkDirty(true);
            }
            else if (roundAmount * 4 < _maxConsumption && Block.Variant["state"] == "enabled") // гасим если питание меньше 25%
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                Blockentity.MarkDirty(true);
            }




            // в любом случае обновляем значение
            LightLevel = roundAmount;

        }

        public float getPowerReceive()
        {
            return this.LightLevel;
        }

        public float getPowerRequest()
        {
            return _maxConsumption;
        }

        public void Update()
        {
            if (Blockentity is not BlockEntityESFonar entity || entity.AllEparams == null)
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

            // Кэшируем значения вариантов блока
            var heightStr = entity.Block.Variant["height"];
            var height = heightStr.ToFloat() - 1;

            // Кэшируем позицию для частиц
            var particlePos = Pos.ToVec3d().Add(0.1, height, 0.1);

            if (hasBurnout)
                ParticleManager.SpawnBlackSmoke(Api.World, particlePos);

            if (prepareBurnout)
                ParticleManager.SpawnWhiteSlowSmoke(Api.World, particlePos);

            if (!hasBurnout || entity.Block.Variant["state"] == "burned")
                return;

            // Кэшируем значение format
            var format = entity.Block.Variant["format"];

            // Используем предварительно созданные массивы для избежания аллокаций
            const string heightType = "height";
            const string formatType = "format";
            const string stateType = "state";
            const string burnedVariant = "burned";

            // Получаем блок только один раз
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariants(
                new[] { heightType, formatType, stateType },
                new[] { heightStr, format, burnedVariant }
            ));

            Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
        }

        #endregion
    }
}
