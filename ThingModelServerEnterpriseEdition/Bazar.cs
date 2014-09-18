using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

            try
            {
                if (!File.Exists(Configuration.BazarPersistentFile)) return;

                var text = File.ReadAllText(Configuration.BazarPersistentFile);
                var json = JObject.Parse(text);

                foreach (var property in json.Properties())
                {
                    CreateChannel(property.Name, property.Value["name"].ToObject<string>(), property.Value["description"].ToObject<string>(),
                        property.Value["creationDate"].ToObject<DateTime>());
                }
            }
            catch (Exception e)
            {
                Logger.Error("Unable to load Bazar Persistent File | "+e.Message); 
                throw;
            }
        }

        public IList<string> ChannelsList
        {
            get { return new List<string>(Channels.Keys); }
        }

        public Channel Get(string endpoint)
        {
            return Channels.ContainsKey(endpoint) ? Channels[endpoint] : null;
        }

        public Channel CreateChannel(string endpoint, string name, string description, DateTime? creationDate = null)
        {
            if (Channels.ContainsKey(endpoint))
            {
                var c = Channels[endpoint];
                if (!String.IsNullOrEmpty(name))
                {
                    c.Name = name;
                }
                if (!String.IsNullOrEmpty(description))
                {
                    c.Description = description;
                }
                c.Deleted = false;
                return c;
            }

            var uglyLock = new object();

            var channel = Channels[endpoint] = new Channel(endpoint, name, description);

            channel.CreationDate = creationDate ?? DateTime.UtcNow;

            Server.AddWebSocketService(endpoint, () =>
                new BroadcastService(channel, uglyLock, StrictServer));

            return channel;
        }

        public bool DeleteChannel(string endpoint)
        {
            if (!Channels.ContainsKey(endpoint)) return false;
            Channels[endpoint].Deleted = true;
            return true;
        }

        public JObject JSON()
        {
            var json = new JObject();
            foreach (var channel in Channels)
            {
                if (channel.Value.Deleted) continue;
                json[channel.Key] = channel.Value.JSON();
            }

            return json;
        }

        public void Save()
        {
            var text = JsonConvert.SerializeObject(JSON(), Formatting.Indented);
            File.WriteAllText(Configuration.BazarPersistentFile, text);
        }
    }
}
