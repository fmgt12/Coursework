using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
namespace Launcher
{
    public class Program
    {
        private static readonly string Github_repozitory = "punochka/Coursework3";
        private static readonly string Github_API_URL = $"https://api.github.com/repos/{Github_repozitory}/releases/latest";
        private static readonly string current_version = "1.0.0";
        private static readonly string dextop_path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        private static readonly string update_folder = Path.Combine(dextop_path, "Coursework3_Update");
        static void Main(string[] args)
        {
            while (true)
            {
                Console.WriteLine("=== Лаунчер системы заметок ===");
                Console.WriteLine("1 - Запуск основной программы");
                Console.WriteLine("2 - Запуск агента сбора статистики");
                Console.WriteLine("3 - Обновление приложения");
                Console.WriteLine("0 - Выход");
                Console.Write("\nВаш выбор: ");
                string choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                        StartMainApplication();
                        return;
                    case "2":
                        StartAgent();
                        break;
                    case "3":
                        Task.Run(async () => await CheckForUpdatesAsync(true)).GetAwaiter().GetResult();
                        break;
                    case "0":
                        Console.WriteLine("Выход из лаунчера...");
                        return;
                    default:
                        Console.WriteLine("Неверный выбор. Попробуйте снова.\n");
                        break;
                }
            }
        }

        public static void StartMainApplication()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string mainAppPath = Path.Combine(basePath, "Coursework.exe");
                if (File.Exists(mainAppPath))
                {
                    Process.Start(mainAppPath);
                    Thread.Sleep(3000);
                }
                else
                {
                    Console.WriteLine($"\nОшибка: Не найден файл Coursework.exe по пути: {mainAppPath}");
                    Console.WriteLine("Нажмите любую клавишу для выхода...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка запуска основной программы: {ex.Message}");
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
        }

        public static void StartAgent()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string agentPath = Path.Combine(basePath, "Agent.exe");
                if (File.Exists(agentPath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = agentPath,
                        Arguments = "stats",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    };
                    Process.Start(startInfo);
                    Console.WriteLine("Нажмите любую клавишу для возврата в меню лаунчера...");
                    Console.ReadKey();
                    Console.Clear();
                }
                else
                {
                    Console.WriteLine($"\nОшибка: Не найден файл Agent.exe по пути: {agentPath}");
                    Console.WriteLine("Убедитесь, что проект Agent скомпилирован.");
                    Console.WriteLine("Нажмите любую клавишу для продолжения...");
                    Console.ReadKey();
                    Console.Clear();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка запуска агента: {ex.Message}");
                Console.WriteLine("Нажмите любую клавишу для продолжения...");
                Console.ReadKey();
                Console.Clear();
            }
        }

        public static async Task CheckForUpdatesAsync(bool showNoUpdateMessage = false)
        {
            try
            {
                Console.WriteLine("\n=== Проверка обновлений ===");
                Console.WriteLine($"Текущая версия: {current_version}");
                Console.WriteLine("Подключение к GitHub API...");

                using (var client = new HttpClient())
                {
                    // GitHub API требует User-Agent
                    client.DefaultRequestHeaders.Add("User-Agent", "Coursework-Launcher");

                    // Получаем информацию о последнем релизе
                    HttpResponseMessage response = await client.GetAsync(Github_API_URL);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            Console.WriteLine("Релизов на GitHub пока нет. Создайте первый релиз.");
                        }
                        else
                        {
                            Console.WriteLine($"Ошибка подключения к GitHub: {response.StatusCode}");
                            Console.WriteLine("Проверьте подключение к интернету.");
                        }
                        return;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    var release = JsonSerializer.Deserialize<GitHubRelease>(json);

                    if (release == null || string.IsNullOrEmpty(release.tag_name))
                    {
                        Console.WriteLine("Не удалось получить информацию о релизах");
                        return;
                    }

                    Console.WriteLine($"Последняя версия на GitHub: {release.tag_name}");

                    // Сравниваем версии (убираем 'v' из тега)
                    string latestVersion = release.tag_name.TrimStart('v');
                    int comparisonResult = CompareVersions(current_version, latestVersion);

                    if (comparisonResult < 0) // Текущая версия меньше
                    {
                        Console.WriteLine($"\n*** ДОСТУПНО ОБНОВЛЕНИЕ! ***");
                        Console.WriteLine($"Текущая: {current_version} -> Новая: {latestVersion}");

                        if (!string.IsNullOrEmpty(release.body))
                        {
                            Console.WriteLine($"\nЧто нового:\n{release.body}");
                        }

                        Console.Write($"\nУстановить обновление? (y/n): ");
                        string answer = Console.ReadLine()?.Trim().ToLower();

                        if (answer == "y" || answer == "yes")
                        {
                            await DownloadAndInstallUpdate(release);
                        }
                        else
                        {
                            Console.WriteLine("\nОбновление отменено.");
                        }
                    }
                    else if (comparisonResult == 0)
                    {
                        if (showNoUpdateMessage)
                            Console.WriteLine($"\n✅ Установлена актуальная версия {current_version}");
                        else
                            Console.WriteLine("Программа актуальна");
                    }
                    else
                    {
                        Console.WriteLine($"\nУ вас более новая версия ({current_version}), чем на GitHub ({latestVersion})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка при проверке обновлений: {ex.Message}");
                Console.WriteLine("Проверьте подключение к интернету.");
            }

            Console.WriteLine("\nНажмите любую клавишу для продолжения...");
            Console.ReadKey();
            Console.Clear();
        }

        public static async Task DownloadAndInstallUpdate(GitHubRelease release)
        {
            try
            {
                Console.WriteLine("\n=== Установка обновления ===");
                var asset = release.assets.Find(a => a.name.EndsWith(".zip") || a.name.EndsWith(".exe")); // Находим ZIP-архив в release assets
                //if (asset == null)
                //{
                //    Console.WriteLine("⚠️ Ошибка: В релизе не найден файл для скачивания (.zip или .exe)");
                //    Console.WriteLine("Убедитесь, что вы прикрепили файлы при создании релиза на GitHub");
                //    return;
                //}

                Console.WriteLine($"Скачивание обновления: {asset.name}");
                Console.WriteLine($"Размер: {asset.size / 1024} KB");

                // Создаем папку для обновления
                if (Directory.Exists(update_folder))
                    Directory.Delete(update_folder, true);
                Directory.CreateDirectory(update_folder);

                string downloadPath = Path.Combine(update_folder, asset.name);

                // Скачиваем файл
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Coursework-Launcher");

                    using (var response = await client.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead))
                    using (var fs = new FileStream(downloadPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                Console.WriteLine("✅ Файл успешно скачан!");

                // Если это ZIP-архив - распаковываем
                if (asset.name.EndsWith(".zip"))
                {
                    Console.WriteLine("Распаковка архива...");
                    ZipFile.ExtractToDirectory(downloadPath, update_folder, true);
                    Console.WriteLine("Архив распакован!");

                    // Ищем основной исполняемый файл
                    var exeFiles = Directory.GetFiles(update_folder, "*.exe", SearchOption.AllDirectories);
                    var mainExe = exeFiles.FirstOrDefault(f =>
                        Path.GetFileName(f).Equals("Coursework.exe", StringComparison.OrdinalIgnoreCase));

                    if (mainExe != null)
                    {
                        Console.WriteLine($"\nОбновление установлено в папку: {update_folder}");
                        Console.WriteLine("Хотите запустить новую версию сейчас? (y/n): ");
                        string run = Console.ReadLine()?.Trim().ToLower();

                        if (run == "y")
                        {
                            Process.Start(mainExe);
                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"\nОбновление распаковано. Найдите файлы в: {update_folder}");
                    }
                }
                else if (asset.name.EndsWith(".exe"))
                {
                    Console.WriteLine($"\nОбновление сохранено в: {downloadPath}");
                    Console.WriteLine("Хотите запустить новую версию сейчас? (y/n): ");
                    string run = Console.ReadLine()?.Trim().ToLower();

                    if (run == "y")
                    {
                        Process.Start(downloadPath);
                        Environment.Exit(0);
                    }
                }

                Console.WriteLine($"\nОбновление успешно установлено!");
                Console.WriteLine($"Файлы сохранены в: {update_folder}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка при установке обновления: {ex.Message}");
            }
        }

        private static int CompareVersions(string version1, string version2) // Сравнение версий
        {
            var v1Parts = version1.Split('.');
            var v2Parts = version2.Split('.');
            int maxLength = Math.Max(v1Parts.Length, v2Parts.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int v1Num = (i < v1Parts.Length && int.TryParse(v1Parts[i], out int n1)) ? n1 : 0;
                int v2Num = (i < v2Parts.Length && int.TryParse(v2Parts[i], out int n2)) ? n2 : 0;
                if (v1Num != v2Num)
                    return v1Num.CompareTo(v2Num);
            }
            return 0;
        }
    }

    public class GitHubRelease // Классы для десериализации ответа GitHub API
    {
        public string tag_name { get; set; }
        public string name { get; set; }
        public string body { get; set; }
        public bool prerelease { get; set; }
        public List<GitHubAsset> assets { get; set; } = [];
    }

    public class GitHubAsset
    {
        public string name { get; set; } = "";
        public long size { get; set; }
        public string browser_download_url { get; set; } = "";
    }
}