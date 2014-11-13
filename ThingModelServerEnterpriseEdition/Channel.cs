using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ThingModel;
using ThingModel.Proto;
using Thing = ThingModel.Thing;

namespace ThingModelServerEnterpriseEdition
{
    class Channel
    {
        public readonly string Endpoint;

        public string Name;
        public string Description;
        public DateTime CreationDate;

        public bool Deleted = false;

        public readonly Warehouse Warehouse;
        public readonly TimeMachine TimeMachine;
        public readonly ProtoModelObserver Observer;

        protected readonly ISet<BroadcastService> Services;
        
        public Channel(string endpoint, string name, string description)
        {
            Endpoint = endpoint;
            Name = name;
            Description = description;

            Warehouse = new Warehouse();

            TimeMachine = new TimeMachine(Warehouse, endpoint);

            Services = new HashSet<BroadcastService>();

            Observer = new ProtoModelObserver();
            Warehouse.RegisterObserver(Observer);

            TimeMachine.StartRecording();
        }

        public void RegisterService(BroadcastService service)
        {
            Services.Add(service);
        }

        public void UnregisterService(BroadcastService service)
        {
            Services.Remove(service);
        }

        public void Clear()
        {
            Warehouse.RemoveCollection(new HashSet<Thing>(Warehouse.Things));
            SynchronizeServices(Configuration.ClearServiceSenderName);
        }

        public void Load(long timestamp)
        {
            var oldSituation = TimeMachine.RetrieveWarehouse(timestamp);
            TimeMachine.SynchronizeWarehouse(oldSituation, Warehouse);
            SynchronizeServices(Configuration.TimeMachineSenderName);
        }

        protected void SynchronizeServices(string senderID)
        {
            foreach (var broadcastService in Services)
            {
                broadcastService.Synchronize(senderID);
            }
        }

        public JObject JSON()
        {
            var c = new JObject();
            c["name"] = Name;
            c["description"] = Description;
            c["creationDate"] = CreationDate;
            return c;
        }
    }
}
