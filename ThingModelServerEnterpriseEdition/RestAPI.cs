using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ThingModel;
using WebSocketSharp;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace TestMonoSqlite
{
    class RestAPI
    {
        protected Bazar Bazar;

        private readonly Regex _createRegex = new Regex(@"^(/?.*)/(create|update)");
        private readonly Regex _deleteRegex = new Regex(@"^(/?.*)/delete");
        private readonly Regex _infosRegex = new Regex(@"^(/?.*)/infos");
        private readonly Regex _historyRegex = new Regex(@"^(/?.*)/history");
        private readonly Regex _defaultRegex = new Regex(@"/(\?.*?|)$");

        private static readonly TimeSpan DateTimeEpoch = new TimeSpan(
			new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc).Ticks);

        public RestAPI(HttpServer server, Bazar bazar)
        {
            server.OnGet += OnGet;
            Bazar = bazar;
        }

        private void OnGet(object sender, HttpRequestEventArgs eventArgs)
        {
            var req = eventArgs.Request;
            var rawUrl = req.RawUrl;
            var res = eventArgs.Response;
            res.ContentType = "application/json;charset=utf-8";

            Logger.Info("REST|"+req.UserHostAddress+"|"+req.RawUrl);
            string response;

            Match match; 

            if ((match = _createRegex.Match(rawUrl)).Success)
            {
                var name = match.Groups[1].Value;
                response = Create(req, res, String.IsNullOrEmpty(name) ? "/" : name);
            }
            else if ((match = _infosRegex.Match(rawUrl)).Success)
            {
                var name = match.Groups[1].Value;
                response = Infos(req, res, String.IsNullOrEmpty(name) ? "/" : name);
            }
            else if ((match = _deleteRegex.Match(rawUrl)).Success)
            {
                var name = match.Groups[1].Value;
                response = Delete(req, res, String.IsNullOrEmpty(name) ? "/" : name);
            }
            else if ((match = _historyRegex.Match(rawUrl)).Success)
            {
                var name = match.Groups[1].Value;
                response = History(req, res, String.IsNullOrEmpty(name) ? "/" : name);
            }
            else if (rawUrl.EndsWith("/clear"))
            {
                var name = rawUrl.Substring(0, rawUrl.Length - 6 /*"/clear".Length*/);
                response = Clear(res, name);
            }
            else if (_defaultRegex.Match(rawUrl).Success)
            {
                response = Default();
            }
            else
            {
                response = NotFound(res, "404 not found");
            }

            res.WriteContent(System.Text.Encoding.UTF8.GetBytes(response));
        }

        private string Delete(HttpListenerRequest req, HttpListenerResponse res, string name)
        {
            if (name == "/")
            {
                return BadRequest(res, "you cannot delete this channel");
            }
            var r = Bazar.DeleteChannel(name);
            Bazar.Save();
            return JsonConvert.SerializeObject(r);
        }

        private string Clear(HttpListenerResponse res, string name)
        {
            var service = Bazar.Get(name);

            if (service == null)
            {
                return NotFound(res);
            }

            return "lol";
        }

        private string Infos(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            var service = Bazar.Get(endpoint);

            if (service == null)
            {
                return NotFound(res);
            }

            string timestamp;
            if ((timestamp = req.QueryString.Get("timestamp")) != null)
            {
                long parsedTimestamp;
                if (!long.TryParse(timestamp, out parsedTimestamp))
                {
                    DateTime parsedDateTime;
                    if (!DateTime.TryParse(timestamp, out parsedDateTime))
                    {
                        return BadRequest(res, "unable to parse the timestamp");
                    }

                    parsedTimestamp = parsedDateTime.Subtract(DateTimeEpoch).Ticks/10000;
                }

                var warehouse = service.TimeMachine.RetrieveWarehouse(parsedTimestamp);
                return JsonConvert.SerializeObject(WarehouseToJSON(warehouse), Formatting.Indented);
            }

            return JsonConvert.SerializeObject(WarehouseToJSON(service.Warehouse), Formatting.Indented);
        }

        private string Create(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            var name = req.QueryString.Get("name");
            var description = req.QueryString.Get("description");
            var c = Bazar.CreateChannel(endpoint, name, description);
            Bazar.Save();
            return JsonConvert.SerializeObject(c.JSON(), Formatting.Indented);
        }
        
        private string History(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            var service = Bazar.Get(endpoint);

            if (service == null)
            {
                return NotFound(res);
            }

            string precision;
            if ((precision = req.QueryString.Get("precision")) != null)
            {
                int parsedPrecision;
                if (!int.TryParse(precision, out parsedPrecision))
                {
                    return BadRequest(res, "unable to parse the precision");
                }

                return JsonConvert.SerializeObject(service.TimeMachine.History(parsedPrecision), Formatting.None);
            }

            return JsonConvert.SerializeObject(service.TimeMachine.History(), Formatting.None);
        }

        private string Default()
        {
            return JsonConvert.SerializeObject(Bazar.JSON(), Formatting.Indented);
        }

        private string NotFound(HttpListenerResponse res, string message = "channel not found")
        {
            Logger.Warn("REST|NotFound|"+message);
            res.StatusCode = (int)HttpStatusCode.NotFound;
            res.ContentType = "text/plain";
            return message;
        }
        
        private string BadRequest(HttpListenerResponse res, string message)
        {
            Logger.Warn("REST|BadRequest|"+message);
            res.StatusCode = (int)HttpStatusCode.BadRequest;
            res.ContentType = "text/plain";
            return message;
        }

        protected JObject WarehouseToJSON(Warehouse warehouse)
        {
            var things = new JObject();

            foreach (var thing in warehouse.Things)
            {
                var t = new JObject();

                if (thing.Type != null)
                {
                    t["type"] = thing.Type.Name;
                }

                if (thing.ConnectedThingsCount > 0)
                {
                    var c = new JArray();
                    foreach (var connection in thing.ConnectedThings)
                    {
                        c.Add(connection.ID); 
                    }
                    t["connections"] = c;
                }

                var properties = new JObject();
                foreach (var property in thing.GetProperties())
                {
                    var p = new JObject();
                    if (property is Property.Location)
                    {
                        var loc = new JObject();
                        var ploc = property as Property.Location;

                        var keyx = "x";
                        var keyy = "y";
                        var keyz = "z";

                        if (property is Property.Location.LatLng)
                        {
                            keyx = "lat";
                            keyy = "lng";
                            keyz = "alt";
                        } else if (property is Property.Location.Equatorial)
                        {
                            keyx = "rightAscension";
                            keyy = "declination";
                            keyz = "hourAngle";
                        }

                        loc[keyx] = ploc.Value.X;
                        loc[keyy] = ploc.Value.Y;

                        if (ploc.Value.Z != null)
                        {
                            loc[keyz] = ploc.Value.Z;
                        }

                        p["value"] = loc;
                    }
                    else if (property is Property.Double)
                    {
                        p["value"] = (property as Property.Double).Value;
                    }
                    else if (property is Property.Int)
                    {
                        p["value"] = (property as Property.Int).Value;
                    }
                    else if (property is Property.Boolean)
                    {
                        p["value"] = (property as Property.Boolean).Value;
                    }
                    else if (property is Property.DateTime)
                    {
                        p["value"] = (property as Property.DateTime).Value;
                    }
                    else if (property is Property.String)
                    {
                        p["value"] = (property as Property.String).Value;
                    }
                    else
                    {
                        p["value"] = property.ValueToString();
                    }
                    
                    p["type"] = property.GetType().Name;
                    properties[property.Key] = p;
                }

                t["properties"] = properties;

                things[thing.ID] = t;
            }

            var types = new JObject();

            foreach (var thingType in warehouse.ThingTypes)
            {
                var t = new JObject();

                if (!String.IsNullOrEmpty(thingType.Description))
                {
                    t["description"] = thingType.Description;
                }

                var properties = new JObject();
                foreach (var propertyType in thingType.GetProperties())
                {
                    var p = new JObject();

                    if (!String.IsNullOrEmpty(propertyType.Name))
                    {
                        p["name"] = propertyType.Name;
                    }

                    if (!String.IsNullOrEmpty(propertyType.Description))
                    {
                        p["description"] = propertyType.Description;
                    }

                    p["required"] = propertyType.Required;

                    p["type"] = propertyType.Type.Name;

                    properties[propertyType.Key] = p;
                }

                t["properties"] = properties;

                types[thingType.Name] = t;
            }

            var result = new JObject();
            result["things"] = things;
            result["thingtypes"] = types;

            return result;
        }
    }
}
