using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using ThingModel;
using ThingModel.Proto;
using Thing = ThingModel.Thing;

namespace TestMonoSqlite
{
    internal class BroadcastService : WebSocketService
    {
        public readonly Warehouse Warehouse;

        private readonly ToProtobuf _toProtobuf;
        private readonly FromProtobuf _fromProtobuf;
        private readonly ProtoModelObserver _protoModelObserver;
        private readonly object _lock;

        private readonly bool _strictServer;

        public BroadcastService(Channel channel, object uglyLock, bool strictServer)
        {
            Warehouse = channel.Warehouse;
            _strictServer = strictServer;

            _protoModelObserver = channel.Observer;

            _toProtobuf = new ToProtobuf();
            _fromProtobuf = new FromProtobuf(Warehouse);
            _lock = uglyLock;
        }

        protected override void OnOpen()
        {
            Transaction transaction;
            lock (_lock)
            {
                transaction = _toProtobuf.Convert(Warehouse.Things, new Thing[0], Warehouse.ThingTypes, Configuration.BroadcastSenderName);
            }
            Send(transaction);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            lock (_lock)
            {
                if (e.Type == Opcode.BINARY)
                {
                    _protoModelObserver.Reset();

                    string senderID;
                    try
                    {
                        senderID = _fromProtobuf.Convert(e.RawData, _strictServer);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        return;
                    }

                    if (senderID == "undefined")
                    {
                        Console.WriteLine("Undefined senderIDs are not allowed");
                        return;
                    }

                    _toProtobuf.ApplyThingsSuppressions(_protoModelObserver.Deletions);

                    // Broadcast to other clients
                    if (_protoModelObserver.SomethingChanged())
                    {
                        foreach (var session in Sessions.Sessions)
                        {
                            if (session != this)
                            {
                                var s = session as BroadcastService;
                                if (s != null)
                                {
                                    var transaction = _protoModelObserver.GetTransaction(s._toProtobuf, senderID);
                                    s.Send(s._toProtobuf.Convert(transaction));
                                }
                            }
                        }
                    }
                }
            }
        }

        private void Send(Transaction transaction)
        {
            lock (_lock)
            {
                var protoData = _toProtobuf.Convert(transaction);
                Send(protoData);
            }
        }
    }
}
