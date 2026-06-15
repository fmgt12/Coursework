using Npgsql;
using System.Data;
using System.Security.Cryptography;
using System.Text;
namespace Coursework
{
    public class Program
    {
        #region Fields and Properties

        static int currentUserId = -1;
        static string currentUsername = "";
        static int currentUserRole = -1;
        private static string connectionString = "Server=localhost;Port=5432;User ID=postgres;Password=3455;Database=Kurs;";

        #endregion

        #region Database Helper Methods

        public static DataTable ExecuteQuery(string query, NpgsqlParameter[] parameters = null)
        {
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    if (parameters != null)
                        cmd.Parameters.AddRange(parameters);
                    using (var adapter = new NpgsqlDataAdapter(cmd))
                    {
                        DataTable dt = new DataTable();
                        adapter.Fill(dt);
                        return dt;
                    }
                }
            }
        }

        public static int ExecuteNonQuery(string query, NpgsqlParameter[] parameters = null)
        {
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    if (parameters != null)
                        cmd.Parameters.AddRange(parameters);
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region Auth Service Methods

        public static string GetMd5Hash(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static (bool success, int role) Login(string username, string password)
        {
            string hash = GetMd5Hash(password);
            string query = "SELECT id, role FROM users WHERE username=@u AND password_md5=@p";
            var parameters = new[]
            {
                new NpgsqlParameter("u", username),
                new NpgsqlParameter("p", hash)
            };
            var result = ExecuteQuery(query, parameters);
            bool success = result.Rows.Count == 1;
            int role = -1;
            if (success)
            {
                role = Convert.ToInt32(result.Rows[0]["role"]);
            }
            SecurityLogs(success ? "LOGIN_SUCCESS" : "LOGIN_FAIL", username,
                           success ? $"Успешный вход (роль: {(role == 0 ? "админ" : "пользователь")})" : "Неверный пароль или логин");
            return (success, role);
        }

        public static bool Register(string username, string password, int role)
        {
            string hash = GetMd5Hash(password);
            try
            {
                string query = "INSERT INTO users (username, password_md5, role) VALUES (@u, @p, @r)";
                var parameters = new[]
                {
                    new NpgsqlParameter("u", username),
                    new NpgsqlParameter("p", hash),
                    new NpgsqlParameter("r", role)
                };
                ExecuteNonQuery(query, parameters);
                string roleName = role == 0 ? "Администратор" : "Пользователь";
                SecurityLogs("REGISTER", username, $"Новая регистрация с ролью: {roleName}");
                return true;
            }
            catch { return false; }
        }

        public static bool UserExists(string username)
        {
            string query = "SELECT id FROM users WHERE username=@u";
            var parameters = new[] { new NpgsqlParameter("u", username) };
            var result = ExecuteQuery(query, parameters);
            return result.Rows.Count > 0;
        }

        public static bool ChangeUserRole(int userId, int newRole)
        {
            try
            {
                string query = "UPDATE users SET role = @r WHERE id = @id";
                var parameters = new[]
                {
                    new NpgsqlParameter("r", newRole),
                    new NpgsqlParameter("id", userId)
                };
                int rowsAffected = ExecuteNonQuery(query, parameters);
                return rowsAffected > 0;
            }
            catch { return false; }
        }

        public static int GetUserRole(int userId)
        {
            string query = "SELECT role FROM users WHERE id = @id";
            var parameters = new[] { new NpgsqlParameter("id", userId) };
            var result = ExecuteQuery(query, parameters);
            if (result.Rows.Count > 0)
                return Convert.ToInt32(result.Rows[0]["role"]);
            return -1;
        }

        #endregion

        #region Security Logger Methods

        public static void SecurityLogs(string eventType, string username, string details)
        {
            string query = "INSERT INTO security_logs (event_type, username, details) VALUES (@et, @u, @d)";
            var parameters = new[]
            {
                new NpgsqlParameter("et", eventType),
                new NpgsqlParameter("u", username),
                new NpgsqlParameter("d", details)
            };
            ExecuteNonQuery(query, parameters);
        }

        #endregion

        #region Note Service Methods

        public static void AddNote(int userId, string noteText)
        {
            string query = "INSERT INTO notes (user_id, note_text, is_deleted) VALUES (@userId, @noteText, false)";
            var parameters = new[]
            {
                new NpgsqlParameter("userId", userId),
                new NpgsqlParameter("noteText", noteText)
            };
            ExecuteNonQuery(query, parameters);
            SecurityLogs("NOTE_CREATED", GetUsernameById(userId), $"Создана заметка: {noteText}");
        }

        private static string GetUsernameById(int userId)
        {
            var dt = ExecuteQuery("SELECT username FROM users WHERE id=@id",
                new[] { new NpgsqlParameter("id", userId) });
            if (dt.Rows.Count > 0)
                return dt.Rows[0]["username"].ToString();
            return "Unknown";
        }

        public static void ListNotes(int userId, int userRole = 1)
        {
            string query;
            NpgsqlParameter[] parameters;
            if (userRole == 0)
            {
                query = @"SELECT n.id, n.note_text, n.created_at, u.username as author 
                  FROM notes n 
                  JOIN users u ON n.user_id = u.id 
                  WHERE n.is_deleted = false
                  ORDER BY n.created_at DESC";
                parameters = new NpgsqlParameter[] { };
            }
            else
            {
                query = "SELECT id, note_text, created_at FROM notes WHERE user_id = @userId AND is_deleted = false ORDER BY created_at DESC";
                parameters = new[] { new NpgsqlParameter("userId", userId) };
            }
            var dt = ExecuteQuery(query, parameters);
            if (dt.Rows.Count == 0)
            {
                Console.WriteLine("Заметок не найдено.");
                return;
            }
            Console.WriteLine("\n=== Заметки ===");
            foreach (DataRow row in dt.Rows)
            {
                if (userRole == 0)
                {
                    Console.WriteLine($"[ID: {row["id"]}] [{row["created_at"]}] {row["author"]}: {row["note_text"]}");
                }
                else
                {
                    Console.WriteLine($"[ID: {row["id"]}] [{row["created_at"]}] {row["note_text"]}");
                }
            }
        }

        public static bool DeleteNote(int noteId, int userId, int userRole)
        {
            try
            {
                string query;
                NpgsqlParameter[] parameters;
                if (userRole == 0)
                {
                    query = "UPDATE notes SET is_deleted = true WHERE id = @noteId";
                    parameters = new[] { new NpgsqlParameter("noteId", noteId) };
                }
                else
                {
                    query = "UPDATE notes SET is_deleted = true WHERE id = @noteId AND user_id = @userId";
                    parameters = new[]
                    {
                        new NpgsqlParameter("noteId", noteId),
                        new NpgsqlParameter("userId", userId)
                    };
                }
                int rowsAffected = ExecuteNonQuery(query, parameters);
                if (rowsAffected > 0)
                {
                    SecurityLogs("NOTE_DELETED", GetUsernameById(userId), $"Удалена заметка ID: {noteId}");
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public static void ShowDeletedNotes(int userId, int userRole)
        {
            string query;
            NpgsqlParameter[] parameters;
            if (userRole == 0)
            {
                query = @"SELECT n.id, n.note_text, n.created_at, u.username as author 
                  FROM notes n 
                  JOIN users u ON n.user_id = u.id 
                  WHERE n.is_deleted = true
                  ORDER BY n.created_at DESC";
                parameters = new NpgsqlParameter[] { };
            }
            else
            {
                query = "SELECT id, note_text, created_at FROM notes WHERE user_id = @userId AND is_deleted = true ORDER BY created_at DESC";
                parameters = new[] { new NpgsqlParameter("userId", userId) };
            }
            var dt = ExecuteQuery(query, parameters);
            if (dt.Rows.Count == 0)
            {
                Console.WriteLine("Удалённых заметок не найдено.");
                return;
            }
            Console.WriteLine("\n=== Удалённые заметки ===");
            foreach (DataRow row in dt.Rows)
            {
                if (userRole == 0) { Console.WriteLine($"[ID: {row["id"]}] [{row["created_at"]}] {row["author"]}: {row["note_text"]}"); }
                else { Console.WriteLine($"[ID: {row["id"]}] [{row["created_at"]}] {row["note_text"]}"); }
            }
        }

        public static bool RestoreNote(int noteId, int userId, int userRole)
        {
            try
            {
                string query;
                NpgsqlParameter[] parameters;
                if (userRole == 0)
                {
                    query = "UPDATE notes SET is_deleted = false WHERE id = @noteId";
                    parameters = new[] { new NpgsqlParameter("noteId", noteId) };
                }
                else
                {
                    query = "UPDATE notes SET is_deleted = false WHERE id = @noteId AND user_id = @userId";
                    parameters = new[]
                    {
                        new NpgsqlParameter("noteId", noteId),
                        new NpgsqlParameter("userId", userId)
                    };
                }
                int rowsAffected = ExecuteNonQuery(query, parameters);
                if (rowsAffected > 0)
                {
                    SecurityLogs("NOTE_RESTORED", GetUsernameById(userId), $"Восстановлена заметка ID: {noteId}");
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public static void TestNoteProcedure(int userId)
        {
            try
            {
                string query = "CALL test_note_operations(@p_user_id, @p_note_text)";
                var parameters = new[]
                {
                    new NpgsqlParameter("p_user_id", userId),
                    new NpgsqlParameter("p_note_text", "Тестовая заметка №1")
                };
                ExecuteNonQuery(query, parameters);
                Console.WriteLine("Хранимая процедура выполнена успешно!");
                Console.WriteLine("Результаты можно посмотреть в таблицах notes и security_logs");
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка при выполнении процедуры: {ex.Message}"); }
        }

        #endregion

        #region Main Program Methods

        static void Main()
        {
            TestDatabaseConnection();
            while (currentUserId == -1)
            {
                Console.WriteLine("\n=== Меню входа ===");
                Console.WriteLine("1 - Войти в существующий аккаунт");
                Console.WriteLine("2 - Зарегистрировать новый аккаунт");
                Console.WriteLine("3 - Выход");
                Console.Write("Ваш выбор: ");
                string choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                        LoginUser();
                        break;
                    case "2":
                        RegisterUser();
                        break;
                    case "3":
                        Environment.Exit(0);
                        break;
                    default:
                        Console.WriteLine("Неверный выбор. Попробуйте снова.");
                        break;
                }
            }
            MainMenu();
        }

        static void MainMenu()
        {
            while (true)
            {
                string rolePrefix = currentUserRole == 0 ? "[Админ]" : "[Обычный пользователь]";
                Console.WriteLine($"\n=== Главное меню {rolePrefix} ===");
                Console.WriteLine("Доступные команды:");
                Console.WriteLine("addNewNote текст      - Добавить заметку");
                Console.WriteLine("listNotes             - Просмотреть заметки");
                Console.WriteLine("deleteNote id         - Удалить заметку");
                if (currentUserRole == 0)
                {
                    Console.WriteLine("securityLogs           - Показать логи безопасности");
                    Console.WriteLine("listUsers              - Показать всех пользователей");
                    Console.WriteLine("changeUserRole id role - Изменить роль пользователя");
                    Console.WriteLine("clearUsers             - Очистить всех пользователей (кроме админов)");
                    Console.WriteLine("showDeleted            - Показать удалённые заметки");
                    Console.WriteLine("restoreNote id         - Восстановить заметку по ID");
                    Console.WriteLine("testProc               - Тест хранимой процедуры");
                }
                Console.WriteLine("help                  - Справка");
                Console.WriteLine("exit                  - Выйти из системы");
                Console.Write("\n> ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                if (input == "help")
                {
                    Console.WriteLine(@"
=== Помощь ===

Доступные команды:

Команда                  | Описание                                   | Пример 
------------------------------------------------------------------------------------------------                
`addNewNote текст`      | Добавить новую заметку                     | `addNewNote ""Купить сервер""` 
`listNotes`             | Показать все заметки текущего пользователя | `listNotes` 
`deleteNote id`         | Удалить заметку                            | `deleteNote 5`

Доступно только администратору:
`securityLogs`          | Показать последние логи безопасности                | `securityLogs` 
`listUsers`             | Показать всех пользователей системы                 | `listUsers`
`changeUserRole id role`| Изменить роль пользователя (0-админ,1-пользователь) | `changeUserRole 5 0`
`clearUsers`            | Удалить всех пользователей кроме текущего           | `clearUsers`
`showDeleted`           | Показать удалённые заметки                          | `showDeleted`
`restoreNote id`        | Восстановить заметку по ID                          | `restoreNote 5`

Обычные команды:
`help`                  | Показать эту справку                       | `help` 
`exit`                  | Выйти из программы                         | `exit` 

Безопасность:
- Пароли хранятся в MD5
- Все действия логируются (записываются) в `security_logs`");
                }
                else if (input.StartsWith("addNewNote"))
                {
                    string note = input.Substring("addNewNote".Length).Trim();
                    if (string.IsNullOrWhiteSpace(note))
                    {
                        Console.WriteLine("Текст заметки не может быть пустым!");
                    }
                    else
                    {
                        AddNote(currentUserId, note);
                        Console.WriteLine("Заметка добавлена.");
                    }
                }
                else if (input == "listNotes") { ListNotes(currentUserId, currentUserRole); }
                else if (input == "securityLogs")
                {
                    if (currentUserRole == 0)
                        ShowSecurityLogs();
                    else
                        Console.WriteLine("Доступ запрещен. Только для администраторов.");
                }
                else if (input == "listUsers")
                {
                    if (currentUserRole == 0)
                        ShowAllUsers();
                    else
                        Console.WriteLine("Доступ запрещен.ьТолько для администраторов.");
                }
                else if (input.StartsWith("changeUserRole"))
                {
                    if (currentUserRole == 0)
                        ChangeUserRole(input);
                    else
                        Console.WriteLine("Доступ запрещен. Только для администраторов.");
                }
                else if (input == "clearUsers")
                {
                    if (currentUserRole == 0)
                        ClearAllUsers();
                    else
                        Console.WriteLine("Доступ запрещен. Только для администраторов.");
                }
                else if (input.StartsWith("deleteNote"))
                {
                    string[] parts = input.Split(' ');
                    if (parts.Length != 2 || !int.TryParse(parts[1], out int noteId))
                    {
                        Console.WriteLine("Использование: deleteNote <id заметки>");
                    }
                    else
                    {
                        if (DeleteNote(noteId, currentUserId, currentUserRole))
                            Console.WriteLine("Заметка удалена.");
                        else
                            Console.WriteLine("Ошибка: заметка не найдена или у вас нет прав.");
                    }
                }
                else if (input == "testProc")
                {
                    if (currentUserRole == 0)
                        TestNoteProcedure(currentUserId);
                    else
                        Console.WriteLine("Доступ запрещен. Только для администраторов.");
                }
                else if (input == "showDeleted")
                {
                    if (currentUserRole == 0)
                        ShowDeletedNotes(currentUserId, currentUserRole);
                    else
                        Console.WriteLine("Доступ запрещен. Только для администраторов.");
                }
                else if (input.StartsWith("restoreNote"))
                {
                    if (currentUserRole == 0)
                    {
                        string[] parts = input.Split(' ');
                        if (parts.Length != 2 || !int.TryParse(parts[1], out int noteId))
                        {
                            Console.WriteLine("Использование: restoreNote <id заметки>");
                        }
                        else
                        {
                            if (RestoreNote(noteId, currentUserId, currentUserRole))
                                Console.WriteLine("Заметка восстановлена.");
                            else
                                Console.WriteLine("Ошибка: заметка не найдена.");
                        }
                    }
                    else
                        Console.WriteLine("Доступ запрещен. Только для администраторов.");
                }
                else if (input == "exit") { break; }
                else { Console.WriteLine("Неизвестная команда. Введите help для списка команд."); }
            }
        }

        static void LoginUser()
        {
            Console.Write("\nЛогин: ");
            string login = Console.ReadLine();
            Console.Write("Пароль: ");
            string pass = Console.ReadLine();
            var (success, role) = Login(login, pass);
            if (success)
            {
                currentUserId = GetUserId(login);
                currentUsername = login;
                currentUserRole = role;
                string roleName = currentUserRole == 0 ? "Администратор" : "Пользователь";
                Console.WriteLine($"\nВход успешен. Роль: {roleName}");
            }
            else
            { Console.WriteLine("\nОшибка входа. Неверный логин или пароль."); }
        }

        static void RegisterUser()
        {
            Console.Write("\nПридумайте логин: ");
            string username = Console.ReadLine();
            Console.Write("Придумайте пароль: ");
            string password = Console.ReadLine();
            Console.Write("Повторите пароль: ");
            string confirmPassword = Console.ReadLine();
            Console.WriteLine("\nВыберите роль:");
            Console.WriteLine("0 - Администратор");
            Console.WriteLine("1 - Пользователь");
            Console.Write("Ваш выбор: ");
            int role;
            if (!int.TryParse(Console.ReadLine(), out role) || (role != 0 && role != 1))
            {
                Console.WriteLine("\nНеверный выбор роли. Будет установлена роль 'Пользователь'.");
                role = 1;
            }
            if (password != confirmPassword)
            {
                Console.WriteLine("\nПароли не совпадают!");
                return;
            }
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("\nЛогин и пароль не могут быть пустыми!");
                return;
            }
            if (Register(username, password, role))
            { Console.WriteLine("\nРегистрация успешна! Теперь вы можете войти."); }
            else { Console.WriteLine("\nОшибка регистрации. Пользователь с таким логином уже существует."); }
        }

        static int GetUserId(string username)
        {
            var dt = ExecuteQuery("SELECT id FROM users WHERE username=@u",
                new[] { new NpgsqlParameter("u", username) });
            if (dt.Rows.Count > 0)
                return Convert.ToInt32(dt.Rows[0]["id"]);
            return -1;
        }

        static void ShowSecurityLogs()
        {
            Console.WriteLine("\nПоследние события:");
            Console.WriteLine("=========================================");
            var dt = ExecuteQuery("SELECT * FROM security_logs ORDER BY event_time DESC LIMIT 20");
            if (dt.Rows.Count == 0)
            {
                Console.WriteLine("Логи безопасности пусты.");
                return;
            }
            foreach (DataRow row in dt.Rows)
            { Console.WriteLine($"{row["event_time"]} | {row["event_type"]} | {row["username"]} | {row["details"]}"); }
        }

        static void ShowAllUsers()
        {
            Console.WriteLine("\nСписок пользователей:");
            Console.WriteLine("=========================================");
            var dt = ExecuteQuery("SELECT id, username, role FROM users ORDER BY id");
            if (dt.Rows.Count == 0)
            {
                Console.WriteLine("Пользователи не найдены.");
                return;
            }
            Console.WriteLine($"{"ID",-5} {"Логин",-20} {"Роль",-15}");
            Console.WriteLine("--------------------------------------------------");
            foreach (DataRow row in dt.Rows)
            {
                int role = Convert.ToInt32(row["role"]);
                string roleName = role == 0 ? "Администратор" : "Пользователь";
                Console.WriteLine($"{row["id"],-5} {row["username"],-20} {roleName,-15}");
            }
        }

        static void ChangeUserRole(string input)
        {
            string[] parts = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                Console.WriteLine("Использование: -changeUserRole <id пользователя> <роль>");
                Console.WriteLine("Роль: 0 - Администратор, 1 - Пользователь");
                return;
            }
            if (!int.TryParse(parts[1], out int userId))
            {
                Console.WriteLine("Ошибка: ID пользователя должен быть числом.");
                return;
            }
            if (!int.TryParse(parts[2], out int newRole) || (newRole != 0 && newRole != 1))
            {
                Console.WriteLine("Ошибка: Роль должна быть 0 (Администратор) или 1 (Пользователь).");
                return;
            }
            if (userId == currentUserId)
            {
                Console.WriteLine("Вы не можете изменить свою собственную роль.");
                return;
            }
            if (ChangeUserRole(userId, newRole))
            {
                string roleName = newRole == 0 ? "Администратор" : "Пользователь";
                Console.WriteLine($"Роль пользователя с ID {userId} успешно изменена на {roleName}.");
                SecurityLogs("ROLE_CHANGED", currentUsername, $"Изменена роль пользователя ID {userId} на {newRole}");
            }
            else { Console.WriteLine("Ошибка при изменении роли. Проверьте, существует ли пользователь."); }
        }

        static void TestDatabaseConnection()
        {
            try
            {
                Console.WriteLine("\nПодключение к БД...");
                var dt = ExecuteQuery("SELECT NOW() as current_time, version() as pg_version");
                if (dt.Rows.Count > 0) { Console.WriteLine($"Подключение к БД успешно!"); }
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка подключения: {ex.Message}"); }
        }

        static void ClearAllUsers()
        {
            Console.WriteLine("\n=== Очистка пользователей ===");
            Console.WriteLine("! Это действие удалит всех пользователей, кроме вас.");
            Console.WriteLine($"Текущий пользователь: {currentUsername} (ID: {currentUserId}) будет сохранен.");
            Console.Write("Вы уверены? (y/n): ");
            string confirmation = Console.ReadLine();
            if (confirmation?.ToLower() != "y")
            {
                Console.WriteLine("Операция отменена.");
                return;
            }
            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        string deleteNotesQuery = "DELETE FROM notes WHERE user_id != @currentUserId";
                        using (var cmd = new NpgsqlCommand(deleteNotesQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("currentUserId", currentUserId);
                            int notesDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"  - Удалено заметок: {notesDeleted}");
                        }
                        string deleteLogsQuery = "DELETE FROM security_logs WHERE username != @currentUsername";
                        using (var cmd = new NpgsqlCommand(deleteLogsQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("currentUsername", currentUsername);
                            int logsDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"  - Удалено логов: {logsDeleted}");
                        }
                        string deleteStatsQuery = "DELETE FROM system_stats";
                        using (var cmd = new NpgsqlCommand(deleteStatsQuery, conn, transaction))
                        {
                            int statsDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"  - Удалено записей статистики: {statsDeleted}");
                        }
                        string deleteUsersQuery = "DELETE FROM users WHERE id != @currentUserId";
                        using (var cmd = new NpgsqlCommand(deleteUsersQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("currentUserId", currentUserId);
                            int usersDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"  - Удалено пользователей: {usersDeleted}");
                        }
                        transaction.Commit();
                        SecurityLogs("USERS_CLEARED", currentUsername, $"Удалено всё: пользователи, заметки, логи, статистика (кроме текущего)");
                        Console.WriteLine("\nОчистка выполнена успешно!");
                        Console.WriteLine($"Текущий пользователь {currentUsername} (ID: {currentUserId}) сохранен.");
                    }
                }
            }
            catch (Exception ex)
            { Console.WriteLine($"\nОшибка при очистке пользователей: {ex.Message}"); }
        }

        #endregion
    }
}