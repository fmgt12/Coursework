using Npgsql;
using System.Diagnostics;
using System.Management;
namespace Agent
{
    public class Program
    {
        private static string connectionString = "Server=localhost;Port=5432;User ID=postgres;Password=3455;Database=Kurs;";
        private static bool continuousMode = false;
        private static int intervalSeconds = 60;

        static void Main(string[] args)
        {
            Console.WriteLine("=== Агент сбора статистики ===\n");

            if (args.Length > 0 && args[0] == "stats")
            {
                CollectAndSaveStats(oneTime: true);

                Console.WriteLine("\n=== Режимы работы ===");
                Console.WriteLine("1 - Получать статистику каждую минуту");
                Console.WriteLine("2 - Получать статистику каждую минуту и вернуться в лаунчер");
                Console.WriteLine("3 - Вернуться в лаунчер");
                Console.Write("\nВаш выбор: ");

                string choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        continuousMode = true;
                        RunContinuousCollection(returnToLauncher: false);
                        break;
                    case "2":
                        continuousMode = true;
                        RunContinuousCollection(returnToLauncher: true);
                        break;
                    case "3":
                        Console.WriteLine("\nАгент закроется через 3 секунды...");
                        Thread.Sleep(3000);
                        return;
                    default:
                        Console.WriteLine("Неверный выбор. Агент закроется через 3 секунды...");
                        Thread.Sleep(3000);
                        return;
                }
            }
            else
            {
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
        }

        public static void RunContinuousCollection(bool returnToLauncher)
        {
            Console.Clear();
            Console.WriteLine("=== Агент сбора статистики ===");
            Console.WriteLine($"Сбор статистики каждые {intervalSeconds} секунд...");
            Console.WriteLine("Нажмите 'Q' для остановки и выхода\n");
            if (returnToLauncher)
            {
                Console.WriteLine("Агент работает в фоне. Лаунчер откроется через 3 секунды...\n");
                Thread.Sleep(3000);
            }
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("\nОстановка сбора статистики. Выход...");
                        return;
                    }
                }
                CollectAndSaveStats(oneTime: false);
                for (int i = intervalSeconds; i > 0; i--)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            Console.WriteLine("\nОстановка сбора статистики. Выход...");
                            return;
                        }
                    }
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write($"Следующий сбор через {i} секунд. Нажмите Q для выхода.");
                    Thread.Sleep(1000);
                }
            }
        }

        public static void CollectAndSaveStats(bool oneTime = true)
        {
            try
            {
                double cpu = GetCpuUsage();
                var (ramUsed, ramTotal, ramPercent) = GetRamUsage();
                var (hddFree, hddTotal, hddPercent) = GetHddUsage();
                Console.WriteLine($"=== СТАТИСТИКА {DateTime.Now:HH:mm:ss} ===");
                Console.WriteLine("─────────────────────────────────────────────────");
                Console.WriteLine($"CPU:  {cpu,6:F1}%");
                Console.WriteLine($"RAM:  {ramPercent,6:F1}%  ({ramUsed} МБ / {ramTotal} МБ)");
                Console.WriteLine($"HDD:  {hddPercent,6:F1}%  (Свободно: {hddFree:F2} ГБ / {hddTotal:F2} ГБ)");
                SaveStatsToDb(cpu, ramUsed, hddFree);
                Console.WriteLine("! Статистика сохранена в БД.");
                Console.WriteLine("─────────────────────────────────────────────────\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"! Ошибка сбора статистики: {ex.Message}\n");
            }
        }

        private static double GetCpuUsage()
        {
            var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            counter.NextValue();
            Thread.Sleep(100);
            return Math.Round(counter.NextValue(), 2);
        }

        private static (int used, int total, double percent) GetRamUsage()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    long total = long.Parse(obj["TotalVisibleMemorySize"].ToString()) / 1024;
                    long free = long.Parse(obj["FreePhysicalMemory"].ToString()) / 1024;
                    long used = total - free;
                    double percent = (double)used / total * 100;
                    return ((int)used, (int)total, Math.Round(percent, 2));
                }
            }
            return (0, 0, 0);
        }

        private static (double free, double total, double percent) GetHddUsage()
        {
            var drive = new DriveInfo("C");
            double total = drive.TotalSize / (1024.0 * 1024 * 1024);
            double free = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            double used = total - free;
            double percent = (used / total) * 100;
            return (Math.Round(free, 2), Math.Round(total, 2), Math.Round(percent, 2));
        }

        public static void SaveStatsToDb(double cpu, int ramUsed, double hddFree)
        {
            string hostname = Environment.MachineName;
            string query = "INSERT INTO system_stats (hostname, cpu_percent, ram_used_mb, hdd_free_gb) VALUES (@h, @cpu, @ram, @hdd)";
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("h", hostname);
                    cmd.Parameters.AddWithValue("cpu", cpu);
                    cmd.Parameters.AddWithValue("ram", ramUsed);
                    cmd.Parameters.AddWithValue("hdd", hddFree);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}