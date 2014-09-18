using System;
using System.Globalization;

namespace TestMonoSqlite
{
    class Logger
    {
        private static void Print(string level, string message)
        {
            Console.Write(DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
            Console.Write("|");
            Console.Write(level);
            Console.Write("|");
            Console.WriteLine(message);
        }

        public static void Info(string message)
        {
            Print("INFO", message);
        }
        
        public static void Debug(string message)
        {
            Print("DEBUG", message);
        }
        public static void Error(string message)
        {
            Print("ERROR", message);
        }
        public static void Warn(string message)
        {
            Print("WARNING", message);
        }
        public static void Fatal(string message)
        {
            Print("FATAL", message);
        }
    }
}
