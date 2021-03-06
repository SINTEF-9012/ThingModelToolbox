using System;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using ThingModel;
using ThingModel.Proto;
using ThingModel.WebSockets;
using Thing = ThingModel.Thing;
using ThingType = ThingModel.ThingType;
using Timer = System.Timers.Timer;
using ICSharpCode.SharpZipLib.GZip;

namespace ThingModelServerEnterpriseEdition
{
    class TimeMachine
    {
		protected readonly SqliteConnection Sqlite;

		protected SqliteCommand InsertTransactionCommand;
        protected SqliteCommand InsertDeclarationCommand;
        protected SqliteCommand FindCommand;
        protected SqliteCommand InfosCommand;
        protected SqliteCommand HistoryCommand;
        protected SqliteCommand DeclarationsCommand;

        protected Warehouse Warehouse;
        //protected Client Client; // TODO
        protected readonly string Name;

        protected IDictionary<string, int> StringDeclarations = new Dictionary<string, int>();
		
        private static readonly TimeSpan DateTimeEpoch = new TimeSpan(
			new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc).Ticks);

        private readonly object _lock = new object();

        protected TimeMachineWarehouseObserver Observer;

        public TimeMachine(Warehouse warehouse, string endpoint)
        {

            Warehouse = warehouse;
            Name = "TimeMachine"+endpoint;

            var databasePath = "URI=file:timemachine"+
                Path.GetInvalidFileNameChars().Aggregate(endpoint, (current, c) => current.Replace(c, '-'))+".db";

            Sqlite = new SqliteConnection(databasePath);
			Sqlite.Open();

            SetUp();
            LoadStringDeclarations();

        }

        protected class TimeMachineWarehouseObserver : IWarehouseObserver
        {
            public double DifferencesCounter;

            public ISet<string> SenderIDs = new HashSet<string>(); 

            public void New(Thing thing, string sender)
            {
                DifferencesCounter += 40;
                SenderIDs.Add(sender);
            }

            public void Deleted(Thing thing, string sender)
            {
                DifferencesCounter += 40;
                SenderIDs.Add(sender);
            }

            public void Updated(Thing thing, string sender)
            {
                DifferencesCounter += 1;
                SenderIDs.Add(sender);
            }

            public void Define(ThingType thingType, string sender)
            {
                DifferencesCounter += 60;
                SenderIDs.Add(sender);
            }

            public void Reset()
            {
                SenderIDs.Clear();
                DifferencesCounter = 0;
            }
        }

        protected void SetUp()
        {
			new SqliteCommand("CREATE TABLE IF NOT EXISTS recorder (datetime INTEGER, scope BLOB, diff INTEGER, senderID INTEGER)", Sqlite).ExecuteNonQuery ();
			new SqliteCommand("CREATE TABLE IF NOT EXISTS declarations (key INTEGER PRIMARY KEY, value TEXT)", Sqlite).ExecuteNonQuery ();
			new SqliteCommand("CREATE UNIQUE INDEX IF NOT EXISTS recorderindex ON recorder(datetime)", Sqlite).ExecuteNonQuery ();

			InsertTransactionCommand = new SqliteCommand ("INSERT INTO recorder VALUES (@datetime, @scope, @diff, @senderID)", Sqlite);
			InsertTransactionCommand.Parameters.Add (new SqliteParameter ("@datetime", DbType.Int64));
			InsertTransactionCommand.Parameters.Add (new SqliteParameter ("@scope", DbType.Binary));
			InsertTransactionCommand.Parameters.Add (new SqliteParameter ("@diff", DbType.Int64));
			InsertTransactionCommand.Parameters.Add (new SqliteParameter ("@senderID", DbType.Int64));
			InsertTransactionCommand.Prepare ();

			InsertDeclarationCommand = new SqliteCommand ("INSERT INTO declarations VALUES (@key, @value)", Sqlite);
			InsertDeclarationCommand.Parameters.Add (new SqliteParameter ("@key", DbType.Int64));
			InsertDeclarationCommand.Parameters.Add (new SqliteParameter ("@value", DbType.Object));
			InsertDeclarationCommand.Prepare ();

		    FindCommand = new SqliteCommand("SELECT datetime, scope, diff FROM recorder" +
		            " WHERE ABS(@time-datetime) = (SELECT MIN(ABS(@time - datetime)) FROM recorder)", Sqlite);
            FindCommand.Parameters.Add(new SqliteParameter("@time", DbType.Int64));
            FindCommand.Prepare();

		    InfosCommand = new SqliteCommand("SELECT (SELECT COUNT(*) FROM recorder) AS count"
		                                    + ", (SELECT datetime FROM recorder"
		                                    + " WHERE datetime = (SELECT MIN(datetime) FROM recorder)) AS oldest"
		                                    + ", (SELECT datetime FROM recorder"
		                                    + " WHERE datetime = (SELECT MAX(datetime) FROM recorder)) AS newest", Sqlite);
            InfosCommand.Prepare();

		    HistoryCommand = new SqliteCommand("SELECT datetime AS d, diff AS s, value AS id" +
		                                      " FROM recorder, declarations WHERE senderID == key ORDER BY datetime ASC", Sqlite);
            HistoryCommand.Prepare();

		    DeclarationsCommand = new SqliteCommand("SELECT key, value FROM declarations", Sqlite);
            DeclarationsCommand.Prepare();

            Observer = new TimeMachineWarehouseObserver();
            Warehouse.RegisterObserver(Observer);

        }

        public void StartRecording()
        {
            var timer = new Timer {Interval = Configuration.TimeMachineSaveFrequency, Enabled = true};
            timer.Elapsed += (sender, args) =>
            {
                lock (_lock)
                {
                    if (Observer.DifferencesCounter > 0.0)
                    {
						try {
							Save();
						} catch (Exception ex) {
							Logger.Error("TimeMachine|"+ex.GetType() + "|" + ex.Message);
						}
                    }
                }
            };
            timer.Start();
        }

        protected void LoadStringDeclarations()
        {
            lock (_lock)
            {
                var reader = DeclarationsCommand.ExecuteReader();
                while (reader.Read())
                {
                    var value = reader["value"].ToString();
                    var key = Convert.ToInt32(reader["key"]);

                    StringDeclarations[value] = key;
                }
                reader.Close();

                Logger.Info(Name+"|Declaration loaded");
            }
        }

        public void Save()
        {
            Logger.Debug(Name+"|Save start");
            // Create a ToProtobuf serializer
            var toProtobuf = new ToProtobuf();

            // Use the shared StringDeclaration dictionnary
            toProtobuf.StringDeclarations = StringDeclarations;

            string id = null;

            foreach (var senderID in Observer.SenderIDs)
            {
                var s = senderID ?? Configuration.TimeMachineSenderName;

                if (id == null)
                {
                    id = "" + s;
                }
                else
                {
                    id = "|" + s;
                }
            }

            // Serialize
            var transaction = toProtobuf.Convert(Warehouse.Things, new Thing[0], Warehouse.ThingTypes, id);

            // Save the new string declarations in the shared dictionnary
            try
            {
                foreach (var stringDeclaration in transaction.string_declarations)
                {
                    InsertDeclarationCommand.Parameters["@key"].Value = stringDeclaration.key;
                    InsertDeclarationCommand.Parameters["@value"].Value = stringDeclaration.value;
                    InsertDeclarationCommand.ExecuteNonQuery();
                }
            }
            catch (Exception)
            {
                Logger.Warn(Name+"|Conflict on string declarations : try again in 1 second");
                Thread.Sleep(1000);
                LoadStringDeclarations();
                Save();
                return;
            }

            // Remove the string declaration from the serialized transaction
            transaction.string_declarations.Clear();

            // Convert the transaction to binary
            var memoryInput = new MemoryStream();
			ProtoBuf.Serializer.Serialize(memoryInput, transaction);
            memoryInput.Position = 0;

            byte[] compressedData;

            // Compress the binary data
            using (var memoryOutput = new MemoryStream())
			{
				using (var zlibStream = new GZipOutputStream(memoryOutput))
				{
                       memoryInput.CopyTo(zlibStream);
                }

                compressedData = memoryOutput.ToArray();
            }

            var date = DateTime.UtcNow.Subtract(DateTimeEpoch).Ticks/10000;
            var diff = Math.Round(Observer.DifferencesCounter);

            Observer.Reset();
            // Store the transaction in the database
            InsertTransactionCommand.Parameters["@datetime"].Value = date;
            InsertTransactionCommand.Parameters["@scope"].Value = compressedData;
            InsertTransactionCommand.Parameters["@diff"].Value = diff;
            InsertTransactionCommand.Parameters["@senderID"].Value = StringDeclarations[id];
            InsertTransactionCommand.ExecuteNonQuery();
            
            Logger.Debug(Name+"|Save end");
        }

        public Warehouse RetrieveWarehouse(long timestamp)
        {
            FindCommand.Parameters["@time"].Value = timestamp;
            var result = FindCommand.ExecuteReader();

            if (!result.Read())
            {
                result.Close();
                return null;
            }

            var scope = result["scope"] as byte[];

            result.Close();

            if (scope == null)
            {
                return null;
            }

            // Decompress the binary data
            byte[] data;
            using (var inputStream = new MemoryStream(scope))
            using (var zlibStream = new GZipInputStream(inputStream))
            using (var outputStream = new MemoryStream())
            {
                zlibStream.CopyTo(outputStream);
                data = outputStream.ToArray();
            }

            var warehouse = new Warehouse();
            var fromProtobuf = new FromProtobuf(warehouse);

			lock (_lock) {
				foreach (var stringDeclaration in StringDeclarations) {
					fromProtobuf.StringDeclarations [stringDeclaration.Value] = stringDeclaration.Key;
				}
			}

            fromProtobuf.Convert(data);

            return warehouse;
        }

        public static void SynchronizeWarehouse(Warehouse input, Warehouse output)
        {
            if (input == null || output == null)
            {
                return;
            }

            // Remove the things
            foreach (var thing in output.Things)
            {
                if (input.GetThing(thing.ID) == null)
                {
                    output.RemoveThing(thing);
                }
            }

            // Register the things
            foreach (var thing in input.Things)
            {
                output.RegisterThing(thing, false);
            }
        }

        public JArray History(int parsedPrecision = 1)
        {
            var result = new JArray();
            long oldTime = 0;
            long currentDiff = 0;

            var reader = HistoryCommand.ExecuteReader();
            while (reader.Read())
            {
                var s = Convert.ToInt64(reader["s"]);
                var d = Convert.ToInt64(reader["d"]);
                
                currentDiff += s;

                if (d - oldTime > parsedPrecision || !reader.HasRows)
                {
                    oldTime = d;
                    var r = new JObject();
                    r["d"] = d;
                    r["s"] = currentDiff;
                    r["id"] = reader["id"].ToString();
                    result.Add(r);
                    currentDiff = 0;
                }
            }
            reader.Close();

            return result;
        }

        public JObject Infos()
        {
            var result = new JObject();
            var reader = InfosCommand.ExecuteReader();
            if (!reader.Read())
            {
                reader.Close();
                return null;
            }

            var count = (long) reader["count"];

            if (count > 0)
            {
                var oldest = reader["oldest"];
                var newest = reader["newest"];
                result["oldest"] = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds((long)oldest);
                result["newest"] = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds((long)newest);
            }
            result["count"] = count;

            reader.Close();

            return result;
        }
    }
}
