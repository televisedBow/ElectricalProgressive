using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Server;

    [assembly: ModDependency("game", "1.21.6")]
    [assembly: ModInfo(
        "Electrical Progressive: Transport",
        "electricalprogressivetransport",
        Website = "https://github.com/tehtelev/ElectricalProgressive",
        Description = "Additional electrical devices.",
        Version = "2.6.6",
        Authors =
        [
            "Tehtelev",
            "Kotl"
        ]
    )]


    namespace ElectricalProgressiveTransport
    {
        public class ElectricalProgressiveTransport : ModSystem
        {
            private PipeNetworkManager networkManager;
            private static ElectricalProgressiveTransport instance;

            public static ElectricalProgressiveTransport Instance => instance;

            public override void Start(ICoreAPI api)
            {
                base.Start(api);
                instance = this;

                // Регистрация блоков
                api.RegisterBlockClass("BlockPipeBase", typeof(BlockPipeBase));
                api.RegisterBlockClass("BlockPipe", typeof(BlockPipe));
                api.RegisterBlockClass("BlockInsertionPipe", typeof(BlockInsertionPipe));
                api.RegisterBlockClass("BlockLiquidInsertionPipe", typeof(BlockLiquidInsertionPipe)); // НОВОЕ

                // Регистрация блок-сущностей
                api.RegisterBlockEntityClass("BEPipe", typeof(BEPipe));
                api.RegisterBlockEntityClass("BEInsertionPipe", typeof(BEInsertionPipe));
                api.RegisterBlockEntityClass("BELiquidInsertionPipe", typeof(BELiquidInsertionPipe)); // НОВОЕ

                // Инициализация менеджера сетей
                networkManager = new PipeNetworkManager();
                networkManager.Initialize(api);
            }

            public override void StartServerSide(ICoreServerAPI api)
            {
                base.StartServerSide(api);

                api.RegisterCommand("pipenetwork", "Управление сетями труб", "",
                    (IServerPlayer player, int groupId, CmdArgs args) =>
                    {
                        var networkCount = networkManager.GetNetworkCount();
                        player.SendMessage(groupId, $"Сетей труб: {networkCount}", EnumChatType.Notification);
                    });
            }

            public PipeNetworkManager GetNetworkManager()
            {
                return networkManager;
            }
        }
    }