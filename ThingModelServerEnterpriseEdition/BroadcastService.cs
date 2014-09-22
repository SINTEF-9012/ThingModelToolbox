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
        private Warehouse CurrentWarehouse;

        private readonly ToProtobuf _toProtobuf;
        private readonly FromProtobuf _fromProtobuf;
        private readonly ProtoModelObserver _protoModelObserver;
        private readonly object _lock;

        private readonly bool _strictServer;

        private readonly Channel _channel;

        protected string LastSenderID = "unknown sender id";

        public bool IsLive = true;
        public bool CanReceive = true;
        public bool CanSend = true;

        public BroadcastService(Channel channel, object uglyLock, bool strictServer)
        {
            LiveWarehouse = channel.Warehouse;
            CurrentWarehouse = null;

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
            if (!string.IsNullOrEmpty(Context.QueryString["readonly"]))
            {
                CanSend = false;
                Logger.Info(_channel.Endpoint + " | new readonly connection");
            } else if (!string.IsNullOrEmpty(Context.QueryString["writeonly"]))
            {
                CanReceive = false;
                Logger.Info(_channel.Endpoint + " | new writeonly connection");
            }
            else
            {
                Logger.Info(_channel.Endpoint + " | new connection");
            }

            lock (_lock)
            {
                Transaction transaction = _toProtobuf.Convert(CanReceive ? LiveWarehouse.Things: new Thing[0],
                    new Thing[0], LiveWarehouse.ThingTypes, Configuration.BroadcastSenderName);
                Send(transaction);
            }
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            lock (_lock)
            {
                if (e.Type == Opcode.BINARY)
                {
                    if (!CanSend)
                    {
                        Send("error: readonly connection");
                        Logger.Warn(_channel.Endpoint+" | received transaction on a readonly connection");
                        return;
                    }

                    if (!IsLive)
                    {
                        Send("error: past situation cannot be edited");
                        Logger.Warn(_channel.Endpoint+" | received transaction on a past situation connection");
                        return;
                    }

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
                                    var transaction = _protoModelObserver.GetTransaction(s._toProtobuf, senderID, false, !s.CanReceive || !s.IsLive);
                                    //s.Send(s._toProtobuf.Convert(transaction));
                                    s.Send(transaction);
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

                        if (CurrentWarehouse != null && CanReceive)
                        {
                            var observer = new ProtoModelObserver();
                            CurrentWarehouse.RegisterObserver(observer);
                            TimeMachine.SynchronizeWarehouse(LiveWarehouse, CurrentWarehouse);
                            if (observer.SomethingChanged())
                            {
                                var transaction = observer.GetTransaction(_toProtobuf, Configuration.TimeMachineSenderName);
                                Send(transaction);
                            }
                            CurrentWarehouse = null;
                        }

                        Send("live");
                        Logger.Info(_channel.Endpoint + " | " + LastSenderID + " | live");
                    }
                    else if ("pause" == e.Data)
                    {
                        IsLive = false;
                        Send("pause");

                        CurrentWarehouse = new Warehouse();
                        CurrentWarehouse.RegisterCollection(LiveWarehouse.Things);

                        Logger.Info(_channel.Endpoint + " | " + LastSenderID + " | pause");
                    }
                }
            }
        }

        private void Send(Transaction transaction)
        {
            if (!CanReceive || !IsLive)
            {
                if (transaction.things_remove_list.Count > 0 || transaction.things_publish_list.Count > 0)
                {
                    transaction.things_remove_list.Clear();
                    transaction.things_publish_list.Clear();
                    Logger.Warn(_channel.Endpoint+" | transaction containing publish or remove on a writeonly connection has been cleaned");
                }
            }
            lock (_lock)
            {
                var protoData = _toProtobuf.Convert(transaction);
                Send(protoData);
            }
        }

        public void Synchronize(string senderID)
        {
            var transaction = _protoModelObserver.GetTransaction(_toProtobuf, senderID, false, !CanReceive||!IsLive);
            Send(transaction);
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
