using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using ProtoBuf;
using ThingModel;
using ThingModel.WebSockets;
using Timer = System.Timers.Timer;

namespace TestMonoSqlite
{
    class TimeMachine
    {
        protected readonly SQLiteConnection Sqlite;

        protected SQLiteCommand InsertTransactionCommand;
        protected SQLiteCommand InsertDeclarationCommand;
        protected SQLiteCommand FindCommand;
        protected SQLiteCommand InfosCommand;
        protected SQLiteCommand HistoryCommand;
        protected SQLiteCommand DeclarationsCommand;

        protected Warehouse Warehouse;
        protected Client Client;

        protected IDictionary<string, int> StringDeclarations = new Dictionary<string, int>();
		
        private static readonly TimeSpan DateTimeEpoch = new TimeSpan(
			new DateTime(1970,1,1,0,0,0, DateTimeKind.Utc).Ticks);

        private readonly object _lock = new object();

        protected TimeMachineWarehouseObserver Observer;

        public TimeMachine(Warehouse warehouse, string endpoint)
        {

            Warehouse = warehouse;

            var databasePath = "URI=file:timemachine"+
                Path.GetInvalidFileNameChars().Aggregate(endpoint, (current, c) => current.Replace(c, '-'))+".db";

            Sqlite = new SQLiteConnection(databasePath);
			Sqlite.Open();

            SetUp();
            LoadStringDeclarations();

        }

        protected class TimeMachineWarehouseObserver : IWarehouseObserver
        {
            public double DifferencesCounter;

            public void New(Thing thing, string sender)
            {
                DifferencesCounter += 40;
            }

            public void Deleted(Thing thing, string sender)
            {
                DifferencesCounter += 40;
            }

            public void Updated(Thing thing, string sender)
            {
                DifferencesCounter += 1;
            }

            public void Define(ThingType thingType, string sender)
            {
                DifferencesCounter += 60;
            }
        }

        protected void SetUp()
        {
			new SQLiteCommand("CREATE TABLE IF NOT EXISTS recorder (datetime INTEGER, scope BLOB, diff INTEGER)", Sqlite).ExecuteNonQuery ();
			new SQLiteCommand("CREATE TABLE IF NOT EXISTS declarations (key INTEGER PRIMARY KEY, value TEXT)", Sqlite).ExecuteNonQuery ();
			new SQLiteCommand("CREATE UNIQUE INDEX IF NOT EXISTS recorderindex ON recorder(datetime)", Sqlite).ExecuteNonQuery ();

			InsertTransactionCommand = new SQLiteCommand ("INSERT INTO recorder VALUES (@datetime, @scope, @diff)", Sqlite);
			InsertTransactionCommand.Parameters.Add (new SQLiteParameter ("@datetime", DbType.Int64));
			InsertTransactionCommand.Parameters.Add (new SQLiteParameter ("@scope", DbType.Binary));
			InsertTransactionCommand.Parameters.Add (new SQLiteParameter ("@diff", DbType.Int64));
			InsertTransactionCommand.Prepare ();

			InsertDeclarationCommand = new SQLiteCommand ("INSERT INTO declarations VALUES (@key, @value)", Sqlite);
			InsertDeclarationCommand.Parameters.Add (new SQLiteParameter ("@key", DbType.Int64));
			InsertDeclarationCommand.Parameters.Add (new SQLiteParameter ("@value", DbType.Object));
			InsertDeclarationCommand.Prepare ();

		    FindCommand = new SQLiteCommand("SELECT datetime, scope, diff FROM recorder" +
		            " WHERE ABS(@time-datetime) = (SELECT MIN(ABS(@time - datetime)) FROM recorder)", Sqlite);
            FindCommand.Parameters.Add(new SQLiteParameter("@time", DbType.Int64));
            FindCommand.Prepare();

		    InfosCommand = new SQLiteCommand("SELECT (SELECT COUNT(*) FROM recorder) AS count"
		                                    + ", (SELECT datetime FROM recorder"
		                                    + " WHERE datetime = (SELECT MIN(datetime) FROM recorder)) AS oldest"
		                                    + ", (SELECT datetime FROM recorder"
		                                    + " WHERE datetime = (SELECT MAX(datetime) FROM recorder)) AS newest", Sqlite);
            InfosCommand.Prepare();

		    HistoryCommand = new SQLiteCommand("SELECT datetime AS d, diff AS s" +
		                                      " FROM recorder ORDER BY datetime ASC", Sqlite);
            HistoryCommand.Prepare();

		    DeclarationsCommand = new SQLiteCommand("SELECT key, value FROM declarations", Sqlite);
            DeclarationsCommand.Prepare();

            Observer = new TimeMachineWarehouseObserver();
            Warehouse.RegisterObserver(Observer);

        }

        public void StartRecording()
        {
            var timer = new Timer {Interval = 2000, Enabled = true};
            timer.Elapsed += (sender, args) =>
            {
                lock (_lock)
                {
                    if (Observer.DifferencesCounter > 0.0)
                    {
                        Save();
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

                Console.WriteLine("Declaration Loaded");
            }
        }

        public void Save()
        {
            Console.WriteLine("Save start");
            // Create a ToProtobuf serializer
            var toProtobuf = new ThingModel.Proto.ToProtobuf();

            // Use the shared StringDeclaration dictionnary
            toProtobuf.StringDeclarations = StringDeclarations;

            // Serialize
            var transaction = toProtobuf.Convert(Warehouse.Things, new Thing[0], Warehouse.ThingTypes, Configuration.TimeMachineSenderName);

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
                Console.WriteLine("Conflict on string declarations : try again in 1 second");
                Thread.Sleep(1000);
                LoadStringDeclarations();
                Save();
                return;
            }

            // Remove the string declaration from the serialized transaction
            transaction.string_declarations.Clear();

            // Convert the transaction to binary
            var memoryInput = new MemoryStream();
            Serializer.Serialize(memoryInput, transaction);
            memoryInput.Position = 0;

            byte[] compressedData;

            // Compress the binary data
            using (var memoryOutput = new MemoryStream())
            {
                using (var zlibStream = new GZipStream(memoryOutput, CompressionMode.Compress))
                {
                       memoryInput.CopyTo(zlibStream);
                }

                compressedData = memoryOutput.ToArray();
            }


            var date = DateTime.UtcNow.Subtract(DateTimeEpoch).Ticks/10000;
            var diff = Math.Round(Observer.DifferencesCounter);
            Observer.DifferencesCounter = 0.0;

            // Store the transaction in the database
            InsertTransactionCommand.Parameters["@datetime"].Value = date;
            InsertTransactionCommand.Parameters["@scope"].Value = compressedData;
            InsertTransactionCommand.Parameters["@diff"].Value = diff;
            InsertTransactionCommand.ExecuteNonQuery();
            
            Console.WriteLine("Save end");
        }
    }
}
