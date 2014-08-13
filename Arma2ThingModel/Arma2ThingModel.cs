using System;
using System.Collections.Generic;
using System.Globalization;
using Arma2Net.AddInProxy;
using ThingModel;
using ThingModel.Builders;
using ThingModel.WebSockets;

namespace Arma2ThingModel
{
	[AddIn("Arma2ThingModel")]
    public class Arma2ThingModel : AddIn
	{
		private readonly Wharehouse _wharehouse;
		private Client _client;
	    
		private readonly ThingType _typeUnit;
	    private readonly ThingType _typeOrder;

		private readonly double[] _centerLocation;
		private double _metersX;
		private double _metersY;
		private double _scale;

		// List of strings to send
        private readonly Queue<string> _action = new Queue<string>();

		public Arma2ThingModel()
		{
			_wharehouse = new Wharehouse();

			_typeUnit = BuildANewThingType.Named("person:arma3:player")
				.ContainingA.String("name")
				.AndA.Location("location")
				.AndA.Double("heading")
				.AndA.Double("dammage")
				.AndA.NotRequired.Double("fatigue")
				.AndA.NotRequired.Double("fuel");
			
			_wharehouse.RegisterType(_typeUnit);
			
			_typeOrder = BuildANewThingType.Named("order:arma3:raw")
				.ContainingA.String("title")
				.AndA.String("script")
				.AndA.DateTime("emission_date")
				.AndA.Boolean("done");

			_wharehouse.RegisterType(_typeOrder);

			_centerLocation = new double[2];
            
		}

		public override string Invoke(string args, int maxResultSize)
		{
			lock (this)
			{
				try
				{
					return Execute(args);
				}
				catch (Exception e)
				{
					return "hint \"" + (e.Message + " - "+e.StackTrace).Replace("\"", "\"\"") + "\";";
				}
			}
		}

		public string Execute(string args) {
			var latLngProjection = DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;
            var metersProjection = DotSpatial.Projections.KnownCoordinateSystems.Projected.World.WebMercator;

			if (args.StartsWith("INIT|"))
			{
				var settings = args.Split('|');

				if (settings.Length >= 8)
				{
					_client = new Client(settings[1], settings[2], _wharehouse);

					double.TryParse(settings[3], NumberStyles.Any, CultureInfo.InvariantCulture, out _centerLocation[0]);
					double.TryParse(settings[4], NumberStyles.Any, CultureInfo.InvariantCulture, out _centerLocation[1]);
					double.TryParse(settings[5], NumberStyles.Any, CultureInfo.InvariantCulture, out _metersX);
					double.TryParse(settings[6], NumberStyles.Any, CultureInfo.InvariantCulture, out _metersY);
					double.TryParse(settings[7], NumberStyles.Any, CultureInfo.InvariantCulture, out _scale);
                            
					DotSpatial.Projections.Reproject.ReprojectPoints(_centerLocation, null,
                                latLngProjection, metersProjection, 0, 1);
					return "connected";
				}

				return "hint \"Wrong connexion format\";";
			}

//			return "hint \""+args.Replace("\"", "\"\"") + "\";";
			IList<object> arguments;

			if (!Format.TrySqfAsCollection(args, out arguments))
			{
				return "hint \"Wrong data format\";";
			}
//			try
//			{
				foreach (IList<object> argument in arguments)
				{
					var x = (double) argument[1];
					var y = (double) argument[2];

					var plusX = (x - _metersX) / _scale;
					var plusY = (y - _metersY) / _scale;

					var p = new [] {plusY + _centerLocation[0], plusX + _centerLocation[1]};
							
					DotSpatial.Projections.Reproject.ReprojectPoints(p, null, metersProjection, latLngProjection, 0, 1);

					Thing unit = BuildANewThing.As(_typeUnit)
						.IdentifiedBy((string)argument[0])
						.ContainingA.Location("location", new Location.LatLng(p[0], p[1], (double) argument[3]));

					_wharehouse.RegisterThing(unit, true, true);
				}
//			}
//			catch (Exception e) { }

			_client.Send();
			
            var responseObject = _action.Count > 0 ? _action.Dequeue() : "nop";

            return responseObject;
		}

		public override void Unload()
		{
			if (_client != null)
			{
				_client.Close();
			}
			base.Unload();
		}
	}
}
