using Microsoft.EntityFrameworkCore;

namespace TcpServer.Core;

public class DbOperations
{
    public async Task DbTestAsync()
    {
        using (ApplicationContext bd = new ApplicationContext())
        {
            // При создании контекста автоматически проверяет наличие бд и, если она существует - удаляется (отладка), отсуствует - создает
            // 1. Закрываем текущее соединение контекста, если оно было открыто
            //bd.Database.CloseConnection();

            // 2. Очищаем пул соединений SQLite (метод синхронный!)
            //Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // 3. Теперь файл разблокирован и его можно удалить
            //await bd.Database.EnsureDeletedAsync(); // Очистит старую БД при старте (отладка)

            await bd.Database.EnsureCreatedAsync(); // Создаст чистую БД со всеми таблицами

            // Проверка на доступность бд 
            bool isAvalaible = await bd.Database.CanConnectAsync();
            if (isAvalaible)
                Console.WriteLine("База данных доступна");
            else
                Console.WriteLine("База данных недоступна");
        }
    }

    public async Task<int?> DbRegistrationAsync(string clientName)
    {
        using (ApplicationContext db = new ApplicationContext())
        {
            // Ищем пользователя в БД по имени
            var user = await db.Users.FirstOrDefaultAsync(u => u.Name == clientName);

            // Если пользователя с таким именем еще нет — регистрируем его автоматически
            if (user == null)
            {
                user = new User { Name = clientName };
                await db.Users.AddAsync(user);
                await db.SaveChangesAsync(); // SQLite автоматически выдаст ему уникальный ID
                Console.WriteLine($"[БД] Зарегистрирован новый пользователь: {clientName} (ID: {user.Id})");
            }
            else
            {
                Console.WriteLine($"[БД] Пользователь {clientName} зашел повторно (Id: {user.Id})");
            }
            return user.Id;
        }
    }

    // Сохранение личного сообщения (конкретному пользователю по ID)
    public async Task DbSavePrivateMessageAsync(int senderId, int recipientId, string clientMessage)
    {
        // Если сообщения нет, выходим
        if (string.IsNullOrEmpty(clientMessage)) return;

        using (ApplicationContext bd = new ApplicationContext())
        {
            // Ищем, существует ли уже чат между ними в обоих направлениях по их Id
            var existingChat = await bd.Chats.FirstOrDefaultAsync(c =>
                (c.User1Id == senderId && c.User2Id == recipientId) ||
                (c.User1Id == recipientId && c.User2Id == senderId));

            // Если чата нет — создаем новый и сохраняем его для получения Id
            if (existingChat == null)
            {
                existingChat = new Chat
                {
                    User1Id = senderId,
                    User2Id = recipientId
                };

                await bd.Chats.AddAsync(existingChat);
                await bd.SaveChangesAsync();
                Console.WriteLine($"[БД] Создан новый чат #{existingChat.Id} между ID:{senderId} и ID:{recipientId}");
            }

            // Создаем и сохраняем само сообщение, привязывая его к Id чата и Id отправителя
            var newMessage = new Message
            {
                ChatId = existingChat.Id,
                SenderId = senderId,
                MessageText = clientMessage
            };

            await bd.Messages.AddAsync(newMessage);
            await bd.SaveChangesAsync();
            Console.WriteLine($"[БД] Личное сообщение успешно сохранено в чат #{existingChat.Id}");
        }
    }

    // Сохранение сообщения для общего чата (всем онлайн)
    public async Task DbSaveBroadcastMessageAsync(int senderId, string clientMessage)
    {
        // Если сообщения нет, выходим
        if (string.IsNullOrEmpty(clientMessage)) return;

        using (ApplicationContext bd = new ApplicationContext())
        {
            // Создаем и сохраняем сообщение для общего чата (зарезервированный ChatId = 0)
            var newMessage = new Message
            {
                ChatId = 0, // 0 означает "Общий чат / Для всех"
                SenderId = senderId,
                MessageText = clientMessage
            };

            await bd.Messages.AddAsync(newMessage);
            await bd.SaveChangesAsync();
            Console.WriteLine($"[БД] Общее сообщение от ID:{senderId} успешно сохранено.");
        }
    }

    // Получение списка всех зарегистрированных пользователей для WPF-клиента
    public async Task<List<User>> GetAllUsersAsync()
    {
        using (ApplicationContext bd = new ApplicationContext())
        {
            // Возвращаем полный список пользователей из таблицы Users
            return await bd.Users.ToListAsync();
        }
    }




    //// Создаем контекст с автоматическим закрытием данного объекта (using) 
    //using (ApplicationContext db = new ApplicationContext())
    //{

    //    // При создании контекста автоматически проверяет наличие бд и, если она существует - удаляется (отладка), отсуствует - создает
    //    //await _db.Database.EnsureDeletedAsync(); // Очистит старую БД при старте (отладка)
    //    await db.Database.EnsureCreatedAsync(); // Создаст чистую БД со всеми таблицами

    //    // Проверка на доступность бд 
    //    bool isAvalaible = await db.Database.CanConnectAsync();
    //    if (isAvalaible)
    //        Console.WriteLine("База данных доступна");
    //    else
    //        Console.WriteLine("База данных недоступна");


    //    // Создаем три объекта User 
    //    User alex = new User { Name = "Alex" };
    //    User ann = new User { Name = "Ann" };
    //    User sofia = new User { Name = "Sofia" };

    //    // Добавление 
    //    await db.Users.AddRangeAsync(alex, ann);
    //    await db.Users.AddAsync(sofia);// Метод Add устанавливает значение Added в качестве состояния нового объекта 
    //    await db.SaveChangesAsync();// Сгенерирует выражение INSERT 
    //    Console.WriteLine("Объекты успешно сохранены");

    //    // Получение 
    //    var getUsers = await db.Users.AsNoTracking().ToListAsync();
    //    Console.WriteLine("Список объектов:");
    //    foreach (User u in getUsers)
    //        Console.WriteLine($"{u.Id}.{u.Name}");

    //    // Редактирование 
    //    //User? user = await db.Users.FirstOrDefaultAsync();// Получаем первый объект 
    //    //User? user = await db.Users.FindAsync(3);// Получаем пользователя с ID = 3 
    //    //User? thirdUser = await db.Users.Skip(2).FirstOrDefaultAsync();// Пропустит 2 элемента и возьмет 3-й по порядку 
    //    User? user = await db.Users.FirstOrDefaultAsync(u => u.Name == "Sofia");// Получаем первого пользователя с именем "Sofia"
    //    if (user != null)
    //    {
    //        user.Name = "Zuhra";
    //        //db.Users.Update(user);//Обновляем объект 
    //        await db.SaveChangesAsync();// Сгенерирует SQL-выражение UPDATE 
    //    }

    //    // Выводим данные после обновления 
    //    Console.WriteLine("\nДанные после редактирования:");
    //    getUsers = await db.Users.AsNoTracking().ToListAsync();
    //    foreach (User u in getUsers)
    //        Console.WriteLine($"{u.Id}.{u.Name}");

    //    // Удаление 
    //    user = await db.Users.FirstOrDefaultAsync(u => u.Name == "Zuhra");
    //    if (user != null)
    //    {
    //        db.Users.Remove(user);//Удаляем объект 
    //        await db.SaveChangesAsync();// Сгенерирует SQL-выражение DELETE 
    //    }

    //    // Выводим данные после обновления 
    //    Console.WriteLine("\nДанные после удаления:");
    //    getUsers = await db.Users.AsNoTracking().ToListAsync();
    //    foreach (User u in getUsers)
    //        Console.WriteLine($"{u.Id}.{u.Name}");
    //}
}
