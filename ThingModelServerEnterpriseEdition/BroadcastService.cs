using System;
using System.Text.RegularExpressions;
using WebSocketSharp;
using WebSocketSharp.Server;
using ThingModel;
using ThingModel.Proto;
using Thing = ThingModel.Thing;

namespace ThingModelServerEnterpriseEdition
{
	internal class BroadcastService : WebSocketBehavior
    {
        public readonly Warehouse LiveWarehouse;
        private Warehouse CurrentWarehouse;

        private readonly TimeMachine TimeMachine;

        private readonly ToProtobuf _toProtobuf;
        private readonly FromProtobuf _fromProtobuf;
        private readonly ProtoModelObserver _protoModelObserver;
        private readonly ProtoModelObserver _currentProtoModelObserver;

        private readonly object _lock;

        private readonly bool _strictServer;

        private readonly Channel _channel;

        protected string LastSenderID = "unknown sender id";

        public bool IsLive = true;
        public bool CanReceive = true;
        public bool CanSend = true;

        private static readonly Regex _loadRegex = new Regex(@"load (.+)");
        private static readonly TimeSpan DateTimeEpoch = new TimeSpan(
			new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc).Ticks);
 
        public BroadcastService(Channel channel, object uglyLock, bool strictServer)
        {
            LiveWarehouse = channel.Warehouse;
            TimeMachine = channel.TimeMachine;

            _strictServer = strictServer;
            _channel = channel;

            _protoModelObserver = channel.Observer;

            _toProtobuf = new ToProtobuf();
            _fromProtobuf = new FromProtobuf(LiveWarehouse);
            _lock = uglyLock;

            CurrentWarehouse = null;
            _currentProtoModelObserver = new ProtoModelObserver();

            channel.RegisterService(this);
        }

        protected override void OnOpen()
        {
            if (!string.IsNullOrEmpty(Context.QueryString["readonly"]))
            {
				if (!Security.Instance.CanRead (_channel, Context.QueryString ["key"])) {
					Send ("authentication error: readonly access refused");
					Logger.Warn (_channel.Endpoint + " | new readonly connection refused");
					Sessions.CloseSession (ID);
					return;
				}
                CanSend = false;
                Logger.Info(_channel.Endpoint + " | new readonly connection");
            } else if (!string.IsNullOrEmpty(Context.QueryString["writeonly"]))
            {
				if (!Security.Instance.CanWrite (_channel, Context.QueryString ["key"])) {
					Send ("authentication error: writeonly access refused");
					Logger.Warn (_channel.Endpoint + " | new writeonly connection refused");
					Sessions.CloseSession (ID);
					return;
				}
                CanReceive = false;
                Logger.Info(_channel.Endpoint + " | new writeonly connection");
            }
            else
            {
				if (!Security.Instance.CanReadWrite (_channel, Context.QueryString ["key"])) {
					Send ("authentication error: access refused");
					Logger.Warn (_channel.Endpoint + " | new connection refused");
					Sessions.CloseSession (ID);
					return;
				}
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
                if (e.Type == Opcode.Binary)
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
				else if (e.Type == Opcode.Text)
                {
                    if ("live" == e.Data)
                    {
                        IsLive = true;

                        if (CurrentWarehouse != null && CanReceive)
                        {
                            _currentProtoModelObserver.Reset();
                            CurrentWarehouse.RegisterObserver(_currentProtoModelObserver);
                            TimeMachine.SynchronizeWarehouse(LiveWarehouse, CurrentWarehouse);
                            if (_currentProtoModelObserver.SomethingChanged())
                            {
                                var transaction = _currentProtoModelObserver.GetTransaction(_toProtobuf,
                                    Configuration.TimeMachineSenderName);
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
                    else
                    {
                        Match match;
                        if ((match = _loadRegex.Match(e.Data)).Success)
                        {
                            IsLive = false;
                            Logger.Info(_channel.Endpoint + " | " + LastSenderID + " | load");
                            var timestamp = match.Groups[1].Value;
                            long parsedTimestamp;
                            if (!long.TryParse(timestamp, out parsedTimestamp))
                            {
                                DateTime parsedDateTime;
                                if (!DateTime.TryParse(timestamp, out parsedDateTime))
                                {
                                    Logger.Info(_channel.Endpoint + " | " + LastSenderID +
                                                " | unable to parse the timestamp");
                                    Send("error: unable to parse the timestamp");
                                }

                                parsedTimestamp = parsedDateTime.Subtract(DateTimeEpoch).Ticks/10000;
                            }


                            if (oldSituation == null)
                            {
                                Send("error: no result found");
                                return;
                            }

                            if (CurrentWarehouse == null)
                            {
                                CurrentWarehouse = new Warehouse();
                                CurrentWarehouse.RegisterCollection(LiveWarehouse.Things);
                            }

                            _currentProtoModelObserver.Reset();
                            CurrentWarehouse.RegisterObserver(_currentProtoModelObserver);
                            TimeMachine.SynchronizeWarehouse(oldSituation, CurrentWarehouse);
                            if (_currentProtoModelObserver.SomethingChanged())
                            {
                                var transaction = _currentProtoModelObserver.GetTransaction(_toProtobuf, Configuration.TimeMachineSenderName);
                                var protoData = _toProtobuf.Convert(transaction);
                                Send(protoData);
                            }

                        }
                        else
                        {
                            Send("error: instruction unknown");
                            Logger.Info(_channel.Endpoint + " | " + LastSenderID + " | instruction unknown");
                        }
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
