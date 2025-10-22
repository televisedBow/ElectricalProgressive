using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Content.Block.EConnector;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
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
using static ElectricalProgressive.Content.Block.ECable.BlockECable;

namespace ElectricalProgressive.Content.Block;

public class BEBehaviorElectricalProgressive : BlockEntityBehavior
{


    public const string InterruptionKey = "electricalprogressive:interruption";
    public const string ConnectionKey = "electricalprogressive:connection";
    public const string IsLoadedKey = "electricalprogressive:isloaded";


    private IElectricAccumulator? accumulator;
    private IElectricConsumer? consumer;
    private IElectricConductor? conductor;
    private IElectricProducer? producer;
    private IElectricTransformator? transformator;


    private Facing connection;
    private Facing interruption;
    private bool isLoaded;

    private bool dirty = true;
    private bool paramsSet = false;


    public EParams eparams;
    public int eparamsFace;
    private EParams[]? allEparams;

    public BEBehaviorElectricalProgressive(BlockEntity blockEntity)
        : base(blockEntity)
    {

    }

    public const int MyPacketIdForServer = 1122334455; // Уникальный идентификатор пакета для передачи данных BEBehaviorElectricalProgressive
    public const int MyPacketIdForClient = 1122334456; // Уникальный идентификатор пакета для передачи данных BEBehaviorElectricalProgressive

    public global::ElectricalProgressive.ElectricalProgressive? System =>
        this.Api?.ModLoader.GetModSystem<global::ElectricalProgressive.ElectricalProgressive>();

    
    public Facing Connection
    {
        get => this.connection;
        set
        {
            if (this.connection != value)
            {
                this.connection = value;
                this.dirty = true;
                this.paramsSet = false;
                this.Update();
            }
        }
    }



    public EParams[]? AllEparams
    {
        get => allEparams!;
        set
        {
            if (this.allEparams != value)
            {
                this.allEparams = value;
                this.dirty = true;
                this.Update();
            }
        }
    }


    public (EParams, int) Eparams
    {
        get => (this.eparams, this.eparamsFace);
        set
        {
            if (!this.eparams.Equals(value.Item1) || this.eparamsFace != value.Item2)
            {
                this.eparams = value.Item1;
                this.eparamsFace = value.Item2;
                this.paramsSet = true;
                this.dirty = true;
                this.Update();
            }
        }
    }

    public Facing Interruption
    {
        get => this.interruption;
        set
        {
            if (this.interruption != value)
            {
                this.interruption = value;
                this.dirty = true;
                this.Update();
            }
        }
    }



    private NetworkInformation? networkInformation=new();
    private DateTime lastExecution = DateTime.MinValue;

    private static double intervalMSeconds;

    private BlockPos[]? multiblockParts; // Все позиции частей мультиблока (включая главную)
    private BlockPos? mainPartPos; // Позиция главной части мультиблока

    /// <summary>
    /// Инициализация поведения электрического блока
    /// </summary>
    /// <param name="api"></param>
    /// <param name="properties"></param>
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        intervalMSeconds = this.System!.tickTimeMs;

        InitMultiblock();

        this.isLoaded = true;   // оно загрузилось!
        this.dirty = true;
        this.Update();          // обновляем систему, чтобы она знала, что блок загрузился
    }


    /// <summary>
    /// Попытка инициализации мультиблока
    /// </summary>
    private void InitMultiblock()
    {
        // какие блоки могут получать электричество
        var blockEProperties = MyMiniLib.GetAttributeString(this.Block, "blockEProperties", "main");

        // Проверка мультиблока
        var multiblockBehavior = this.Block.GetBehavior<BlockBehaviorMultiblock>();
        if (multiblockBehavior != null && (blockEProperties == "all" || blockEProperties == "all_down"))
        {
            var properti = multiblockBehavior.propertiesAtString;

            int sizeX = 1, sizeY = 1, sizeZ = 1;
            int[] cposition = new int[3];

            if (!string.IsNullOrEmpty(properti))
            {
                try
                {
                    JObject jo = JObject.Parse(properti);
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
            for (int dx = 0; dx < sizeX; dx++)
            for (int dy = 0; dy < sizeY; dy++)
            for (int dz = 0; dz < sizeZ; dz++)
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
            multiblockParts = new[] { this.Blockentity.Pos };
        }
    }

    /// <summary>
    /// Что-то в цепи поменялось
    /// </summary>
    /// <param name="force"></param>
    public void Update(bool force = false)
    {
        if (!this.dirty && !force)
            return;

        var system = this.System;
        if (system is null)
        {
            this.dirty = true;
            return;
        }

        this.dirty = false;


        this.consumer = null;
        this.conductor = null;
        this.producer = null;
        this.accumulator = null;
        this.transformator = null;

        foreach (var entityBehavior in this.Blockentity.Behaviors)
        {
            switch (entityBehavior)
            {
                case IElectricConsumer { } consumer:
                    this.consumer = consumer;
                    break;

                case IElectricProducer { } producer:
                    this.producer = producer;
                    break;

                case IElectricAccumulator { } accumulator:
                    this.accumulator = accumulator;
                    break;

                case IElectricTransformator { } transformator:
                    this.transformator = transformator;
                    break;

                case IElectricConductor { } conductor:
                    this.conductor = conductor;
                    break;
            }
        }

        // Главная часть мультиблока — обычная регистрация
        system.SetConductor(this.Blockentity.Pos, this.conductor);
        system.SetConsumer(this.Blockentity.Pos, this.consumer);
        system.SetProducer(this.Blockentity.Pos, this.producer);
        system.SetAccumulator(this.Blockentity.Pos, this.accumulator);
        system.SetTransformator(this.Blockentity.Pos, this.transformator);

        // Для всех остальных частей мультиблока (кроме главной) — регистрируем как проводник с теми же EParams
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
            {
                if (partPos == this.Blockentity.Pos)
                    continue; // Главная часть уже обработана выше
                // Создаём виртуальный проводник для каждой части
                var virtualConductor = new VirtualConductor(partPos);
                system.SetConductor(partPos, virtualConductor);
                // Для Update: используем те же параметры, что и у главной части
                EParams[]? partEparams = allEparams;
                (EParams, int) Epar = this.paramsSet ?
                    Eparams :
                    (new(), 0);
                system.Update(partPos, this.connection & ~this.interruption, Epar, ref partEparams!, isLoaded);
            }
        }

        // Главная часть — обычный Update
        (EParams, int) mainEpar = this.paramsSet ? Eparams : (new(), 0);
        if (system.Update(this.Blockentity.Pos, this.connection & ~this.interruption, mainEpar, ref allEparams!, isLoaded))
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
        // Удаляем все части мультиблока из системы
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
    }



    /// <summary>
    /// Вызывается, когда блок выгружается из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.isLoaded = false;
        this.dirty = true;
        // Обновляем все части мультиблока
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
            {
                EParams[]? partEparams = allEparams;
                (EParams, int) Epar = this.paramsSet ? Eparams : (new(), 0);
                this.System?.Update(partPos, this.connection & ~this.interruption, Epar, ref partEparams!, false);
            }
        }
        else
        {
            this.Update();
        }
        networkInformation = null;
    }

 

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
            var sapi= (ICoreServerAPI)Api;
            IServerPlayer? fromServerPlayer = fromPlayer as IServerPlayer;
            sapi.Network.SendBlockEntityPacket(fromServerPlayer,this.Blockentity.Pos, MyPacketIdForClient, NetworkInformationSerializer.Serialize(networkInformation!));
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
            networkInformation= NetworkInformationSerializer.Deserialize(data);
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
        string methodForInformation = ""; //метод получения информации о сети, в зависимости от типа блока-энитити

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
        if (this.System?.parts.TryGetValue(this.Blockentity.Pos, out var part) ?? false)
        {
            foreach (var face in FacingHelper.Faces(selectedFacing))
            {
                var faceIndex = face.Index;

                if (part.eparams[faceIndex].burnout || part.eparams[faceIndex].ticksBeforeBurnout > 0) // показываем причину сгорания, когда горит и когда уже сгорело
                {
                    string cause = part.eparams[faceIndex].causeBurnout switch
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
        bool altPressed = capi.Input.IsHotKeyPressed("AltPressForNetwork");
        string nameAltPressed = capi.Input.GetHotKeyByCode("AltPressForNetwork").CurrentMapping.ToString();

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

        float capacity = (float)((networkInformation.MaxCapacity == 0f) ? 0f : (networkInformation.Capacity * 100.0F / networkInformation.MaxCapacity));

        stringBuilder.AppendLine("└ " + Lang.Get("Capacity") + ": " + (int)networkInformation.Capacity + "/" + (int)networkInformation.MaxCapacity+ " " + Lang.Get("J")+ "(" +capacity.ToString("F3") + " %)");

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



    /// <summary>
    /// Сохраняет в дерево атрибутов
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(ConnectionKey, SerializerUtil.Serialize(this.connection));
        tree.SetBytes(InterruptionKey, SerializerUtil.Serialize(this.interruption));
        tree.SetBool(IsLoadedKey, this.isLoaded);

        // Сохраняем флаг формата (например, "binary" или "json")
        tree.SetString("SerializationFormat", "binary");

        if (allEparams != null)
        {
            // Используем пользовательскую бинарную сериализацию
            tree.SetBytes(BlockEntityEBase.AllEparamsKey, EParamsSerializer.Serialize(allEparams!));
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

        Facing connection = SerializerUtil.Deserialize<Facing>(tree.GetBytes(ConnectionKey));
        Facing interruption = SerializerUtil.Deserialize<Facing>(tree.GetBytes(InterruptionKey));

        bool isLoaded = tree.GetBool(IsLoadedKey, false);

        // Проверяем флаг формата
        string format = tree.GetString("SerializationFormat", "json"); // По умолчанию "json", если флаг отсутствует

        EParams[]? AllEparamss;
        if (format == "binary")
        {
            // Используем бинарную десериализацию
            AllEparamss = EParamsSerializer.Deserialize(tree.GetBytes(BlockEntityEBase.AllEparamsKey));
        }
        else
        {
            // Используем Newtonsoft.Json для старых данных
            AllEparamss = JsonConvert.DeserializeObject<EParams[]>(Encoding.UTF8.GetString(tree.GetBytes(BlockEntityEBase.AllEparamsKey)));
        }



        // Проверяем, изменились ли данные
        if (connection == this.connection &&
            interruption == this.interruption &&
            isLoaded == this.isLoaded &&
            AllEparamss!.SequenceEqual(allEparams!))
        {
            return;
        }

        this.interruption = interruption;
        this.isLoaded = isLoaded;
        this.connection = connection;
        this.allEparams = AllEparamss;
        this.dirty = true;
        this.Update();
    }



}