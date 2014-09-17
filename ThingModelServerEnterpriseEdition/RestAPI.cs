using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace TestMonoSqlite
{
    class RestAPI
    {
        protected Bazar Bazar;

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
            res.ContentType = "application/json";

            string response;

            if (rawUrl.Contains("/get/"))
            {
                var name = rawUrl.Substring(rawUrl.IndexOf("/get/", System.StringComparison.Ordinal) + 5);
                response = Get(res, name);
            }
            else if (rawUrl.Contains("/create/"))
            {
                var name = rawUrl.Substring(rawUrl.IndexOf("/create/", System.StringComparison.Ordinal) + 8);
                response = Create(res, name);
            }
            else if (rawUrl.EndsWith("/history"))
            {
                var name = rawUrl.Substring(0, rawUrl.Length - 8 /*"/history".Length*/);
                response = History(res, name);
            }
            else
            {
                response = Default();
            }

            res.WriteContent(System.Text.Encoding.UTF8.GetBytes(response));
        }

        private string Get(HttpListenerResponse res, string name)
        {
            var service = Bazar.Get(name);

            if (service == null)
            {
                return NotFound(res);
            }

            return name;
        }

        private string Create(HttpListenerResponse res, string name)
        {
            return JsonConvert.SerializeObject(Bazar.CreateChannel("/" + name));
        }
        
        private string History(HttpListenerResponse res, string name)
        {
            return JsonConvert.SerializeObject(Bazar.CreateChannel("/" + name));
        }

        private string Default()
        {
            return JsonConvert.SerializeObject(Bazar.ChannelsList);
        }

        private string NotFound(HttpListenerResponse res)
        {
            res.StatusCode = (int)HttpStatusCode.NotFound;
            res.ContentType = "text/plain";
            return "channel not found";
        }
    }
}
