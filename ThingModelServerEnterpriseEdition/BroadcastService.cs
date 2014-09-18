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
        public readonly Warehouse LiveWarehouse;

        private readonly ToProtobuf _toProtobuf;
        private readonly FromProtobuf _fromProtobuf;
        private readonly ProtoModelObserver _protoModelObserver;
        private readonly object _lock;

        private readonly bool _strictServer;

        private readonly Channel _channel;

        protected string LastSenderID = "unknown sender id";

        public bool IsLive; 

        public BroadcastService(Channel channel, object uglyLock, bool strictServer)
        {
            IsLive = true;

            LiveWarehouse = channel.Warehouse;
            _strictServer = strictServer;
            _channel = channel;

            _protoModelObserver = channel.Observer;

            _toProtobuf = new ToProtobuf();
            _fromProtobuf = new FromProtobuf(LiveWarehouse);
            _lock = uglyLock;

            channel.RegisterService(this);
        }

        protected override void OnOpen()
        {
            lock (_lock)
            {
                Transaction transaction = _toProtobuf.Convert(LiveWarehouse.Things,
                    new Thing[0], LiveWarehouse.ThingTypes, Configuration.BroadcastSenderName);
                Send(transaction);
                Logger.Info(_channel.Endpoint + " | new connection");
            }
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
                        Logger.Warn(ex.Message);
                        return;
                    }

                    if (senderID == "undefined")
                    {
                        Logger.Warn(_channel.Endpoint + " | undefined senderID detected. Ignores");
                        return;
                    }
                       
                    Logger.Info(_channel.Endpoint + " | " + senderID + " | transaction received | "+e.RawData.Length + " bytes");
                    Logger.Debug(Convert.ToBase64String(e.RawData));

                    LastSenderID = senderID;

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
                else if (e.Type == Opcode.TEXT)
                {
                    if ("live" == e.Data)
                    {
                        IsLive = true;
                        Send("live");
                        Logger.Info(_channel.Endpoint + " | " + LastSenderID + " | live");
                    }
                    else if ("pause" == e.Data)
                    {
                        IsLive = false;
                        Send("pause");
                        Logger.Info(_channel.Endpoint + " | " + LastSenderID + " | pause");
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

        protected override void OnClose(CloseEventArgs e)
        {
            _channel.UnregisterService(this);
            base.OnClose(e);
        }
        
        protected override void OnError(ErrorEventArgs e)
        {
            _channel.UnregisterService(this);
            base.OnError(e);
        }
    }
}
