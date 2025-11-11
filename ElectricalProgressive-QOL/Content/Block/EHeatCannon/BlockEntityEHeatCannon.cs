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

        private ILoadedSound _ambientSound;
        private AssetLocation _heatCannonSound;

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

                _heatCannonSound = new AssetLocation("electricalprogressiveqol:sounds/eheatcannon.ogg");
            }


        }


        private void Every1000Ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorEHeatCannon>();
            // мало ли поведение не загрузилось еще
            if (beh == null)
            {
                StopAnimation();
                StopSound();
                return;
            }

            // включает и выключает анимацию
            if (this.Block.Variant["state"] == "enabled")
            {
                StartAnimation();
                StartSound();
            }
            else
            {
                StopAnimation();
                StopSound();
            }

        }


        /// <summary>
        /// Запуск звука
        /// </summary>
        public void StartSound()
        {
            if (this._ambientSound != null)
                return;
            if ((Api != null ? (Api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
                return;
            this._ambientSound = (this.Api as ICoreClientAPI).World.LoadSound(new SoundParams()
            {
                Location = _heatCannonSound,
                ShouldLoop = true,
                Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                DisposeOnFinish = false,
                Volume = 1f,
            });

            this._ambientSound.Start();
        }



        /// <summary>
        /// Остановка звука
        /// </summary>
        public void StopSound()
        {
            if (this._ambientSound == null)
                return;
            this._ambientSound.Stop();
            this._ambientSound.Dispose();
            this._ambientSound = (ILoadedSound)null;
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
                    EaseOutSpeed = 15f,
                    EaseInSpeed = 15f
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

            if (this._ambientSound != null)
            {
                this._ambientSound.Stop();
                this._ambientSound.Dispose();
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

            if (this._ambientSound != null)
            {
                this._ambientSound.Stop();
                this._ambientSound.Dispose();
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
