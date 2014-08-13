using System;
using System.Configuration;
using System.Threading;
using AIMLbot;
using ThingModel;
using ThingModel.Builders;
using ThingModel.WebSockets;

namespace SuperRogerBot3000
{
	class Program
	{
		static void Main(/*string[] args*/)
		{
			// Load settings
			var settings = ConfigurationManager.AppSettings;

			ThingType messageType = BuildANewThingType.Named("messenger:message")
				.ContainingA.String("author")
				.AndA.String("content")
				.AndA.DateTime("datetime")
				.AndA.NotRequired.Int("number");

			var myBot = new Bot();
			myBot.loadSettings();
			myBot.isAcceptingUserInput = false;
			myBot.loadAIMLFromFiles();
			myBot.isAcceptingUserInput = true;

			//var users = new Dictionary<string, User>();

			var warehouse = new Warehouse();
			var client = new Client(settings["senderId"], settings["connection"], warehouse);

			var maxNumber = 0;

			warehouse.Events.OnReceivedNew += delegate(object sender, WarehouseEvents.ThingEventArgs eventArgs)
			{
				var thing = eventArgs.Thing;
				if (thing.Type != null && thing.Type.Name.StartsWith("messenger:message"))
				{
					var date = thing.DateTime("datetime");
					maxNumber = Math.Max(maxNumber, thing.Int("number"));

					var diff = Math.Abs((DateTime.UtcNow - date).TotalSeconds);
					if (diff > 20)
					{
						Console.WriteLine("Old message ignored: "+diff);
						return;
					}

					var content = thing.String("content");

					var result = myBot.Chat(content, thing.String("author"));

					Thread.Sleep(content.Length * 20 + result.Output.Length * 30 + 800);

					Thing answer = BuildANewThing.As(messageType).IdentifiedBy(Guid.NewGuid().ToString())
						.ContainingA.String("author", "Roger")
						.AndA.String("content", result.Output)
						.AndA.DateTime("datetime", DateTime.UtcNow)
						.AndA.Int("number", ++maxNumber);

					warehouse.RegisterThing(answer);
		
					client.Send();
				}
			};

			Thread.Sleep(Timeout.Infinite);

			/*var text = "Hi";

			Console.WriteLine("Alain: Hi");
			var mod = 1;
 
			var timer = new Timer();
			timer.Interval = 3500;
			timer.Elapsed += (sender, eventArgs) =>
			{

				Request r;
				if (mod == 1)
				{
					mod = 0;
					r = new Request(text, myUser, myBot);
					Console.Write("Roger: ");
				}
				else
				{
					mod = 1;
					r = new Request(text, myUser2, myBot);
					Console.Write("Alain: ");
				}
				Result res = myBot.Chat(r);
				text = res.Output;
				Console.WriteLine(text);


			};

			timer.Enabled = true;
			timer.Start();

			Thread.Sleep(Timeout.Infinite);
			while (true)
			{
				Console.Write("You: ");
				string input = Console.ReadLine();
				if (input.ToLower() == "quit")
				{
					break;
				}
				else
				{
					Request r = new Request(input, myUser, myBot);
					Result res = myBot.Chat(r);
					Console.WriteLine("Bot: " + res.Output);
				}
			}*/

		}
	}
}
