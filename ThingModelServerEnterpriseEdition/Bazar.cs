using System.Collections.Generic;
using WebSocketSharp.Server;

namespace TestMonoSqlite
{
    class Bazar
    {
        protected IDictionary<string, Channel> Channels;

        protected readonly bool StrictServer;

        protected readonly HttpServer Server;

        public Bazar(HttpServer server, bool strictServer = false)
        {
            Channels = new Dictionary<string, Channel>();

            Server = server;
            StrictServer = strictServer;
        }

        public IList<string> ChannelsList
        {
            get { return new List<string>(Channels.Keys); }
        }

        public Channel Get(string endpoint)
        {
            return Channels.ContainsKey(endpoint) ? Channels[endpoint] : null;
        }

        public bool CreateChannel(string endpoint)
        {
            if (Channels.ContainsKey(endpoint))
            {
                return false;
            }

            var uglyLock = new object();

            var channel = Channels[endpoint] = new Channel(endpoint);

            Server.AddWebSocketService(endpoint, () =>
                new BroadcastService(channel, uglyLock, StrictServer));

            return true;
        }
    }
}
