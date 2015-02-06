using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ThingModel;
using WebSocketSharp;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace ThingModelServerEnterpriseEdition
{
    class RestAPI
    {
        protected Bazar Bazar;

        private static readonly Regex _createRegex = new Regex(@"^(/[^?]*|)/(create|update)");
        private static readonly Regex _deleteRegex = new Regex(@"^(/[^?]*|)/delete");
        private static readonly Regex _infosRegex = new Regex(@"^(/[^?]*|)/infos");
        private static readonly Regex _clearRegex = new Regex(@"^(/[^?]*|)/clear");
        private static readonly Regex _loadRegex = new Regex(@"^(/[^?]*|)/load/([^?]+)");
        private static readonly Regex _timelineRegex = new Regex(@"^(/[^?]*|)/timeline");
        private static readonly Regex _channelsRegex = new Regex(@"^[^?]*/channels");
        private static readonly Regex _dataRegex = new Regex(@"^(/[^?]*)");
		private static readonly Regex _reloadRegex = new Regex(@"^(/[^?]*|)/securityreload");

        private static readonly Regex _badNamesRegex = new Regex(@"(create|update|delete|infos|timeline|channels|clear|load|securityreload)$");

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

            try
            {
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
                else if ((match = _timelineRegex.Match(rawUrl)).Success)
                {
                    var name = match.Groups[1].Value;
                    response = Timeline(req, res, String.IsNullOrEmpty(name) ? "/" : name);
                }
                else if ((match = _clearRegex.Match(rawUrl)).Success)
                {
                    var name = match.Groups[1].Value;
                    response = Clear(req, res, name);
                }
                else if ((match = _loadRegex.Match(rawUrl)).Success)
                {
                    var name = match.Groups[1].Value;
                    var timestamp = match.Groups[2].Value;
                    response = Load(req, res, name, timestamp);
                }
                else if (_channelsRegex.Match(rawUrl).Success)
                {
                    response = Channels();
                }
				else if (_reloadRegex.Match(rawUrl).Success)
				{
					response = SecurityReload();
				}
                else if ((match = _dataRegex.Match(rawUrl)).Success)
                {
                    var name = match.Groups[1].Value;
                    response = Data(req, res, String.IsNullOrEmpty(name) ? "/" : name);
                }
                else
                {
                    response = NotFound(res, "404 not found");
                }
            }
            catch (Exception e)
            {
                Logger.Error("REST|"+e.Message);
                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                res.ContentType = "text/plain";
                response = "internal server error";
            }

            res.WriteContent(System.Text.Encoding.UTF8.GetBytes(response));
        }

        private string Delete(HttpListenerRequest req, HttpListenerResponse res, string name)
        {
            if (name == "/")
            {
                return BadRequest(res, "you cannot delete this channel");
            }

            var channel = Bazar.Get(name);

            if (channel == null)
            {
                return NotFound(res);
            }

            if (!Security.Instance.CanWrite(channel, req.QueryString.Get("key")))
            {
                return Unauthorized(res);
            }

            var r = Bazar.DeleteChannel(name);
            Bazar.Save();
            return JsonConvert.SerializeObject(r);
        }

        private string Clear(HttpListenerRequest req, HttpListenerResponse res, string name)
        {
            var channel = Bazar.Get(name);

            if (channel == null)
            {
                return NotFound(res);
            }

            if (!Security.Instance.CanWrite(channel, req.QueryString.Get("key")))
            {
                return Unauthorized(res);
            }

            channel.Clear();

            return "true";
        }

        private string Data(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            var channel = Bazar.Get(endpoint);

            if (channel == null)
            {
                return NotFound(res);
            }
            
            if (!Security.Instance.CanRead(channel, req.QueryString.Get("key")))
            {
                return Unauthorized(res);
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

                var warehouse = channel.TimeMachine.RetrieveWarehouse(parsedTimestamp);
                return JsonConvert.SerializeObject(WarehouseToJSON(warehouse), Formatting.Indented);
            }

            return JsonConvert.SerializeObject(WarehouseToJSON(channel.Warehouse), Formatting.Indented);
        }

        private string Create(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            if (_badNamesRegex.Match(endpoint).Success)
            {
                return BadRequest(res, "forbidden endpoint endpoint is invalid");
            }

			var channel = Bazar.Get(endpoint);

			if (channel != null && !Security.Instance.CanWrite(channel, req.QueryString.Get("key")))
			{
				return Unauthorized(res);
			}

            var name = req.QueryString.Get("name");
            var description = req.QueryString.Get("description");
            var c = Bazar.CreateChannel(endpoint, name, description);
            Bazar.Save();
            return JsonConvert.SerializeObject(c.JSON(), Formatting.Indented);
        }
        
        private string Timeline(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            var channel = Bazar.Get(endpoint);

            if (channel == null)
            {
                return NotFound(res);
            }

            if (!Security.Instance.CanRead(channel, req.QueryString.Get("key")))
            {
                return Unauthorized(res);
            }

            string precision;
            if ((precision = req.QueryString.Get("precision")) != null)
            {
                int parsedPrecision;
                if (!int.TryParse(precision, out parsedPrecision))
                {
                    return BadRequest(res, "unable to parse the precision");
                }

                return JsonConvert.SerializeObject(channel.TimeMachine.History(parsedPrecision), Formatting.None);
            }

            return JsonConvert.SerializeObject(channel.TimeMachine.History(), Formatting.None);
        }
        
        private string Load(HttpListenerRequest req, HttpListenerResponse res, string endpoint, string timestamp)
        {
            var channel = Bazar.Get(endpoint);

            if (channel == null)
            {
                return NotFound(res);
            }

            if (!Security.Instance.CanWrite(channel, req.QueryString.Get("key")))
            {
                return Unauthorized(res);
            }

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

            channel.Load(parsedTimestamp);

            return "true";
        }

        private string Infos(HttpListenerRequest req, HttpListenerResponse res, string endpoint)
        {
            var channel = Bazar.Get(endpoint);

            if (channel == null)
            {
                return NotFound(res);
            }
            
            if (!Security.Instance.CanRead(channel, req.QueryString.Get("key")))
            {
                return Unauthorized(res);
            }

            return JsonConvert.SerializeObject(channel.TimeMachine.Infos(), Formatting.None);
        }

        private string Channels()
        {
            return JsonConvert.SerializeObject(Bazar.JSON(), Formatting.Indented);
        }

		private string SecurityReload()
		{
			Security.Instance.Reload ();
			return "true";
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

        private string Unauthorized(HttpListenerResponse res, string message = "get out of my lawn")
        {
            Logger.Warn("REST|Aunauthorized|"+message);
            res.StatusCode = (int) HttpStatusCode.Unauthorized;
            res.ContentType = "text/plain";
            return message;
        }

        protected JObject WarehouseToJSON(Warehouse warehouse)
        {
            var things = new JObject();

            if (warehouse != null) foreach (var thing in warehouse.Things)
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

            if (warehouse != null) foreach (var thingType in warehouse.ThingTypes)
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
