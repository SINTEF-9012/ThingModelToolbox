using System;
using System.Net;
using System.Text;
using System.Threading;
using ThingModel;
using ThingModel.Builders;
using ThingModel.WebSockets;
using Timer = System.Timers.Timer;

namespace LiveCamerasForOsloValEDation
{
    class Program
    {
        private static ThingType webcamType = BuildANewThingType.Named("webcamPictureType")
            .ContainingA.LocationLatLng()
            .AndA.String("name")
            .AndA.String("url");

        private string _url;

        private Thing _thing;

        private Random _random;
 
        public Program(string name, string url, Location.LatLng location)
        {
            _random = new Random();
            _url = url;
            _thing = BuildANewThing.As(webcamType)
                .IdentifiedBy(computeID(url + "-canard"))
                .ContainingA.Location(location)
                .AndA.String("name", name)
                .AndA.String("url", url);
        }

        public void CommitAndUpdate(Warehouse warehouse)
        {
            _thing.String("url", _url + "?r=" + _random.Next());
            warehouse.RegisterThing(_thing);

            using (var Client = new WebClient())
            {
                Client.DownloadStringAsync(new Uri(_thing.String("url")));
            }


        }

        public string computeID(string text)
        {
            using (var sha1 = new System.Security.Cryptography.SHA1Managed())
            {
                byte[] textData = Encoding.UTF8.GetBytes(text);

                byte[] hash = sha1.ComputeHash(textData);

                return BitConverter.ToString(hash);
            }
        }

        static void Main(string[] args)
        {
            var webcamA = new Program(
                "Sjursoya webcam",
                "http://www.oslohavn.no/filestore/webcam/sjursoya.jpg",
                new Location.LatLng(59.88883328504564, 10.755322873592378));

            var warehouse = new Warehouse();

            var client = new Client("LiveCamerasForOsloValEDation", "wss://master-bridge.eu/thingmodel/vbs?writeonly=1",
                warehouse);

            webcamA.CommitAndUpdate(warehouse);
            client.Send();

            var timer = new Timer();
            timer.Interval = 5*60*1000;
            timer.Elapsed += (sender, eventArgs) =>
            {
                webcamA.CommitAndUpdate(warehouse);
                client.Send();
            };
            timer.Enabled = true;
            timer.Start();

            (new ManualResetEvent(false)).WaitOne();

        }
    }
}
