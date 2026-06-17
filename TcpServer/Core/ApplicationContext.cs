using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace TcpServer.Core;

// Взаимодействие с базой данных
public class ApplicationContext : DbContext // Определяет контекст данных, используемый для взаимодействия с бд
{
    // ОДИН ОБЩИЙ объект блокировки для всех потоков и экземпляров контекста, чтобы избежать IOException
    private static readonly object _logLock = new object();

    // Устанавливает параметры подключения
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var config = new ConfigurationBuilder()// Создается объект, который умеет собирать настройки приложения из разных источников (файлов, переменных среды и т.д.)
                        .SetBasePath(Directory.GetCurrentDirectory())// Говорит программе искать файл appsettings.json в текущей рабочей директории приложения
                        .AddJsonFile("appsettings.json", optional: false)// Указывает, что настройки нужно читать из файла appsettings.json
                        .Build();// Объединяет все указанные источники и создает готовый объект config

        optionsBuilder.UseSqlite(config.GetConnectionString("DefaultConnection"));// Настройка подключения к SQLite

        // Безопасная запись в лог без удержания статического потока в памяти
        // EF Core сам откроет файл, запишет лог и закроет его, что предотвратит блокировку файла
        var logPath = Path.Combine(Directory.GetCurrentDirectory(), "mylog.txt");

        // Используем File.AppendAllText внутри конструкции lock вместо создания StreamWriter на каждый контекст.
        // Это гарантирует, что даже при параллельных запросах от множества клиентов логи запишутся без ошибок доступа.
        optionsBuilder.LogTo(
            logMessage =>
            {
                lock (_logLock) // Блокируем доступ к файлу на время записи текущей строки лога через единый статический объект
                {
                    try
                    {
                        // Открываем файл с флагом FileShare.ReadWrite, чтобы избежать конфликтов со сторонними программами
                        using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        using var writer = new StreamWriter(fs, Encoding.UTF8);
                        writer.WriteLine(logMessage + Environment.NewLine);
                    }
                    catch (IOException)
                    {
                        // Предотвращает падение сервера, если файл заблокирован внешним редактором
                    }
                }
            },
            LogLevel.Information); // Позволяет получить лог о выполняемых в Entity Framework операциях (уровень логгирования)
    }

    // Представляет набор объектов, которые хранятся в бд
    public DbSet<User> Users => Set<User>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Chat> Chats => Set<Chat>();
}
