//using ElectricalProgressive.Content.Block.ECable;
//using ElectricalProgressive.Content.Block.EConnector;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using ElectricalProgressiveImmersive.Interface;
using EPImmersive.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
//using static ElectricalProgressive.Content.Block.ECable.BlockECable;

namespace ElectricalProgressive.Content.Block;

public class BEBehaviorEPImmersive : BlockEntityBehavior
{
    public BEBehaviorEPImmersive(BlockEntity blockEntity)
        : base(blockEntity)
    {

    }
    // Уникальные идентификаторы пакетов для передачи данных между клиентом и сервером
    public const int MyPacketIdForServer = 1122334457;
    public const int MyPacketIdForClient = 1122334458;

    // Ключи для сохранения в дерево атрибутов
    public const string InterruptionKey = "electricalprogressive:interruption";
    public const string ConnectionKey = "electricalprogressive:connection";
    public const string IsLoadedKey = "electricalprogressive:isloaded";

    // элементы интерфейса электрической системы
    private IEImmersiveAccumulator? _accumulator;
    private IEImmersiveConsumer? _consumer;
    private IEImmersiveConductor? _conductor;
    private IEImmersiveProducer? _producer;
    private IEImmersiveTransformator? _transformator;

    // настройка частиц и анимаций
    public List<Vec3d> ParticlesOffsetPos = new List<Vec3d>(1);
    public List<int[]> ParticlesFramesAnim = new List<int[]>(1);
    public int ParticlesType = 0;
    private BlockEntityAnimationUtil AnimUtil;
    //private BlockPos[]? multiblockParts; // Все позиции частей мультиблока (включая главную)
    private BlockPos? mainPartPos; // Позиция главной части мультиблока

    /// <summary>
    /// Подгружаем систему иммерсивных проводов
    /// </summary>
    public global::EPImmersive.ElectricalProgressiveImmersive? System =>
        this.Api?.ModLoader.GetModSystem<global::EPImmersive.ElectricalProgressiveImmersive>();



    // хранит направления подключений проводов в этом блоке (позицию соседа, индекс нода(изолятора) своего и соседа)
    private List<(byte, BlockPos, byte)> _immersiveConnection=new(0);

    // блок требует обновления?
    private bool _dirty = true;

    // параметры проводов заменить полностью?
    private bool _paramsSet = false;
    private (EParams param, byte index) _eparams;

    // загружен ли блок в мир
    private bool _isLoaded;

    // хранит список с параметрами и индексом подключения
    private List<(EParams, byte)>? _allEparams;

    //private Facing interruption;




    


    /// <summary>
    /// Добавляет направление подключений иммерсивных проводов (позицию соседа, индекс нода(изолятора) своего и соседа)
    /// </summary>
    /// <param name="value"></param>
    public void AddImmersiveConnection(byte indexHere, BlockPos neighborPos, byte indexNeighbor)
    {
        if (_immersiveConnection!=null)
        {
            this._immersiveConnection.Add((indexHere, neighborPos, indexNeighbor));
        }
        else
        {
            this._immersiveConnection = new List<(byte, BlockPos, byte)>();
            this._immersiveConnection.Add((indexHere, neighborPos, indexNeighbor));
        }
        
        this._dirty = true;
        this._paramsSet = false;

        // вызываем обновление сети
        this.Update();
    }



    /// <summary>
    /// Отдает список подключений этого блока
    /// </summary>
    public List<(byte, BlockPos, byte)> ImmersiveConnection
    {
        get => this._immersiveConnection;
    }


    /// <summary>
    /// 
    /// </summary>
    public void AddEparamsAt(EParams param, byte index)
    {
        if (_allEparams != null)
        {
            if (_allEparams.Count > index)
            {
                this._allEparams[index]=(param, index);
            }
            else
            {
                this._allEparams.Add((param, index));
            }
        }
        else
        {
            this._allEparams = new List<(EParams, byte)>();
            this._allEparams.Add((param, index));
        }

        this._dirty = true;
        this._paramsSet = true;
        this._eparams = (param, index);
        // вызываем обновление сети
        this.Update();
    }








    /// <summary>
    /// Отдает список параметров всех подключений этого блока (второе число - индекс в массиве _immersiveConnection)
    /// </summary>
    public List<(EParams, byte)>? AllEparams
    {
        get => this._allEparams;
    }

    







    /*

    // информация о сети для подсказки 
    public NetworkInformation? NetworkInformation
    {
        get => networkInformation;
    }

    private NetworkInformation? networkInformation = new();


    private DateTime lastExecution = DateTime.MinValue;

    private static double intervalMSeconds;
    */




    /// <summary>
    /// Инициализация поведения электрического блока
    /// </summary>
    /// <param name="api"></param>
    /// <param name="properties"></param>
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        // получаем параметры частиц
        GetParticles();

        // получаем аниматор, если есть
        AnimUtil = Blockentity.GetBehavior<BEBehaviorAnimatable>()?.animUtil!;

        // Регистрируем спавнер частиц для асинхронных частиц
        if (api is ICoreClientAPI capi)
        {
            capi.Event.RegisterAsyncParticleSpawner(OnAsyncParticles);
        }


        //intervalMSeconds = this.System!.TickTimeMs;

        // Инициализация мультиблока
        InitMultiblock();

        this._isLoaded = true;   // оно загрузилось!
        this._dirty = true;
        this.Update();          // обновляем систему, чтобы она знала, что блок загрузился
    }

    /// <summary>
    /// Получение позиций частиц из атрибутов блока
    /// </summary>
    private void GetParticles()
    {
        // тип частиц
        ParticlesType = MyMiniLib.GetAttributeInt(this.Block, "particlesType", 0);

        // получаем позиции частиц из атрибутов блока
        ParticlesOffsetPos.Clear(); // чистим данные из сохранений
        var arrayOffsetPos = MyMiniLib.GetAttributeArrayArrayFloat(this.Block, "particlesOffsetPos", new float[1][] { [0, 0, 0] });
        if (arrayOffsetPos != null)
        {
            for (int i = 0; i < arrayOffsetPos.Length; i++)
            {
                Vec3d buf = new Vec3d(
                    arrayOffsetPos[i][0],
                    arrayOffsetPos[i][1],
                    arrayOffsetPos[i][2]
                );

                ParticlesOffsetPos.Add(buf);
            }
        }

        // получаем привязку частиц к фрэймам анимации
        ParticlesFramesAnim.Clear(); // чистим данные из сохранений
        var arrayFrames = MyMiniLib.GetAttributeArrayArrayInt(this.Block, "particlesFramesAnim", new int[1][] { [-1, -1] });
        if (arrayFrames != null)
        {
            for (int i = 0; i < arrayFrames.Length; i++)
            {
                int[] buf =
                [
                    arrayFrames[i][0],
                    arrayFrames[i][1]
                ];

                ParticlesFramesAnim.Add(buf);
            }
        }
    }


    /// <summary>
    /// Асинхронный спавн частиц
    /// </summary>
    /// <param name="dt"></param>
    /// <param name="manager"></param>
    /// <returns></returns>
    private bool OnAsyncParticles(float dt, IAsyncParticleManager manager)
    {
        // еще не загружено или нет параметров
        if (!this._isLoaded || AllEparams is null)
            return true;

        var hasBurnout = false;
        var prepareBurnout = false;

        // Однопроходная проверка условий
        
        /*
        hasBurnout |= _allEparams.burnout;
        prepareBurnout |= eParam.ticksBeforeBurnout > 0;



        // Спавн частиц в указанных позициях
        if (ParticlesOffsetPos != null && ParticlesOffsetPos.Count > 0)
        {
            int k = 0;
            foreach (var partPos in ParticlesOffsetPos)
            {
                // Обработка prepareBurnout
                if (prepareBurnout)
                {
                    ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos).Add(0.5d, 0d, 0.5d), 0);
                }

                // Обработка burnout
                if (hasBurnout)
                {
                    ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos).Add(0.5d, 0d, 0.5d), 1);
                }

                // частицы собственные для блока
                if (ParticlesType > 1 && !hasBurnout && !prepareBurnout)
                {
                    // настреок анимации нет?
                    if (ParticlesFramesAnim == null || ParticlesFramesAnim[k][0] == -1 || ParticlesFramesAnim[k][1] == -1)
                        ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos), ParticlesType);

                    // ищем аниматор
                    if (AnimUtil != null && AnimUtil.animator != null && AnimUtil.animator.Animations.Length > 0)
                    {
                        float buf = AnimUtil.animator.Animations[0].CurrentFrame;
                        if (buf > ParticlesFramesAnim[k][0] && buf < ParticlesFramesAnim[k][1])
                            ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos), ParticlesType);
                    }
                }

                k++;
            }
        }
        */
        return this._isLoaded;
    }




    
    /// <summary>
    /// Попытка инициализации мультиблока
    /// </summary>
    private void InitMultiblock()
    {
        // какие блоки могут получать электричество
        var blockEProperties = MyMiniLib.GetAttributeString(this.Block, "blockEProperties", "main");

        // Проверка мультиблока
        /*
        var multiblockBehavior = this.Block.GetBehavior<BlockBehaviorMultiblock>();
        if (multiblockBehavior != null && (blockEProperties == "all" || blockEProperties == "all_down"))
        {
            
            var properti = multiblockBehavior.propertiesAtString;

            int sizeX = 1, sizeY = 1, sizeZ = 1;
            var cposition = new int[3];

            if (!string.IsNullOrEmpty(properti))
            {
                try
                {
                    var jo = JObject.Parse(properti);
                    sizeX = (int)jo["sizex"]!;
                    sizeY = (int)jo["sizey"]!;
                    sizeZ = (int)jo["sizez"]!;
                    var cpos = jo["cposition"];
                    cposition[0] = (int)cpos["x"];
                    cposition[1] = (int)cpos["y"];
                    cposition[2] = (int)cpos["z"];
                }
                catch
                {
                    // Логирование ошибки, если требуется
                }
            }

            // Определяем позицию главного блока
            mainPartPos = this.Blockentity.Pos;
            // вычисляем нулевую часть мультиблока
            var zeroPartPos = this.Blockentity.Pos.AddCopy(-cposition[0], -cposition[1], -cposition[2]);

            // Собираем все позиции частей мультиблока
            var parts = new List<BlockPos>();
            for (var dx = 0; dx < sizeX; dx++)
                for (var dy = 0; dy < sizeY; dy++)
                    for (var dz = 0; dz < sizeZ; dz++)
                    {
                        var partPos = zeroPartPos.AddCopy(dx, dy, dz);
                        // Для "all_down" добавляем только нижние блоки (dy == 0), для "all" — все
                        if (blockEProperties == "all" || (blockEProperties == "all_down" && dy == 0))
                        {
                            parts.Add(partPos);
                        }
                    }
            multiblockParts = parts.ToArray();
        }
        else
        {
            mainPartPos = this.Blockentity.Pos;
            //multiblockParts = [this.Blockentity.Pos];
        }
        */
    }



    /// <summary>
    /// Что-то в цепи поменялось
    /// </summary>
    /// <param name="force"></param>
    public void Update(bool force = false)
    {
        if (!this._dirty && !force)
            return;

        var system = this.System;
        if (system is null)
        {
            this._dirty = true;
            return;
        }

        this._dirty = false;


        this._consumer = null;
        this._conductor = null;
        this._producer = null;
        this._accumulator = null;
        this._transformator = null;

        foreach (var entityBehavior in this.Blockentity.Behaviors)
        {
            switch (entityBehavior)
            {
                case IEImmersiveConsumer { } consumer:
                    this._consumer = consumer;
                    break;

                case IEImmersiveProducer { } producer:
                    this._producer = producer;
                    break;

                case IEImmersiveAccumulator { } accumulator:
                    this._accumulator = accumulator;
                    break;

                case IEImmersiveTransformator { } transformator:
                    this._transformator = transformator;
                    break;

                case IEImmersiveConductor { } conductor:
                    this._conductor = conductor;
                    break;
            }
        }

        // Главная часть мультиблока — обычная регистрация
        system.SetConductor(this.Blockentity.Pos, this._conductor);
        system.SetConsumer(this.Blockentity.Pos, this._consumer);
        system.SetProducer(this.Blockentity.Pos, this._producer);
        system.SetAccumulator(this.Blockentity.Pos, this._accumulator);
        system.SetTransformator(this.Blockentity.Pos, this._transformator);



        // Главная часть — обычный Update
        var mainEpar = this._paramsSet ? _eparams : (new(), 0);
        if (system.Update(this.Blockentity.Pos, this._immersiveConnection, mainEpar, ref _allEparams!, _isLoaded))
        {
            try
            {
                this.Blockentity.MarkDirty(true);
            }
            catch { }
        }
    }



    /// <summary>
    /// Вызывается, когда блок удаляется из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        this._isLoaded = false;

        // Удаляем все части мультиблока из системы
        /*
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
                this.System?.Remove(partPos);
        }
        else
        {
            this.System?.Remove(this.Blockentity.Pos);
        }

        
        networkInformation = null;
        */
        this.System?.Remove(this.Blockentity.Pos);
        AnimUtil?.Dispose();
    }



    /// <summary>
    /// Вызывается, когда блок выгружается из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._isLoaded = false;
        this._dirty = true;
        // Обновляем все части мультиблока
        /*
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
            {
                var partEparams = allEparams;
                var Epar = this.paramsSet ? Eparams : (new(), 0);
                this.System?.Update(partPos, this.connection & ~this.interruption, Epar, ref partEparams!, false);
            }
        }
        else
        {
            this.Update();
        }


        networkInformation = null;
        */

        this.Update();
        AnimUtil?.Dispose();
    }


    /*
    /// <summary>
    /// Принимает сигнал от клиента, который наводится на блок, что инициирует обновление информации о блоке-энтити
    /// </summary>
    /// <param name="fromPlayer"></param>
    /// <param name="packetid"></param>
    /// <param name="data"></param>
    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (packetid == MyPacketIdForServer) // проверяем, что пакет именно мой
        {
            var dataTuple = SerializerUtil.Deserialize<(BlockPos, Facing, string)>(data);
            networkInformation = this.System?.GetNetworks(dataTuple.Item1, dataTuple.Item2, dataTuple.Item3);
            var sapi = (ICoreServerAPI)Api;
            var fromServerPlayer = fromPlayer as IServerPlayer;
            sapi.Network.SendBlockEntityPacket(fromServerPlayer, this.Blockentity.Pos, MyPacketIdForClient, NetworkInformationSerializer.Serialize(networkInformation!));
            this.Blockentity.MarkDirty();
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);

    }

    /// <summary>
    /// Принимает сигнал от сервера, что пришла информация о сети
    /// </summary>
    /// <param name="packetid"></param>
    /// <param name="data"></param>
    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        if (packetid == MyPacketIdForClient) // проверяем, что пакет именно мой
        {
            networkInformation = NetworkInformationSerializer.Deserialize(data);
        }

        base.OnReceivedServerPacket(packetid, data);
    }


    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);



        if (Api is not ICoreClientAPI)
            return;



        //храним направления проводов в этом блоке
        var selectedFacing = Facing.None;

        var entity = this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos); // получаем блок-энитити, чтобы получить информацию о нем
        var methodForInformation = ""; //метод получения информации о сети, в зависимости от типа блока-энитити

        //если это кабель, то мы можем вывести только информацию о сети на одной грани
        if (entity is BlockEntityECable blockEntityECable && entity is not BlockEntityEConnector && blockEntityECable.ElectricalProgressive.AllEparams != null)
        {
            if (forPlayer is { CurrentBlockSelection: { } blockSelection })
            {
                var key = CacheDataKey.FromEntity(blockEntityECable);
                var hitPosition = blockSelection.HitPosition;

                var sf = new SelectionFacingCable();
                selectedFacing = sf.SelectionFacing(key, hitPosition, this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos));  //выделяем напрвление для слома под курсором

                if (selectedFacing != Facing.None)
                    selectedFacing = FacingHelper.FromFace(FacingHelper.Faces(selectedFacing).First());  //выбираем одну грань, если даже их там вдруг окажется больше
                else
                    return;

                methodForInformation = "thisFace"; // только указанную грань


            }
        }
        else if (entity is BlockEntityEConnector blockEntityEConnector && blockEntityEConnector.ElectricalProgressive.AllEparams != null) //если это мет блок
        {
            selectedFacing = Facing.AllAll;
            methodForInformation = "currentFace"; // берем информацию о любой грани, где ток больше 0
        }
        else     //для не кабелей берем все что есть
        {
            selectedFacing = this.Connection;
            methodForInformation = "firstFace"; // берем информацию о первой грани в массиве из многих
        }



        // работаем с выводом информации о причинах сгорания
        if (this.System?.Parts.TryGetValue(this.Blockentity.Pos, out var part) ?? false)
        {
            foreach (var face in FacingHelper.Faces(selectedFacing))
            {
                var faceIndex = face.Index;

                if (part.eparams[faceIndex].burnout || part.eparams[faceIndex].ticksBeforeBurnout > 0) // показываем причину сгорания, когда горит и когда уже сгорело
                {
                    var cause = part.eparams[faceIndex].causeBurnout switch
                    {
                        1 => ElectricalProgressiveBasics.causeBurn[1],
                        2 => ElectricalProgressiveBasics.causeBurn[2],
                        3 => ElectricalProgressiveBasics.causeBurn[3],
                        _ => null!
                    };

                    if (cause is not null)
                    {
                        if (part.eparams[faceIndex].burnout)
                            stringBuilder.AppendLine(Lang.Get("Burned"));

                        stringBuilder.AppendLine(cause);
                        break;
                    }
                }
            }
        }







        // получаем информацию о сети раз в секунду!
        if ((DateTime.Now - lastExecution).TotalMilliseconds >= intervalMSeconds)
        {
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket<(BlockPos, Facing, string)>(this.Blockentity.Pos, MyPacketIdForServer,
                (this.Blockentity.Pos, selectedFacing, methodForInformation));

            lastExecution = DateTime.Now;
        }

        // если нет информации о сети, то просто выходим
        if (networkInformation == null)
        {
            return;
        }

        //отслеживаем состояние кнопки для подробностей
        var capi = (ICoreClientAPI)Api;
        var altPressed = capi.Input.IsHotKeyPressed("AltPressForNetwork");
        var nameAltPressed = capi.Input.GetHotKeyByCode("AltPressForNetwork").CurrentMapping.ToString();

        if (!altPressed)
        {
            stringBuilder.AppendLine(Lang.Get("Press") + " " + nameAltPressed + " " + Lang.Get("for details"));
            return;
        }


        stringBuilder.AppendLine(Lang.Get("Electricity"));
        stringBuilder.AppendLine("├ " + Lang.Get("Consumers") + ": " + networkInformation.NumberOfConsumers);
        stringBuilder.AppendLine("├ " + Lang.Get("Generators") + ": " + networkInformation.NumberOfProducers);
        stringBuilder.AppendLine("├ " + Lang.Get("Batteries") + ": " + networkInformation.NumberOfAccumulators);
        stringBuilder.AppendLine("├ " + Lang.Get("Transformers") + ": " + networkInformation.NumberOfTransformators);
        stringBuilder.AppendLine("├ " + Lang.Get("Blocks") + ": " + networkInformation.NumberOfBlocks);
        stringBuilder.AppendLine("├ " + Lang.Get("Generation") + ": " + networkInformation.Production + " " + Lang.Get("W"));
        stringBuilder.AppendLine("├ " + Lang.Get("Consumption") + ": " + networkInformation.Consumption + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Request") + ": " + networkInformation.Request + " " + Lang.Get("W"));

        var capacity = (float)((networkInformation.MaxCapacity == 0f) ? 0f : (networkInformation.Capacity * 100.0F / networkInformation.MaxCapacity));

        stringBuilder.AppendLine("└ " + Lang.Get("Capacity") + ": " + (int)networkInformation.Capacity + "/" + (int)networkInformation.MaxCapacity + " " + Lang.Get("J") + "(" + capacity.ToString("F3") + " %)");

        stringBuilder.AppendLine(Lang.Get("Block"));
        stringBuilder.AppendLine("├ " + Lang.Get("Max. current") + ": " + networkInformation.eParamsInNetwork.maxCurrent * networkInformation.eParamsInNetwork.lines + " " + Lang.Get("A"));
        stringBuilder.AppendLine("├ " + Lang.Get("Current") + ": " + Math.Abs(networkInformation.current).ToString("F3") + " " + Lang.Get("A"));

        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is BlockEntityECable) //если кабель!
        {
            stringBuilder.AppendLine("├ " + Lang.Get("Resistivity") + ": " + networkInformation.eParamsInNetwork.resistivity / (networkInformation.eParamsInNetwork.isolated ? 2.0F : 1.0F)
                + " " + Lang.Get("Om/line"));
            stringBuilder.AppendLine("├ " + Lang.Get("Resistance") + ": " + networkInformation.eParamsInNetwork.resistivity / (networkInformation.eParamsInNetwork.lines
                * networkInformation.eParamsInNetwork.crossArea) / (networkInformation.eParamsInNetwork.isolated ? 2.0F : 1.0F) + " " + Lang.Get("Om"));
            stringBuilder.AppendLine("├ " + Lang.Get("Lines") + ": " + networkInformation.eParamsInNetwork.lines + " " + Lang.Get("pcs."));
            stringBuilder.AppendLine("├ " + Lang.Get("Section size") + ": " + networkInformation.eParamsInNetwork.crossArea * networkInformation.eParamsInNetwork.lines + " " + Lang.Get("units"));
        }

        stringBuilder.AppendLine("└ " + Lang.Get("Max voltage") + ": " + networkInformation?.eParamsInNetwork.voltage + " " + Lang.Get("V"));
    }

    */

    /// <summary>
    /// Сохраняет в дерево атрибутов
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(ConnectionKey, SerializerUtil.Serialize(this._immersiveConnection));

        tree.SetBool(IsLoadedKey, this._isLoaded);

        if (_allEparams != null)
        {
            tree.SetBytes(BlockEntityEBase.AllEparamsKey, EParamsSerializer.Serialize(_allEparams!));
        }

        // Сохраняем параметры частиц
        tree.SetInt("ParticlesType", ParticlesType);

        // Сохраняем массив смещений частиц
        tree.SetInt("ParticlesOffsetPosCount", ParticlesOffsetPos.Count);
        for (int i = 0; i < ParticlesOffsetPos.Count; i++)
        {
            tree.SetDouble($"ParticlesOffsetPosX_{i}", ParticlesOffsetPos[i].X);
            tree.SetDouble($"ParticlesOffsetPosY_{i}", ParticlesOffsetPos[i].Y);
            tree.SetDouble($"ParticlesOffsetPosZ_{i}", ParticlesOffsetPos[i].Z);
        }

        // Сохраняем массив фреймов для синхронизации
        tree.SetInt("ParticlesFramesAnimCount", ParticlesFramesAnim.Count);
        for (int i = 0; i < ParticlesOffsetPos.Count; i++)
        {
            tree.SetInt($"ParticlesFramesAnimMin_{i}", ParticlesFramesAnim[i][0]);
            tree.SetInt($"ParticlesFramesAnimMax_{i}", ParticlesFramesAnim[i][1]);

        }

    }

    /// <summary>
    /// Считывает из дерева атрибутов
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var connection = SerializerUtil.Deserialize<List<(byte, BlockPos, byte)>>(tree.GetBytes(ConnectionKey));
        
        var isLoaded = tree.GetBool(IsLoadedKey, false);


        List<(EParams, byte)>? AllEparamss;

        AllEparamss = EParamsSerializer.Deserialize(tree.GetBytes(BlockEntityEBase.AllEparamsKey));
        

        // Загрузка параметров частиц
        ParticlesType = tree.GetInt("ParticlesType", 0);

        int count = tree.GetInt("ParticlesOffsetPosCount", 0);
        ParticlesOffsetPos = new(count);
        for (int i = 0; i < count; i++)
        {
            double x = tree.GetDouble($"ParticlesOffsetPosX_{i}", 0.0);
            double y = tree.GetDouble($"ParticlesOffsetPosY_{i}", 0.0);
            double z = tree.GetDouble($"ParticlesOffsetPosZ_{i}", 0.0);
            ParticlesOffsetPos.Add(new Vec3d(x, y, z));
        }

        count = tree.GetInt("ParticlesFramesAnimCount", 0);
        ParticlesFramesAnim = new(count);
        for (int i = 0; i < count; i++)
        {
            int min = tree.GetInt($"ParticlesFramesAnimMin_{i}", -1);
            int max = tree.GetInt($"ParticlesFramesAnimMax_{i}", -1);
            ParticlesFramesAnim.Add([min, max]);
        }



        
        this._isLoaded = isLoaded;
        this._immersiveConnection = connection;
        this._allEparams = AllEparamss;
        this._dirty = true;
        this.Update();
    }



}