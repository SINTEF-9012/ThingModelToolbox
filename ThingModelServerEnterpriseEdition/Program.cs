using System;
using System.Data;
using System.Data.SQLite;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace TestMonoSqlite
{
	class MainClass
	{
		public static void Main (string[] args)
		{
		    Logger.Info("Starting up");

		    /*var timeMachine = new TimeMachine("DevelopmentTimeMachine", "wss://master-bridge.eu/thingmodel",
		        "URI=file:data.db"); */
            
		    var server = new HttpServer(8083);
		    var bazar = new Bazar(server);
		    var api = new RestAPI(server, bazar);

            bazar.CreateChannel("/", "Default channel", "");
            bazar.CreateChannel("/thingmodel", "ThingModel development channel", "With a lot of data");

            bazar.Save();

            server.Start();

            Thread.Sleep(2000);

            //timeMachine.StartRecording();

            Thread.Sleep(Timeout.Infinite);

		}

	}
}
