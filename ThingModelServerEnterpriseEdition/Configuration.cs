namespace ThingModelServerEnterpriseEdition
{
    class Configuration
    {
        public static readonly int HttpServerPort = 8083;

        public static readonly string BroadcastSenderName = "Server";

        public static readonly string TimeMachineSenderName = "TimeMachine";

        public static readonly string ClearServiceSenderName = "ClearService";

        public static readonly int TimeMachineSaveFrequency = 2000;

        public static readonly string BazarPersistentFile = "bazar.json";

        public static readonly string DefaultChannelName = "Default channel";

        public static readonly string DefaultChannelDescription = "";
    }
}
