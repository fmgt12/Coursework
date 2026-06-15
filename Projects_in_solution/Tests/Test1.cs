using Coursework;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Npgsql;
using System.Data;
using System.Diagnostics;
namespace Tests
{
    [TestClass]
    public class IntegrationTests
    {
        private static string testConnectionString = "Server=localhost;Port=5432;User ID=postgres;Password=3455;Database=Coursework;";
        private string testUser;
        private readonly string testPass = "testpass123";
        private int userId;
        private int userRole;

        [TestInitialize]
        public void Setup()
        {
            testUser = $"testuser_{DateTime.Now.Ticks}";
            bool registered = Coursework.Program.Register(testUser, testPass, 1);
            Assert.IsTrue(registered, "Не удалось создать тестового пользователя");
            userId = GetUserId(testUser);
            Assert.IsTrue(userId > 0, "Не удалось получить ID тестового пользователя");
            userRole = Coursework.Program.GetUserRole(userId);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try // Очищаем тестовые данные
            {
                using (var conn = new NpgsqlConnection(testConnectionString))
                {
                    conn.Open();
                    // Удаляем заметки тестового пользователя
                    string deleteNotes = "DELETE FROM notes WHERE user_id = @userId";
                    using (var cmd = new NpgsqlCommand(deleteNotes, conn))
                    {
                        cmd.Parameters.AddWithValue("userId", userId);
                        cmd.ExecuteNonQuery();
                    }
                    // Удаляем тестового пользователя
                    string deleteUser = "DELETE FROM users WHERE id = @userId";
                    using (var cmd = new NpgsqlCommand(deleteUser, conn))
                    {
                        cmd.Parameters.AddWithValue("userId", userId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Cleanup error: {ex.Message}"); }
        }

        #region Тестирование подключения к БД (TC-DB-001, TC-DB-002)

        [TestMethod]
        public void DatabaseConnection_CanExecuteQuery_ReturnsData()
        {
            // Act
            var dt = Coursework.Program.ExecuteQuery("SELECT 1 as value");

            // Assert
            Assert.IsNotNull(dt);
            Assert.IsTrue(dt.Rows.Count > 0);
            Assert.AreEqual(1, Convert.ToInt32(dt.Rows[0]["value"]));
        }

        [TestMethod]
        public void DatabaseConnection_CanExecuteNonQuery_ReturnsAffectedRows()
        {
            int result = Coursework.Program.ExecuteNonQuery("SELECT 1"); // Act
            Assert.AreEqual(-1, result); // Assert - ExecuteNonQuery для SELECT возвращает -1 в Npgsql
        }

        #endregion

        #region Тестирование авторизации (TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-005)

        [TestMethod]
        [DataRow("admin", "3455", true, 0, "Существующий админ")]  // Вход с верными данными (админ)
        [DataRow("testuser_placeholder", "testpass123", true, 1, "Существующий пользователь")] // Будет заменен на testUser
        [DataRow("testuser_placeholder", "wrongpass", false, -1, "Неверный пароль")] // Неверный пароль
        [DataRow("nonexistent_user", "anypass", false, -1, "Несуществующий пользователь")] // Несуществующий пользователь
        [DataRow("newuser_temp", "newpass123", false, -1, "Новый пользователь (еще не зарегистрирован)")] // Новый пользователь
        public void Authorization_VariousScenarios_ReturnsExpectedResult(string username, string password, bool expectedSuccess, int expectedRole, string scenario)
        {
            string actualUsername = username == "testuser_placeholder" ? testUser : username; // Arrange - для сценариев с testuser_placeholder подставляем реального тестового пользователя
            if (username == "newuser_temp") // Для сценария с новым пользователем сначала проверяем, что его нет
            {
                if (Coursework.Program.UserExists(actualUsername)) // Убеждаемся, что пользователь не существует
                {
                    int existingId = GetUserId(actualUsername);
                    if (existingId > 0)
                        CleanupUser(existingId);
                }
            }
            var (success, role) = Coursework.Program.Login(actualUsername, password); // Act 
            Assert.AreEqual(expectedSuccess, success, $"Сценарий '{scenario}': ожидался результат {expectedSuccess}"); // Assert
            if (expectedSuccess) { Assert.AreEqual(expectedRole, role, $"Сценарий '{scenario}': ожидалась роль {expectedRole}"); }
        }

        [TestMethod]
        [DataRow("newuser_{0}", "newpass123", 1, true, "Регистрация нового пользователя")]
        [DataRow("existinguser", "somepass", 1, false, "Регистрация существующего пользователя")]
        public void Register_VariousScenarios_ReturnsExpectedResult(string usernameTemplate, string password, int role, bool expectedResult, string scenario)
        {
            string username = string.Format(usernameTemplate, DateTime.Now.Ticks); // Arrange
            if (usernameTemplate == "existinguser") // Для сценария с существующим пользователем
            {
                username = testUser; // Используем уже созданного тестового пользователя
            }
            bool result = Coursework.Program.Register(username, password, role); // Act
            Assert.AreEqual(expectedResult, result, $"Сценарий '{scenario}': ожидался результат {expectedResult}"); // Assert    
            if (result && usernameTemplate != "existinguser") // Cleanup для успешной регистрации нового пользователя
            {
                int newUserId = GetUserId(username);
                if (newUserId > 0)
                {
                    CleanupUser(newUserId);
                }
            }
        }

        [TestMethod]
        [DataRow("existinguser", true, "Существующий пользователь")]
        [DataRow("nonexistent_{0}", false, "Несуществующий пользователь")]
        public void UserExists_VariousScenarios_ReturnsExpectedResult(string usernameTemplate, bool expectedExists, string scenario)
        {
            string username; // Arrange
            if (usernameTemplate == "existinguser")
            {
                username = testUser;
            }
            else
            {
                username = string.Format(usernameTemplate, DateTime.Now.Ticks);
            }
            bool exists = Coursework.Program.UserExists(username); // Act
            Assert.AreEqual(expectedExists, exists, $"Сценарий '{scenario}': ожидался результат {expectedExists}"); // Assert
        }

        [TestMethod]
        [DataRow(true, 1, "Существующий пользователь")]
        [DataRow(false, -1, "Несуществующий пользователь")]
        public void GetUserRole_VariousScenarios_ReturnsExpectedRole(bool existingUser, int expectedRole, string scenario)
        {
            int targetUserId = existingUser ? userId : -9999; // Arrange  
            int role = Coursework.Program.GetUserRole(targetUserId); // Act
            Assert.AreEqual(expectedRole, role, $"Сценарий '{scenario}': ожидалась роль {expectedRole}"); // Assert
        }

        #endregion

        #region Тестирование управления ролями (TC-ROLE-001, TC-ROLE-002)

        [TestMethod]
        [DataRow(true, 0, true, "Изменение роли существующего пользователя")]
        [DataRow(false, 0, false, "Изменение роли несуществующего пользователя")]
        public void ChangeUserRole_VariousScenarios_ReturnsExpectedResult(bool existingUser, int newRole, bool expectedResult, string scenario)
        {
            // Arrange
            int targetUserId = existingUser ? userId : -9999;
            int originalRole = -1;
            if (existingUser)
            {
                originalRole = Coursework.Program.GetUserRole(userId);
            }
            bool result = Coursework.Program.ChangeUserRole(targetUserId, newRole); // Act
            Assert.AreEqual(expectedResult, result, $"Сценарий '{scenario}': ожидался результат {expectedResult}");
            if (existingUser && result) // Verify and cleanup для существующего пользователя
            {
                int updatedRole = Coursework.Program.GetUserRole(userId);
                Assert.AreEqual(newRole, updatedRole, $"Сценарий '{scenario}': роль не изменилась");

                // Возвращаем обратно
                Coursework.Program.ChangeUserRole(userId, originalRole);
            }
        }

        #endregion

        #region Тестирование заметок (TC-NOTE-001, TC-NOTE-002, TC-NOTE-003, TC-NOTE-004)

        [TestMethod]
        [DataRow("Hello World", "Обычный текст")]
        [DataRow("Тестовая заметка с русским текстом", "Русский текст")]
        [DataRow("1234567890", "Числа")]
        [DataRow("Заметка со спецсимволами !@#$%^&*()", "Спецсимволы")]
        [DataRow("Многострочная\nзаметка\nс переносами", "Многострочный текст")]
        [DataRow("", "Пустая строка")]
        public void AddNote_DifferentTexts_SuccessfullyAdded(string noteText, string scenario)
        {
            Coursework.Program.AddNote(userId, noteText);
            var dt = Coursework.Program.ExecuteQuery(
                "SELECT COUNT(*) FROM notes WHERE user_id = @uid AND note_text = @txt AND is_deleted = false",
                new[] {
                    new NpgsqlParameter("uid", userId),
                    new NpgsqlParameter("txt", noteText)
                });

            long count = Convert.ToInt64(dt.Rows[0][0]);
            Assert.IsTrue(count > 0, $"Сценарий '{scenario}': заметка с текстом '{noteText}' не была добавлена");
        }

        [TestMethod]
        [DataRow(true, "Пользователь с заметками")]
        [DataRow(false, "Пользователь без заметок")]
        public void ListNotes_VariousScenarios_ShowsAppropriateMessage(bool hasNotes, string scenario)
        {
            // Arrange
            if (hasNotes)
            {
                Coursework.Program.AddNote(userId, "Тестовая заметка для списка");
            }

            // Act & Assert
            using (var sw = new StringWriter())
            {
                Console.SetOut(sw);
                Coursework.Program.ListNotes(userId, 1);
                string output = sw.ToString();

                if (hasNotes)
                {
                    Assert.IsTrue(output.Contains("Заметки") || output.Contains("Тестовая заметка"),
                        $"Сценарий '{scenario}': ожидались заметки в выводе");
                }
                else
                {
                    Assert.IsTrue(output.Contains("не найдено"),
                        $"Сценарий '{scenario}': ожидалось сообщение об отсутствии заметок");
                }
            }
            // Восстанавливаем стандартный вывод
            var standardOutput = new StreamWriter(Console.OpenStandardOutput());
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);
        }

        [TestMethod]
        [DataRow(true, true, true, "Удаление существующей заметки")]
        [DataRow(false, true, false, "Удаление несуществующей заметки")]
        [DataRow(true, false, false, "Удаление чужой заметки")]
        public void DeleteNote_VariousScenarios_ReturnsExpectedResult(bool existingNote, bool isOwnNote, bool expectedResult, string scenario)
        {
            // Arrange
            int noteId = -9999;
            int ownerId = userId;
            if (existingNote)
            {
                Coursework.Program.AddNote(userId, "Заметка для теста удаления");
                var dt = Coursework.Program.ExecuteQuery(
                    "SELECT id FROM notes WHERE user_id = @uid AND is_deleted = false ORDER BY id DESC LIMIT 1",
                    new[] { new NpgsqlParameter("uid", userId) });
                noteId = Convert.ToInt32(dt.Rows[0]["id"]);

                if (!isOwnNote)
                {
                    // Создаем другого пользователя для чужой заметки
                    string otherUser = $"other_{DateTime.Now.Ticks}";
                    Coursework.Program.Register(otherUser, "pass123", 1);
                    int otherUserId = GetUserId(otherUser);
                    Coursework.Program.AddNote(otherUserId, "Чужая заметка");

                    var otherDt = Coursework.Program.ExecuteQuery(
                        "SELECT id FROM notes WHERE user_id = @uid AND is_deleted = false LIMIT 1",
                        new[] { new NpgsqlParameter("uid", otherUserId) });
                    noteId = Convert.ToInt32(otherDt.Rows[0]["id"]);
                    ownerId = otherUserId;
                }
            }
            bool result = Coursework.Program.DeleteNote(noteId, ownerId, 1);
            Assert.AreEqual(expectedResult, result, $"Сценарий '{scenario}': ожидался результат {expectedResult}");
            if (existingNote && !isOwnNote)
            {
                int otherUserId = GetOwnerIdForNote(noteId);
                if (otherUserId > 0 && otherUserId != userId)
                {
                    CleanupUser(otherUserId);
                }
            }
        }

        [TestMethod]
        [DataRow(true, true, "Восстановление удаленной заметки")]
        [DataRow(true, false, "Восстановление неудаленной заметки")]
        public void RestoreNote_VariousScenarios_ReturnsExpectedResult(bool initiallyDeleted, bool expectedResult, string scenario)
        {
            Coursework.Program.AddNote(userId, "Заметка для теста восстановления");
            var dt = Coursework.Program.ExecuteQuery(
                "SELECT id FROM notes WHERE user_id = @uid AND is_deleted = false ORDER BY id DESC LIMIT 1",
                new[] { new NpgsqlParameter("uid", userId) });
            int noteId = Convert.ToInt32(dt.Rows[0]["id"]);
            if (initiallyDeleted)
            {
                Coursework.Program.DeleteNote(noteId, userId, 1);
            }
            bool result = Coursework.Program.RestoreNote(noteId, userId, 1);
            Assert.AreEqual(expectedResult, result, $"Сценарий '{scenario}': ожидался результат {expectedResult}");
            var checkDt = Coursework.Program.ExecuteQuery(
                "SELECT is_deleted FROM notes WHERE id = @id",
                new[] { new NpgsqlParameter("id", noteId) });
            bool isDeleted = Convert.ToBoolean(checkDt.Rows[0]["is_deleted"]);
            if (expectedResult)
            {
                Assert.IsFalse(isDeleted, $"Сценарий '{scenario}': заметка не была восстановлена");
            }
            else
            {
                Assert.IsTrue(isDeleted, $"Сценарий '{scenario}': заметка была неожиданно восстановлена");
            }
        }

        #endregion

        #region Тестирование безопасности (TC-SEC-001, TC-SEC-002)

        [TestMethod]
        [DataRow("password123", "Тестирование хеширования")]
        [DataRow("", "Пустая строка")]
        [DataRow("a", "Одиночный символ")]
        public void PasswordHash_ConsistentResults(string password, string scenario)
        {
            string hash1 = Coursework.Program.GetMd5Hash(password);
            string hash2 = Coursework.Program.GetMd5Hash(password);
            Assert.AreEqual(hash1, hash2, $"Сценарий '{scenario}': хеши не совпадают");
            Assert.AreEqual(32, hash1.Length, $"Сценарий '{scenario}': неверная длина хеша");
            Assert.IsFalse(hash1.Contains(password), $"Сценарий '{scenario}': хеш содержит исходный пароль");
        }

        [TestMethod]
        [DataRow("LOGIN_SUCCESS", "Успешный вход", true, "Событие входа")]
        [DataRow("LOGIN_FAIL", "Неудачная попытка входа", true, "Событие ошибки")]
        [DataRow("TEST_EVENT", "Тестовое сообщение", true, "Тестовое событие")]
        public void SecurityLogs_EventLogged_RecordAdded(string eventType, string details, bool expectedResult, string scenario)
        {
            long beforeCount = GetSecurityLogsCount();
            Coursework.Program.SecurityLogs(eventType, testUser, details);
            long afterCount = GetSecurityLogsCount();
            Assert.IsTrue(afterCount > beforeCount, $"Сценарий '{scenario}': лог не был добавлен в таблицу security_logs");
        }

        #endregion

        #region Тестирование хеширования паролей (TC-HASH-001)

        [TestMethod]
        [DataRow("", "d41d8cd98f00b204e9800998ecf8427e", "Пустая строка")]
        [DataRow("a", "0cc175b9c0f1b6a831c399e269772661", "Одиночный символ 'a'")]
        [DataRow("password", "5f4dcc3b5aa765d61d8327deb882cf99", "Слово 'password'")]
        [DataRow("123456", "e10adc3949ba59abbe56e057f20f883e", "Числа '123456'")]
        [DataRow("HelloWorld", "fc5e038d38a57032085441e7fe7010b0", "Слово 'HelloWorld'")]
        public void GetMd5Hash_KnownValues_ReturnsExpectedHash(string input, string expectedHash, string scenario)
        {
            string hash = Coursework.Program.GetMd5Hash(input);
            Assert.AreEqual(expectedHash, hash, $"Сценарий '{scenario}': хеш не соответствует ожидаемому");
        }

        #endregion

        #region Вспомогательные методы

        private int GetUserId(string username)
        {
            var dt = Coursework.Program.ExecuteQuery("SELECT id FROM users WHERE username=@u",
                new[] { new NpgsqlParameter("u", username) });
            if (dt.Rows.Count > 0)
                return Convert.ToInt32(dt.Rows[0]["id"]);
            return -1;
        }

        private int GetOwnerIdForNote(int noteId)
        {
            var dt = Coursework.Program.ExecuteQuery("SELECT user_id FROM notes WHERE id=@id",
                new[] { new NpgsqlParameter("id", noteId) });
            if (dt.Rows.Count > 0)
                return Convert.ToInt32(dt.Rows[0]["user_id"]);
            return -1;
        }

        private void CleanupUser(int userIdToClean)
        {
            try
            {
                using (var conn = new NpgsqlConnection(testConnectionString))
                {
                    conn.Open();

                    string deleteNotes = "DELETE FROM notes WHERE user_id = @uid";
                    using (var cmd = new NpgsqlCommand(deleteNotes, conn))
                    {
                        cmd.Parameters.AddWithValue("uid", userIdToClean);
                        cmd.ExecuteNonQuery();
                    }
                    string deleteUser = "DELETE FROM users WHERE id = @uid";
                    using (var cmd = new NpgsqlCommand(deleteUser, conn))
                    {
                        cmd.Parameters.AddWithValue("uid", userIdToClean);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup error for user {userIdToClean}: {ex.Message}");
            }
        }

        private long GetSecurityLogsCount()
        {
            var dt = Coursework.Program.ExecuteQuery("SELECT COUNT(*) FROM security_logs");
            return Convert.ToInt64(dt.Rows[0][0]);
        }

        #endregion
    }

    #region Модульные тесты для утилитарных методов

    [TestClass]
    public class UtilityTests
    {
        [TestMethod]
        [DataRow("test", "Тестовый ввод")]
        [DataRow("", "Пустая строка")]
        [DataRow("very_long_string_for_testing_hash_function_1234567890", "Длинная строка")]
        public void GetMd5Hash_ReturnsCorrectLength(string input, string scenario)
        {
            string hash = Coursework.Program.GetMd5Hash(input);
            Assert.AreEqual(32, hash.Length, $"Сценарий '{scenario}': неверная длина хеша");
        }

        [TestMethod]
        [DataRow("same_input_123", "Первый тест")]
        [DataRow("another_input", "Второй тест")]
        public void GetMd5Hash_SameInput_ReturnsSameHash(string input, string scenario)
        {
            string hash1 = Coursework.Program.GetMd5Hash(input);
            string hash2 = Coursework.Program.GetMd5Hash(input);
            Assert.AreEqual(hash1, hash2, $"Сценарий '{scenario}': хеши не совпадают для одинакового ввода");
        }

        [TestMethod]
        public void GetMd5Hash_DifferentInputs_ReturnDifferentHashes()
        {
            string hash1 = Coursework.Program.GetMd5Hash("input1");
            string hash2 = Coursework.Program.GetMd5Hash("input2");
            Assert.AreNotEqual(hash1, hash2);
        }
    }

    #endregion

    #region Тесты производительности (TC-PERF-001, TC-PERF-002)

    [TestClass]
    public class PerformanceTests
    {
        private string testUser;
        private int userId;
        private static string connectionString = "Server=localhost;Port=5432;User ID=postgres;Password=3455;Database=Kurs;";

        [TestInitialize]
        public void Setup()
        {
            testUser = $"perfuser_{DateTime.Now.Ticks}";
            Coursework.Program.Register(testUser, "perfpass", 1);
            userId = GetUserId(testUser);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand("DELETE FROM notes WHERE user_id = @uid", conn))
                    {
                        cmd.Parameters.AddWithValue("uid", userId);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @uid", conn))
                    {
                        cmd.Parameters.AddWithValue("uid", userId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }

        [TestMethod]
        [DataRow(100, 5000, "Добавление 100 заметок")]
        [DataRow(50, 3000, "Добавление 50 заметок")]
        public void AddNote_BulkInsert_CompletesWithinTime(int notesCount, int maxMilliseconds, string scenario)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            for (int i = 0; i < notesCount; i++)
            {
                Coursework.Program.AddNote(userId, $"Тестовая заметка #{i}");
            }
            stopwatch.Stop();
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < maxMilliseconds,
                $"Сценарий '{scenario}': добавление {notesCount} заметок заняло {stopwatch.ElapsedMilliseconds} мс, что превышает {maxMilliseconds} мс");
        }

        [TestMethod]
        [DataRow(1000, "Логин должен выполняться менее чем за 1 секунду")]
        public void Login_Performance_CompletesWithinTime(int maxMilliseconds, string scenario)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var (success, role) = Coursework.Program.Login(testUser, "perfpass");
            stopwatch.Stop();
            Assert.IsTrue(success, "Логин должен быть успешным");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < maxMilliseconds,
                $"{scenario}: логин занял {stopwatch.ElapsedMilliseconds} мс, что превышает {maxMilliseconds} мс");
        }

        private int GetUserId(string username)
        {
            var dt = Coursework.Program.ExecuteQuery("SELECT id FROM users WHERE username=@u",
                new[] { new NpgsqlParameter("u", username) });
            if (dt.Rows.Count > 0)
                return Convert.ToInt32(dt.Rows[0]["id"]);
            return -1;
        }
    }

    #endregion
}