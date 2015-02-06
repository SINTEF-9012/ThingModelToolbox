using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ThingModelServerEnterpriseEdition
{
	class Security
	{
        public static readonly Security Instance = new Security();

		public enum AccessType {
			read, write, readwrite
		};

		protected IDictionary<string, IDictionary<string, AccessType>> Keys;

		public Security ()
		{
			Reload ();
		}

		public void Reload() {
			Keys = new Dictionary<string, IDictionary<string, AccessType>> ();

			try {
				if (!File.Exists(Configuration.SecurityFile)) return;

				var text = File.ReadAllText(Configuration.SecurityFile);
				var json = JObject.Parse(text);
				foreach (var channel in json.Properties())
                {
					var channelKeys = new Dictionary<string, AccessType>();
                    Keys.Add(channel.Name, channelKeys);
					foreach (var key in channel.Values())
					{
					    var jProperty = key as JProperty;
					    if (jProperty != null)
					    {
					        var sType = key.ToObject<string>().ToLower();
                            var type = AccessType.readwrite;
                            if (sType.Equals("readonly"))
                            {
                                type = AccessType.read;
                            }
                            else if (sType.Equals("writeonly"))
                            {
                                type = AccessType.write;
                            }
                            channelKeys.Add(jProperty.Name, type);
                        }
				    }
				}
			} catch (Exception e) {
				Logger.Error ("Unable to load security file | " + e.Message);
				throw;
			}
		}

	    public bool IsSecure(Channel channel)
	    {
	        return Keys.ContainsKey(channel.Endpoint);
	    }

	    public bool CanRead(Channel channel, string key)
	    {
	        return CanAccess(channel, key, AccessType.read);
	    }

	    public bool CanWrite(Channel channel, string key)
	    {
	        return CanAccess(channel, key, AccessType.write);
	    }

	    public bool CanReadWrite(Channel channel, string key)
	    {
	        return CanAccess(channel, key, AccessType.readwrite);
	    }

        protected bool CanAccess(Channel channel, string key, AccessType access) {
        	if (!IsSecure(channel))
	        {
	            return true;
	        }

            var channelkeys = Keys[channel.Endpoint];
			if (string.IsNullOrEmpty(key) || !channelkeys.ContainsKey(key))
            {
                return false;
            }

            var type = channelkeys[key];
            return type == access || type == AccessType.readwrite;
        }
	}
}

