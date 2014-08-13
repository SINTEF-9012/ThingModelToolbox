using System;
using ThingModel.WebSockets;
using System.Threading;

namespace MonoThingModelBroadcastServer
{
	class MainClass
	{
		public static void Main (string[] args)
		{
			var endpoint = args.Length >= 1 ? args[0] : "ws://localhost:8083/";

			Console.WriteLine("Server listening to "+endpoint);

			var server = new Server (endpoint, "/thingmodel", false);
			server.Debug ();
			Thread.Sleep (Timeout.Infinite);
		}
	}
}
