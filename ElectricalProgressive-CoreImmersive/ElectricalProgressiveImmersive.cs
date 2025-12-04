using ElectricalProgressive;
using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using EPImmersive.Content.Block.EAccumulator;
using EPImmersive.Content.Block.ECable1;
using EPImmersive.Content.Block.EGenerator;
using EPImmersive.Content.Block.EMotor;
using EPImmersive.Interface;
using EPImmersive.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static EPImmersive.ElectricalProgressiveImmersive;


[assembly: ModDependency("game", "1.21.0")]
[assembly: ModInfo(
    "Electrical Progressive: CoreImmersive",
    "electricalprogressivecoreimmersive",
    Website = "https://github.com/tehtelev/ElectricalProgressiveImmersive",
    Description = "Electrical logic library.",
    Version = "2.6.2",
    Authors = ["Tehtelev", "Kotl"]
)]



namespace EPImmersive
{
    public class ElectricalProgressiveImmersive : ModSystem
    {
        public readonly HashSet<ImmersiveNetwork> Networks = [];
        public readonly Dictionary<BlockPos, ImmersiveNetworkPart> Parts = new(new BlockPosComparer()); // Хранит все элементы всех цепей

        private Dictionary<BlockPos, List<EnergyPacket>> _packetsByPosition = new(new BlockPosComparer()); //Словарь для хранения пакетов по позициям

        private readonly List<ImmersiveEnergyPacket> _globalEnergyPackets = []; // Глобальный список пакетов энергии

        private ImmersiveAsyncPathFinder _immersiveAsyncPathFinder = null!;

        private ImmersiveNetworkInformation _result = new();

        private Dictionary<BlockPos, float> _sumEnergy = new();

        public ICoreAPI Api = null!;
        public ICoreClientAPI _capi = null!;
        private ICoreServerAPI _sapi = null!;




        private ImmersiveNetwork _localNetwork = new();




        public static AssetLocation soundElectricShok;

        public int TickTimeMs;
        private float _elapsedMs = 0f;

        int _envUpdater = 0;

        private long _listenerId1;
        //private long listenerId2;

        //private ImmersiveNetworkInformation _result = new();

        /// <summary>
        /// Запуск модификации
        /// </summary>
        /// <param name="api"></param>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            this.Api = api;

            soundElectricShok = new AssetLocation("electricalprogressivecore:sounds/electric-shock.ogg");


            api.RegisterBlockClass("BlockEIAccumulator1", typeof(BlockEIAccumulator1));
            api.RegisterBlockEntityClass("BlockEntityEIAccumulator1", typeof(BlockEntityEIAccumulator1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorEIAccumulator1", typeof(BEBehaviorEIAccumulator1));

            api.RegisterBlockClass("BlockECable1", typeof(BlockECable1));
            api.RegisterBlockEntityClass("BlockEntityECable1", typeof(BlockEntityECable1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorECable1", typeof(BEBehaviorECable1));

            api.RegisterBlockClass("BlockEMotor1", typeof(BlockEMotor1));
            api.RegisterBlockEntityClass("BlockEntityEMotor1", typeof(BlockEntityEMotor1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorEMotor1", typeof(BEBehaviorEMotor1));


            api.RegisterBlockClass("BlockEGenerator1", typeof(BlockEGenerator1));
            api.RegisterBlockEntityClass("BlockEntityEGenerator1", typeof(BlockEntityEGenerator1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorEGenerator1", typeof(BEBehaviorEGenerator1));

            api.RegisterBlockEntityBehaviorClass("ElectricalProgressiveImmersive", typeof(BEBehaviorEPImmersive));
        }


        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();

            // Удаляем слушатель тиков игры
            if (_sapi != null)
            {
                _sapi.Event.UnregisterGameTickListener(_listenerId1);
                _immersiveAsyncPathFinder.Stop();
                _immersiveAsyncPathFinder = null;
            }

            _globalEnergyPackets.Clear();

            _sumEnergy.Clear();
            _packetsByPosition.Clear();


            Api = null!;
            _capi = null!;
            _sapi = null!;




            Networks.Clear();
            Parts.Clear();

            ImmersivePathCacheManager.Dispose();
        }

        /// <summary>
        /// Загрузка конфигурации и начальная инициализация
        /// </summary>
        /// <param name="api"></param>
        public override void StartPre(ICoreAPI api)
        {
        }

        /// <summary>
        /// Запуск клиентской стороны
        /// </summary>
        /// <param name="api"></param>
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this._capi = api;
            RegisterAltKeys();

        }


        /// <summary>
        /// Регистрация клавиш Alt
        /// </summary>
        private void RegisterAltKeys()
        {
            _capi.Input.RegisterHotKey("AltPressForNetwork", Lang.Get("AltPressForNetworkName"), GlKeys.LAlt);
        }

        /// <summary>
        /// Серверная сторона
        /// </summary>
        /// <param name="api"></param>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            this._sapi = api;



            //инициализируем обработчик уронов
            //damageManager = new DamageManager(api);

            _listenerId1 = _sapi.Event.RegisterGameTickListener(this.OnGameTickServer, TickTimeMs);

            _immersiveAsyncPathFinder = new ImmersiveAsyncPathFinder(Parts, ElectricalProgressive.ElectricalProgressive.multiThreading); // вычислитель параллельных задач поиска путей
        }

        /// <summary>
        /// Обновление электрической сети для иммерсивных проводов
        /// </summary>
        /// <param name="position">Позиция блока</param>
        /// <param name="wireNodes">Точки подключения</param>
        /// <param name="connections">Соединения</param>
        /// <param name="mainEparam">Основные параметры блока</param>
        /// <param name="currentEparam">Параметры текущего подключения</param>
        /// <param name="isLoaded">Загружен ли блок</param>
        /// <returns>Успешно ли обновлено</returns>
        public bool Update(BlockPos position, List<WireNode> wireNodes, ref List<ConnectionData> connections,
            EParams mainEparam, (EParams param, byte index) currentEparam, bool isLoaded)
        {
            if (!Parts.TryGetValue(position, out var part))
            {
                part = Parts[position] = new ImmersiveNetworkPart(position);
            }

            // Обновляем точки подключения
            part.WireNodes.Clear();
            part.WireNodes.AddRange(wireNodes);

            // Обновляем соединения
            part.Connections.Clear();
            part.Connections.AddRange(connections);

            part.IsLoaded = isLoaded;

            // Если установлены основные параметры блока, применяем их
            if (!mainEparam.Equals(new EParams()))
            {
                part.MainEparams = mainEparam;
            }

            // Если установлены параметры конкретного подключения, обновляем соответствующее соединение
            if (!currentEparam.param.Equals(new EParams()))
            {
                ConnectionData? connectionToUpdate = null;

                // Находим соединение с указанным индексом
                if (currentEparam.index < part.Connections.Count)
                    connectionToUpdate = part.Connections[currentEparam.index];

                if (connectionToUpdate != null)
                {
                    // Обновляем параметры существующего соединения
                    connectionToUpdate.Parameters = currentEparam.param;
                }
                else
                {
                    // Если соединение не найдено, создаем новое
                    var newConnection = new ConnectionData
                    {
                        LocalNodeIndex = currentEparam.index,
                        Parameters = currentEparam.param
                        // NeighborPos и NeighborNodeIndex должны быть установлены вызывающим кодом
                    };
                    part.Connections.Add(newConnection);
                }
            }

            // Обновляем соединения в сети
            UpdateImmersiveConnections(ref part);

            // Возвращаем обновленный список соединений
            connections = new(part.Connections);
            return true;
        }




        /// <summary>
        /// Обновление иммерсивных соединений в сети
        /// </summary>
        private void UpdateImmersiveConnections(ref ImmersiveNetworkPart part)
        {
            // Создаем временную сеть для этого блока если её нет
            var network = GetOrCreateNetworkForPart(part);

            // Собираем все сети, которые нужно объединить
            //var networksToMerge = new HashSet<ImmersiveNetwork> { network };

            // Добавляем/обновляем соединения и собираем сети для объединения
            foreach (var connection in part.Connections)
            {
                AddImmersiveConnection(part, connection, network);
            }

            // Удаляем устаревшие соединения
            RemoveStaleImmersiveConnections(part, network);
        }


        /// <summary>
        /// Получает или создает сеть для части
        /// </summary>
        private ImmersiveNetwork GetOrCreateNetworkForPart(ImmersiveNetworkPart part)
        {
            // Ищем существующую сеть для этого блока
            foreach (var network in Networks)
            {
                if (network.PartPositions.Contains(part.Position))
                    return network;
            }

            // Создаем новую сеть если не нашли
            var newNetwork = CreateNetwork();
            newNetwork.PartPositions.Add(part.Position);

            // Добавляем компоненты в сеть
            if (part.Conductor is { } conductor) newNetwork.Conductors.Add(conductor);
            if (part.Consumer is { } consumer) newNetwork.Consumers.Add(consumer);
            if (part.Producer is { } producer) newNetwork.Producers.Add(producer);
            if (part.Accumulator is { } accumulator) newNetwork.Accumulators.Add(accumulator);
            if (part.Transformator is { } transformator) newNetwork.Transformators.Add(transformator);

            return newNetwork;
        }

        /// <summary>
        /// Добавляет иммерсивное соединение в сеть
        /// </summary>
        private void AddImmersiveConnection(ImmersiveNetworkPart part, ConnectionData connection, ImmersiveNetwork network)
        {
            // Проверяем, есть ли уже такое соединение
            var existingConnection = network.ImmersiveConnections
                .FirstOrDefault(c =>
                    c.LocalPos.Equals(part.Position) &&
                    c.LocalNodeIndex == connection.LocalNodeIndex &&
                    c.NeighborPos.Equals(connection.NeighborPos) &&
                    c.NeighborNodeIndex == connection.NeighborNodeIndex);

            if (existingConnection == null)
            {
                // Создаем новое сетевое соединение
                var networkConnection = new NetworkImmersiveConnection
                {
                    LocalPos = part.Position,
                    LocalNodeIndex = connection.LocalNodeIndex,
                    NeighborPos = connection.NeighborPos,
                    NeighborNodeIndex = connection.NeighborNodeIndex,
                    Parameters = connection.Parameters
                };
                network.ImmersiveConnections.Add(networkConnection);
                network.version++;
            }
            else
            {
                // Обновляем параметры существующего соединения
                existingConnection.Parameters = connection.Parameters;
            }

            // Добавляем соседа в сеть если его там нет
            if (!network.PartPositions.Contains(connection.NeighborPos))
            {
                // Ищем сеть, к которой принадлежит сосед
                ImmersiveNetwork neighborNetwork = null;
                foreach (var net in Networks)
                {
                    if (net.PartPositions.Contains(connection.NeighborPos))
                    {
                        neighborNetwork = net;
                        break;
                    }
                }

                if (neighborNetwork != null && neighborNetwork != network)
                {
                    // Если сосед уже в другой сети, объединяем сети
                    var networksToMerge = new HashSet<ImmersiveNetwork> { network, neighborNetwork };
                    var mergedNetwork = MergeNetworks(networksToMerge);

                    // Обновляем ссылку на сеть
                    network = mergedNetwork;
                }
                else if (neighborNetwork == null)
                {
                    // Сосед не в сети, добавляем его
                    if (Parts.TryGetValue(connection.NeighborPos, out var neighborPart))
                    {
                        network.PartPositions.Add(connection.NeighborPos);

                        // Добавляем компоненты соседа в сеть
                        if (neighborPart.Conductor is { } conductor) network.Conductors.Add(conductor);
                        if (neighborPart.Consumer is { } consumer) network.Consumers.Add(consumer);
                        if (neighborPart.Producer is { } producer) network.Producers.Add(producer);
                        if (neighborPart.Accumulator is { } accumulator) network.Accumulators.Add(accumulator);
                        if (neighborPart.Transformator is { } transformator) network.Transformators.Add(transformator);
                    }
                }
            }
        }

        /// <summary>
        /// Удаляет устаревшие иммерсивные соединения
        /// </summary>
        private void RemoveStaleImmersiveConnections(ImmersiveNetworkPart part, ImmersiveNetwork network)
        {
            // Находим соединения которые больше не актуальны
            var staleConnections = network.ImmersiveConnections
                .Where(c => c.LocalPos.Equals(part.Position) &&
                           !part.Connections.Any(pc =>
                               pc.LocalNodeIndex == c.LocalNodeIndex &&
                               pc.NeighborPos.Equals(c.NeighborPos) &&
                               pc.NeighborNodeIndex == c.NeighborNodeIndex))
                .ToList();

            // Удаляем устаревшие соединения
            foreach (var staleConnection in staleConnections)
            {
                network.ImmersiveConnections.Remove(staleConnection);
                network.version++;
            }
        }

        /// <summary>
        /// Удаляем соединения
        /// </summary>
        /// <param name="position"></param>
        public void Remove(BlockPos position)
        {
            if (Parts.TryGetValue(position, out var part))
            {
                Parts.Remove(position);

                // Удаляем из всех сетей
                foreach (var network in Networks.Where(n => n.PartPositions.Contains(position)).ToList())
                {
                    network.PartPositions.Remove(position);

                    // Удаляем соединения связанные с этой позицией
                    network.ImmersiveConnections.RemoveAll(c =>
                        c.LocalPos.Equals(position) || c.NeighborPos.Equals(position));

                    network.version++;

                    // Если сеть пустая, удаляем её
                    if (network.PartPositions.Count == 0)
                    {
                        Networks.Remove(network);
                    }
                }
            }
        }

        /// <summary>
        /// Чистка всего и вся
        /// </summary>
        public void Cleaner()
        {
            foreach (var part in Parts)
            {

                // подчищаем списки пакетов, но не в ноль
                part.Value.Packets?.Clear();

                // Обработка параметров для иммерсивных проводов
                foreach (var connection in part.Value.Connections)
                {
                    if (!connection.Parameters.burnout && connection.Parameters.ticksBeforeBurnout > 0 && part.Value.Conductor is not VirtualConductor)
                    {
                        connection.Parameters.ticksBeforeBurnout--;
                    }
                }

                // Обработка основных параметров
                if (!part.Value.MainEparams.burnout && part.Value.MainEparams.ticksBeforeBurnout > 0 && part.Value.Conductor is not VirtualConductor)
                {
                    part.Value.MainEparams.ticksBeforeBurnout--;
                }

                //заполняем нулями токи и выданные энергии с пакетов
                if (!_sumEnergy.TryAdd(part.Key, 0F))
                {
                    _sumEnergy[part.Key] = 0F;
                }

                // Обнуляем токи в соединениях
                foreach (var connection in part.Value.Connections)
                {
                    connection.Parameters.current = 0f;
                }

                // Обнуляем основной ток
                part.Value.MainEparams.current = 0f;
            }
        }

        /// <summary>
        /// Логистическая задача для иммерсивных проводов
        /// </summary>
        private void LogisticalTask(ImmersiveNetwork network,
            List<BlockPos> consumerPositions,
            List<float> consumerRequests,
            List<BlockPos> producerPositions,
            List<float> producerGive,
            ImmersiveSimulation sim)
        {
            var cP = sim.CountWorkingCustomers = consumerPositions.Count; // Количество потребителей
            var pP = sim.CountWorkingStores = producerPositions.Count; // Количество производителей


            BlockPos start;
            BlockPos end;

            // обновляем массив для расстояний, магазинов и клиентов
            if (sim.Distances.Length < cP * pP)
            {
                Array.Resize(ref sim.Distances, cP * pP);
                Array.Resize(ref sim.Path, cP * pP);
                Array.Resize(ref sim.NodeIndices, cP * pP);
                Array.Resize(ref sim.Voltage, cP * pP);
            }

            if (sim.Stores == null || sim.Stores.Length < pP)
                sim.Stores = new ImmersiveStore[pP];

            if (sim.Customers == null || sim.Customers.Length < cP)
                sim.Customers = new ImmersiveCustomer[cP];


            for (var i = 0; i < cP; i++)
            {
                for (var j = 0; j < pP; j++)
                {
                    start = consumerPositions[i];
                    end = producerPositions[j];

                    if (ImmersivePathFinder.Heuristic(start, end) < ElectricalProgressive.ElectricalProgressive.maxDistanceForFinding)
                    {
                        if (ImmersivePathCacheManager.TryGet(start, end, out var cachedPath, out var nodeIndices, out var version, out var voltage))
                        {
                            sim.Distances[i * pP + j] = cachedPath != null ? cachedPath.Length : int.MaxValue;
                            if (version != network
                                    .version) // Если версия сети не совпадает, то добавляем запрос в очередь
                            {
                                _immersiveAsyncPathFinder.EnqueueRequest(start, end, network); // Добавляем запрос в очередь
                            }

                            sim.Path[i * pP + j] = cachedPath;
                            sim.NodeIndices[i * pP + j] = nodeIndices;
                            sim.Voltage[i * pP + j] = voltage;
                        }
                        else
                        {
                            _immersiveAsyncPathFinder.EnqueueRequest(start, end, network); // Добавляем запрос в очередь
                            sim.Distances[i * pP + j] = int.MaxValue; // Пока маршрута нет, ставим максимальное значение

                            sim.Path[i * pP + j] = null;
                            sim.NodeIndices[i * pP + j] = null;
                            sim.Voltage[i * pP + j] = 0;
                        }
                    }
                    else
                    {
                        sim.Distances[i * pP + j] = int.MaxValue;
                        sim.Path[i * pP + j] = null;
                        sim.NodeIndices[i * pP + j] = null;
                        sim.Voltage[i * pP + j] = 0;
                    }
                }
            }



            // инициализируем магазины
            for (var j = 0; j < pP; j++)
            {
                var store = sim.Stores[j];
                if (store == null)
                    sim.Stores[j] = store = new ImmersiveStore(j, producerGive[j]);
                store.Update(j, producerGive[j]);
            }


            // инициализируем клиентов
            for (var i = 0; i < cP; i++)
            {
                var distBuffer = new int[pP];

                for (var j = 0; j < pP; j++)
                {
                    distBuffer[j] = sim.Distances[i * pP + j];
                }

                var cust = sim.Customers[i];
                if (cust == null)
                    sim.Customers[i] = cust = new ImmersiveCustomer(i, consumerRequests[i], distBuffer);
                else
                    cust.Update(i, consumerRequests[i], distBuffer);
            }


            sim.Run(); // Запускаем симуляцию для распределения энергии между потребителями и производителями
        }


        /// <summary>
        /// Обновление электрических сетей
        /// </summary>
        private void UpdateNetworkComponents()
        {
            if (_elapsedMs > 1.0f) //обновляем инфу раз в секунду
            {
                foreach (var part in Parts.Values)
                {
                    // проводники первыми, так как обычно их больше
                    if (part.Conductor is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Conductor.Update();
                        continue;
                    }

                    if (part.Producer is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Producer.Update();
                        continue;
                    }

                    if (part.Consumer is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Consumer.Update();
                        continue;
                    }

                    if (part.Accumulator is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Accumulator.Update();
                        continue;
                    }

                    if (part.Transformator is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Transformator.Update();
                        continue;
                    }
                }

                _elapsedMs = 0f; // сбросить накопленное время
            }
        }



        // Добавляем класс для пула контекстов обработки
        private class NetworkProcessingContext
        {
            public List<Consumer> LocalConsumers { get; } = [];
            public List<Producer> LocalProducers { get; } = [];
            public List<Accumulator> LocalAccums { get; } = [];
            public List<ImmersiveEnergyPacket> LocalPackets { get; } = [];

            public List<BlockPos> ConsumerPositions { get; } = [];
            public List<float> ConsumerRequests { get; } = [];
            public List<BlockPos> ProducerPositions { get; } = [];
            public List<float> ProducerGive { get; } = [];
            public List<BlockPos> Consumer2Positions { get; } = [];
            public List<float> Consumer2Requests { get; } = [];
            public List<BlockPos> Producer2Positions { get; } = [];
            public List<float> Producer2Give { get; } = [];

            public ImmersiveSimulation Sim { get; } = new();
            public ImmersiveSimulation Sim2 { get; } = new();

            public void Clear()
            {
                LocalConsumers.Clear();
                LocalProducers.Clear();
                LocalAccums.Clear();
                LocalPackets.Clear();
                ConsumerPositions.Clear();
                ConsumerRequests.Clear();
                ProducerPositions.Clear();
                ProducerGive.Clear();
                Consumer2Positions.Clear();
                Consumer2Requests.Clear();
                Producer2Positions.Clear();
                Producer2Give.Clear();
            }
        }

        // Добавляем пул контекстов
        private readonly ConcurrentBag<NetworkProcessingContext> _contextPool = [];

        // Метод для получения контекста из пула
        private NetworkProcessingContext GetContext()
        {
            if (_contextPool.TryTake(out var context))
            {
                context.Clear();
                return context;
            }
            return new NetworkProcessingContext();
        }

        // Метод для возврата контекста в пул
        private void ReturnContext(NetworkProcessingContext context)
        {
            _contextPool.Add(context);
        }



        private void ProcessNetwork(ImmersiveNetwork network, NetworkProcessingContext context)
        {
            // Этап 1: Очищаем локальные переменные цикла ----------------------------------------------------------------------------



            // Этап 2: Сбор запросов от потребителей----------------------------------------------------------------------------
            var cons = network.Consumers.Count; // Количество потребителей в сети
            float requestedEnergy; // Запрошенная энергия от потребителей


            foreach (var electricConsumer in network.Consumers)
            {
                if (network.PartPositions.Contains(electricConsumer.Pos) // Проверяем, что потребитель находится в части сети
                    && Parts[electricConsumer.Pos].IsLoaded              // Проверяем, что потребитель загружен
                    && electricConsumer.Consume_request() > 0)             // Проверяем, что потребитель запрашивает энергию вообще
                {
                    context.LocalConsumers.Add(new Consumer(electricConsumer));

                    // Если включена компенсация потерь
                    if (ElectricalProgressive.ElectricalProgressive.enableLossCompensation)
                    {
                        var received = electricConsumer.getPowerReceive();
                        var requested = electricConsumer.Consume_request();

                        // если ниже нуля, то ставим 1.0
                        if (electricConsumer.AvgConsumeCoeff < 1.0f)
                            electricConsumer.AvgConsumeCoeff = 1.0f;

                        var smoothedCoeff = electricConsumer.AvgConsumeCoeff;


                        // если запрошено больше, чем получено, то увеличиваем коэффициент сглаживания
                        if (requested > received)
                        {
                            if (smoothedCoeff < 2.0)
                            {
                                smoothedCoeff += 0.1f;
                                smoothedCoeff = CalculateEma(0.05f, smoothedCoeff, electricConsumer.AvgConsumeCoeff);
                            }
                        }
                        else
                        {
                            if (smoothedCoeff > 1.0)
                            {
                                smoothedCoeff -= 0.1f;
                                smoothedCoeff = CalculateEma(0.05f, smoothedCoeff, electricConsumer.AvgConsumeCoeff);
                            }
                        }


                        requestedEnergy = requested * smoothedCoeff; // Запрашиваем с учётом сглаженного коэффициента
                        electricConsumer.AvgConsumeCoeff = smoothedCoeff;  // Храним сглаженный coeff (не энергию!)
                    }
                    else
                    {
                        requestedEnergy = electricConsumer.Consume_request();
                    }


                    context.ConsumerPositions.Add(electricConsumer.Pos);
                    context.ConsumerRequests.Add(requestedEnergy);
                }
            }

            // Этап 3: Сбор энергии с генераторов и аккумуляторов----------------------------------------------------------------------------
            var prod = network.Producers.Count + network.Accumulators.Count; // Количество производителей в сети
            float giveEnergy; // Энергия, которую отдают производители

            foreach (var electricProducer in network.Producers)
            {
                if (network.PartPositions.Contains(electricProducer.Pos) // Проверяем, что генератор находится в части сети
                    && Parts[electricProducer.Pos].IsLoaded              // Проверяем, что генератор загружен
                    && electricProducer.Produce_give() > 0)                // Проверяем, что генератор отдает энергию вообще
                {
                    context.LocalProducers.Add(new Producer(electricProducer));
                    giveEnergy = electricProducer.Produce_give();
                    context.ProducerPositions.Add(electricProducer.Pos);
                    context.ProducerGive.Add(giveEnergy);

                }
            }

            foreach (var electricAccum in network.Accumulators)
            {
                if (network.PartPositions.Contains(electricAccum.Pos)   // Проверяем, что аккумулятор находится в части сети
                    && Parts[electricAccum.Pos].IsLoaded                // Проверяем, что аккумулятор загружен
                    && electricAccum.canRelease() > 0)                    // Проверяем, что аккумулятор может отдать энергию вообще
                {
                    context.LocalAccums.Add(new Accumulator(electricAccum));
                    giveEnergy = electricAccum.canRelease();
                    context.ProducerPositions.Add(electricAccum.Pos);
                    context.ProducerGive.Add(giveEnergy);

                }
            }

            // Этап 4: Распределение энергии ----------------------------------------------------------------------------
            LogisticalTask(network, context.ConsumerPositions, context.ConsumerRequests, context.ProducerPositions, context.ProducerGive, context.Sim);



            ImmersiveEnergyPacket packet;   // Временная переменная для пакета энергии
            BlockPos posStore; // Позиция магазина в мире
            BlockPos posCustomer; // Позиция потребителя в мире
            var customCount = context.ConsumerPositions.Count; // Количество клиентов в симуляции
            var storeCount = context.ProducerPositions.Count; // Количество магазинов в симуляции
            var k = 0;
            for (var i = 0; i < customCount; i++)
            {
                for (k = 0; k < storeCount; k++)
                {
                    var value = context.Sim.Customers![i].Received[context.Sim.Stores![k].Id];
                    if (value > 0)
                    {

                        // Проверяем, что пути и направления не равны null
                        if (context.Sim.Path[i * storeCount + k] == null)
                            continue;

                        // создаём пакет, не копируя ничего
                        packet = new ImmersiveEnergyPacket(
                            value,
                            context.Sim.Voltage[i * storeCount + k],
                            context.Sim.Path[i * storeCount + k].Length - 1,
                            context.Sim.Path[i * storeCount + k],
                            context.Sim.NodeIndices[i * storeCount + k]
                        );


                        // Добавляем пакет в глобальный список
                        context.LocalPackets.Add(packet);


                    }


                }
            }


            // Этап 5: Забираем у аккумуляторов выданное----------------------------------------------------------------------------
            var consIter = 0; // Итератор
            foreach (var accum in context.LocalAccums)
            {
                if (context.Sim.Stores![consIter + context.LocalProducers.Count].Stock < accum.ImmersiveAccum.canRelease())
                {
                    accum.ImmersiveAccum.Release(accum.ImmersiveAccum.canRelease() -
                                                 context.Sim.Stores[consIter + context.LocalProducers.Count].Stock);
                }

                consIter++;
            }


            // Этап 6: Зарядка аккумуляторов    ----------------------------------------------------------------------------
            cons = network.Accumulators.Count; // Количество аккумов в сети

            context.LocalAccums.Clear();
            foreach (var electricAccum in network.Accumulators)
            {
                if (network.PartPositions.Contains(electricAccum.Pos)   // Проверяем, что аккумулятор находится в части сети
                    && Parts[electricAccum.Pos].IsLoaded)                // Проверяем, что аккумулятор загружен
                                                                         // Проверяем, что аккумулятор может отдать энергию вообще
                {
                    context.LocalAccums.Add(new Accumulator(electricAccum));
                    requestedEnergy = electricAccum.canStore();

                    context.Consumer2Positions.Add(electricAccum.Pos);
                    context.Consumer2Requests.Add(requestedEnergy);
                }
            }




            // Этап 7: Остатки генераторов  ----------------------------------------------------------------------------
            prod = context.LocalProducers.Count; // Количество производителей в сети
            var prodIter = 0; // Итератор для производителей


            foreach (var producer in context.LocalProducers)
            {
                giveEnergy = context.Sim.Stores![prodIter].Stock;
                context.Producer2Positions.Add(producer.ImmersiveProducer.Pos);
                context.Producer2Give.Add(giveEnergy);
                prodIter++;
            }


            // Этап 8: Распределение энергии для аккумуляторов ----------------------------------------------------------------------------
            LogisticalTask(network, context.Consumer2Positions, context.Consumer2Requests, context.Producer2Positions, context.Producer2Give, context.Sim2);


            customCount = context.Consumer2Positions.Count; // Количество клиентов в симуляции 2
            storeCount = context.Producer2Positions.Count; // Количество магазинов в симуляции 2

            for (var i = 0; i < customCount; i++)
            {
                for (k = 0; k < storeCount; k++)
                {
                    var value = context.Sim2.Customers![i].Received[context.Sim2.Stores![k].Id];
                    if (value > 0)
                    {
                        // Проверяем, что пути и направления не равны null
                        if (context.Sim2.Path[i * storeCount + k] == null)
                            continue;

                        // создаём пакет, не копируя ничего
                        packet = new ImmersiveEnergyPacket(
                            value,
                            context.Sim2.Voltage[i * storeCount + k],
                            context.Sim2.Path[i * storeCount + k].Length - 1,
                            context.Sim2.Path[i * storeCount + k],
                            context.Sim2.NodeIndices[i * storeCount + k]
                        );


                        // Добавляем пакет в глобальный список
                        context.LocalPackets.Add(packet);


                    }
                }
            }


            // Этап 9: Сообщение генераторам о нагрузке ----------------------------------------------------------------------------
            var j = 0;
            foreach (var producer in context.LocalProducers)
            {
                var totalOrder = context.Sim.Stores![j].TotalRequest + context.Sim2.Stores![j].TotalRequest;
                producer.ImmersiveProducer.Produce_order(totalOrder);
                j++;
            }



            // Обновляем инфу об электрических цепях
            UpdateNetworkInfo(network);
        }



        /// <summary>
        /// Тикаем сервер
        /// </summary>
        /// <param name="deltaTime"></param>
        private void OnGameTickServer(float deltaTime)
        {
            // выходим полюбому, если нет API
            if (_sapi == null)
                return;

            //Очищаем старые пути
            if (_sapi.World.Rand.NextDouble() < 0.01d)
            {
                PathCacheManager.Cleanup();
            }

            // Если время очистки кэша путей вышло, то очищаем кэш
            Cleaner();


            // Потокобезопасный контейнер для сбора пакетов
            var packetsBag = new ConcurrentBag<List<ImmersiveEnergyPacket>>();

            // Обрабатываем сети параллельно с использованием пула контекстов
            Parallel.ForEach(Networks, new ParallelOptions
            {
                MaxDegreeOfParallelism = ElectricalProgressive.ElectricalProgressive.multiThreading
            }, network =>
            {
                var context = GetContext();
                try
                {
                    // Используем контекст вместо локальных переменных
                    ProcessNetwork(network, context);

                    if (context.LocalPackets.Count > 0)
                    {
                        // Создаем копию только если есть пакеты
                        var packetsCopy = new List<ImmersiveEnergyPacket>(context.LocalPackets);
                        packetsBag.Add(packetsCopy);
                    }
                }
                finally
                {
                    ReturnContext(context);
                }
            });

            // Собираем все пакеты
            foreach (var packets in packetsBag)
                _globalEnergyPackets.AddRange(packets);




            // Обновление электрических компонентов в сети, если прошло достаточно времени около 0.5 секунд
            _elapsedMs += deltaTime;
            UpdateNetworkComponents();


            // Этап 11: Потребление энергии пакетами и Этап 12: Перемещение пакетов-----------------------------------------------
            ConsumeAndMovePackets();


            foreach (var pair in _sumEnergy)
            {
                if (Parts.TryGetValue(pair.Key, out var parta))
                {
                    if (parta.Consumer != null)
                        parta.Consumer!.Consume_receive(pair.Value);
                    else if (parta.Accumulator != null)
                        parta.Accumulator!.Store(pair.Value);
                }
                else
                {
                    _sumEnergy.Remove(pair.Key); // Удаляем, если части сети этой уже нет
                }
            }




            // Этап 13: Проверка сгорания проводов и трансформаторов ----------------------------------------------------------------------------

            var bAccessor = _sapi!.World.BlockAccessor; // аксессор для блоков
            BlockPos partPos;                        // Временная переменная для позиции части сети
            ImmersiveNetworkPart part;                        // Временная переменная для части сети
            bool updated;                            // Флаг обновления части сети от повреждения
            EParams faceParams;                      // Параметры грани сети
            int lastFaceIndex;                       // Индекс последней грани в пакете
            float totalEnergy;                       // Суммарная энергия в трансформаторе
            float totalCurrent;                      // Суммарный ток в трансформаторе
            var kons = 0;

            foreach (var partEntry in Parts)
            {
                partPos = partEntry.Key;
                part = partEntry.Value;

                //обновляем каждый блок сети
                updated = kons % 20 == _envUpdater &&
                          part.IsLoaded && false         // блок загружен?
                                                         //(damageManager?.DamageByEnvironment(this._sapi, ref part, ref bAccessor) ?? false)
                          ;
                kons++;


                if (updated)
                {
                    // Проверяем сгорание для иммерсивных соединений
                    foreach (var connection in part.Connections)
                    {
                        if (connection.Parameters.voltage == 0 || !connection.Parameters.burnout)
                            continue;

                        ResetComponents(ref part); // сброс компонентов сети
                    }
                }



                var bufPartTrans = part.Transformator;
                // Обработка трансформаторов
                if (bufPartTrans != null)
                {
                    totalEnergy = 0f;
                    totalCurrent = 0f;

                    foreach (var packet2 in part.Packets)
                    {

                        totalEnergy += packet2.energy;
                        totalCurrent += packet2.energy / packet2.voltage;


                        if (packet2.voltage == bufPartTrans.highVoltage)
                            packet2.voltage = bufPartTrans.lowVoltage;
                        else if (packet2.voltage == bufPartTrans.lowVoltage)
                            packet2.voltage = bufPartTrans.highVoltage;

                    }


                    part.MainEparams.current = totalCurrent;
                    bufPartTrans.setPower(totalEnergy);

                }


                // Проверка на превышение напряжения для иммерсивных соединений
                foreach (var packet2 in part.Packets)
                {
                    foreach (var connection in part.Connections)
                    {
                        if (connection.Parameters.voltage != 0 && packet2.voltage > connection.Parameters.voltage)
                        {
                            connection.Parameters.prepareForBurnout(2);

                            if (packet2.path[packet2.currentIndex] == partPos)
                                packet2.shouldBeRemoved = true;

                            ResetComponents(ref part);
                        }
                    }
                }


                // Проверка на превышение тока для иммерсивных соединений
                foreach (var connection in part.Connections)
                {
                    if (connection.Parameters.voltage == 0 ||
                        Math.Abs(connection.Parameters.current) <= connection.Parameters.maxCurrent * connection.Parameters.lines)
                        continue;

                    connection.Parameters.prepareForBurnout(1);

                    foreach (var p in _globalEnergyPackets)
                    {
                        // Здесь нужно добавить логику проверки для иммерсивных соединений
                        if (p.path[p.currentIndex] == partPos)
                        {
                            p.shouldBeRemoved = true;
                        }
                    }

                    ResetComponents(ref part);
                }
            }


            _envUpdater++;
            if (_envUpdater > 19)
                _envUpdater = 0;



            //Удаление ненужных пакетов
            _globalEnergyPackets.RemoveAll(p => p.shouldBeRemoved);


        }


        /// <summary>
        /// Потребление и перемещение пакетов энергии
        /// </summary>
        private void ConsumeAndMovePackets()
        {
            BlockPos pos;                   // Временная переменная для позиции
            float resistance, current, lossEnergy;  // Переменные для расчета сопротивления, тока и потерь энергии                    
            int curIndex, currentFacingFrom;        // текущий индекс и направление в пакете
            BlockPos currentPos;           // текущая и следующая позиции в пути пакета
            ImmersiveNetworkPart currentPart;      // Временные переменные для частей сети



            // Заполняем списки пакетов по позициям
            foreach (var packet in _globalEnergyPackets)
            {
                pos = packet.path[packet.currentIndex];
                if (Parts.TryGetValue(pos, out var partValue))
                {
                    if (partValue.Packets == null)
                        partValue.Packets = [];
                    else
                    {
                        partValue.Packets.Add(packet);
                    }
                }
                else // чтобы не застревали в частях сети, которые перестали существовать
                {
                    packet.shouldBeRemoved = true;
                }

            }

            // перебираем все части
            foreach (var partValue in Parts.Values)
            {
                // если пакетов нет тут, то пропускаем
                if (partValue.Packets == null || partValue.Packets.Count == 0)
                    continue;

                //int deltaX, deltaY, deltaZ;

                // перебираем все пакеты, которые могут находиться в этой части
                foreach (var packet in partValue.Packets)
                {
                    curIndex = packet.currentIndex; //текущий индекс в пакете

                    if (curIndex > 0)
                    {
                        // ищем провода которые ведут на слежующий блок в пути
                        // по-хорошему номера проводов надо сохранять в вместе с блоками, где они крепятся
                        var connections =
                            partValue.Connections.Where(c => c.NeighborPos.Equals(packet.path[curIndex - 1]));
                        if (connections == null)
                        {
                            packet.shouldBeRemoved = true;
                            continue;
                        }

                        // Проверяем иммерсивные провода
                        if (!partValue.Connections.Any(c => c.Parameters.burnout) //проверяем не сгорели ли соединения
                            && connections.Count() > 0) //а есть ли такой провод?
                        {
                            ConnectionData buf = null;
                            foreach (var conn in connections)
                            {

                                // проверяем может ли пакет тут пройти
                                if (conn.Parameters.voltage > 0
                                    && !conn.Parameters.burnout
                                    && packet.voltage <= conn.Parameters.voltage)
                                {
                                    buf = conn;
                                    break; // может пройти - значит выходим из цикла
                                }

                            }

                            // если вдруг параметры у провода кривые 
                            if (buf == null || buf.Parameters.voltage == 0)
                            {
                                packet.shouldBeRemoved = true;
                                continue;
                            }

                            // считаем сопротивление для основного блока (используем основные параметры как fallback)
                            resistance = ElectricalProgressive.ElectricalProgressive.energyLossFactor *
                                         buf.WireLength*
                                         buf.Parameters.resistivity /
                                         (buf.Parameters.lines *
                                          buf.Parameters.crossArea);

                            // Провод в изоляции теряет меньше энергии
                            if (buf.Parameters.isolated)
                                resistance /= 2.0f;

                            // считаем ток по закону Ома
                            current = packet.energy / packet.voltage;

                            // считаем потерю энергии по закону Джоуля
                            lossEnergy = current * current * resistance;
                            packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                            // пересчитаем ток уже с учетом потерь
                            current = packet.energy / packet.voltage;

                            // Учитываем ток в основных параметрах
                            buf.Parameters.current += current;

                            // 3) Если энергия пакета почти нулевая — удаляем пакет
                            if (packet.energy <= 0.001f)
                            {
                                packet.shouldBeRemoved = true;
                            }



                        }
                        else
                        {
                            packet.shouldBeRemoved = true; // этот пакет удаляем
                        }

                    }


                    // последний блок?
                    if (curIndex == 0)
                    {
                        pos = packet.path[0];

                        if (Parts.TryGetValue(pos, out var part2))
                        {
                            // ReSharper disable once ReplaceWithSingleAssignment.False
                            bool isValid = false;


                            // Также проверяем основные параметры
                            if (!isValid
                                && part2.MainEparams.voltage > 0
                                && !part2.MainEparams.burnout
                                && packet.voltage == part2.MainEparams.voltage)
                            {
                                isValid = true;
                            }

                            // если все ок, то продолжаем
                            if (isValid)
                            {
                                if (_sumEnergy.ContainsKey(pos))
                                {
                                    _sumEnergy[pos] += packet.energy;
                                }
                                else
                                {
                                    _sumEnergy.Add(pos, packet.energy);
                                }
                            }
                        }

                        packet.shouldBeRemoved = true; // этот пакет удаляем
                    }
                    else
                    {
                        // первый блок пропускаем из расчета
                        if (curIndex == packet.path.Length - 1)
                        {
                            packet.currentIndex--;
                            continue;
                        }

                        //currentPos = packet.path[curIndex]; // текущая позиция в пути пакета


                        // считаем сопротивление для основного блока (используем основные параметры как fallback)
                        resistance = ElectricalProgressive.ElectricalProgressive.energyLossFactor *
                                     partValue.MainEparams.resistivity /
                                     (partValue.MainEparams.lines *
                                      partValue.MainEparams.crossArea);

                        // Провод в изоляции теряет меньше энергии
                        if (partValue.MainEparams.isolated)
                            resistance /= 2.0f;

                        // считаем ток по закону Ома
                        current = packet.energy / packet.voltage;

                        // считаем потерю энергии по закону Джоуля
                        lossEnergy = current * current * resistance;
                        packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                        // пересчитаем ток уже с учетом потерь
                        current = packet.energy / packet.voltage;

                        // Учитываем ток в основных параметрах
                        partValue.MainEparams.current += current;

                        // 3) Если энергия пакета почти нулевая — удаляем пакет
                        if (packet.energy <= 0.001f)
                        {
                            packet.shouldBeRemoved = true;
                        }

                        // переходим к следующему блоку в пути
                        packet.currentIndex--;




                    }
                }
            }


            //Удаление ненужных пакетов
            _globalEnergyPackets.RemoveAll(p => p.shouldBeRemoved);

        }


        /// <summary>
        /// Обновление информации о сети
        /// </summary>
        /// <param name="network"></param>
        private void UpdateNetworkInfo(ImmersiveNetwork network)
        {
            // расчет емкости
            var capacity = 0f; // Суммарная емкость сети
            var maxCapacity = 0f; // Максимальная емкость сети

            foreach (var electricAccum in network.Accumulators)
            {
                if (network.PartPositions.Contains(electricAccum.Pos)   // Проверяем, что аккумулятор находится в части сети
                    && Parts[electricAccum.Pos].IsLoaded)               // Проверяем, что аккумулятор загружен
                                                                        // Проверяем, что аккумулятор может отдать энергию вообще
                {
                    capacity += electricAccum.GetCapacity();
                    maxCapacity += electricAccum.GetMaxCapacity();
                }


            }

            network.Capacity = capacity;
            network.MaxCapacity = maxCapacity;



            // Расчет производства (чистая генерация генераторами)
            var production = 0f;
            foreach (var electricProducer in network.Producers)
            {
                if (network.PartPositions.Contains(electricProducer.Pos)    // Проверяем, что генератор находится в части сети
                    && Parts[electricProducer.Pos].IsLoaded)                // Проверяем, что генератор загружен
                {
                    production += Math.Min(electricProducer.getPowerGive(), electricProducer.getPowerOrder());
                }
            }

            network.Production = production;


            // Расчет необходимой энергии для потребителей!
            var requestSum = 0f;
            foreach (var electricConsumer in network.Consumers)
            {
                if (network.PartPositions.Contains(electricConsumer.Pos) // Проверяем, что потребитель находится в части сети
                    && Parts[electricConsumer.Pos].IsLoaded) // Проверяем, что потребитель загружен
                {
                    requestSum += electricConsumer.getPowerRequest();
                }
            }

            network.Request = Math.Max(requestSum, 0f);


            // Расчет потребления (только потребителями)
            var consumption = 0f;

            // потребление в первой симуляции
            foreach (var electricConsumer in network.Consumers)
            {
                if (network.PartPositions.Contains(electricConsumer.Pos) // Проверяем, что потребитель находится в части сети
                    && Parts[electricConsumer.Pos].IsLoaded) // Проверяем, что потребитель загружен
                {
                    consumption += electricConsumer.getPowerReceive();
                }
            }


            network.Consumption = consumption;
        }

        // Вынесенный метод сброса компонентов
        private static void ResetComponents(ref ImmersiveNetworkPart part)
        {
            part.Consumer?.Consume_receive(0f);
            part.Producer?.Produce_order(0f);
            part.Accumulator?.SetCapacity(0f);
            part.Transformator?.setPower(0f);
        }

        /// <summary>
        /// Объединение цепей
        /// </summary>
        /// <param name="networks"></param>
        /// <returns></returns>
        private ImmersiveNetwork MergeNetworks(HashSet<ImmersiveNetwork> networks)
        {
            ImmersiveNetwork? outNetwork = null;

            foreach (var network in networks)
            {
                if (outNetwork == null || outNetwork.PartPositions.Count < network.PartPositions.Count)
                {
                    outNetwork = network;
                }
            }

            if (outNetwork != null)
            {
                foreach (var network in networks)
                {
                    if (outNetwork == network)
                    {
                        continue;
                    }

                    foreach (var position in network.PartPositions)
                    {
                        var part = this.Parts[position];
                        // Для иммерсивных проводов обновляем сеть
                        part.Network = outNetwork;

                        if (part.Conductor is { } conductor) outNetwork.Conductors.Add(conductor);
                        if (part.Consumer is { } consumer) outNetwork.Consumers.Add(consumer);
                        if (part.Producer is { } producer) outNetwork.Producers.Add(producer);
                        if (part.Accumulator is { } accumulator) outNetwork.Accumulators.Add(accumulator);
                        if (part.Transformator is { } transformator) outNetwork.Transformators.Add(transformator);

                        outNetwork.PartPositions.Add(position);
                    }

                    // Переносим иммерсивные соединения
                    outNetwork.ImmersiveConnections.AddRange(network.ImmersiveConnections);

                    network.PartPositions.Clear();
                    network.ImmersiveConnections.Clear();
                    this.Networks.Remove(network);
                }
            }

            outNetwork ??= this.CreateNetwork();

            return outNetwork;
        }

        /// <summary>
        /// Удаляем сеть
        /// </summary>
        /// <param name="network"></param>
        private void RemoveNetwork(ref ImmersiveNetwork network)
        {
            var partPositions = new BlockPos[network.PartPositions.Count];
            network.PartPositions.CopyTo(partPositions);
            network.version++;
            this.Networks.Remove(network);                                  //удаляем цепь из списка цепей

            foreach (var position in partPositions)                         //перебираем по всем бывшим элементам этой цепи
            {
                if (this.Parts.TryGetValue(position, out var part))         //есть такое соединение?
                {
                    part.Network = null;                                    //обнуляем сеть
                }
            }

            // Для иммерсивных проводов создаем новые сети для оставшихся частей
            foreach (var position in partPositions)                                 //перебираем по всем бывшим элементам этой цепи
            {
                if (this.Parts.TryGetValue(position, out var part))                 //есть такое соединение?
                {
                    UpdateImmersiveConnections(ref part);                           //обновляем соединения
                }
            }
        }

        /// <summary>
        /// Создаем новую цепь
        /// </summary>
        /// <returns></returns>
        private ImmersiveNetwork CreateNetwork()
        {
            var network = new ImmersiveNetwork();
            this.Networks.Add(network);

            return network;
        }

        /// <summary>
        /// Задать проводник
        /// </summary>
        /// <param name="position"></param>
        /// <param name="conductor"></param>
        public void SetConductor(BlockPos position, IEImmersiveConductor? conductor) =>
        SetComponent(
            position,
            conductor,
            part => part.Conductor,
            (part, c) => part.Conductor = c,
            network => network.Conductors);


        /// <summary>
        /// Задать потребителя
        /// </summary>
        /// <param name="position"></param>
        /// <param name="consumer"></param>
        public void SetConsumer(BlockPos position, IEImmersiveConsumer? consumer) =>
        SetComponent(
            position,
            consumer,
            part => part.Consumer,
            (part, c) => part.Consumer = c,
            network => network.Consumers);

        /// <summary>
        /// Задать генератор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="producer"></param>
        public void SetProducer(BlockPos position, IEImmersiveProducer? producer) =>
            SetComponent(
                position,
                producer,
                part => part.Producer,
                (part, p) => part.Producer = p,
                network => network.Producers);

        /// <summary>
        /// Задать аккумулятор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="accumulator"></param>
        public void SetAccumulator(BlockPos position, IEImmersiveAccumulator? accumulator) =>
            SetComponent(
                position,
                accumulator,
                part => part.Accumulator,
                (part, a) => part.Accumulator = a,
                network => network.Accumulators);

        /// <summary>
        /// Задать трансформатор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="transformator"></param>
        public void SetTransformator(BlockPos position, IEImmersiveTransformator? transformator) =>
            SetComponent(
                position,
                transformator,
                part => part.Transformator,
                (part, a) => part.Transformator = a,
                network => network.Transformators);


        /// <summary>
        /// Задает компоненты разных типов
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="position"></param>
        /// <param name="newComponent"></param>
        /// <param name="getComponent"></param>
        /// <param name="setComponent"></param>
        /// <param name="getCollection"></param>
        private void SetComponent<T>(
            BlockPos position,
            T? newComponent,
            System.Func<ImmersiveNetworkPart, T?> getComponent,
            Action<ImmersiveNetworkPart, T?> setComponent,
            System.Func<ImmersiveNetwork, ICollection<T>> getCollection)
            where T : class
        {
            if (!this.Parts.TryGetValue(position, out var part))
            {
                if (newComponent == null)
                {
                    return;
                }

                part = this.Parts[position] = new ImmersiveNetworkPart(position);
            }

            var oldComponent = getComponent(part);
            if (oldComponent != newComponent)
            {
                // Находим сети, к которым принадлежит эта часть
                var networks = Networks.Where(n => n.PartPositions.Contains(position)).ToList();

                foreach (var network in networks)
                {
                    var collection = getCollection(network);

                    if (oldComponent != null)
                    {
                        collection.Remove(oldComponent);
                    }

                    if (newComponent != null)
                    {
                        collection.Add(newComponent);
                    }
                }

                setComponent(part, newComponent);
            }
        }


        /// <summary>
        /// Cобирает информацию по цепи для иммерсивных проводов
        /// </summary>
        /// <param name="position">Позиция блока</param>
        /// <param name="nodeIndex">Индекс точки подключения</param>
        /// <returns>Информация о сети</returns>
        public ImmersiveNetworkInformation GetNetworkForImmersiveWire(BlockPos position, byte nodeIndex)
        {
            _result.Reset(); // сбрасываем значения

            if (this.Parts.TryGetValue(position, out var part))
            {
                // Ищем сеть для этой части
                var immersiveNetwork = Networks.FirstOrDefault(n => n.PartPositions.Contains(position));
                if (immersiveNetwork != null)
                {
                    _localNetwork = immersiveNetwork;

                    // Находим соединение для этой точки подключения
                    var connection = part.Connections.FirstOrDefault(c => c.LocalNodeIndex == nodeIndex);
                    if (connection != null)
                    {
                        _result.eParamsInNetwork = connection.Parameters;
                        _result.current = connection.Parameters.current;
                    }
                    else
                    {
                        _result.eParamsInNetwork = part.MainEparams;
                        _result.current = part.MainEparams.current;
                    }

                    // Заполняем информацию о сети
                    _result.NumberOfBlocks = _localNetwork.PartPositions.Count;
                    _result.NumberOfConsumers = _localNetwork.Consumers.Count;
                    _result.NumberOfProducers = _localNetwork.Producers.Count;
                    _result.NumberOfAccumulators = _localNetwork.Accumulators.Count;
                    _result.NumberOfTransformators = _localNetwork.Transformators.Count;
                    _result.Production = _localNetwork.Production;
                    _result.Consumption = _localNetwork.Consumption;
                    _result.Capacity = _localNetwork.Capacity;
                    _result.MaxCapacity = _localNetwork.MaxCapacity;
                    _result.Request = _localNetwork.Request;
                }
            }

            return _result;
        }

        /// <summary>
        /// Вычисление экспоненциального скользящего среднего (EMA) на лету
        /// </summary>
        /// <param name="alpha"></param>
        /// <param name="currentValue"></param>
        /// <param name="previousSmoothedValue"></param>
        /// <returns></returns>
        public static float CalculateEma(float alpha, float currentValue, float previousSmoothedValue)
        {
            return alpha * currentValue + (1 - alpha) * previousSmoothedValue;
        }

    }


    /// <summary>
    /// Проводник тока
    /// </summary>
    internal class Conductor
    {
        public readonly IEImmersiveConductor ImmersiveConductor;
        public Conductor(IEImmersiveConductor immersiveConductor) => ImmersiveConductor = immersiveConductor;
    }

    /// <summary>
    /// Потребитель
    /// </summary>
    internal class Consumer
    {
        public readonly IEImmersiveConsumer ImmersiveConsumer;
        public Consumer(IEImmersiveConsumer immersiveConsumer) => ImmersiveConsumer = immersiveConsumer;
    }

    /// <summary>
    /// Трансформатор
    /// </summary>
    internal class Transformator
    {
        public readonly IEImmersiveTransformator ImmersiveTransformator;
        public Transformator(IEImmersiveTransformator immersiveTransformator) => ImmersiveTransformator = immersiveTransformator;
    }


    /// <summary>
    /// Генератор
    /// </summary>
    internal class Producer
    {
        public readonly IEImmersiveProducer ImmersiveProducer;
        public Producer(IEImmersiveProducer immersiveProducer) => ImmersiveProducer = immersiveProducer;
    }


    /// <summary>
    /// Аккумулятор
    /// </summary>
    internal class Accumulator
    {
        public readonly IEImmersiveAccumulator ImmersiveAccum;
        public Accumulator(IEImmersiveAccumulator immersiveAccum) => ImmersiveAccum = immersiveAccum;
    }




}