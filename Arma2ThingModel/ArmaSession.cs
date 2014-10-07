using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using ThingModel;
using ThingModel.Builders;
using ThingModel.WebSockets;

namespace Arma2ThingModel
{
    class ArmaSession
    {
        private readonly Warehouse _warehouse;
        private readonly Client _client;

		// List of strings to send
        private readonly Queue<string> _action = new Queue<string>();

        private bool _cameraRegistered;

        private readonly object _lock = new object();

        private bool _hasReceivedSomething;
        private bool _firstExcecute = true;
        private int _nbExecute = 0;

        public ArmaSession(string senderID, string endpoint)
        {
            _warehouse = new Warehouse();
            _client = new Client(senderID, endpoint, _warehouse);

            _warehouse.Events.OnReceivedUpdate += OnCameraUpdate;
            //_warehouse.Events.OnNew += OnCameraUpdate;

            _warehouse.Events.OnReceivedNew += OnReceivedNew;
            _hasReceivedSomething = false;
        }

        private void OnReceivedNew(object sender, WarehouseEvents.ThingEventArgs thingEventArgs)
        {
            _hasReceivedSomething = true;
            _warehouse.Events.OnReceivedNew -= OnReceivedNew;
        }

        private void OnCameraUpdate(object sender, WarehouseEvents.ThingEventArgs args)
        {
            lock (_lock)
            {
                if (args.Thing.Type != null && Model.ArmaCamera.Name == args.Thing.Type.Name)
                {
                    _action.Enqueue("hint \"camera update\"");
                    var computerName = Environment.MachineName;
                    var cameraComputerName = args.Thing.String("computerName");
                    if (cameraComputerName == computerName)
                    {
                        var target = args.Thing.String("target");
                        _action.Enqueue("vehicle "+ target + " switchCamera \"" + args.Thing.String("view") + "\";" +
                                        target + " switchCamera \"" + args.Thing.String("view") + "\"");
                    }
                }
                else
                {
                    _action.Enqueue("hint \"pas camera "+args.Thing.ID+" - "+(args.Thing.Type == null ? "null" : args.Thing.Type.Name) + "\"");
                }
            }
        }


        private void RegisterCamera()
        {
            var computerName = Environment.MachineName;

            var camera = BuildANewThing.As(Model.ArmaCamera)
                .IdentifiedBy(computerName + "-ArmaCamera")
                .ContainingA.String("view", "default")
                .ContainingA.String("target", "player")
                .AndA.String("computerName", computerName);

            _warehouse.RegisterThing(camera);
            _cameraRegistered = true;
        }

        private static readonly Regex _testUAV = new Regex(@"(copter|uav|plane)", RegexOptions.IgnoreCase);
        private static readonly Regex _testUGV = new Regex(@"(ground|ugv)", RegexOptions.IgnoreCase);

        public string Execute(IList<object> units_data, IList<object> markers_data)
        {
            lock (_lock)
            {
                if (!_client.IsConnected())
                {
                    _client.Connect();
                    return "notconnected";
                }

                if (!_hasReceivedSomething && ++_nbExecute > 3)
                {
                    return "hint \"not connected 2\"";
                }
                
                if (_firstExcecute)
                {
                    _firstExcecute = false;

                    foreach (var thing in _warehouse.Things)
                    {
                        if (thing.Type != null) {
                            var name = thing.Type.Name.ToLowerInvariant();
                            if (name.StartsWith("arma") || name.EndsWith("armacamera"))
                            {
                                _warehouse.RemoveThing(thing);
                            }
                        }
                    }

                    _client.Send();
                }

                if (!_cameraRegistered)
                {
                    RegisterCamera();
                }

                if (units_data != null) foreach (IList<object> argument in units_data)
                {
                    if (argument == null)
                    {
                        continue;
                    }

                    if (argument.Count != 14)
                    {
                        throw new FormatException(
                            "This should be a list of [ID,lat,lng,alt,heading,dammage,fatigue,fuel,type,isPlayer,side,vehicle]");
                    }

                    var ID = (string) argument[0];

                    var x = (double) argument[1];
                    var y = (double) argument[2];
                    var alt = (double) argument[3];

                    var heading = (double) argument[4];
                    var dammage = (double) argument[5];
                    var fatigue = (double) argument[6];
                    var fuel = Math.Round((double) argument[7], 1);
                    var speed = Math.Round((double) argument[8], 1);
                    var type = (string) argument[9];
                    var isPlayer = (bool) argument[10];
                    var side = (string) argument[11];
                    var vehicle = (string) argument[12];
                    var detectedBy = (string) argument[13];


                    Thing unit;

                    if (isPlayer)
                    {
                        unit = BuildANewThing.As(Model.ArmaPlayer)
                            .IdentifiedBy(ID);

                        FillArmaUnit(unit, fatigue);
                    }
                    else if (vehicle != ID)
                    {
                        if (_testUAV.IsMatch(type))
                        {
                            unit = BuildANewThing.As(Model.ArmaUAV).IdentifiedBy(vehicle);
                        }
                        else if (_testUGV.IsMatch(type))
                        {
                            unit = BuildANewThing.As(Model.ArmaUGV).IdentifiedBy(vehicle);
                            unit.Boolean("waiting", Math.Abs(speed) < 0.01);
                        }
                        else
                        {
                            unit = BuildANewThing.As(Model.ArmaVehicle).IdentifiedBy(vehicle);
                        }

                        unit.Double("fuel", fuel);
                    }
                    else
                    {
                        unit = BuildANewThing.As(Model.ArmaDetectedUnit)
                            .IdentifiedBy(ID)
                            .ContainingA.String("side", side);

                        if (!string.IsNullOrEmpty(detectedBy))
                        {
                            var source = _warehouse.GetThing(detectedBy);
                            if (source != null)
                            {
                                unit.Connect(source);
                            }
                        }

                        FillArmaUnit(unit, fatigue);
                    }

                    FillArmaThing(unit,
                        Tools.ConvertArmaLocationToLatLng(y, x, alt),
                        //new Location.Point(x, y, alt),
                        heading, speed, dammage, type);

                    _warehouse.RegisterThing(unit);
                }

                if (markers_data != null) foreach (IList<object> argument in markers_data)
                {
                    if (argument == null)
                    {
                        continue;
                    }

                    if (argument.Count != 6)
                    {
                        throw new FormatException(
                            "This should be a list of [ID,lat,lng,alt,type,text]");
                    }

                    var ID = (string) argument[0];

                    var x = (double) argument[1];
                    var y = (double) argument[2];
                    var alt = (double) argument[3];
                    var type = (string) argument[4];
                    var text = (string) argument[5];

                    _warehouse.RegisterThing(BuildANewThing.As(Model.ArmaMarker).IdentifiedBy(ID)
                        .ContainingA.Location(Tools.ConvertArmaLocationToLatLng(y, x, alt))
                        .AndA.String("type", type)
                        .AndA.String("text", text));
                }

                _client.Send();

                var responseObject = _action.Count > 0 ? _action.Dequeue() : "nop";

                return responseObject;
            }
        }

        public string Ping()
        {
            lock (_lock)
            {
                return _action.Count > 0 ? _action.Dequeue() : "pong";
            }
        }

        public void FillArmaThing(Thing thing, Location.LatLng location,
            /*Location.Point gameLocation,*/ double heading, double speed, double dammage, string type) 
        {
            thing.LocationLatLng("location", location);
            //thing.LocationPoint("gameLocation", gameLocation);
            thing.Double("heading", heading);
            thing.Double("speed", speed);
            thing.Double("dammage", dammage);
            thing.String("type", type);
        }

        public void FillArmaUnit(Thing thing, double fatigue)
        {
            thing.Double("fatigue", fatigue);
        }

        public void CreatePlayer()
        {
            
        }

        public void Close()
        {
            if (_client.IsConnected())
            {
                _client.Close();
            }
        }
    }
}
