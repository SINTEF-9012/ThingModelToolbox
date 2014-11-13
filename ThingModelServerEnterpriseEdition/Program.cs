using System.Threading;
using WebSocketSharp.Server;

namespace ThingModelServerEnterpriseEdition
{
	class MainClass
	{
		public static void Main (string[] args)
		{
		    Logger.Info("Starting up");
            
		    var server = new HttpServer(Configuration.HttpServerPort);
		    var bazar = new Bazar(server);
		    var api = new RestAPI(server, bazar);

		    bazar.CreateChannel("/", Configuration.DefaultChannelName, Configuration.DefaultChannelDescription);

            bazar.Save();

            server.Start();

		    (new ManualResetEvent(false)).WaitOne();
		}

	}
}
