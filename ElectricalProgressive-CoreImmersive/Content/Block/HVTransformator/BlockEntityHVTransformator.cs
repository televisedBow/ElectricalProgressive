using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Utils;
using EPImmersive.Content.Block.CableSwitch;
using EPImmersive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;


namespace EPImmersive.Content.Block.HVTransformator
{
    internal class BlockEntityHVTransformator : BlockEntityEIBase
    {
        public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();



        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (this.EPImmersive == null || byItemStack == null || this.ElectricalProgressive == null) 
                return;

            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this.Block, this);
            LoadImmersiveEProperties.Load(this.Block, this);
        }


        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            StopAnim();
            this.ElectricalProgressive?.OnBlockUnloaded();
            animUtil?.Dispose();
        }


        private ICoreClientAPI _capi;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RotationCache = CreateRotationCache();


            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;

                // инициализируем аниматор
                if (animUtil != null)
                {
                    animUtil.InitializeAnimator("hvtransformator", null, null, new Vec3f(0, GetRotation(), 0f));

                    StartAnim();
                }
            }


        }


        /// <summary>
        /// Получает угол поворота блока в градусах
        /// </summary>
        /// <returns></returns>
        public float GetRotation()
        {
            float rotateY = 0;

            if (Facing != Facing.None && RotationCache != null)
            {
                if (RotationCache.TryGetValue(Facing, out var rotation))
                {
                    rotateY = rotation.Y;
                }
            }
            else
            {
                rotateY = Block.Shape.rotateY;
            }
            return rotateY;
        }


        /// <summary>
        /// Аниматор блока, используется для анимации открывания дверцы генератора
        /// </summary>
        public BlockEntityAnimationUtil animUtil
        {
            get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
        }



        /// <summary>
        /// Запускает анимацию открытия дверцы
        /// </summary>
        public void StartAnim()
        {
            //animUtil.Dispose();
            animUtil.InitializeAnimator("hvtransformator", null, null, new Vec3f(0, GetRotation(), 0f));

            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work") == false)
            {
                animUtil?.StartAnimation(new AnimationMetaData()
                {
                    Animation = "work",
                    Code = "work",
                    AnimationSpeed = 1.8f,
                    EaseOutSpeed = 6,
                    EaseInSpeed = 6
                });

                //применяем цвет и яркость
                //Block.LightHsv = new byte[] { 7, 7, 11 };

                //добавляем звук
                //_capi.World.PlaySoundAt(new("electricalprogressiveqol:sounds/freezer_open.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);

            }

        }


        /// <summary>
        /// Закрывает дверцу генератора, останавливая анимацию открытия, если она запущена
        /// </summary>
        public void StopAnim()
        {
            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work") == true)
            {
                animUtil?.StopAnimation("work");

                //применяем цвет и яркость
                //Block.LightHsv = new byte[] { 7, 7, 0 };

                //добавляем звук
                //_capi.World.PlaySoundAt(new("electricalprogressiveqol:sounds/freezer_close.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
            }
        }


        /// <summary>
        /// Вызывается при тесселяции блока
        /// </summary>
        /// <param name="mesher"></param>
        /// <param name="tessThreadTesselator"></param>
        /// <returns></returns>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            base.OnTesselation(mesher, tessThreadTesselator); // вызываем базовую логику тесселяции


            // если анимации нет, то рисуем блок базовый
            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work") == false)
            {
                (this.Block as ImmersiveWireBlock)._drawBaseMesh = true;
                return false;
            }

            (this.Block as ImmersiveWireBlock)._drawBaseMesh = false;

            return false;
        }


        /// <summary>
        /// Вызывается при удалении блока
        /// </summary>
        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            StopAnim();
            animUtil?.Dispose();
        }
        

        private static Dictionary<Facing, RotationData> CreateRotationCache()
        {
            return new Dictionary<Facing, RotationData>
            {
                { Facing.NorthEast, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthWest, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthUp, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthDown, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.EastNorth, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastSouth, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastUp, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastDown, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.SouthEast, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthWest, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthUp, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthDown, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.WestNorth, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestSouth, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestUp, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestDown, new RotationData(0.0f, 90.0f, 0.0f) }
            };
        }


    }
}

