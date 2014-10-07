using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using Arma2Net.AddInProxy;
using ThingModel;
using ThingModel.Builders;
using ThingModel.WebSockets;

namespace Arma2ThingModel
{
	[AddIn("Arma2ThingModel")]
    public class Arma2ThingModel : AddIn
	{
        private ArmaSession _session;
	    
		public Arma2ThingModel()
		{
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
			if (args.StartsWith("INIT|"))
			{
			    if (_session != null)
			    {
                    _session.Close();
			    }

				var settings = args.Split('|');

				if (settings.Length >= 3)
				{
				    _session = new ArmaSession(settings[1], settings[2]);

					return "connected";
				}

				return "hint \"Wrong connexion format\";";
			}

		    if (_session == null)
		    {
		        return "hint \"Missing INIT instruction\"";
		    }

		    if (args.StartsWith("PING"))
		    {
		        return _session.Ping();
		    }

			var data = args.Split('|');
			IList<object> units_data;
			IList<object> markers_data;

			if (data.Length < 2 ||
                !Format.TrySqfAsCollection(data[0], out units_data) ||
			    !Format.TrySqfAsCollection(data[1], out markers_data))
			{
				return "hint \"Wrong data format\";";
			}

            return _session.Execute(units_data, markers_data);
		}

		public override void Unload()
		{
			if (_session != null)
			{
				_session.Close();
			}
			base.Unload();
		}
	}
}
