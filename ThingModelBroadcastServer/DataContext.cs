using System;
using System.Collections.Generic;
using System.Timers;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using ThingModel.WebSockets;

namespace ThingModelBroadcastServer
{
	public class DataContext
	{
		public System.Windows.Threading.Dispatcher Dispatcher;
		public PlotModel DataModel { get; private set; }

		private Server _server;
		private readonly Dictionary<string, Tuple<LineSeries, Queue<Tuple<DateTime, DataPoint>>>> _series;

		private readonly LineSeries _line;

		private double _max = 1000;

		private const int TimeArea = 30;
		private const int TimeLimit = 20;

		public bool Draw = true;

		private object lockDraw = new object();

		private static MarkerType[] Symbols =
		{
			MarkerType.Cross,
			MarkerType.Diamond,
			MarkerType.Circle,
			MarkerType.Plus,
			MarkerType.Square,
			MarkerType.Triangle,
			MarkerType.Star
		};

		private static OxyColor[] Colors =
		{
			OxyColor.FromRgb(26, 188, 156),
			OxyColor.FromRgb(41, 128, 185),
			OxyColor.FromRgb(142, 68, 173),
			OxyColor.FromRgb(243, 156, 18),
			OxyColor.FromRgb(192, 57, 43),
			OxyColor.FromRgb(189, 195, 199),
			OxyColor.FromRgb(39, 174, 96)
		};

		public DataContext(Server server)
		{
			_server = server;

			_series = new Dictionary<string, Tuple<LineSeries, Queue<Tuple<DateTime, DataPoint>>>>();

			DataModel = new PlotModel("ThingModel Broadcast Server")
			{
				TextColor = OxyColor.FromRgb(231, 76, 60),
				PlotAreaBorderColor = OxyColor.FromRgb(22, 160, 133),
				PlotType = PlotType.Polar,
				PlotAreaBorderThickness = 0,
				LegendPlacement = LegendPlacement.Outside
			};

			_line = new LineSeries
			{
				LineStyle = LineStyle.Solid,
				Color = OxyColors.White,
				StrokeThickness = 1.0
			};

			DataModel.Axes.Add(new AngleAxis
			{
				MajorGridlineThickness = 1,
				MinorGridlineThickness = 0.5,
				MajorGridlineColor = OxyColor.FromRgb(8, 54, 66),
				MinorGridlineColor = OxyColor.FromRgb(7, 54, 66),
				MajorStep = 5, //Math.PI/2,
				MinorStep = 2.5, //Math.PI/8,
				FractionUnit = Math.PI,
				Minimum = 0,
				Maximum = TimeArea, //Math.PI*2,
				MajorGridlineStyle = LineStyle.Solid,
				MinorGridlineStyle = LineStyle.Dash,
				TicklineColor = OxyColors.IndianRed,
				StartAngle = 3,
				EndAngle = 360 + 3
			});

			DataModel.Axes.Add(new MagnitudeAxis 
			{
				MajorGridlineThickness = 1,
				MinorGridlineThickness = 0.5,
				MajorGridlineStyle = LineStyle.Solid,
				MinorGridlineStyle = LineStyle.Dash,
//				MajorStep = 500,
//				MinorStep = 250,
				Minimum = 0,
//				Maximum = 100,
				MajorGridlineColor = OxyColor.FromRgb(8, 54, 66),
				MinorGridlineColor = OxyColor.FromRgb(7, 54, 66)
			});

			_line.Points.Add(new DataPoint(0, 0));
			_line.Points.Add(new DataPoint(_max, 0));

			DataModel.Series.Add(_line);
			DataModel.Update();
			DataModel.InvalidatePlot(true);
		}

		private double Angle(DateTime time)
		{
			return (time.Second+time.Millisecond/1000.0)%TimeArea;
		}

		private readonly Random _random = new Random();

		private Tuple<LineSeries, Queue<Tuple<DateTime, DataPoint>>> GetSerie(string senderID)
		{
			Tuple<LineSeries, Queue<Tuple<DateTime, DataPoint>>> value;

			if (!_series.TryGetValue(senderID, out value))
			{
				var isServer = senderID.StartsWith(Server.ServerSenderID);

				var serie = new LineSeries(senderID)
				{
					MarkerType = isServer ? MarkerType.Circle: Symbols[_series.Count%Symbols.Length],
					MarkerStrokeThickness = 1.5,
					LineStyle = LineStyle.None,
					Color = OxyColors.Transparent,
					MarkerSize = isServer ? 1 : 4.0,
					MarkerStroke = _series.Count >= Colors.Length ?
						OxyColor.FromHsv(Math.Round(_random.NextDouble(), 1), 1.0, 0.5)
						: Colors[_series.Count]
				};

				value = new Tuple<LineSeries, Queue<Tuple<DateTime, DataPoint>>>(serie, new Queue<Tuple<DateTime, DataPoint>>());

				_series.Add(senderID, value);

				DataModel.Series.Add(serie);
			}

			return value;
		}

		public void Start() {
			var uaParser = UAParser.Parser.GetDefault();
			_server.Transaction += delegate(object sender, Server.TransactionEventArgs args)
			{
				lock (lockDraw)
				{
					var date = DateTime.Now;
					var point = new DataPoint(args.Message.Length, Angle(date));
					_max = Math.Max(_max, args.Message.Length);

					var senderId = args.SenderID;
					if (senderId.Contains("Mozilla") && senderId.Contains("-")) // All browsers contains mozilla
					{
						var iStart = senderId.IndexOf('-');
						var userAgent = senderId.Substring(iStart);
						var infos = uaParser.Parse(userAgent);

						if (!infos.Device.ToString().Contains("Other"))
						{
							senderId = senderId.Substring(0, iStart+1) + ' ' + infos.Device.Family + " " + infos.UserAgent.Family + " " +infos.OS.Family;
						}
						else
						{
							senderId = senderId.Substring(0, iStart+1)+ ' ' + infos.UserAgent.Family + " " + infos.OS.Family;
						}
					}

					if (args.EndPoint.StartsWith("127.0.0.1"))
					{
						senderId += " - " + args.EndPoint.Substring(10);
					}
					else
					{
						senderId += " - " + args.EndPoint;
					}

					var serie = GetSerie(senderId);
					serie.Item1.Points.Add(point);
					serie.Item2.Enqueue(new Tuple<DateTime, DataPoint>(date, point));
					DataModel.Update();
				}
			};

			var timer = new Timer();
			timer.Interval = 200;

			var cptNotDraw = 0;
			timer.Elapsed += (sender, args) =>
			{
				if (!Draw)
				{
					if (++cptNotDraw < 10)
					{
						return;
					}
					else
					{
						cptNotDraw = 0;
					}
				}

				lock (lockDraw)
				{
					foreach (var tuple in _series)
					{
						var points = tuple.Value.Item2;
						while (points.Count > 0)
						{
							var point = points.Peek();
							if ((args.SignalTime - point.Item1).TotalSeconds > TimeLimit)
							{
								tuple.Value.Item1.Points.Remove(point.Item2);
								points.Dequeue();
								continue;
							}
							break;
						}
					}

					var angle = Angle(args.SignalTime);
		
					_line.Points.RemoveAt(1);					
					_line.Points.Add(new DataPoint(_max, angle));

					try
					{
						Dispatcher.Invoke(() => DataModel.InvalidatePlot(true));
					}
					catch
					{
					}
				}
			};

			timer.Start();

		}

		public void Stop()
		{
			if (_server != null)
			{
				_server.Close();
			}
		}
	}
}
