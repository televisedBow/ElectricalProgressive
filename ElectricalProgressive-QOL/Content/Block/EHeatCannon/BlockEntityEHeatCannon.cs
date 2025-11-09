using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHeatCannon
{
    public class BlockEntityEHeatCannon : BlockEntityEFacingBase, IHeatSource
    {
        private BEBehaviorEHeatCannon Behavior => this.GetBehavior<BEBehaviorEHeatCannon>();

        public bool IsEnabled => this.Behavior?.HeatLevel >= 1;

        public override Facing GetConnection(Facing value)
        {
            return FacingHelper.FullFace(value);
        }

        private BlockEntityAnimationUtil AnimUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;



        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            

            if (api.Side == EnumAppSide.Client)
            {
                this.RegisterGameTickListener(new Action<float>(this.Every1000Ms), 1000);
                if (AnimUtil != null)
                {
                    AnimUtil.InitializeAnimator("eheatcannon", null, null, new Vec3f(0, GetRotation(), 0f));
                }
                
            }
        }


        private void Every1000Ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorEHeatCannon>();
            // мало ли поведение не загрузилось еще
            if (beh == null)
            {
                StopAnimation();
                return;
            }

            // включает и выключает анимацию
            if (this.Block.Variant["state"] == "enabled")
            {
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }

        }




        public int GetRotation()
        {
            var side = Block.Variant["side"];
            var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
            return adjustedIndex * 90;
        }


        /// <summary>
        /// Старт анимации
        /// </summary>
        private void StartAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
            {
                AnimUtil.InitializeAnimator("eheatcannon", null, null, new Vec3f(0, GetRotation(), 0f));

                AnimUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "work-on",
                    Code = "work-on",
                    AnimationSpeed = 1.0f,
                    EaseOutSpeed = 2.0f,
                    EaseInSpeed = 1f
                });
            }
        }

        /// <summary>
        /// Стоп анимации
        /// </summary>
        private void StopAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
            {
                AnimUtil.StopAnimation("work-on");
            }
        }


        /// <summary>
        /// Вызывается при тесселяции блока
        /// </summary>
        /// <param name="mesher"></param>
        /// <param name="tesselator"></param>
        /// <returns></returns>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            base.OnTesselation(mesher, tesselator);


            // если анимации нет, то рисуем блок базовый
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false && this.Block.Variant["state"] == "disabled")
            {
                return false;
            }
            else if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true && this.Block.Variant["state"] == "enabled")
            {
                return true;
            }

            return false;  // не рисует базовый блок, если есть анимация
        }


        /// <summary>
        /// Отвечает за тепло отдаваемое в окружающую среду
        /// </summary>
        /// <param name="world"></param>
        /// <param name="heatSourcePos"></param>
        /// <param name="heatReceiverPos"></param>
        /// <returns></returns>
        public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
        {
            if (this.Behavior == null)
                return 0.0f;

            return this.Behavior.HeatLevel / this.Behavior.getPowerRequest() * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F);
        }


        /// <summary>
        /// Выгруженный блок
        /// </summary>
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();


            StopAnimation();

            if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
            {
                this.AnimUtil.Dispose();
            }
        }



        /// <summary>
        /// Блок удален из мира
        /// </summary>
        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            StopAnimation();

            if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
            {
                this.AnimUtil.Dispose();
            }
        }



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
    }
}
