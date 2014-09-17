using ThingModel;
using ThingModel.Proto;

namespace TestMonoSqlite
{
    class Channel
    {
        public readonly string Endpoint;
        public readonly Warehouse Warehouse;
        public readonly TimeMachine TimeMachine;
        public readonly ProtoModelObserver Observer;

        public Channel(string endpoint)
        {
            Endpoint = endpoint;
            Warehouse = new Warehouse();

            TimeMachine = new TimeMachine(Warehouse, endpoint);

            Observer = new ProtoModelObserver();
            Warehouse.RegisterObserver(Observer);

            TimeMachine.StartRecording();
        }
    }
}
