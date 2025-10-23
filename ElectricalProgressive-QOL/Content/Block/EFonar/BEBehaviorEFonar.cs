using ElectricalProgressive.Utils;
using System;
using System.Text;
using ElectricalProgressive.Interface;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EFonar
{
    public class BEBehaviorEFonar : BEBehaviorBase, IElectricConsumer
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

        public float AvgConsumeCoeff { get; set; }

        public BEBehaviorEFonar(BlockEntity blockEntity) : base(blockEntity)
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 4);
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

        public float Consume_request()
        {
            return _maxConsumption;
        }

        public void Consume_receive(float amount)
        {
            if (Api is null)
                return;

            var roundAmount = (int)Math.Round(Math.Min(amount, _maxConsumption), MidpointRounding.AwayFromZero);

            if (roundAmount == LightLevel || IsBurned) //если сгорело или не поменялось
                return;

            //включаем если питание больше 25 %
            if (roundAmount * 4 >= _maxConsumption && Block.Variant["state"] == "disabled")
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                Blockentity.MarkDirty(true);
            }
            //гасим если питание меньше 25 %
            else if (roundAmount * 4 < _maxConsumption && Block.Variant["state"] == "enabled")
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                Blockentity.MarkDirty(true);
            }



            // в любом случае обновляем значение
            LightLevel = roundAmount;

        }

        public float getPowerReceive()
        {
            return LightLevel;
        }

        public float getPowerRequest()
        {
            return _maxConsumption;
        }

        public void Update()
        {
            if (Blockentity is not BlockEntityEFonar entity ||
                entity.ElectricalProgressive == null ||
                entity.ElectricalProgressive.AllEparams is null)
            {
                return;
            }

            var hasBurnout = false;
            var prepareBurnout = false;

            // Однопроходная проверка всех условий
            foreach (var eParam in entity.ElectricalProgressive.AllEparams)
            {
                hasBurnout |= eParam.burnout;
                prepareBurnout |= eParam.ticksBeforeBurnout > 0;

                // Ранний выход если оба условия уже выполнены
                if (hasBurnout || prepareBurnout)
                    break;
            }

            // Кэшируем позицию для частиц
            var particlePos = Pos.ToVec3d().Add(0.1, 0.3, 0.1);

            if (hasBurnout)
                ParticleManager.SpawnBlackSmoke(Api.World, particlePos);

            if (prepareBurnout)
                ParticleManager.SpawnWhiteSlowSmoke(Api.World, particlePos);

            if (hasBurnout && entity.Block.Variant["state"] != "burned")
            {
                // Кэшируем блок для обмена
                var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
                Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
            }
        }


        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);

            //проверяем не сгорел ли прибор
            if (Blockentity is not BlockEntityEFonar entity)
                return;

            if (IsBurned)
            {
                return;
            }

            stringBuilder.AppendLine(StringHelper.Progressbar(LightLevel * 100.0f / _maxConsumption));
            stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + LightLevel + "/" + _maxConsumption + " " + Lang.Get("W"));

            stringBuilder.AppendLine();
        }
    }
}
